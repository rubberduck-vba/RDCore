using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Flow;
using RDCore.SDK.Semantics.Instructions;
using RDCore.SDK.Semantics.Static;
using RDCore.SDK.Semantics.Static.Abstract;
using RDCore.SDK.Workspace;
using RDCore.Symbols;

namespace RDCore.Tests.Semantics.Flow;

/// <summary>
/// A procedure <c>Run</c> of a module, analyzed as the host analyzes it when it loads the code: bound, lowered, and its data flow found - what the flow analyses,
/// and the refactorings built on them, are tested on.
/// </summary>
internal static class FlowFixture
{
    private static readonly Uri Root = new("file://rdcore-test");

    private static Uri ModuleUri(string name) => new UriBuilder(Root) { Fragment = name }.Uri;

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
    /// Analyzes a procedure <c>Run</c> whose body is <paramref name="body"/>, in a module that declares what <paramref name="declarations"/> says before the rest.
    /// </summary>
    public static Analyzed Analyze(string body, string header = "Public Sub Run(ByVal n As Long)", string end = "End Sub", bool optionExplicit = true, string declarations = "")
    {
        var moduleLevel = $"{declarations}\r\n{Others}".ReplaceLineEndings("\r\n");
        var prefix = $"Attribute VB_Name = \"Main\"\r\n{(optionExplicit ? "Option Explicit" : "' no Option Explicit")}\r\n{moduleLevel}\r\n{header}\r\n";
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Main.bas"), $"{prefix}{body.ReplaceLineEndings("\r\n")}\r\n{end}\r\n");
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

        var composition = WorkspaceSymbolResolver.ComposeWithScopes(Root, [(ModuleUri("Main"), ModuleType.StdModule, parse)], new IntrinsicSymbolResolver());
        Assert.IsTrue(composition.ScopeTree.TryGetScope(ModuleUri("Main.Run"), out var scope));
        var run = parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single(member => member.Name == "Run");
        var block = new StatementBlock([.. run.Children]);
        var model = StatementStaticSemanticsEvaluator.Analyze(new SemanticId(ModuleUri("Main.Run")), block, kind: run.MemberKind,
            context: new StaticEvaluationContext(composition.Resolver, scope));
        Assert.IsTrue(model.IsFullyAnalyzed, string.Join("; ", model.CompileErrors.Select(error => error.ToString())));

        var instructions = InstructionListLowering.Lower(block, default, run.MemberKind).InstructionList;
        var graph = ControlFlowGraph.Of(instructions, model);
        return new Analyzed(run, instructions, model, DefinitionsAndUses.Of(instructions, model, composition.ScopeTree), graph, composition.ScopeTree, prefix.Split("\r\n").Length - 1);
    }
}
