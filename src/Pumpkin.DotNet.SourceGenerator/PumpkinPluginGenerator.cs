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

        var compilationAndPlugins = pluginsProvider
            .Collect()
            .Select(static (plugins, _) => plugins
                .OfType<INamedTypeSymbol>()
                .Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default)
                .ToImmutableArray())
            .Combine(optionsProvider)
            .Combine(context.CompilationProvider)
            .Combine(eventMethods.Collect())
            .Combine(commandClasses.Collect());

        context.RegisterSourceOutput(compilationAndPlugins, static (spc, source) =>
        {
            var ((((plugins, options), compilation), methods), commands) = source;
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
            Generate(spc, plugins[0]!, info, methods!, commands!);
        });
    }

    private static CommandClassModel? GetCommandClassInfo(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol classSymbol)
            return null;

        // Allow static classes (Roslyn reports IsAbstract == true and IsStatic == true for them).
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

                // 1. Check whether the parameter is an injectable service.
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

                // 2. Otherwise, treat it as a Brigadier command argument.
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
            {
                args.Add("ctx");
            }
            else if (typeFull == ServerTypeName)
            {
                args.Add("plugin.Server");
            }
            else if (typeFull == LoggerTypeName)
            {
                args.Add("plugin.Logger");
            }
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

        var eventType = TryGetEventType(method.Parameters, eventBaseInterface);
        if (eventType is null)
            return null;

        List<string> arguments = new(method.Parameters.Length);

        foreach (var param in method.Parameters)
        {
            var paramType = param.Type;

            if (SymbolEqualityComparer.Default.Equals(paramType, eventType))
                arguments.Add("evt");
            else if (SymbolEqualityComparer.Default.Equals(paramType, serverInterface))
                arguments.Add("plugin.Server");
            else if (SymbolEqualityComparer.Default.Equals(paramType, loggerInterface))
                arguments.Add("plugin.Logger");
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
            description: description ?? string.Empty
        );
    }

    private static void Generate(
        SourceProductionContext context,
        INamedTypeSymbol pluginSymbol,
        PluginInfo plugin,
        ImmutableArray<EventMethodModel> methods,
        ImmutableArray<CommandClassModel> commands)
    {
        var safeVersion = EscapeString(plugin.Version);
        var safeDescription = EscapeString(plugin.Description);
        var safeName = EscapeString(plugin.Name);
        var authorsLiteral = string.Join(", ", plugin.Authors.Select(a => $"\"{EscapeString(a)}\""));

        // --- 1. Generate BindEvents ---
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

        // --- 2. Generate BindCommands ---
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
            using System.Runtime.InteropServices;
            using Pumpkin.DotNet.Api.Plugins;
            using Pumpkin.DotNet.Api.Events;
            using Pumpkin.DotNet.Api.Commands;

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
                            "{{safeDescription}}"
                        ),
                        BindEvents,
                        BindCommands
                    );
                }

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

    private sealed class PluginInfo(string typeName, string name, string version, string[] authors, string description)
    {
        public string TypeName { get; } = typeName;
        public string Name { get; } = name;
        public string Version { get; } = version;
        public string[] Authors { get; } = authors;
        public string Description { get; } = description;
    }

    private sealed class EventMethodModel(
        INamedTypeSymbol containingType,
        string methodName,
        string[] arguments,
        string eventTypeFullName,
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