using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Pumpkin.DotNet.SourceGenerator;

[Generator]
public sealed class PumpkinPluginGenerator : IIncrementalGenerator
{
    private const string BaseTypeName = "PumpkinPlugin";
    private const string BaseTypeNamespace = "Pumpkin.DotNet.Api.Plugins";

    private const string EventAttributeName = "EventHandler";
    private const string EventAttributeFullName = "EventHandlerAttribute";

    private const string CommandAttributeFullName = "Pumpkin.DotNet.Api.Commands.CommandAttribute";
    private const string SubcommandAttributeFullName = "Pumpkin.DotNet.Api.Commands.SubcommandAttribute";
    private const string ExecuteAttributeFullName = "Pumpkin.DotNet.Api.Commands.ExecuteAttribute";
    private const string ArgAttributeFullName = "Pumpkin.DotNet.Api.Commands.ArgAttribute";
    private const string SuggestAttributeFullName = "Pumpkin.DotNet.Api.Commands.SuggestAttribute";

    private const string CommandContextTypeName = "Pumpkin.DotNet.Api.Commands.ICommandContext";
    private const string ServerTypeName = "Pumpkin.DotNet.Api.Server.IServer";
    private const string LoggerTypeName = "Pumpkin.DotNet.Api.Logging.ILogger";
    private const string ConfigurationTypeName = "Microsoft.Extensions.Configuration.IConfiguration";

    private static readonly DiagnosticDescriptor MissingPluginError = new(
        id: "PMP001",
        title: "Plugin class not found",
        messageFormat: "No non-abstract class inheriting from 'PumpkinPlugin' was found in the assembly",
        category: "Pumpkin.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    private static readonly DiagnosticDescriptor MultiplePluginsError = new(
        id: "PMP002",
        title: "Multiple plugin classes detected",
        messageFormat: "Duplicate plugin class '{0}' found. Only one class inheriting from 'PumpkinPlugin' is allowed per assembly.",
        category: "Pumpkin.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    private static readonly DiagnosticDescriptor MissingParameterlessCtorError = new(
        id: "PMP003",
        title: "Missing accessible parameterless constructor",
        messageFormat: "Type '{0}' declares instance handler methods but has no accessible parameterless constructor",
        category: "Pumpkin.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    private static readonly DiagnosticDescriptor InvalidSuggestMethodError = new(
        id: "PMP004",
        title: "Invalid command suggestion method",
        messageFormat: "Suggestion method '{0}' on '{1}' was not found, is inaccessible, or has unsupported parameter type '{2}'",
        category: "Pumpkin.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    private static readonly DiagnosticDescriptor InaccessibleHandlerMethodError = new(
        id: "PMP005",
        title: "Inaccessible handler method",
        messageFormat: "Handler method '{0}' on '{1}' must be public or internal to be invoked by the source generator",
        category: "Pumpkin.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    private static readonly DiagnosticDescriptor InvalidEventReturnTypeError = new(
        id: "PMP006",
        title: "Invalid event handler return type",
        messageFormat: "Event handler method '{0}' on '{1}' has unsupported return type '{2}'. Event handlers must return 'void'. Use 'PumpkinTask.Spawn' for asynchronous work.",
        category: "Pumpkin.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    private enum FsAccessLevel
    {
        None = 0,
        ReadData = 1,
        WriteData = 2
    }

    private sealed class ConfigUsageModel(INamedTypeSymbol? configType, FsAccessLevel accessLevel)
    {
        public INamedTypeSymbol? ConfigType { get; } = configType;
        public FsAccessLevel AccessLevel { get; } = accessLevel;
    }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var optionsProvider = context.AnalyzerConfigOptionsProvider
            .Select(static (p, _) => p.GlobalOptions);

        var pluginsProvider = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) =>
                    node is ClassDeclarationSyntax { BaseList.Types.Count: > 0 },
                transform: static (ctx, ct) => GetPlugin(ctx, ct))
            .Where(static x => x is not null);

        var eventMethods = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is MethodDeclarationSyntax { AttributeLists.Count: > 0 },
                transform: static (ctx, ct) => GetEventMethodInfo(ctx, ct))
            .Where(static x => x is not null);

        var commandClasses = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                CommandAttributeFullName,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, ct) => GetCommandClassInfo(ctx, ct))
            .Where(static x => x is not null);

        var configUsages = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsConfigCandidateNode(node),
                transform: static (ctx, ct) => AnalyzeConfigUsage(ctx, ct))
            .Where(static x => x is not null);

        var compilationAndPlugins = pluginsProvider
            .Collect()
            .Select(static (plugins, _) => plugins
                .OfType<INamedTypeSymbol>()
                .Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default)
                .ToImmutableArray())
            .Combine(optionsProvider)
            .Combine(context.CompilationProvider)
            .Combine(eventMethods.Collect())
            .Combine(commandClasses.Collect())
            .Combine(configUsages.Collect());

        context.RegisterSourceOutput(compilationAndPlugins, static (spc, source) =>
        {
            var (((((plugins, options), compilation), methods), commands), configs) = source;
            if (plugins.IsDefaultOrEmpty)
            {
                spc.ReportDiagnostic(Diagnostic.Create(MissingPluginError, Location.None));
                return;
            }
            if (plugins.Length > 1)
            {
                foreach (var duplicate in plugins.Skip(1))
                {
                    var location = duplicate?.Locations.FirstOrDefault() ?? Location.None;
                    spc.ReportDiagnostic(Diagnostic.Create(
                        MultiplePluginsError,
                        location,
                        duplicate?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
                    ));
                }
                return;
            }

            var info = ExtractMetadata(plugins[0]!, options, compilation);
            Generate(spc, plugins[0]!, info, methods!, commands!, configs!);
        });
    }

    private static bool IsConfigCandidateNode(SyntaxNode node)
    {
        if (node is InvocationExpressionSyntax invocation)
        {
            var name = invocation.Expression switch
            {
                SimpleNameSyntax s => s.Identifier.Text,
                MemberAccessExpressionSyntax { Name: SimpleNameSyntax s } => s.Identifier.Text,
                _ => null
            };

            return name is "GetConfig" or "SaveConfig" or "SaveDefaultConfig" or "ReloadConfig" or "GetConfigFiles" or "RegisterConfigFormat";
        }

        if (node is MemberAccessExpressionSyntax { Name.Identifier.Text: "Configuration" })
            return true;

        if (node is IdentifierNameSyntax { Identifier.Text: "Configuration" })
            return true;

        return false;
    }

    private static ConfigUsageModel? AnalyzeConfigUsage(GeneratorSyntaxContext context, CancellationToken ct)
    {
        var symbol = context.SemanticModel.GetSymbolInfo(context.Node, ct).Symbol;
        if (symbol is null)
            return null;

        var containingType = symbol.ContainingType?.ToDisplayString();
        if (containingType is not (
            "Pumpkin.DotNet.Api.Plugins.IPluginContext" or
            "Pumpkin.DotNet.Api.Plugins.PumpkinPlugin"))
        {
            return null;
        }

        if (symbol is IPropertySymbol { Name: "Configuration" })
            return new ConfigUsageModel(null, FsAccessLevel.ReadData);

        if (symbol is IMethodSymbol method)
        {
            var access = method.Name switch
            {
                "SaveDefaultConfig" or "SaveConfig" => FsAccessLevel.WriteData,
                "GetConfig" or "ReloadConfig" or "GetConfigFiles" => FsAccessLevel.ReadData,
                _ => FsAccessLevel.None
            };

            if (access == FsAccessLevel.None)
                return null;

            INamedTypeSymbol? targetType = null;
            if (method.Name is "GetConfig" or "SaveConfig" &&
                method.TypeArguments.FirstOrDefault() is INamedTypeSymbol named &&
                named.TypeKind != TypeKind.TypeParameter &&
                !named.IsAbstract)
            {
                targetType = named;
            }

            return new ConfigUsageModel(targetType, access);
        }

        return null;
    }

    private static CommandClassModel? GetCommandClassInfo(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol classSymbol)
            return null;

        if ((classSymbol.IsAbstract && !classSymbol.IsStatic) || classSymbol.IsGenericType)
            return null;

        var cmdAttr = ctx.Attributes.FirstOrDefault();
        if (cmdAttr is null || cmdAttr.ConstructorArguments.Length == 0)
            return null;

        string name = cmdAttr.ConstructorArguments[0].Value?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var aliases = new List<string>();
        if (cmdAttr.ConstructorArguments.Length > 1 && cmdAttr.ConstructorArguments[1].Kind == TypedConstantKind.Array)
        {
            foreach (var item in cmdAttr.ConstructorArguments[1].Values)
            {
                if (item.Value is string alias && !string.IsNullOrWhiteSpace(alias))
                    aliases.Add(alias);
            }
        }

        string description = GetNamedArg<string>(cmdAttr, "Description") ?? string.Empty;
        string permission = GetNamedArg<string>(cmdAttr, "Permission") ?? string.Empty;
        int permLevel = GetNamedArg<int>(cmdAttr, "PermissionLevel");

        var allClassMethods = classSymbol.GetMembers().OfType<IMethodSymbol>()
            .Where(m => m.MethodKind == MethodKind.Ordinary)
            .ToList();

        var methods = new List<CommandExecuteMethodModel>();
        var diagnostics = new List<DiagnosticInfo>();

        foreach (var member in allClassMethods)
        {
            var execAttr = member.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == ExecuteAttributeFullName);
            if (execAttr is null)
                continue;

            if (!IsAccessibleToGenerator(member))
            {
                diagnostics.Add(new DiagnosticInfo(
                    InaccessibleHandlerMethodError,
                    member.Locations.FirstOrDefault() ?? Location.None,
                    [member.Name, classSymbol.Name]));
                continue;
            }

            var subAttr = member.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == SubcommandAttributeFullName);
            string? subName = subAttr?.ConstructorArguments.FirstOrDefault().Value?.ToString();
            int subPermLevel = subAttr is not null ? GetNamedArg<int>(subAttr, "PermissionLevel") : 0;

            var allCallParams = new List<ExecuteParamSlot>();
            var brigadierArgs = new List<CommandParamModel>();

            foreach (var p in member.Parameters)
            {
                string typeFull = p.Type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString();

                if (typeFull == CommandContextTypeName)
                {
                    allCallParams.Add(new ExecuteParamSlot(isBrigadierArg: false, injectedExpr: "ctx", brigadierArgIndex: -1));
                    continue;
                }
                if (typeFull == ServerTypeName)
                {
                    allCallParams.Add(new ExecuteParamSlot(isBrigadierArg: false, injectedExpr: "plugin.Server", brigadierArgIndex: -1));
                    continue;
                }
                if (typeFull == LoggerTypeName)
                {
                    allCallParams.Add(new ExecuteParamSlot(isBrigadierArg: false, injectedExpr: "plugin.Logger", brigadierArgIndex: -1));
                    continue;
                }
                if (typeFull == ConfigurationTypeName)
                {
                    allCallParams.Add(new ExecuteParamSlot(isBrigadierArg: false, injectedExpr: "plugin.Configuration", brigadierArgIndex: -1));
                    continue;
                }

                var argAttr = p.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == ArgAttributeFullName);
                var sugAttr = p.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == SuggestAttributeFullName);

                string argName = (argAttr is not null ? GetNamedArg<string>(argAttr, "Name") : null) ?? p.Name;
                double min = argAttr is not null ? GetNamedArgOrDefault(argAttr, "Min", double.NaN) : double.NaN;
                double max = argAttr is not null ? GetNamedArgOrDefault(argAttr, "Max", double.NaN) : double.NaN;
                int strMode = argAttr is not null ? GetNamedArg<int>(argAttr, "StringMode") : 0;
                string? suggestMethodName = sugAttr?.ConstructorArguments.FirstOrDefault().Value?.ToString();

                SuggestMethodModel? suggestModel = null;
                if (!string.IsNullOrEmpty(suggestMethodName))
                {
                    var sugSymbol = allClassMethods.FirstOrDefault(m => m.Name == suggestMethodName);
                    if (sugSymbol is null || !IsAccessibleToGenerator(sugSymbol))
                    {
                        diagnostics.Add(new DiagnosticInfo(
                            InvalidSuggestMethodError,
                            p.Locations.FirstOrDefault() ?? Location.None,
                            [suggestMethodName!, classSymbol.Name, "<missing or inaccessible>"]));
                    }
                    else
                    {
                        suggestModel = BuildSuggestModel(sugSymbol, classSymbol, p.Locations.FirstOrDefault() ?? Location.None, diagnostics);
                    }
                }

                var cleanType = p.Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nts
                    ? nts.TypeArguments[0]
                    : p.Type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);

                string defaultExpr = p.HasExplicitDefaultValue
                    ? FormatLiteral(p.ExplicitDefaultValue)
                    : "default!";

                int argIndex = brigadierArgs.Count;
                brigadierArgs.Add(new CommandParamModel(
                    argName: argName,
                    typeFullName: cleanType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    isOptional: p.IsOptional,
                    defaultValueExpr: defaultExpr,
                    min: min,
                    max: max,
                    stringMode: strMode,
                    suggest: suggestModel
                ));

                allCallParams.Add(new ExecuteParamSlot(isBrigadierArg: true, injectedExpr: null, brigadierArgIndex: argIndex));
            }

            methods.Add(new CommandExecuteMethodModel(
                methodName: member.Name,
                isStatic: member.IsStatic,
                returnsInt: member.ReturnType.SpecialType == SpecialType.System_Int32,
                subcommandName: subName,
                subcommandPermLevel: subPermLevel,
                callSlots: allCallParams,
                brigadierArgs: brigadierArgs
            ));
        }

        return new CommandClassModel(
            containingType: classSymbol,
            name: name,
            aliases: aliases,
            description: description,
            permission: permission,
            permissionLevel: permLevel,
            methods: methods,
            diagnostics: diagnostics
        );
    }

    private static SuggestMethodModel? BuildSuggestModel(
        IMethodSymbol sugSymbol,
        INamedTypeSymbol classSymbol,
        Location location,
        List<DiagnosticInfo> diagnostics)
    {
        var args = new List<string>(sugSymbol.Parameters.Length);
        bool stringConsumed = false;

        foreach (var p in sugSymbol.Parameters)
        {
            string typeFull = p.Type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString();

            if (typeFull == CommandContextTypeName)
                args.Add("ctx");
            else if (typeFull == ServerTypeName)
                args.Add("plugin.Server");
            else if (typeFull == LoggerTypeName)
                args.Add("plugin.Logger");
            else if (typeFull == ConfigurationTypeName)
                args.Add("plugin.Configuration");
            else if (p.Type.SpecialType == SpecialType.System_String && !stringConsumed)
            {
                args.Add("input");
                stringConsumed = true;
            }
            else
            {
                diagnostics.Add(new DiagnosticInfo(
                    InvalidSuggestMethodError,
                    location,
                    [sugSymbol.Name, classSymbol.Name, p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)]));
                return null;
            }
        }

        return new SuggestMethodModel(sugSymbol.Name, sugSymbol.IsStatic, args);
    }

    private static EventMethodModel? GetEventMethodInfo(GeneratorSyntaxContext ctx, CancellationToken ct)
    {
        var methodDecl = (MethodDeclarationSyntax)ctx.Node;
        if (ctx.SemanticModel.GetDeclaredSymbol(methodDecl, ct) is not IMethodSymbol method)
            return null;

        var attr = method.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.Name is EventAttributeName or EventAttributeFullName);

        if (attr is null)
            return null;

        var compilation = ctx.SemanticModel.Compilation;

        var eventBaseInterface = compilation.GetTypeByMetadataName("Pumpkin.DotNet.Api.Events.IEvent");
        var serverInterface = compilation.GetTypeByMetadataName(ServerTypeName);
        var loggerInterface = compilation.GetTypeByMetadataName(LoggerTypeName);
        var configurationInterface = compilation.GetTypeByMetadataName(ConfigurationTypeName);

        var eventType = TryGetEventType(method.Parameters, eventBaseInterface);
        if (eventType is null)
            return null;

        List<string> arguments = new(method.Parameters.Length);

        foreach (var param in method.Parameters)
        {
            var paramType = param.Type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);

            if (SymbolEqualityComparer.Default.Equals(paramType, eventType))
                arguments.Add("evt");
            else if (SymbolEqualityComparer.Default.Equals(paramType, serverInterface))
                arguments.Add("plugin.Server");
            else if (SymbolEqualityComparer.Default.Equals(paramType, loggerInterface))
                arguments.Add("plugin.Logger");
            else if (SymbolEqualityComparer.Default.Equals(paramType, configurationInterface))
                arguments.Add("plugin.Configuration");
            else
                return null;
        }

        int priority = 2;
        bool isModifying = true;

        foreach (var namedArg in attr.NamedArguments)
        {
            if (namedArg.Value.IsNull)
                continue;

            switch (namedArg.Key)
            {
                case "Priority":
                    if (namedArg.Value.Value is not null)
                        priority = Convert.ToInt32(namedArg.Value.Value);
                    break;
                case "IsModifying":
                    if (namedArg.Value.Value is bool modVal)
                        isModifying = modVal;
                    break;
            }
        }

        return new EventMethodModel(
            method.ContainingType,
            method.Name,
            arguments.ToArray(),
            eventType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            method.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            method.ReturnsVoid,
            method.IsStatic,
            priority,
            isModifying,
            IsAccessibleToGenerator(method),
            method.Locations.FirstOrDefault() ?? Location.None
        );
    }

    private static ITypeSymbol? TryGetEventType(ImmutableArray<IParameterSymbol> parameters, INamedTypeSymbol? eventBaseInterface)
    {
        if (parameters.IsDefaultOrEmpty)
            return null;

        foreach (var param in parameters)
        {
            var paramType = param.Type;
            if (SymbolEqualityComparer.Default.Equals(paramType, eventBaseInterface))
                return paramType;
            if (paramType.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, eventBaseInterface)))
                return paramType;
        }

        return null;
    }

    private static INamedTypeSymbol? GetPlugin(GeneratorSyntaxContext ctx, CancellationToken ct)
    {
        var classDecl = (ClassDeclarationSyntax)ctx.Node;
        if (ctx.SemanticModel.GetDeclaredSymbol(classDecl, ct) is not INamedTypeSymbol type)
            return null;

        if (type.IsAbstract || type.IsStatic || type.IsGenericType)
            return null;

        return InheritsFrom(type, BaseTypeName, BaseTypeNamespace) ? type : null;
    }

    private static bool InheritsFrom(INamedTypeSymbol type, string name, string ns)
    {
        for (var t = type.BaseType; t is not null; t = t.BaseType)
        {
            if (t.Name == name && t.ContainingNamespace.ToDisplayString() == ns)
                return true;
        }
        return false;
    }

    private static bool IsAccessibleToGenerator(ISymbol symbol)
    {
        for (ISymbol? current = symbol; current is not null && current is not INamespaceSymbol; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal))
                return false;
        }
        return true;
    }

    private static bool HasAccessibleParameterlessConstructor(INamedTypeSymbol type)
    {
        if (type.IsAbstract || type.IsStatic)
            return false;

        return type.InstanceConstructors.Any(ctor =>
            ctor.Parameters.Length == 0 &&
            ctor.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal);
    }

    private static PluginInfo ExtractMetadata(
        INamedTypeSymbol pluginType,
        AnalyzerConfigOptions options,
        Compilation compilation)
    {
        var typeName = pluginType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var minimalTypeName = pluginType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

        options.TryGetValue("build_property.PluginName", out var name);
        options.TryGetValue("build_property.Version", out var version);
        options.TryGetValue("build_property.Authors", out var authors);
        options.TryGetValue("build_property.Description", out var description);
        options.TryGetValue("build_property.PluginPermissions", out var rawPermissions);

        var permissionsArray = string.IsNullOrWhiteSpace(rawPermissions)
            ? Array.Empty<string>()
            : rawPermissions!
                .Split([';', ','], StringSplitOptions.RemoveEmptyEntries)
                .Select(static p => p.Trim())
                .Where(static p => !string.IsNullOrEmpty(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        string finalAuthors = !string.IsNullOrWhiteSpace(authors) ? authors! : "Unknown";
        var authorsArray = finalAuthors
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Select(static a => a.Trim())
            .Where(static a => !string.IsNullOrEmpty(a))
            .ToArray();

        if (string.IsNullOrWhiteSpace(version))
            version = compilation.Assembly.Identity.Version.ToString();

        if (string.IsNullOrWhiteSpace(description))
        {
            var descAttr = compilation.Assembly.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.Name is "AssemblyDescriptionAttribute" or "AssemblyDescription");
            description = descAttr?.ConstructorArguments.FirstOrDefault().Value?.ToString();
        }

        return new PluginInfo(
            typeName: typeName,
            name: name ?? minimalTypeName,
            version: version ?? "0.1.0",
            authors: authorsArray,
            description: description ?? string.Empty,
            permissions: permissionsArray
        );
    }

    private static void Generate(
        SourceProductionContext context,
        INamedTypeSymbol pluginSymbol,
        PluginInfo plugin,
        ImmutableArray<EventMethodModel> methods,
        ImmutableArray<CommandClassModel> commands,
        ImmutableArray<ConfigUsageModel> configUsages)
    {
        var safeVersion = EscapeString(plugin.Version);
        var safeDescription = EscapeString(plugin.Description);
        var safeName = EscapeString(plugin.Name);
        var authorsLiteral = string.Join(", ", plugin.Authors.Select(a => $"\"{EscapeString(a)}\""));

        var maxFsAccess = FsAccessLevel.None;
        foreach (var usage in configUsages)
        {
            if (usage.AccessLevel > maxFsAccess)
                maxFsAccess = usage.AccessLevel;
        }

        bool usesConfigurationDi =
            methods.Any(m => m.Arguments.Contains("plugin.Configuration")) ||
            commands.Any(c => c.Methods.Any(m =>
                m.CallSlots.Any(s => s.InjectedExpr == "plugin.Configuration") ||
                m.BrigadierArgs.Any(a => a.Suggest?.Arguments.Contains("plugin.Configuration") == true)));

        if (usesConfigurationDi && maxFsAccess < FsAccessLevel.ReadData)
            maxFsAccess = FsAccessLevel.ReadData;

        var finalPermissions = new HashSet<string>(plugin.Permissions, StringComparer.OrdinalIgnoreCase);
        if (maxFsAccess == FsAccessLevel.WriteData)
        {
            finalPermissions.Remove("fs.read.data");
            finalPermissions.Add("fs.write.data");
        }
        else if (maxFsAccess == FsAccessLevel.ReadData && !finalPermissions.Contains("fs.write.data"))
        {
            finalPermissions.Add("fs.read.data");
        }

        var permissionsLiteral = string.Join(", ", finalPermissions.Select(p => $"\"{EscapeString(p)}\""));

        var distinctConfigTypes = configUsages
            .Where(static u => u.ConfigType is not null)
            .Select(static u => u.ConfigType!)
            .Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default)
            .ToList();

        var configRegistrarBuilder = new StringBuilder();
        var configMethodsBuilder = new StringBuilder();

        for (int i = 0; i < distinctConfigTypes.Count; i++)
        {
            var type = distinctConfigTypes[i];
            var fullName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            configRegistrarBuilder.AppendLine($"        registrar.Register<{fullName}>(BindConfig_{i}, SerializeConfig_{i});");

            configMethodsBuilder.AppendLine();
            configMethodsBuilder.AppendLine($"    private static {fullName} BindConfig_{i}(IReadOnlyDictionary<string, string?> values)");
            configMethodsBuilder.AppendLine("    {");
            configMethodsBuilder.AppendLine($"        var cfg = new {fullName}();");
            int varIndex = 0;
            var visited = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default) { type };
            EmitBindProperties(configMethodsBuilder, type, "cfg", prefix: "", indent: "        ", visited, ref varIndex);
            configMethodsBuilder.AppendLine("        return cfg;");
            configMethodsBuilder.AppendLine("    }");

            configMethodsBuilder.AppendLine();
            configMethodsBuilder.AppendLine($"    private static Dictionary<string, string?> SerializeConfig_{i}({fullName} config)");
            configMethodsBuilder.AppendLine("    {");
            configMethodsBuilder.AppendLine("        var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);");
            int serIndex = 0;
            var serVisited = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default) { type };
            EmitSerializeProperties(configMethodsBuilder, type, "config", keyPrefix: "", isDynamicPrefix: false, indent: "        ", serVisited, ref serIndex);
            configMethodsBuilder.AppendLine("        return dict;");
            configMethodsBuilder.AppendLine("    }");
        }

        var eventsBuilder = new StringBuilder();
        var listenerInstances = new HashSet<string>();

        foreach (var method in methods)
        {
            if (!method.IsAccessible)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InaccessibleHandlerMethodError,
                    method.Location,
                    method.MethodName,
                    method.ContainingType.Name));
                continue;
            }

            if (!method.ReturnsVoid)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InvalidEventReturnTypeError,
                    method.Location,
                    method.MethodName,
                    method.ContainingType.Name,
                    method.ReturnTypeDisplay));
                continue;
            }

            var containingTypeFullName = method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            string methodTarget;

            if (method.IsStatic)
            {
                methodTarget = $"{containingTypeFullName}.{method.MethodName}";
            }
            else if (SymbolEqualityComparer.Default.Equals(method.ContainingType, pluginSymbol))
            {
                methodTarget = $"plugin.{method.MethodName}";
            }
            else
            {
                if (!HasAccessibleParameterlessConstructor(method.ContainingType))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        MissingParameterlessCtorError,
                        method.ContainingType.Locations.FirstOrDefault() ?? Location.None,
                        method.ContainingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
                    continue;
                }

                var varName = $"listener_{method.ContainingType.Name}";
                if (listenerInstances.Add(containingTypeFullName))
                {
                    eventsBuilder.AppendLine($"        var {varName} = new {containingTypeFullName}();");
                }
                methodTarget = $"{varName}.{method.MethodName}";
            }

            methodTarget += $"({string.Join(", ", method.Arguments)});";

            eventsBuilder.AppendLine($$"""
                    registrar.Register<{{method.EventTypeFullName}}>(
                        (evt) => {{{methodTarget}}}, 
                        (global::Pumpkin.DotNet.Api.Events.EventPriority){{method.Priority}},
                        {{(method.IsModifying ? "true" : "false")}}
                    );
            """);
        }

        var commandsBuilder = new StringBuilder();
        var commandInstances = new Dictionary<string, string>();

        for (int cIdx = 0; cIdx < commands.Length; cIdx++)
        {
            var cmd = commands[cIdx];
            foreach (var diag in cmd.Diagnostics)
            {
                context.ReportDiagnostic(Diagnostic.Create(diag.Descriptor, diag.Location, diag.Args));
            }

            var typeFullName = cmd.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            bool isPluginSelf = SymbolEqualityComparer.Default.Equals(cmd.ContainingType, pluginSymbol);
            bool hasInstanceMethods = cmd.Methods.Any(m => !m.IsStatic || m.BrigadierArgs.Any(a => a.Suggest is { IsStatic: false }));

            string targetPrefix;
            if (cmd.ContainingType.IsStatic)
            {
                targetPrefix = typeFullName;
            }
            else if (isPluginSelf)
            {
                targetPrefix = "plugin";
            }
            else if (hasInstanceMethods)
            {
                if (!HasAccessibleParameterlessConstructor(cmd.ContainingType))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        MissingParameterlessCtorError,
                        cmd.ContainingType.Locations.FirstOrDefault() ?? Location.None,
                        cmd.ContainingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
                    continue;
                }

                if (!commandInstances.TryGetValue(typeFullName, out var instanceVar))
                {
                    instanceVar = $"cmdHandler_{cIdx}";
                    commandInstances[typeFullName] = instanceVar;
                    commandsBuilder.AppendLine($"        var {instanceVar} = new {typeFullName}();");
                }
                targetPrefix = instanceVar;
            }
            else
            {
                targetPrefix = typeFullName;
            }

            string rootVar = $"cmd_{cIdx}";
            var allNames = new List<string> { $"\"{EscapeString(cmd.Name)}\"" };
            allNames.AddRange(cmd.Aliases.Select(a => $"\"{EscapeString(a)}\""));

            commandsBuilder.AppendLine($"        var {rootVar} = Command.Create({string.Join(", ", allNames)});");

            if (!string.IsNullOrEmpty(cmd.Description))
                commandsBuilder.AppendLine($"        {rootVar}.WithDescription(\"{EscapeString(cmd.Description)}\");");

            if (!string.IsNullOrEmpty(cmd.Permission))
                commandsBuilder.AppendLine($"        {rootVar}.WithPermission(\"{EscapeString(cmd.Permission)}\");");

            if (cmd.PermissionLevel > 0)
                commandsBuilder.AppendLine($"        {rootVar}.Requires((global::Pumpkin.DotNet.Api.Permissions.PermissionLevel){cmd.PermissionLevel});");

            var createdLiterals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int mIdx = 0; mIdx < cmd.Methods.Count; mIdx++)
            {
                var m = cmd.Methods[mIdx];
                string callOwner = m.IsStatic ? typeFullName : targetPrefix;
                string parentVar = rootVar;

                if (!string.IsNullOrEmpty(m.SubcommandName))
                {
                    string litVar = $"lit_{cIdx}_{SanitizeIdentifier(m.SubcommandName!)}";
                    if (createdLiterals.Add(m.SubcommandName!))
                    {
                        commandsBuilder.AppendLine($"        var {litVar} = Command.Literal(\"{EscapeString(m.SubcommandName!)}\");");
                        if (m.SubcommandPermLevel > 0)
                            commandsBuilder.AppendLine($"        {litVar}.Requires((global::Pumpkin.DotNet.Api.Permissions.PermissionLevel){m.SubcommandPermLevel});");
                        commandsBuilder.AppendLine($"        {rootVar}.Then({litVar});");
                    }
                    parentVar = litVar;
                }

                if (m.BrigadierArgs.Count == 0)
                {
                    AppendCommandExecutes(commandsBuilder, parentVar, callOwner, m, passedArgCount: 0);
                    continue;
                }

                string prevNodeVar = parentVar;
                for (int pIdx = 0; pIdx < m.BrigadierArgs.Count; pIdx++)
                {
                    var p = m.BrigadierArgs[pIdx];
                    string argVar = $"arg_{cIdx}_{mIdx}_{pIdx}";

                    commandsBuilder.AppendLine($"        var {argVar} = Command.Argument<{p.TypeFullName}>(\"{EscapeString(p.ArgName)}\");");

                    if (!double.IsNaN(p.Min) || !double.IsNaN(p.Max) || p.StringMode != 0)
                    {
                        string minStr = double.IsNaN(p.Min) ? "double.NaN" : p.Min.ToString(CultureInfo.InvariantCulture);
                        string maxStr = double.IsNaN(p.Max) ? "double.NaN" : p.Max.ToString(CultureInfo.InvariantCulture);
                        commandsBuilder.AppendLine($"        {argVar}.WithConstraints({minStr}, {maxStr}, (global::Pumpkin.DotNet.Api.Commands.StringArgMode){p.StringMode});");
                    }

                    if (p.Suggest is not null)
                    {
                        string sugOwner = p.Suggest.IsStatic ? typeFullName : targetPrefix;
                        string sugCall = $"{sugOwner}.{p.Suggest.MethodName}({string.Join(", ", p.Suggest.Arguments)})";
                        commandsBuilder.AppendLine($"        {argVar}.Suggests((ctx, input) => {sugCall});");
                    }

                    if (pIdx == 0 && p.IsOptional)
                    {
                        AppendCommandExecutes(commandsBuilder, prevNodeVar, callOwner, m, passedArgCount: 0);
                    }

                    bool isLast = pIdx == m.BrigadierArgs.Count - 1;
                    bool nextIsOptional = !isLast && m.BrigadierArgs[pIdx + 1].IsOptional;

                    if (isLast || nextIsOptional)
                    {
                        AppendCommandExecutes(commandsBuilder, argVar, callOwner, m, passedArgCount: pIdx + 1);
                    }

                    commandsBuilder.AppendLine($"        {prevNodeVar}.Then({argVar});");
                    prevNodeVar = argVar;
                }
            }

            commandsBuilder.AppendLine($"        registrar.Register({rootVar});");
            commandsBuilder.AppendLine();
        }

        var source = $$"""
            // <auto-generated />
            #nullable enable

            using System;
            using System.Collections.Generic;
            using System.Globalization;
            using System.Runtime.InteropServices;
            using Pumpkin.DotNet.Api.Plugins;
            using Pumpkin.DotNet.Api.Events;
            using Pumpkin.DotNet.Api.Commands;
            using Pumpkin.DotNet.Runtime.Configuration;

            public static class PumpkinPluginEntryPoint
            {
                [UnmanagedCallersOnly(EntryPoint = "init-plugin")]
                public static void Initialize()
                {
                    Pumpkin.DotNet.Runtime.PumpkinRuntime.Instance.RegisterPlugin(
                        new {{plugin.TypeName}}(),
                        new PluginMetadata(
                            "{{safeName}}",
                            "{{safeVersion}}",
                            new string[] { {{authorsLiteral}} },
                            "{{safeDescription}}",
                            new string[] { {{permissionsLiteral}} }
                        ),
                        BindEvents,
                        BindCommands,
                        BindConfigs
                    );
                }

                private static void BindConfigs(IConfigBinderRegistrar registrar)
                {
            {{configRegistrarBuilder}}
                }
            {{configMethodsBuilder}}

                private static void BindEvents(PumpkinPlugin rawPlugin, IEventRegistrar registrar)
                {
                    var plugin = ({{plugin.TypeName}})rawPlugin;
            {{eventsBuilder}}
                }

                private static void BindCommands(PumpkinPlugin rawPlugin, ICommandManager registrar)
                {
                    var plugin = ({{plugin.TypeName}})rawPlugin;
            {{commandsBuilder}}
                }
            }
            """;

        context.AddSource(
            "PumpkinPluginEntryPoint.g.cs",
            SourceText.From(source, Encoding.UTF8));
    }

    private static void EmitBindProperties(
        StringBuilder sb,
        ITypeSymbol type,
        string targetExpr,
        string prefix,
        string indent,
        HashSet<ITypeSymbol> visited,
        ref int varIndex)
    {
        foreach (var prop in GetWritableProperties(type))
        {
            var key = ToSnakeCase(prop.Name);
            var fullKey = string.IsNullOrEmpty(prefix) ? key : $"{prefix}:{key}";
            var propExpr = $"{targetExpr}.{prop.Name}";
            var propType = UnwrapNullable(prop.Type);

            if (IsScalarType(propType))
            {
                EmitScalarBind(sb, propType, propExpr, fullKey, indent, ref varIndex);
            }
            else if (TryGetSequenceElementType(propType, out var elementType))
            {
                var unwrappedElem = UnwrapNullable(elementType!);
                if (IsScalarType(unwrappedElem))
                {
                    EmitScalarListBind(sb, propType, unwrappedElem, propExpr, fullKey, indent, ref varIndex);
                }
                else if (IsComplexObjectType(unwrappedElem) && visited.Add(unwrappedElem))
                {
                    EmitObjectListBind(sb, propType, unwrappedElem, propExpr, fullKey, indent, visited, ref varIndex);
                    visited.Remove(unwrappedElem);
                }
            }
            else if (IsComplexObjectType(propType) && visited.Add(propType))
            {
                var nestedTypeName = propType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (!propType.IsValueType)
                    sb.AppendLine($"{indent}{propExpr} ??= new {nestedTypeName}();");

                EmitBindProperties(sb, propType, propExpr, fullKey, indent, visited, ref varIndex);
                visited.Remove(propType);
            }
        }
    }

    private static void EmitScalarBind(
        StringBuilder sb,
        ITypeSymbol propType,
        string propExpr,
        string key,
        string indent,
        ref int idx)
    {
        var rawVar = $"v{idx++}";
        var parsedVar = $"p{idx++}";
        var escapedKey = EscapeString(key);

        if (propType.SpecialType == SpecialType.System_String)
        {
            sb.AppendLine($"{indent}if (values.TryGetValue(\"{escapedKey}\", out var {rawVar}) && {rawVar} is not null)");
            sb.AppendLine($"{indent}    {propExpr} = {rawVar};");
            return;
        }

        var parseExpr = BuildScalarTryParseExpression(propType, rawVar, parsedVar);
        if (parseExpr is not null)
        {
            sb.AppendLine($"{indent}if (values.TryGetValue(\"{escapedKey}\", out var {rawVar}) && {parseExpr})");
            sb.AppendLine($"{indent}    {propExpr} = {parsedVar};");
        }
    }

    private static void EmitScalarListBind(
        StringBuilder sb,
        ITypeSymbol listType,
        ITypeSymbol elementType,
        string propExpr,
        string keyPrefix,
        string indent,
        ref int idx)
    {
        var listVar = $"list{idx++}";
        var iVar = $"i{idx++}";
        var rawVar = $"raw{idx++}";
        var itemVar = $"item{idx++}";
        var elemFullName = elementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var escapedPrefix = EscapeString(keyPrefix);

        sb.AppendLine($"{indent}if (values.ContainsKey(\"{escapedPrefix}:0\"))");
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{indent}    var {listVar} = new List<{elemFullName}>();");
        sb.AppendLine($"{indent}    for (int {iVar} = 0; values.TryGetValue($\"{escapedPrefix}:{{{iVar}}}\", out var {rawVar}); {iVar}++)");
        sb.AppendLine($"{indent}    {{");

        if (elementType.SpecialType == SpecialType.System_String)
        {
            sb.AppendLine($"{indent}        if ({rawVar} is not null) {listVar}.Add({rawVar});");
        }
        else
        {
            var parseExpr = BuildScalarTryParseExpression(elementType, rawVar, itemVar);
            if (parseExpr is not null)
                sb.AppendLine($"{indent}        if ({parseExpr}) {listVar}.Add({itemVar});");
        }

        sb.AppendLine($"{indent}    }}");
        sb.AppendLine($"{indent}    {propExpr} = {listVar}{(listType.TypeKind == TypeKind.Array ? ".ToArray()" : "")};");
        sb.AppendLine($"{indent}}}");
    }

    private static void EmitObjectListBind(
        StringBuilder sb,
        ITypeSymbol listType,
        ITypeSymbol elementType,
        string propExpr,
        string keyPrefix,
        string indent,
        HashSet<ITypeSymbol> visited,
        ref int idx)
    {
        var listVar = $"objList{idx++}";
        var iVar = $"idx{idx++}";
        var itemPrefixVar = $"pfx{idx++}";
        var itemVar = $"obj{idx++}";
        var elemFullName = elementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var escapedPrefix = EscapeString(keyPrefix);

        var firstProp = GetWritableProperties(elementType).FirstOrDefault();
        if (firstProp is null)
            return;

        var firstKey = ToSnakeCase(firstProp.Name);

        sb.AppendLine($"{indent}if (values.ContainsKey(\"{escapedPrefix}:0:{firstKey}\"))");
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{indent}    var {listVar} = new List<{elemFullName}>();");
        sb.AppendLine($"{indent}    for (int {iVar} = 0; ; {iVar}++)");
        sb.AppendLine($"{indent}    {{");
        sb.AppendLine($"{indent}        var {itemPrefixVar} = $\"{escapedPrefix}:{{{iVar}}}\";");
        sb.AppendLine($"{indent}        bool hasAny = false;");
        sb.AppendLine($"{indent}        foreach (var k in values.Keys)");
        sb.AppendLine($"{indent}        {{");
        sb.AppendLine($"{indent}            if (k.StartsWith({itemPrefixVar} + \":\", StringComparison.OrdinalIgnoreCase)) {{ hasAny = true; break; }}");
        sb.AppendLine($"{indent}        }}");
        sb.AppendLine($"{indent}        if (!hasAny) break;");
        sb.AppendLine($"{indent}        var {itemVar} = new {elemFullName}();");

        EmitDynamicPrefixBindProperties(sb, elementType, itemVar, itemPrefixVar, indent + "        ", visited, ref idx);

        sb.AppendLine($"{indent}        {listVar}.Add({itemVar});");
        sb.AppendLine($"{indent}    }}");
        sb.AppendLine($"{indent}    {propExpr} = {listVar}{(listType.TypeKind == TypeKind.Array ? ".ToArray()" : "")};");
        sb.AppendLine($"{indent}}}");
    }

    private static void EmitDynamicPrefixBindProperties(
        StringBuilder sb,
        ITypeSymbol type,
        string targetExpr,
        string prefixVarExpr,
        string indent,
        HashSet<ITypeSymbol> visited,
        ref int idx)
    {
        foreach (var prop in GetWritableProperties(type))
        {
            var key = ToSnakeCase(prop.Name);
            var propExpr = $"{targetExpr}.{prop.Name}";
            var propType = UnwrapNullable(prop.Type);

            if (IsScalarType(propType))
            {
                var rawVar = $"v{idx++}";
                var parsedVar = $"p{idx++}";
                if (propType.SpecialType == SpecialType.System_String)
                {
                    sb.AppendLine($"{indent}if (values.TryGetValue($\"{{{prefixVarExpr}}}:{key}\", out var {rawVar}) && {rawVar} is not null)");
                    sb.AppendLine($"{indent}    {propExpr} = {rawVar};");
                }
                else
                {
                    var parseExpr = BuildScalarTryParseExpression(propType, rawVar, parsedVar);
                    if (parseExpr is not null)
                    {
                        sb.AppendLine($"{indent}if (values.TryGetValue($\"{{{prefixVarExpr}}}:{key}\", out var {rawVar}) && {parseExpr})");
                        sb.AppendLine($"{indent}    {propExpr} = {parsedVar};");
                    }
                }
            }
            else if (IsComplexObjectType(propType) && visited.Add(propType))
            {
                var nestedTypeName = propType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var subPrefixVar = $"subPfx{idx++}";
                if (!propType.IsValueType)
                    sb.AppendLine($"{indent}{propExpr} ??= new {nestedTypeName}();");

                sb.AppendLine($"{indent}var {subPrefixVar} = $\"{{{prefixVarExpr}}}:{key}\";");
                EmitDynamicPrefixBindProperties(sb, propType, propExpr, subPrefixVar, indent, visited, ref idx);
                visited.Remove(propType);
            }
        }
    }

    private static string? BuildScalarTryParseExpression(ITypeSymbol type, string rawVar, string outVar)
    {
        var fullName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        if (type.SpecialType == SpecialType.System_Boolean)
            return $"bool.TryParse({rawVar}, out var {outVar})";

        if (type.SpecialType is
            SpecialType.System_Byte or SpecialType.System_SByte or
            SpecialType.System_Int16 or SpecialType.System_UInt16 or
            SpecialType.System_Int32 or SpecialType.System_UInt32 or
            SpecialType.System_Int64 or SpecialType.System_UInt64 or
            SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal)
        {
            return $"{fullName}.TryParse({rawVar}, NumberStyles.Any, CultureInfo.InvariantCulture, out var {outVar})";
        }

        if (type.TypeKind == TypeKind.Enum)
            return $"Enum.TryParse<{fullName}>({rawVar}, ignoreCase: true, out var {outVar})";

        var metadataName = type.ToDisplayString();
        if (metadataName is "System.Guid" or "System.TimeSpan" or "System.DateTime" or "System.DateTimeOffset" or "System.DateOnly" or "System.TimeOnly")
            return $"{fullName}.TryParse({rawVar}, CultureInfo.InvariantCulture, out var {outVar})";

        if (ImplementsIParsable(type))
            return $"{fullName}.TryParse({rawVar}, CultureInfo.InvariantCulture, out var {outVar})";

        return null;
    }

    private static void EmitSerializeProperties(
        StringBuilder sb,
        ITypeSymbol type,
        string sourceExpr,
        string keyPrefix,
        bool isDynamicPrefix,
        string indent,
        HashSet<ITypeSymbol> visited,
        ref int idx)
    {
        foreach (var prop in GetWritableProperties(type))
        {
            var propType = UnwrapNullable(prop.Type);
            var key = ToSnakeCase(prop.Name);
            var fullKeyExpr = BuildKeyExpression(keyPrefix, key, isDynamicPrefix);
            var valExpr = $"{sourceExpr}.{prop.Name}";

            if (IsScalarType(propType))
            {
                EmitScalarSerialize(sb, prop.Type, propType, fullKeyExpr, valExpr, indent);
            }
            else if (TryGetSequenceElementType(propType, out var elemType))
            {
                var unwrappedElem = UnwrapNullable(elemType!);
                if (IsScalarType(unwrappedElem))
                    EmitScalarListSerialize(sb, unwrappedElem, fullKeyExpr, valExpr, indent, ref idx);
                else if (IsComplexObjectType(unwrappedElem) && visited.Add(unwrappedElem))
                {
                    EmitObjectListSerialize(sb, unwrappedElem, fullKeyExpr, valExpr, indent, visited, ref idx);
                    visited.Remove(unwrappedElem);
                }
            }
            else if (IsComplexObjectType(propType) && visited.Add(propType))
            {
                if (!prop.Type.IsValueType)
                {
                    sb.AppendLine($"{indent}if ({valExpr} is not null)");
                    sb.AppendLine($"{indent}{{");
                    EmitSerializeProperties(sb, propType, valExpr, fullKeyExpr, isDynamicPrefix, indent + "    ", visited, ref idx);
                    sb.AppendLine($"{indent}}}");
                }
                else
                {
                    EmitSerializeProperties(sb, propType, valExpr, fullKeyExpr, isDynamicPrefix, indent, visited, ref idx);
                }

                visited.Remove(propType);
            }
        }
    }

    private static string BuildKeyExpression(string prefix, string key, bool isDynamicPrefix)
    {
        if (string.IsNullOrEmpty(prefix))
            return $"\"{EscapeString(key)}\"";

        if (isDynamicPrefix)
            return $"$\"{{{prefix}}}:{EscapeString(key)}\"";

        var rawPrefix = prefix.Trim('"');
        return $"\"{rawPrefix}:{EscapeString(key)}\"";
    }

    private static void EmitScalarSerialize(
        StringBuilder sb,
        ITypeSymbol originalType,
        ITypeSymbol unwrappedType,
        string keyExpr,
        string valExpr,
        string indent)
    {
        var isNullable = originalType.NullableAnnotation == NullableAnnotation.Annotated ||
                         originalType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;

        if (isNullable)
        {
            sb.AppendLine($"{indent}if ({valExpr} is not null)");
            indent += "    ";
            if (originalType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
                valExpr += ".Value";
        }

        var formatted = FormatScalarValue(unwrappedType, valExpr);
        sb.AppendLine($"{indent}dict[{keyExpr}] = {formatted};");
    }

    private static void EmitScalarListSerialize(
        StringBuilder sb,
        ITypeSymbol elementType,
        string keyExpr,
        string valExpr,
        string indent,
        ref int idx)
    {
        var listIdxVar = $"i{idx++}";
        var itemVar = $"item{idx++}";
        var formattedItem = FormatScalarValue(elementType, itemVar);
        var rawKey = keyExpr.Trim('"');

        sb.AppendLine($"{indent}if ({valExpr} is not null)");
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{indent}    int {listIdxVar} = 0;");
        sb.AppendLine($"{indent}    foreach (var {itemVar} in {valExpr})");
        sb.AppendLine($"{indent}    {{");
        if (!elementType.IsValueType)
            sb.AppendLine($"{indent}        if ({itemVar} is null) continue;");

        if (keyExpr.StartsWith("$\""))
            sb.AppendLine($"{indent}        dict[$\"{{{keyExpr}}}:{{{listIdxVar}++}}\"] = {formattedItem};");
        else
            sb.AppendLine($"{indent}        dict[$\"{rawKey}:{{{listIdxVar}++}}\"] = {formattedItem};");

        sb.AppendLine($"{indent}    }}");
        sb.AppendLine($"{indent}}}");
    }

    private static void EmitObjectListSerialize(
        StringBuilder sb,
        ITypeSymbol elementType,
        string keyExpr,
        string valExpr,
        string indent,
        HashSet<ITypeSymbol> visited,
        ref int idx)
    {
        var listIdxVar = $"i{idx++}";
        var itemVar = $"elem{idx++}";
        var itemPrefixVar = $"elemPfx{idx++}";
        var rawKey = keyExpr.Trim('"');

        sb.AppendLine($"{indent}if ({valExpr} is not null)");
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{indent}    int {listIdxVar} = 0;");
        sb.AppendLine($"{indent}    foreach (var {itemVar} in {valExpr})");
        sb.AppendLine($"{indent}    {{");
        if (!elementType.IsValueType)
            sb.AppendLine($"{indent}        if ({itemVar} is null) continue;");

        if (keyExpr.StartsWith("$\""))
            sb.AppendLine($"{indent}        var {itemPrefixVar} = $\"{{{keyExpr}}}:{{{listIdxVar}++}}\";");
        else
            sb.AppendLine($"{indent}        var {itemPrefixVar} = $\"{rawKey}:{{{listIdxVar}++}}\";");

        EmitSerializeProperties(sb, elementType, itemVar, itemPrefixVar, isDynamicPrefix: true, indent + "        ", visited, ref idx);
        sb.AppendLine($"{indent}    }}");
        sb.AppendLine($"{indent}}}");
    }

    private static string FormatScalarValue(ITypeSymbol type, string expr)
    {
        if (type.SpecialType == SpecialType.System_String)
            return expr;

        if (type.SpecialType == SpecialType.System_Boolean)
            return $"({expr} ? \"true\" : \"false\")";

        if (type.SpecialType is
            SpecialType.System_Byte or SpecialType.System_SByte or
            SpecialType.System_Int16 or SpecialType.System_UInt16 or
            SpecialType.System_Int32 or SpecialType.System_UInt32 or
            SpecialType.System_Int64 or SpecialType.System_UInt64 or
            SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal)
        {
            return $"Convert.ToString({expr}, CultureInfo.InvariantCulture)";
        }

        if (type.ToDisplayString() is "System.Guid" or "System.TimeSpan" or "System.DateTime" or "System.DateTimeOffset" or "System.DateOnly" or "System.TimeOnly")
            return $"{expr}.ToString(null, CultureInfo.InvariantCulture)";

        return $"{expr}.ToString()";
    }

    private static IEnumerable<IPropertySymbol> GetWritableProperties(ITypeSymbol type) =>
        type.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => !p.IsStatic &&
                        p.DeclaredAccessibility == Accessibility.Public &&
                        p.GetMethod is not null &&
                        p.SetMethod is not null &&
                        p.SetMethod.DeclaredAccessibility == Accessibility.Public &&
                        !p.IsIndexer);

    private static ITypeSymbol UnwrapNullable(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            return nullable.TypeArguments[0];

        return type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
    }

    private static bool IsScalarType(ITypeSymbol t)
    {
        if (t.SpecialType is
            SpecialType.System_String or
            SpecialType.System_Boolean or
            SpecialType.System_Byte or SpecialType.System_SByte or
            SpecialType.System_Int16 or SpecialType.System_UInt16 or
            SpecialType.System_Int32 or SpecialType.System_UInt32 or
            SpecialType.System_Int64 or SpecialType.System_UInt64 or
            SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal)
        {
            return true;
        }

        if (t.TypeKind == TypeKind.Enum)
            return true;

        var fullName = t.ToDisplayString();
        if (fullName is "System.Guid" or "System.TimeSpan" or "System.DateTime" or "System.DateTimeOffset" or "System.DateOnly" or "System.TimeOnly")
            return true;

        return ImplementsIParsable(t);
    }

    private static bool ImplementsIParsable(ITypeSymbol type)
    {
        foreach (var iface in type.AllInterfaces)
        {
            if (iface.IsGenericType &&
                iface.OriginalDefinition.ToDisplayString() == "System.IParsable<TSelf>")
            {
                return true;
            }
        }
        return false;
    }

    private static bool TryGetSequenceElementType(ITypeSymbol type, out ITypeSymbol? elementType)
    {
        if (type.SpecialType == SpecialType.System_String)
        {
            elementType = null;
            return false;
        }

        if (type is IArrayTypeSymbol arrayType)
        {
            elementType = arrayType.ElementType;
            return true;
        }

        if (type is INamedTypeSymbol { IsGenericType: true } named)
        {
            var orig = named.OriginalDefinition.ToDisplayString();
            if (orig is
                "System.Collections.Generic.List<T>" or
                "System.Collections.Generic.IList<T>" or
                "System.Collections.Generic.ICollection<T>" or
                "System.Collections.Generic.IReadOnlyList<T>" or
                "System.Collections.Generic.IReadOnlyCollection<T>" or
                "System.Collections.Generic.IEnumerable<T>")
            {
                elementType = named.TypeArguments[0];
                return true;
            }
        }

        elementType = null;
        return false;
    }

    private static bool IsComplexObjectType(ITypeSymbol type)
    {
        if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct))
            return false;

        if (type.IsAbstract || type.SpecialType != SpecialType.None)
            return false;

        if (type is INamedTypeSymbol named)
        {
            return named.InstanceConstructors.Any(c =>
                c.Parameters.Length == 0 &&
                c.DeclaredAccessibility == Accessibility.Public);
        }

        return false;
    }

    private static string ToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        var sb = new StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (c == '_')
            {
                if (sb.Length > 0 && sb[sb.Length - 1] != '_')
                    sb.Append('_');
                continue;
            }

            if (char.IsUpper(c))
            {
                if (i > 0 && sb.Length > 0 && sb[sb.Length - 1] != '_')
                    sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private static void AppendCommandExecutes(
        StringBuilder sb,
        string nodeVar,
        string callOwner,
        CommandExecuteMethodModel m,
        int passedArgCount)
    {
        var callArgs = new List<string>(m.CallSlots.Count);
        foreach (var slot in m.CallSlots)
        {
            if (!slot.IsBrigadierArg)
            {
                callArgs.Add(slot.InjectedExpr!);
            }
            else
            {
                var arg = m.BrigadierArgs[slot.BrigadierArgIndex];
                if (slot.BrigadierArgIndex < passedArgCount)
                    callArgs.Add($"ctx.Get<{arg.TypeFullName}>(\"{EscapeString(arg.ArgName)}\")");
                else
                    callArgs.Add(arg.DefaultValueExpr);
            }
        }

        string invocation = $"{callOwner}.{m.MethodName}({string.Join(", ", callArgs)})";
        if (m.ReturnsInt)
            sb.AppendLine($"        {nodeVar}.Executes(ctx => {invocation});");
        else
            sb.AppendLine($"        {nodeVar}.Executes(ctx => {{ {invocation}; }});");
    }

    private static T? GetNamedArg<T>(AttributeData attr, string name)
    {
        foreach (var kv in attr.NamedArguments)
            if (kv.Key == name && kv.Value.Value is T val)
                return val;
        return default;
    }

    private static T GetNamedArgOrDefault<T>(AttributeData attr, string name, T defaultValue)
    {
        foreach (var kv in attr.NamedArguments)
            if (kv.Key == name && kv.Value.Value is T val)
                return val;
        return defaultValue;
    }

    private static string FormatLiteral(object? value) => value switch
    {
        null => "default!",
        string s => $"\"{EscapeString(s)}\"",
        bool b => b ? "true" : "false",
        float f => f.ToString(CultureInfo.InvariantCulture) + "f",
        double d => d.ToString(CultureInfo.InvariantCulture) + "d",
        long l => l + "L",
        _ => value.ToString() ?? "default!"
    };

    private static string EscapeString(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", " ");

    private static string SanitizeIdentifier(string s) =>
        new string(s.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

    private sealed class DiagnosticInfo(DiagnosticDescriptor descriptor, Location location, object?[] args)
    {
        public DiagnosticDescriptor Descriptor { get; } = descriptor;
        public Location Location { get; } = location;
        public object?[] Args { get; } = args;
    }

    private sealed class PluginInfo(
        string typeName,
        string name,
        string version,
        string[] authors,
        string description,
        string[] permissions)
    {
        public string TypeName { get; } = typeName;
        public string Name { get; } = name;
        public string Version { get; } = version;
        public string[] Authors { get; } = authors;
        public string Description { get; } = description;
        public string[] Permissions { get; } = permissions;
    }

    private sealed class EventMethodModel(
        INamedTypeSymbol containingType,
        string methodName,
        string[] arguments,
        string eventTypeFullName,
        string returnTypeDisplay,
        bool returnsVoid,
        bool isStatic,
        int priority,
        bool isModifying,
        bool isAccessible,
        Location location)
    {
        public INamedTypeSymbol ContainingType = containingType;
        public string MethodName = methodName;
        public string[] Arguments = arguments;
        public string EventTypeFullName = eventTypeFullName;
        public string ReturnTypeDisplay = returnTypeDisplay;
        public bool ReturnsVoid = returnsVoid;
        public bool IsStatic = isStatic;
        public int Priority = priority;
        public bool IsModifying = isModifying;
        public bool IsAccessible = isAccessible;
        public Location Location = location;
    }

    private sealed class CommandClassModel(
        INamedTypeSymbol containingType,
        string name,
        List<string> aliases,
        string description,
        string permission,
        int permissionLevel,
        List<CommandExecuteMethodModel> methods,
        List<DiagnosticInfo> diagnostics)
    {
        public INamedTypeSymbol ContainingType = containingType;
        public string Name = name;
        public List<string> Aliases = aliases;
        public string Description = description;
        public string Permission = permission;
        public int PermissionLevel = permissionLevel;
        public List<CommandExecuteMethodModel> Methods = methods;
        public List<DiagnosticInfo> Diagnostics = diagnostics;
    }

    private sealed class ExecuteParamSlot(bool isBrigadierArg, string? injectedExpr, int brigadierArgIndex)
    {
        public bool IsBrigadierArg = isBrigadierArg;
        public string? InjectedExpr = injectedExpr;
        public int BrigadierArgIndex = brigadierArgIndex;
    }

    private sealed class CommandExecuteMethodModel(
        string methodName,
        bool isStatic,
        bool returnsInt,
        string? subcommandName,
        int subcommandPermLevel,
        List<ExecuteParamSlot> callSlots,
        List<CommandParamModel> brigadierArgs)
    {
        public string MethodName = methodName;
        public bool IsStatic = isStatic;
        public bool ReturnsInt = returnsInt;
        public string? SubcommandName = subcommandName;
        public int SubcommandPermLevel = subcommandPermLevel;
        public List<ExecuteParamSlot> CallSlots = callSlots;
        public List<CommandParamModel> BrigadierArgs = brigadierArgs;
    }

    private sealed class SuggestMethodModel(string methodName, bool isStatic, List<string> arguments)
    {
        public string MethodName = methodName;
        public bool IsStatic = isStatic;
        public List<string> Arguments = arguments;
    }

    private sealed class CommandParamModel(
        string argName,
        string typeFullName,
        bool isOptional,
        string defaultValueExpr,
        double min,
        double max,
        int stringMode,
        SuggestMethodModel? suggest)
    {
        public string ArgName = argName;
        public string TypeFullName = typeFullName;
        public bool IsOptional = isOptional;
        public string DefaultValueExpr = defaultValueExpr;
        public double Min = min;
        public double Max = max;
        public int StringMode = stringMode;
        public SuggestMethodModel? Suggest = suggest;
    }
}