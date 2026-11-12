using RDCore.LanguageServer.Services.Refactoring.ExtractMethod;
using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.Runtime.LanguageCore.Flow;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.Completion;
using RDCore.SDK.Semantics;
using RDCore.Runtime.LanguageCore.Static;
using RDCore.Runtime.LanguageCore.Static.Abstract;
using RDCore.SDK.Workspace;
using RDCore.Symbols;
using RDCore.Runtime.LanguageCore.Instructions;
using RDCore.Runtime.LanguageCore.Refactoring;

namespace RDCore.Tests.Semantics.Flow;

/// <summary>
/// A procedure <c>Run</c> of a module, analyzed as the host analyzes it when it loads the code: bound, lowered, and its data flow found - what the flow analyses,
/// and the refactorings built on them, are tested on.
/// </summary>
internal static class FlowFixture
{
    private static readonly Uri Root = new("file://rdcore-test");

    private static Uri ModuleUri(string name) => new UriBuilder(Root) { Fragment = name }.Uri;

    // what a class module's text starts with, before its name.
    private const string ClassHeader = "VERSION 1.0 CLASS\r\nBEGIN\r\n  MultiUse = -1  'True\r\nEND\r\n";

    /// <summary>
    /// The file of the module <c>Main</c>: a class module's, or a standard module's.
    /// </summary>
    public static Uri FileOf(bool classModule) => new(classModule ? "file:///c:/ws/Main.cls" : "file:///c:/ws/Main.bas");

    private static ModuleType TypeOf(bool classModule) => classModule ? ModuleType.ClassModule : ModuleType.StdModule;

    // what the module declares besides Run, which its body may use.
    private const string Others = """
        Private Type Point
            X As Long
            Y As Long
        End Type

        Private field As Long

        Private Sub Bump(ByRef v As Long)
            v = v + 1
        End Sub

        Private Function G() As Long
            G = 1
        End Function
        """;

    /// <summary>
    /// The analyzed procedure.
    /// </summary>
    /// <param name="Procedure">Its declaration.</param>
    /// <param name="Instructions">Its lowered body.</param>
    /// <param name="Model">What the static pass found out about it.</param>
    /// <param name="Accesses">The definitions and uses of its variables.</param>
    /// <param name="Graph">Its code paths.</param>
    /// <param name="Scopes">The scopes of the workspace.</param>
    /// <param name="FirstBodyLine">The line of the module its body starts at.</param>
    internal sealed record class Analyzed(
        MemberDeclarationNode Procedure, InstructionList Instructions, ProcedureSemanticModel Model, DefinitionsAndUses Accesses, ControlFlowGraph Graph, ScopeTree Scopes, int FirstBodyLine)
    {
        /// <summary>
        /// The module's syntax tree.
        /// </summary>
        public ModuleNode Module { get; init; } = null!;

        /// <summary>
        /// The module's text.
        /// </summary>
        public string Text { get; init; } = string.Empty;

        /// <summary>
        /// The workspace, bound, as the refactorings see it.
        /// </summary>
        public RenameContext Context { get; init; } = null!;

        /// <summary>
        /// The <c>#If</c> blocks of the module.
        /// </summary>
        public ExtractionDirectives Directives { get; init; } = ExtractionDirectives.None;

        /// <summary>
        /// The analysis of a module of the workspace, by its address in <see cref="Context"/>, as an extraction analyzes it; <see langword="null"/> for none.
        /// </summary>
        public Func<Uri, ExtractionModuleAnalysis?> AnalysisOf { get; init; } = _ => null;

        /// <summary>
        /// The module's address in <see cref="Context"/>.
        /// </summary>
        public Uri ModuleAddress => ModuleUri("Main");

        /// <summary>
        /// The address in <see cref="Context"/> of the module named <paramref name="name"/>.
        /// </summary>
        public static Uri AddressOf(string name) => ModuleUri(name);

        public LiveVariables Live => LiveVariables.Of(Graph, Accesses);

        public ReachingDefinitions Reaching => ReachingDefinitions.Of(Graph, Accesses);

        public SemanticId Variable(string name)
            => Accesses.Variables.Single(variable => variable.Uri.Fragment.EndsWith($".{name}", StringComparison.OrdinalIgnoreCase));

        // the instructions of the lines of the body from `first` to `last` (zero-based, the first line of the body is 0).
        public IReadOnlySet<int> Lines(int first, int last)
            => Instructions.Items
                .Where(instruction => instruction.Location is { } location
                    && location.Range.Start.Line - FirstBodyLine >= first && location.Range.Start.Line - FirstBodyLine <= last)
                .Select(instruction => instruction.Offset)
                .ToHashSet();

        public int Line(int line) => Lines(line, line).Min();
    }

    /// <summary>
    /// The module whose text is <paramref name="text"/>, its names bound as <see cref="Analyze"/> binds them, analyzed as an extraction analyzes it.
    /// </summary>
    /// <param name="text">The module's text: its name is <c>Main</c>.</param>
    /// <param name="classes">The name and the source of each class module of the workspace beside the module; none when omitted.</param>
    /// <param name="classModule">Whether the module is a class module.</param>
    public static ExtractionModuleAnalysis Module(string text, IReadOnlyList<(string Name, string Source)>? classes = null, bool classModule = false)
    {
        var parse = new ModuleParser().Parse(FileOf(classModule), text);
        var composition = WorkspaceSymbolResolver.ComposeWithScopes(
            Root, [(ModuleUri("Main"), TypeOf(classModule), parse), .. OthersOf(classes, null).Select(other => (other.Module, other.Type, other.Parse))],
            new IntrinsicSymbolResolver());
        return ExtractionModuleAnalysis.Of(ModuleUri("Main"), text, parse, composition.Resolver, composition.ScopeTree);
    }

    // the class modules, then the standard modules, beside the module, each parsed.
    private static (Uri Module, ModuleType Type, ModuleParseResult Parse, string Source)[] OthersOf(
        IReadOnlyList<(string Name, string Source)>? classes, IReadOnlyList<(string Name, string Source)>? modules)
        => [.. (classes ?? []).Select(module => (module.Name, module.Source, Type: ModuleType.ClassModule, Extension: "cls"))
            .Concat((modules ?? []).Select(module => (module.Name, module.Source, Type: ModuleType.StdModule, Extension: "bas")))
            .Select(other =>
            {
                var parse = new ModuleParser().Parse(new Uri($"file:///c:/ws/{other.Name}.{other.Extension}"), other.Source);
                Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));
                return (ModuleUri(other.Name), other.Type, parse, other.Source);
            })];

    /// <summary>
    /// Analyzes a procedure <c>Run</c> whose body is <paramref name="body"/>, in a module that declares what <paramref name="declarations"/> says before the rest.
    /// </summary>
    /// <param name="classes">The name and the source of each class module of the workspace beside the module; none when omitted.</param>
    /// <param name="classModule">Whether the module is a class module, whose procedure <c>Run</c> is a member of each of its objects.</param>
    /// <param name="modules">The name and the source of each standard module of the workspace beside the module; none when omitted.</param>
    public static Analyzed Analyze(
        string body, string header = "Public Sub Run(ByVal n As Long)", string end = "End Sub", bool optionExplicit = true, string declarations = "",
        IReadOnlyList<(string Name, string Source)>? classes = null, bool classModule = false, IReadOnlyList<(string Name, string Source)>? modules = null)
    {
        var moduleLevel = $"{declarations}\r\n{Others}".ReplaceLineEndings("\r\n");
        var prefix = $"{(classModule ? ClassHeader : string.Empty)}Attribute VB_Name = \"Main\"\r\n{(optionExplicit ? "Option Explicit" : "' no Option Explicit")}\r\n{moduleLevel}\r\n{header}\r\n";
        var text = $"{prefix}{body.ReplaceLineEndings("\r\n")}\r\n{end.ReplaceLineEndings("\r\n")}\r\n";
        var parse = new ModuleParser().Parse(FileOf(classModule), text);
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

        var others = OthersOf(classes, modules);
        var composition = WorkspaceSymbolResolver.ComposeWithScopes(
            Root, [(ModuleUri("Main"), TypeOf(classModule), parse), .. others.Select(other => (other.Module, other.Type, other.Parse))], new IntrinsicSymbolResolver());
        // the first Run, the one whose body is given: a Property Let and its Property Get share the name, and the other comes after it, in end. Its scope is
        // the one its declaration is in, since a property's procedures are not told apart by their name alone.
        var run = parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().First(member => member.Name == "Run");
        Assert.IsTrue(composition.ScopeTree.TryGetScope(CompletionCandidates.OriginAt(composition.ScopeTree, ModuleUri("Main"), run.SourceLocation.Range.Start), out var scope));
        Assert.AreEqual(LexicalScopeKind.Procedure, scope.Kind);
        var block = new StatementBlock([.. run.Children]);
        var model = StatementStaticSemanticsEvaluator.Analyze(new SemanticId(scope.Uri), block, kind: run.MemberKind,
            context: new StaticEvaluationContext(composition.Resolver, scope));
        Assert.IsTrue(model.IsFullyAnalyzed, string.Join("; ", model.CompileErrors.Select(error => error.ToString())));

        // every branch of the #If blocks is lowered, as a refactoring analyzes the code: they are alternatives on its code paths.
        var instructions = InstructionListLowering.Lower(block, default, run.MemberKind).InstructionList;
        var graph = ControlFlowGraph.Of(instructions, model, blocks: ConditionalCompilationBlocks.Of(parse.PrecompilerTrivia));
        return new Analyzed(run, instructions, model, DefinitionsAndUses.Of(instructions, model, composition.ScopeTree), graph, composition.ScopeTree, prefix.Split("\r\n").Length - 1)
        {
            Module = parse.SyntaxTree!,
            Text = text,
            Context = new RenameContext(composition.Resolver, composition.ScopeTree,
                [new RenameModule(ModuleUri("Main"), parse.SyntaxTree!, text), .. others.Select(other => new RenameModule(other.Module, other.Parse.SyntaxTree!, other.Source))]),
            Directives = ExtractionDirectives.Of(parse.PrecompilerTrivia, text),
            AnalysisOf = uri => uri.AbsoluteUri == ModuleUri("Main").AbsoluteUri
                ? ExtractionModuleAnalysis.Of(uri, text, parse, composition.Resolver, composition.ScopeTree)
                : others.Where(other => other.Module.AbsoluteUri == uri.AbsoluteUri).Select(other => ExtractionModuleAnalysis.Of(uri, other.Source, other.Parse, composition.Resolver, composition.ScopeTree)).FirstOrDefault(),
        };
    }
}
