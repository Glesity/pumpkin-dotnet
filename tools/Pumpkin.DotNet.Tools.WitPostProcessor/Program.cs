using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pumpkin.DotNet.Tools.WitPostProcessor;

internal static class Program
{
    public const string MarkerToolName = "WitPostProcessor";
    public const string MarkerToolVersion = "1.0.0";
    private const string MarkerCommentPrefix = "// WitPostProcessor:";

    private static int Main(string[] args)
    {
        bool directoryMode = args.Length == 2 && args[0] == "--directory";
        string? directoryPath = null;

        if (directoryMode)
        {
            directoryPath = Path.GetFullPath(args[1]);
            if (!Directory.Exists(directoryPath))
            {
                Console.Error.WriteLine($"[WitPostProcessor] Directory not found: {directoryPath}");
                return 1;
            }
        }
        else if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: WitPostProcessor <input.cs> <output.cs> | --directory <dir>");
            return 1;
        }

        string? inputPath = directoryMode ? null : Path.GetFullPath(args[0]);
        string? outputPath = directoryMode ? null : Path.GetFullPath(args[1]);

        if (!directoryMode && !File.Exists(inputPath))
        {
            Console.Error.WriteLine($"[WitPostProcessor] Input file not found: {inputPath}");
            return 1;
        }

        int exitCode = 0;

        var thread = new Thread(() =>
        {
            try
            {
                exitCode = directoryMode
                    ? ProcessDirectory(directoryPath!)
                    : ProcessFile(inputPath!, outputPath!);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[WitPostProcessor] Critical error: {ex}");
                exitCode = 1;
            }
        }, 64 * 1024 * 1024);

        thread.Start();
        thread.Join();

        return exitCode;
    }

    private static int ProcessDirectory(string directoryPath)
    {
        string[] files = Directory.EnumerateFiles(directoryPath, "*.cs", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        int exitCode = 0;
        object gate = new();
        Parallel.ForEach(
            files,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Min(4, Math.Max(1, Environment.ProcessorCount))
            },
            (filePath, state) =>
            {
                if (Volatile.Read(ref exitCode) != 0)
                {
                    state.Stop();
                    return;
                }

                if (ProcessFile(filePath, filePath) != 0)
                {
                    lock (gate)
                    {
                        exitCode = 1;
                    }

                    state.Stop();
                }
            });

        return exitCode;
    }

    private static int ProcessFile(string inputPath, string outputPath)
    {
        string sourceText = File.ReadAllText(inputPath, Encoding.UTF8);

        // Quickly check for the marker without building the AST.
        if (HasCurrentMarkerFast(sourceText))
        {
            if (!string.Equals(inputPath, outputPath, StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(inputPath, outputPath, overwrite: true);
            }

            return 0;
        }

        bool hasPreviousMarker = sourceText.Contains(MarkerCommentPrefix, StringComparison.Ordinal) ||
                                 (sourceText.Contains("GeneratedCode", StringComparison.Ordinal) &&
                                  sourceText.Contains(MarkerToolName, StringComparison.Ordinal));

        // If the file does not need modification, add only the marker.
        if (!hasPreviousMarker && !MayRequireSyntaxRewrite(sourceText))
        {
            WriteMarkerOnly(outputPath, sourceText);
            return 0;
        }

        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(
            sourceText,
            new CSharpParseOptions(LanguageVersion.Latest));

        CompilationUnitSyntax root = syntaxTree.GetCompilationUnitRoot();

        TryGetGeneratedMarkerVersion(root, MarkerToolName, out string? existingVersion);
        if (existingVersion == null)
            TryGetCommentMarkerVersion(sourceText, out existingVersion);

        string fileName = Path.GetFileName(inputPath);
        bool isCollisionTargetFile = fileName.EndsWith("IRecipeImports.cs", StringComparison.OrdinalIgnoreCase) ||
                            fileName.EndsWith("IBedrockPacketsImports.cs", StringComparison.OrdinalIgnoreCase) ||
                            fileName.Equals("Plugin.cs", StringComparison.OrdinalIgnoreCase);

        if (existingVersion != null)
        {
            if (existingVersion == MarkerToolVersion)
            {
                if (!string.Equals(inputPath, outputPath, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(inputPath, outputPath, overwrite: true);
                }
                return 0;
            }

            Console.WriteLine($"[WitPostProcessor] Migrating file from version {existingVersion} to {MarkerToolVersion}...");
            
            var migratedAbiCounts = new AbiCountRewriter();
            var migratedRoot = (CompilationUnitSyntax)migratedAbiCounts.Visit(root)!;
            
            var migratedLoopBounds = new LoopBoundRewriter();
            migratedRoot = (CompilationUnitSyntax)migratedLoopBounds.Visit(migratedRoot)!;
            
            var migratedPointerReads = new PointerReadRewriter();
            migratedRoot = (CompilationUnitSyntax)migratedPointerReads.Visit(migratedRoot)!;
            
            var migratedEmptySwitches = new EmptySwitchRewriter();
            migratedRoot = (CompilationUnitSyntax)migratedEmptySwitches.Visit(migratedRoot)!;

            if (isCollisionTargetFile)
            {
                var migratedMethodTagCollision = new MethodTagCollisionRewriter();
                migratedRoot = (CompilationUnitSyntax)migratedMethodTagCollision.Visit(migratedRoot)!;
            }
            
            migratedRoot = UpdateGeneratedMarkerVersion(migratedRoot, MarkerToolName, MarkerToolVersion);

            WriteOutput(outputPath, migratedRoot);
            return 0;
        }

        var errors = syntaxTree.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Take(5)
            .ToList();

        if (errors.Count > 0)
        {
            Console.Error.WriteLine($"[WitPostProcessor] The source AST contains syntax errors ({errors.Count}):");
            foreach (var err in errors)
            {
                Console.Error.WriteLine($"  {err}");
            }
            return 1;
        }

        var rewriter = new MonolithicSwitchRewriter();
        SyntaxNode newRoot = rewriter.Visit(root);

        var abiCountRewriter = new AbiCountRewriter();
        newRoot = abiCountRewriter.Visit(newRoot);

        var loopBoundRewriter = new LoopBoundRewriter();
        newRoot = loopBoundRewriter.Visit(newRoot);

        var pointerReadRewriter = new PointerReadRewriter();
        newRoot = pointerReadRewriter.Visit(newRoot);

        var emptySwitchRewriter = new EmptySwitchRewriter();
        newRoot = emptySwitchRewriter.Visit(newRoot);

        if (isCollisionTargetFile)
        {
            var isolatedRewriter = new MethodTagCollisionRewriter();
            newRoot = isolatedRewriter.Visit(newRoot);
        }

        newRoot = AppendMarker((CompilationUnitSyntax)newRoot);

        WriteOutput(outputPath, (CompilationUnitSyntax)newRoot);
        return 0;
    }

    private static bool MayRequireSyntaxRewrite(string sourceText)
    {
        return sourceText.Contains("RecipeItemDescriptor", StringComparison.Ordinal) ||
               sourceText.Contains("Ingredient", StringComparison.Ordinal) ||
               sourceText.Contains("wasmExportHandleEvent", StringComparison.Ordinal) ||
               sourceText.Contains("cabi_post_wasmExportHandleEvent", StringComparison.Ordinal) ||
               sourceText.Contains("Span<", StringComparison.Ordinal) ||
               sourceText.Contains("Result<", StringComparison.Ordinal) ||
               sourceText.Contains("Tag", StringComparison.Ordinal);
    }

    private static void WriteMarkerOnly(string outputPath, string sourceText)
    {
        if (sourceText.EndsWith(MarkerCommentPrefix + MarkerToolVersion, StringComparison.Ordinal))
            return;

        string suffix = sourceText.EndsWith("\n", StringComparison.Ordinal)
            ? string.Empty
            : Environment.NewLine;

        File.WriteAllText(
            outputPath,
            sourceText + suffix + MarkerCommentPrefix + MarkerToolVersion + Environment.NewLine,
            Encoding.UTF8);
    }

    private static void WriteOutput(string outputPath, CompilationUnitSyntax root)
    {
        string? outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        File.WriteAllText(outputPath, root.ToFullString(), Encoding.UTF8);
    }

    private static bool HasCurrentMarkerFast(string sourceText)
    {
        if (TryGetCommentMarkerVersion(sourceText, out string? commentVersion) &&
            commentVersion == MarkerToolVersion)
        {
            return true;
        }

        const string generatedCode = "GeneratedCode";
        int markerIndex = sourceText.IndexOf(generatedCode, StringComparison.Ordinal);

        while (markerIndex >= 0)
        {
            int lineStart = sourceText.LastIndexOf('\n', markerIndex);
            int lineEnd = sourceText.IndexOf('\n', markerIndex);
            if (lineEnd < 0)
                lineEnd = sourceText.Length;

            ReadOnlySpan<char> line = sourceText.AsSpan(
                lineStart < 0 ? 0 : lineStart + 1,
                lineEnd - (lineStart < 0 ? 0 : lineStart + 1));

            if (line.IndexOf("[assembly".AsSpan(), StringComparison.Ordinal) >= 0 &&
                line.IndexOf(MarkerToolName.AsSpan(), StringComparison.Ordinal) >= 0 &&
                line.IndexOf(MarkerToolVersion.AsSpan(), StringComparison.Ordinal) >= 0)
                return true;

            markerIndex = sourceText.IndexOf(generatedCode, markerIndex + generatedCode.Length, StringComparison.Ordinal);
        }

        return false;
    }

    private static bool TryGetCommentMarkerVersion(string sourceText, out string? version)
    {
        version = null;
        int markerIndex = sourceText.IndexOf(MarkerCommentPrefix, StringComparison.Ordinal);

        while (markerIndex >= 0)
        {
            int lineStart = sourceText.LastIndexOf('\n', markerIndex);
            int lineEnd = sourceText.IndexOf('\n', markerIndex);
            if (lineEnd < 0)
                lineEnd = sourceText.Length;

            ReadOnlySpan<char> line = sourceText.AsSpan(
                lineStart < 0 ? 0 : lineStart + 1,
                lineEnd - (lineStart < 0 ? 0 : lineStart + 1)).Trim();

            if (line.StartsWith(MarkerCommentPrefix.AsSpan(), StringComparison.Ordinal))
            {
                string value = line[MarkerCommentPrefix.Length..].Trim().ToString();
                if (value.Length > 0)
                {
                    version = value;
                    return true;
                }
            }

            markerIndex = sourceText.IndexOf(
                MarkerCommentPrefix,
                markerIndex + MarkerCommentPrefix.Length,
                StringComparison.Ordinal);
        }

        return false;
    }

    private static bool TryGetGeneratedMarkerVersion(CompilationUnitSyntax root, string toolName, out string? version)
    {
        version = null;

        foreach (var attrList in root.AttributeLists)
        {
            foreach (var attr in attrList.Attributes)
            {
                if (!IsGeneratedCodeAttribute(attr))
                    continue;

                var args = attr.ArgumentList?.Arguments;
                if (args == null || args.Value.Count < 2)
                    continue;

                if (args.Value[0].Expression is LiteralExpressionSyntax nameLit &&
                    nameLit.Token.ValueText == toolName &&
                    args.Value[1].Expression is LiteralExpressionSyntax verLit)
                {
                    version = verLit.Token.ValueText;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsGeneratedCodeAttribute(AttributeSyntax attr)
    {
        string name = attr.Name switch
        {
            IdentifierNameSyntax id => id.Identifier.Text,
            QualifiedNameSyntax q => q.Right.Identifier.Text,
            _ => attr.Name.ToString()
        };

        return name is "GeneratedCode" or "GeneratedCodeAttribute";
    }

    private static CompilationUnitSyntax AppendMarker(CompilationUnitSyntax root)
    {
        return AppendMarkerComment(root, MarkerToolVersion);
    }

    private static CompilationUnitSyntax UpdateGeneratedMarkerVersion(
        CompilationUnitSyntax root,
        string toolName,
        string version)
    {
        foreach (var trivia in root.DescendantTrivia())
        {
            if (!trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) ||
                !trivia.ToString().TrimStart().StartsWith(MarkerCommentPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            return root.ReplaceTrivia(
                trivia,
                SyntaxFactory.Comment($"{MarkerCommentPrefix}{version}"));
        }

        foreach (var attrList in root.AttributeLists)
        {
            var markerAttributes = attrList.Attributes
                .Where(IsSwitchSplitterAttribute)
                .ToList();

            if (markerAttributes.Count == 0)
            {
                continue;
            }

            CompilationUnitSyntax withoutAttribute = markerAttributes.Count == attrList.Attributes.Count
                ? (CompilationUnitSyntax)root.RemoveNode(attrList, SyntaxRemoveOptions.KeepNoTrivia)!
                : (CompilationUnitSyntax)root.ReplaceNode(
                    attrList,
                    attrList.WithAttributes(
                        SyntaxFactory.SeparatedList(
                            attrList.Attributes.Where(attr => !IsSwitchSplitterAttribute(attr)))));

            return AppendMarkerComment(withoutAttribute, version);
        }

        return AppendMarkerComment(root, version);
    }

    private static bool IsSwitchSplitterAttribute(AttributeSyntax attr)
    {
        if (!IsGeneratedCodeAttribute(attr))
            return false;

        var args = attr.ArgumentList?.Arguments;
        return args is { Count: >= 2 } &&
               args.Value[0].Expression is LiteralExpressionSyntax nameLit &&
               nameLit.Token.ValueText == MarkerToolName;
    }

    private static CompilationUnitSyntax AppendMarkerComment(
        CompilationUnitSyntax root,
        string version)
    {
        var endOfFile = root.EndOfFileToken;
        var leadingTrivia = endOfFile.LeadingTrivia
            .Add(SyntaxFactory.CarriageReturnLineFeed)
            .Add(SyntaxFactory.Comment($"{MarkerCommentPrefix}{version}"))
            .Add(SyntaxFactory.CarriageReturnLineFeed);

        return root.WithEndOfFileToken(endOfFile.WithLeadingTrivia(leadingTrivia));
    }
}

internal sealed class EmptySwitchRewriter : CSharpSyntaxRewriter
{
    public override SyntaxNode? VisitSwitchStatement(SwitchStatementSyntax node)
    {
        var visited = (SwitchStatementSyntax)base.VisitSwitchStatement(node)!;
        if (visited.Sections.Count < 2)
            return visited;

        var sections = new List<SwitchSectionSyntax>(visited.Sections.Count);
        SwitchSectionSyntax? pending = null;

        foreach (var section in visited.Sections)
        {
            if (IsEmptySection(section))
            {
                pending = pending is null
                    ? section
                    : pending.WithLabels(pending.Labels.AddRange(section.Labels));
                continue;
            }

            if (pending is not null)
            {
                sections.Add(pending);
                pending = null;
            }

            sections.Add(section);
        }

        if (pending is not null)
            sections.Add(pending);

        return visited.WithSections(SyntaxFactory.List(sections));
    }

    private static bool IsEmptySection(SwitchSectionSyntax section)
    {
        return section.Statements.Count == 1 &&
               section.Statements[0].IsKind(SyntaxKind.BreakStatement);
    }
}

internal sealed class MethodTagCollisionRewriter : CSharpSyntaxRewriter
{
    private static readonly HashSet<string> TargetClasses = new(StringComparer.Ordinal)
    {
        "Ingredient",
        "RecipeItemDescriptor"
    };

    public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        var visited = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node)!;

        if (visited.Identifier.Text == "Tag" &&
            visited.Parent is ClassDeclarationSyntax parentClass &&
            TargetClasses.Contains(parentClass.Identifier.Text))
        {
            return visited.WithIdentifier(
                SyntaxFactory.Identifier("FromTag").WithTriviaFrom(visited.Identifier));
        }

        return visited;
    }

    public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        var visited = (InvocationExpressionSyntax)base.VisitInvocationExpression(node)!;

        if (visited.Expression is MemberAccessExpressionSyntax
            {
                Name.Identifier.Text: "Tag"
            } memberAccess)
        {
            string callerType = memberAccess.Expression switch
            {
                IdentifierNameSyntax id => id.Identifier.Text,
                MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
                QualifiedNameSyntax qn => qn.Right.Identifier.Text,
                _ => string.Empty
            };

            if (TargetClasses.Contains(callerType))
            {
                var newName = SyntaxFactory.IdentifierName("FromTag")
                    .WithTriviaFrom(memberAccess.Name);

                return visited.WithExpression(memberAccess.WithName(newName));
            }
        }

        return visited;
    }
}

internal sealed class PointerReadRewriter : CSharpSyntaxRewriter
{
    public int ReplacedCount { get; private set; }
    public int SimplifiedCastCount { get; private set; }

    public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        var visited = (InvocationExpressionSyntax)base.VisitInvocationExpression(node)!;

        if (!IsUnsafeUnalignedAccess(visited) ||
            visited.ArgumentList.Arguments.Count == 0)
        {
            return visited;
        }

        var firstArgument = visited.ArgumentList.Arguments[0];
        if (!TryRemoveRedundantVoidPointerCast(firstArgument.Expression, out var simplified))
        {
            return visited;
        }

        var updatedArgument = firstArgument.WithExpression(simplified);
        SimplifiedCastCount++;
        return visited.WithArgumentList(
            visited.ArgumentList.WithArguments(
                visited.ArgumentList.Arguments.Replace(firstArgument, updatedArgument)));
    }

    public override SyntaxNode? VisitAssignmentExpression(AssignmentExpressionSyntax node)
    {
        var visited = (AssignmentExpressionSyntax)base.VisitAssignmentExpression(node)!;

        if (!visited.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
            visited.Left is not ElementAccessExpressionSyntax left ||
            !TryGetPointerSpanAccess(left, out var elementType, out var sourceExpression))
        {
            return visited;
        }

        var replacement = SyntaxFactory.ParseExpression(
            $"global::System.Runtime.CompilerServices.Unsafe.WriteUnaligned<{elementType}>({sourceExpression}, {visited.Right})")
            .WithTriviaFrom(visited);

        ReplacedCount++;
        return replacement;
    }

    public override SyntaxNode? VisitElementAccessExpression(ElementAccessExpressionSyntax node)
    {
        var visited = (ElementAccessExpressionSyntax)base.VisitElementAccessExpression(node)!;

        if (IsWriteContext(node) ||
            !TryGetPointerSpanAccess(visited, out var elementType, out var sourceExpression))
        {
            return visited;
        }

        var replacement = SyntaxFactory.ParseExpression(
            $"global::System.Runtime.CompilerServices.Unsafe.ReadUnaligned<{elementType}>({sourceExpression})")
            .WithTriviaFrom(visited);

        ReplacedCount++;
        return replacement;
    }

    private static bool IsUnsafeUnalignedAccess(InvocationExpressionSyntax invocation)
    {
        string name = invocation.Expression switch
        {
            MemberAccessExpressionSyntax { Name: GenericNameSyntax genericName } => genericName.Identifier.Text,
            GenericNameSyntax genericName => genericName.Identifier.Text,
            _ => string.Empty
        };

        return name is "ReadUnaligned" or "WriteUnaligned";
    }

    private static bool TryRemoveRedundantVoidPointerCast(
        ExpressionSyntax expression,
        out ExpressionSyntax simplified)
    {
        simplified = expression;

        if (expression is not CastExpressionSyntax
            {
                Type: PointerTypeSyntax { ElementType: PredefinedTypeSyntax predefinedType },
                Expression: ParenthesizedExpressionSyntax
                {
                    Expression: CastExpressionSyntax inner
                }
            } ||
            !predefinedType.Keyword.IsKind(SyntaxKind.VoidKeyword) ||
            !IsVoidPointerType(inner.Type))
        {
            return false;
        }

        simplified = inner.WithTriviaFrom(expression);
        return true;
    }

    private static bool IsVoidPointerType(TypeSyntax type)
    {
        return type is PointerTypeSyntax
        {
            ElementType: PredefinedTypeSyntax predefinedType
        } && predefinedType.Keyword.IsKind(SyntaxKind.VoidKeyword);
    }

    private static bool TryGetPointerSpanAccess(
        ElementAccessExpressionSyntax access,
        out TypeSyntax elementType,
        out string sourceExpression)
    {
        elementType = null!;
        sourceExpression = string.Empty;

        if (access.ArgumentList.Arguments.Count != 1 ||
            !IsNumericLiteral(access.ArgumentList.Arguments[0].Expression, "0") ||
            access.Expression is not ObjectCreationExpressionSyntax creation ||
            creation.ArgumentList == null ||
            creation.ArgumentList.Arguments.Count != 2 ||
            !IsNumericLiteral(creation.ArgumentList.Arguments[1].Expression, "1") ||
            !TryGetSpanElementType(creation.Type, out elementType))
        {
            return false;
        }

        sourceExpression = creation.ArgumentList.Arguments[0].Expression.ToString();
        return true;
    }

    private static bool IsWriteContext(ElementAccessExpressionSyntax node)
    {
        if (node.Parent is AssignmentExpressionSyntax assignment && assignment.Left == node)
            return true;

        if (node.Parent is PrefixUnaryExpressionSyntax prefix &&
            prefix.Operand == node &&
            (prefix.IsKind(SyntaxKind.PreIncrementExpression) ||
             prefix.IsKind(SyntaxKind.PreDecrementExpression)))
        {
            return true;
        }

        if (node.Parent is PostfixUnaryExpressionSyntax postfix &&
            postfix.Operand == node &&
            (postfix.IsKind(SyntaxKind.PostIncrementExpression) ||
             postfix.IsKind(SyntaxKind.PostDecrementExpression)))
        {
            return true;
        }

        return node.Parent is ArgumentSyntax argument &&
               argument.Expression == node &&
               argument.RefOrOutKeyword.RawKind != 0;
    }

    private static bool TryGetSpanElementType(TypeSyntax type, out TypeSyntax elementType)
    {
        elementType = null!;
        string typeText = type.ToString();
        if (!typeText.StartsWith("global::System.Span<", StringComparison.Ordinal) &&
            !typeText.StartsWith("System.Span<", StringComparison.Ordinal) &&
            !typeText.StartsWith("Span<", StringComparison.Ordinal))
        {
            return false;
        }

        var spanName = type.DescendantNodesAndSelf()
            .OfType<GenericNameSyntax>()
            .LastOrDefault(name => name.Identifier.Text == "Span");

        if (spanName?.TypeArgumentList.Arguments.Count != 1)
            return false;

        elementType = spanName.TypeArgumentList.Arguments[0];
        return true;
    }

    private static bool IsNumericLiteral(ExpressionSyntax expression, string value)
    {
        return expression is LiteralExpressionSyntax literal && literal.Token.ValueText == value;
    }
}

internal sealed class AbiCountRewriter : CSharpSyntaxRewriter
{
    private int _counter;

    public int ReplacedCount { get; private set; }

    public override SyntaxNode? VisitBlock(BlockSyntax node)
    {
        var visited = (BlockSyntax)base.VisitBlock(node)!;
        if (visited.Statements.Count < 2)
            return visited;

        var statements = visited.Statements.ToList();
        for (int i = 0; i < statements.Count - 1; i++)
        {
            if (statements[i] is not LocalDeclarationStatementSyntax local ||
                statements[i + 1] is not ForStatementSyntax loop ||
                !TryGetAbiCount(local, out var countRead))
            {
                continue;
            }

            string countName = CreateCountName(visited);
            if (!TryGetLoopCount(loop, countRead, countName, out var updatedLoop))
            {
                continue;
            }

            var countIdentifier = SyntaxFactory.IdentifierName(countName)
                .WithTriviaFrom(countRead);

            var updatedLocal = local.ReplaceNode(
                countRead,
                countIdentifier);

            var countDeclaration = SyntaxFactory.VariableDeclarator(
                    SyntaxFactory.Identifier(countName))
                .WithInitializer(SyntaxFactory.EqualsValueClause(countRead.WithoutTrivia()));
            var countStatement = SyntaxFactory.LocalDeclarationStatement(
                    SyntaxFactory.VariableDeclaration(
                        SyntaxFactory.IdentifierName("var")
                            .WithTrailingTrivia(SyntaxFactory.Space),
                        SyntaxFactory.SingletonSeparatedList(countDeclaration)))
                .WithLeadingTrivia(local.GetLeadingTrivia());

            statements[i] = updatedLocal;
            statements[i + 1] = updatedLoop;
            statements.Insert(i, countStatement);
            i++;
            ReplacedCount++;
        }

        return visited.WithStatements(SyntaxFactory.List(statements));
    }

    private string CreateCountName(BlockSyntax block)
    {
        string name;
        do
        {
            name = $"__witPostProcessorCount{_counter++}";
        }
        while (block.DescendantTokens().Any(token => token.ValueText == name));

        return name;
    }

    private static bool TryGetAbiCount(
        LocalDeclarationStatementSyntax local,
        out InvocationExpressionSyntax countRead)
    {
        countRead = null!;

        if (local.Declaration.Variables.Count != 1)
            return false;

        var value = local.Declaration.Variables[0].Initializer?.Value;
        if (value is ObjectCreationExpressionSyntax objectCreation &&
            objectCreation.ArgumentList?.Arguments.Count == 1 &&
            objectCreation.ArgumentList.Arguments[0].Expression is InvocationExpressionSyntax objectCount &&
            IsAbiIntRead(objectCount))
        {
            countRead = objectCount;
            return true;
        }

        if (value is ArrayCreationExpressionSyntax arrayCreation &&
            arrayCreation.Type.RankSpecifiers.Count == 1 &&
            arrayCreation.Type.RankSpecifiers[0].Sizes.Count == 1 &&
            arrayCreation.Type.RankSpecifiers[0].Sizes[0] is InvocationExpressionSyntax arrayCount &&
            IsAbiIntRead(arrayCount))
        {
            countRead = arrayCount;
            return true;
        }

        return false;
    }

    private static bool TryGetLoopCount(
        ForStatementSyntax loop,
        InvocationExpressionSyntax countRead,
        string countName,
        out ForStatementSyntax updatedLoop)
    {
        updatedLoop = loop;

        if (loop.Declaration is not { Variables.Count: >= 1 } declaration ||
            declaration.Variables[0].Initializer?.Value is not LiteralExpressionSyntax initializer ||
            initializer.Token.ValueText != "0" ||
            loop.Condition is not BinaryExpressionSyntax
            {
                RawKind: (int)SyntaxKind.LessThanExpression,
                Left: IdentifierNameSyntax left,
                Right: ExpressionSyntax right
            } ||
            !IsIncrementOf(loop.Incrementors, left.Identifier.Text))
        {
            return false;
        }

        if (right is InvocationExpressionSyntax directRead)
        {
            if (!AreEquivalentRead(directRead, countRead))
                return false;

            var condition = ((BinaryExpressionSyntax)loop.Condition)
                .WithRight(SyntaxFactory.IdentifierName(countName));
            updatedLoop = loop.WithCondition(condition);
            return true;
        }

        if (right is not IdentifierNameSyntax boundName || declaration.Variables.Count != 2)
            return false;

        var boundVariable = declaration.Variables[1];
        if (boundVariable.Identifier.Text != boundName.Identifier.Text ||
            boundVariable.Initializer?.Value is not InvocationExpressionSyntax boundRead ||
            !AreEquivalentRead(boundRead, countRead))
        {
            return false;
        }

        var updatedDeclaration = declaration.WithVariables(
            SyntaxFactory.SingletonSeparatedList(declaration.Variables[0]));
        updatedLoop = loop
            .WithDeclaration(updatedDeclaration)
            .WithCondition(((BinaryExpressionSyntax)loop.Condition)
                .WithRight(SyntaxFactory.IdentifierName(countName)));
        return true;
    }

    private static bool IsAbiIntRead(InvocationExpressionSyntax invocation)
    {
        if (invocation.ArgumentList.Arguments.Count != 1)
            return false;

        return invocation.Expression switch
        {
            MemberAccessExpressionSyntax
            {
                Name: GenericNameSyntax
                {
                    Identifier.Text: "ReadUnaligned",
                    TypeArgumentList.Arguments.Count: 1
                } name
            } => name.TypeArgumentList.Arguments[0].ToString() == "int",
            GenericNameSyntax
            {
                Identifier.Text: "ReadUnaligned",
                TypeArgumentList.Arguments.Count: 1
            } name => name.TypeArgumentList.Arguments[0].ToString() == "int",
            _ => false
        };
    }

    private static bool AreEquivalentRead(
        InvocationExpressionSyntax left,
        InvocationExpressionSyntax right)
    {
        return IsAbiIntRead(left) &&
               IsAbiIntRead(right) &&
               left.ToString() == right.ToString();
    }

    private static bool IsIncrementOf(
        SeparatedSyntaxList<ExpressionSyntax> incrementors,
        string variableName)
    {
        return incrementors.Count == 1 && incrementors[0] switch
        {
            PrefixUnaryExpressionSyntax
            {
                RawKind: (int)SyntaxKind.PreIncrementExpression,
                Operand: IdentifierNameSyntax operand
            } => operand.Identifier.Text == variableName,
            PostfixUnaryExpressionSyntax
            {
                RawKind: (int)SyntaxKind.PostIncrementExpression,
                Operand: IdentifierNameSyntax operand
            } => operand.Identifier.Text == variableName,
            _ => false
        };
    }
}

internal sealed class LoopBoundRewriter : CSharpSyntaxRewriter
{
    private int _counter;

    public int ReplacedCount { get; private set; }

    public override SyntaxNode? VisitForStatement(ForStatementSyntax node)
    {
        var visited = (ForStatementSyntax)base.VisitForStatement(node)!;

        if (visited.Declaration is not { Variables.Count: 1 } declaration ||
            declaration.Type.ToString() != "int" ||
            declaration.Variables[0].Initializer?.Value is not LiteralExpressionSyntax initializer ||
            initializer.Token.ValueText != "0" ||
            visited.Condition is not BinaryExpressionSyntax
            {
                RawKind: (int)SyntaxKind.LessThanExpression,
                Left: IdentifierNameSyntax left,
                Right: InvocationExpressionSyntax bound
            } ||
            left.Identifier.Text != declaration.Variables[0].Identifier.Text ||
            !IsIncrementOf(visited.Incrementors, left.Identifier.Text) ||
            !TryGetUnalignedIntRead(bound))
        {
            return visited;
        }

        string countName = $"__witPostProcessorCount{_counter++}";
        while (visited.DescendantTokens().Any(token => token.ValueText == countName))
            countName = $"__witPostProcessorCount{_counter++}";

        var countVariable = SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier(countName))
            .WithInitializer(SyntaxFactory.EqualsValueClause(bound));

        var newDeclaration = declaration.AddVariables(countVariable);
        var newCondition = SyntaxFactory.BinaryExpression(
            SyntaxKind.LessThanExpression,
            SyntaxFactory.IdentifierName(left.Identifier.Text),
            SyntaxFactory.IdentifierName(countName));

        ReplacedCount++;
        return visited.WithDeclaration(newDeclaration).WithCondition(newCondition);
    }

    private static bool IsIncrementOf(
        SeparatedSyntaxList<ExpressionSyntax> incrementors,
        string variableName)
    {
        return incrementors.Count == 1 && incrementors[0] switch
        {
            PrefixUnaryExpressionSyntax
            {
                RawKind: (int)SyntaxKind.PreIncrementExpression,
                Operand: IdentifierNameSyntax operand
            } => operand.Identifier.Text == variableName,
            PostfixUnaryExpressionSyntax
            {
                RawKind: (int)SyntaxKind.PostIncrementExpression,
                Operand: IdentifierNameSyntax operand
            } => operand.Identifier.Text == variableName,
            _ => false
        };
    }

    private static bool TryGetUnalignedIntRead(
        InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            MemberAccessExpressionSyntax
            {
                Name: GenericNameSyntax
                {
                    Identifier.Text: "ReadUnaligned",
                    TypeArgumentList.Arguments.Count: 1
                } genericName
            } => genericName.TypeArgumentList.Arguments[0].ToString() == "int" &&
                 invocation.ArgumentList.Arguments.Count == 1,
            GenericNameSyntax
            {
                Identifier.Text: "ReadUnaligned",
                TypeArgumentList.Arguments.Count: 1
            } genericName => genericName.TypeArgumentList.Arguments[0].ToString() == "int" &&
                             invocation.ArgumentList.Arguments.Count == 1,
            _ => false
        };
    }
}

internal sealed class MonolithicSwitchRewriter : CSharpSyntaxRewriter
{
    private static readonly HashSet<string> TargetMethods = new(StringComparer.Ordinal)
    {
        "wasmExportHandleEvent",
        "cabi_post_wasmExportHandleEvent"
    };

    private readonly List<MethodDeclarationSyntax> _extractedHelpers = new();
    private int _methodCounter;
    private int _totalExtractedCount;
    private int _skippedEmptyCount;
    private int _replacedResultCount;

    public int TotalExtractedCount => _totalExtractedCount;
    public int SkippedEmptyCount => _skippedEmptyCount;
    public int ReplacedResultCount => _replacedResultCount;

    public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        if (IsBoxedResultDeclaration(node))
        {
            _replacedResultCount++;
            return GenerateUnboxedResultStruct(node);
        }

        var visitedClass = (ClassDeclarationSyntax)base.VisitClassDeclaration(node)!;

        if (_extractedHelpers.Count > 0)
        {
            visitedClass = visitedClass.AddMembers(_extractedHelpers.ToArray());
            _extractedHelpers.Clear();
        }

        return visitedClass;
    }

    public override SyntaxNode? VisitStructDeclaration(StructDeclarationSyntax node)
    {
        if (IsBoxedResultDeclaration(node))
        {
            _replacedResultCount++;
            return GenerateUnboxedResultStruct(node);
        }

        return base.VisitStructDeclaration(node);
    }

    private static bool IsBoxedResultDeclaration(TypeDeclarationSyntax node)
    {
        if (node.Identifier.Text != "Result" || node.TypeParameterList?.Parameters.Count != 2)
            return false;

        return node.Members
            .OfType<FieldDeclarationSyntax>()
            .SelectMany(field => field.Declaration.Variables
                .Select(variable => (Type: field.Declaration.Type.ToString().Trim(), Name: variable.Identifier.Text)))
            .Any(field => field.Name == "value" &&
                          (field.Type == "object" || field.Type == "object?"));
    }

    private static StructDeclarationSyntax GenerateUnboxedResultStruct(TypeDeclarationSyntax original)
    {
        const string structCode = @"
[global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Auto)]
public readonly struct Result<TOk, TErr>
{
    public readonly byte Tag;
    private readonly TOk _ok;
    private readonly TErr _err;

    public const byte OK = 0;
    public const byte ERR = 1;

    public bool IsOk => Tag == OK;
    public bool IsErr => Tag == ERR;

    public TOk AsOk => Tag == OK ? _ok : throw new global::PluginWorld.WitException(""Called AsOk on an Err Result"", 0);
    public TErr AsErr => Tag == ERR ? _err : throw new global::PluginWorld.WitException(""Called AsErr on an Ok Result"", 0);

    public static Result<TOk, TErr> Ok(TOk ok) => new(OK, ok, default!);
    public static Result<TOk, TErr> Err(TErr err) => new(ERR, default!, err);

    private Result(byte tag, TOk ok, TErr err)
    {
        Tag = tag;
        _ok = ok;
        _err = err;
    }
}
";
        var parsed = (StructDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(structCode)!;
        return parsed.WithLeadingTrivia(original.GetLeadingTrivia()).WithTrailingTrivia(original.GetTrailingTrivia());
    }

    public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        if (!TargetMethods.Contains(node.Identifier.Text) || node.Body == null)
        {
            return base.VisitMethodDeclaration(node);
        }

        var methodParameters = node.ParameterList.Parameters
            .Where(p => IsValidIdentifier(p.Identifier.Text))
            .ToList();

        var newStatements = new List<StatementSyntax>();
        var currentScopeLocals = new Dictionary<string, string>();
        string? lastDeclaredLocalBeforeSwitch = null;

        foreach (var statement in node.Body.Statements)
        {
            if (statement is LocalDeclarationStatementSyntax localDecl)
            {
                string typeStr = localDecl.Declaration.Type.ToString().Trim();
                foreach (var v in localDecl.Declaration.Variables)
                {
                    string varName = v.Identifier.Text;
                    string resolvedType = typeStr;
                    if (resolvedType == "var")
                    {
                        if (varName == "ptr") resolvedType = "nint";
                        else continue;
                    }
                    currentScopeLocals[varName] = resolvedType;
                    lastDeclaredLocalBeforeSwitch = varName;
                }
                newStatements.Add(statement);
            }
            else if (statement is SwitchStatementSyntax switchStmt && switchStmt.Sections.Count > 50)
            {
                newStatements.Add(RewriteMonolithSwitch(
                    switchStmt,
                    node.Identifier.Text,
                    methodParameters,
                    currentScopeLocals,
                    lastDeclaredLocalBeforeSwitch));
            }
            else
            {
                newStatements.Add(statement);
            }
        }

        return node.WithBody(SyntaxFactory.Block(newStatements));
    }

    private SwitchStatementSyntax RewriteMonolithSwitch(
        SwitchStatementSyntax switchStmt,
        string enclosingMethodName,
        List<ParameterSyntax> methodParameters,
        Dictionary<string, string> currentScopeLocals,
        string? contextLiftedVar)
    {
        var rewrittenSections = new List<SwitchSectionSyntax>();

        bool isLoweringSwitch = switchStmt.Expression is MemberAccessExpressionSyntax
        {
            Expression: IdentifierNameSyntax { Identifier.Text: "ret" },
            Name.Identifier.Text: "Tag"
        };

        bool isCleanupMethod = enclosingMethodName.StartsWith("cabi_post", StringComparison.Ordinal);

        string? liftedVarName = null;
        string? liftedVarType = null;
        if (!isLoweringSwitch && !isCleanupMethod && contextLiftedVar != null)
        {
            if (currentScopeLocals.TryGetValue(contextLiftedVar, out var type) && type.EndsWith("Event", StringComparison.Ordinal))
            {
                liftedVarName = contextLiftedVar;
                liftedVarType = type;
            }
        }

        foreach (var section in switchStmt.Sections)
        {
            if (!TryExtractCleanStatements(section, out var cleanedStatements))
            {
                rewrittenSections.Add(section);
                continue;
            }

            if (cleanedStatements.Count == 0)
            {
                _skippedEmptyCount++;
                rewrittenSections.Add(section.WithStatements(
                    SyntaxFactory.SingletonList<StatementSyntax>(SyntaxFactory.BreakStatement())));
                continue;
            }

            if (isCleanupMethod && cleanedStatements.Count == 1 &&
                IsNativeMemoryFreeStatement(cleanedStatements[0]))
            {
                rewrittenSections.Add(section.WithStatements(
                    SyntaxFactory.List(cleanedStatements.Append(SyntaxFactory.BreakStatement()))));
                continue;
            }

            var helperParams = new List<ParameterSyntax>();

            if (isLoweringSwitch)
            {
                string retType = currentScopeLocals.TryGetValue("ret", out var resolvedRetType)
                    ? resolvedRetType
                    : "global::PluginWorld.wit.Imports.pumpkin.plugin.v0_1_0.IEventImports.Event";

                helperParams.Add(CreateSimpleParameter("ret", retType));

                if (currentScopeLocals.ContainsKey("ptr"))
                {
                    helperParams.Add(CreateSimpleParameter("ptr", "nint"));
                }
            }
            else if (!isCleanupMethod && liftedVarName != null)
            {
                helperParams.AddRange(methodParameters);

                var outLiftedParam = CreateSimpleParameter(liftedVarName, liftedVarType!)
                    .AddModifiers(SyntaxFactory.Token(SyntaxKind.OutKeyword).WithTrailingTrivia(SyntaxFactory.Space));
                helperParams.Add(outLiftedParam);

                if (!HasDirectAssignment(cleanedStatements, liftedVarName) &&
                    !AlwaysTerminates(cleanedStatements))
                {
                    cleanedStatements.Insert(0, SyntaxFactory.ParseStatement($"{liftedVarName} = default!;\n"));
                }
            }
            else
            {
                helperParams.AddRange(methodParameters);
            }

            string helperName = $"__{enclosingMethodName}_Case_{_methodCounter++}";

            var helperMethod = CreateHelperMethod(helperName, cleanedStatements, helperParams);
            _extractedHelpers.Add(helperMethod);
            _totalExtractedCount++;

            var invocation = CreateHelperCall(helperName, helperParams);
            var callStatement = SyntaxFactory.ExpressionStatement(invocation);

            var newSectionStatements = new List<StatementSyntax>
            {
                callStatement,
                SyntaxFactory.BreakStatement()
            };

            rewrittenSections.Add(section.WithStatements(SyntaxFactory.List(newSectionStatements)));
        }

        return switchStmt.WithSections(SyntaxFactory.List(MergeEmptySections(rewrittenSections)));
    }

    private static IEnumerable<SwitchSectionSyntax> MergeEmptySections(
        List<SwitchSectionSyntax> sections)
    {
        SwitchSectionSyntax? pending = null;

        foreach (var section in sections)
        {
            if (IsEmptySection(section))
            {
                pending = pending is null
                    ? section
                    : pending.WithLabels(pending.Labels.AddRange(section.Labels));
                continue;
            }

            if (pending is not null)
            {
                yield return pending;
                pending = null;
            }

            yield return section;
        }

        if (pending is not null)
            yield return pending;
    }

    private static bool IsEmptySection(SwitchSectionSyntax section)
    {
        return section.Statements.Count == 1 &&
               section.Statements[0].IsKind(SyntaxKind.BreakStatement);
    }

    private static bool TryExtractCleanStatements(SwitchSectionSyntax section, out List<StatementSyntax> result)
    {
        result = new List<StatementSyntax>();

        bool hasInvalidJump = section.Statements
            .SelectMany(stmt => stmt.DescendantNodesAndSelf(descendant =>
                descendant is not (LambdaExpressionSyntax or LocalFunctionStatementSyntax or AnonymousMethodExpressionSyntax)))
            .Any(node => node is GotoStatementSyntax or ContinueStatementSyntax or ReturnStatementSyntax);

        if (hasInvalidJump)
        {
            return false;
        }

        var rawStatements = section.Statements.ToList();
        if (rawStatements.Count == 1 && rawStatements[0] is BlockSyntax singleBlock)
        {
            rawStatements = singleBlock.Statements.ToList();
        }

        if (rawStatements.Count == 0)
        {
            return true;
        }

        int lastIndex = rawStatements.Count - 1;
        if (rawStatements[lastIndex] is BreakStatementSyntax)
        {
            rawStatements.RemoveAt(lastIndex);
        }

        bool hasRemainingBreaks = rawStatements.Any(stmt =>
            stmt.DescendantNodesAndSelf(d => d is not (ForStatementSyntax or ForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax or SwitchStatementSyntax))
                .Any(node => node is BreakStatementSyntax));

        if (hasRemainingBreaks)
        {
            return false;
        }

        result = rawStatements;
        return true;
    }

    private static bool HasDirectAssignment(IEnumerable<StatementSyntax> statements, string variableName)
    {
        return statements.Any(statement =>
            statement is ExpressionStatementSyntax
            {
                Expression: AssignmentExpressionSyntax
                {
                    Left: IdentifierNameSyntax left
                }
            } && left.Identifier.Text == variableName);
    }

    private static bool AlwaysTerminates(IReadOnlyList<StatementSyntax> statements)
    {
        return statements.LastOrDefault() is ThrowStatementSyntax;
    }

    private static bool IsNativeMemoryFreeStatement(StatementSyntax statement)
    {
        return statement is ExpressionStatementSyntax
        {
            Expression: InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax
                {
                    Name.Identifier.Text: "Free",
                    Expression: MemberAccessExpressionSyntax
                    {
                        Name.Identifier.Text: "NativeMemory"
                    }
                }
            }
        } && statement.ToFullString().Length < 220;
    }

    private static ParameterSyntax CreateSimpleParameter(string name, string type)
    {
        return SyntaxFactory.Parameter(SyntaxFactory.Identifier(name))
            .WithType(SyntaxFactory.ParseTypeName(type).WithTrailingTrivia(SyntaxFactory.Space));
    }

    private static MethodDeclarationSyntax CreateHelperMethod(
        string methodName,
        List<StatementSyntax> statements,
        List<ParameterSyntax> parameters)
    {
        int bodyLength = statements.Sum(statement => statement.ToFullString().Length);
        bool addNoInlining = bodyLength >= 256 || parameters.Count > 2;
        AttributeListSyntax? attributeList = addNoInlining
            ? SyntaxFactory.AttributeList(
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.Attribute(
                        SyntaxFactory.ParseName("global::System.Runtime.CompilerServices.MethodImpl"),
                        SyntaxFactory.AttributeArgumentList(
                            SyntaxFactory.SingletonSeparatedList(
                                SyntaxFactory.AttributeArgument(
                                    SyntaxFactory.ParseExpression("global::System.Runtime.CompilerServices.MethodImplOptions.NoInlining")))))))
                .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed)
            : null;

        var method = SyntaxFactory.MethodDeclaration(
                SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword)).WithTrailingTrivia(SyntaxFactory.Space),
                SyntaxFactory.Identifier(methodName))
            .AddModifiers(
                SyntaxFactory.Token(SyntaxKind.PrivateKeyword).WithTrailingTrivia(SyntaxFactory.Space),
                SyntaxFactory.Token(SyntaxKind.StaticKeyword).WithTrailingTrivia(SyntaxFactory.Space),
                SyntaxFactory.Token(SyntaxKind.UnsafeKeyword).WithTrailingTrivia(SyntaxFactory.Space))
            .WithParameterList(
                SyntaxFactory.ParameterList(
                    SyntaxFactory.SeparatedList(
                        parameters,
                        Enumerable.Repeat(
                            SyntaxFactory.Token(SyntaxKind.CommaToken).WithTrailingTrivia(SyntaxFactory.Space),
                            Math.Max(0, parameters.Count - 1))
                    )))
            .WithBody(SyntaxFactory.Block(statements));

        return attributeList is null ? method : method.AddAttributeLists(attributeList);
    }

    private static InvocationExpressionSyntax CreateHelperCall(
        string methodName,
        List<ParameterSyntax> parameters)
    {
        var arguments = parameters.Select(p =>
        {
            SyntaxToken refOrOutToken = default;

            foreach (var mod in p.Modifiers)
            {
                if (mod.IsKind(SyntaxKind.OutKeyword))
                {
                    refOrOutToken = SyntaxFactory.Token(SyntaxKind.OutKeyword).WithTrailingTrivia(SyntaxFactory.Space);
                    break;
                }
                if (mod.IsKind(SyntaxKind.RefKeyword))
                {
                    refOrOutToken = SyntaxFactory.Token(SyntaxKind.RefKeyword).WithTrailingTrivia(SyntaxFactory.Space);
                    break;
                }
                if (mod.IsKind(SyntaxKind.InKeyword))
                {
                    refOrOutToken = SyntaxFactory.Token(SyntaxKind.InKeyword).WithTrailingTrivia(SyntaxFactory.Space);
                    break;
                }
            }

            return SyntaxFactory.Argument(
                null,
                refOrOutToken,
                SyntaxFactory.IdentifierName(p.Identifier.Text));
        });

        return SyntaxFactory.InvocationExpression(
            SyntaxFactory.IdentifierName(methodName),
            SyntaxFactory.ArgumentList(
                SyntaxFactory.SeparatedList(
                    arguments,
                    Enumerable.Repeat(
                        SyntaxFactory.Token(SyntaxKind.CommaToken).WithTrailingTrivia(SyntaxFactory.Space),
                        Math.Max(0, parameters.Count - 1))
                )
            ));
    }

    private static bool IsValidIdentifier(string name)
    {
        return !string.IsNullOrWhiteSpace(name)
               && name != "_"
               && SyntaxFacts.IsValidIdentifier(name);
    }
}