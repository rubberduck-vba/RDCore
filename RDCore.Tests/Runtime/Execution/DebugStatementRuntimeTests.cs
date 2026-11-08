using NSubstitute;
using RDCore.Parsing;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics.Instructions;
using RDCore.SDK.Services.VerboseMessages;
using RDCore.Runtime.LanguageCore.Instructions;

namespace RDCore.Tests.Runtime.Execution;

/// <summary>
/// <c>Debug.Assert</c> at run time. The <c>Debug</c> object is not MS-VBAL's — it comes from the
/// host's development environment — and <c>Assert</c> suspends execution when its expression is
/// <c>False</c>, which is the same thing a <c>Stop</c> statement does.
/// </summary>
[TestClass]
public sealed class DebugStatementRuntimeTests
{
    private static readonly Uri ProcedureUri = TestUri.TestSubProcUri();
    private static readonly SyntaxNodeId NodeId = new(ProcedureUri.AbsolutePath, [1]);

    private sealed class Provider(Symbol[] symbols) : ISymbolProvider
    {
        public IEnumerable<Symbol> ProvideSymbols() => symbols;
    }

    private static (RuntimeExecutionOutcome Outcome, IReadOnlyList<string> Output) Run(params string[] body)
    {
        var output = new RuntimeOutputBuffer();
        var session = RuntimeSessionComposer.Compose(
            new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false), [], [new Provider([])], output);

        var pipeline = RuntimeExecutionPipeline.Create(
            session, new Dictionary<SemanticId, InstructionList>(), Substitute.For<IVerboseMessageBuilder>());

        var frame = session.Symbols.CreateFrame(NodeId, new StaticSymbol("Foo", SymbolKindExt.Procedure, VBVoidType.TypeInfo));
        session.CallStack.TryPush(frame);

        var source = $"Sub Foo()\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n";
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), source);
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

        var member = parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var lowering = InstructionListLowering.Lower(new StatementBlock([.. member.Children]));
        Assert.IsEmpty(lowering.Errors, string.Join("; ", lowering.Errors.Select(error => error.Verbose)));

        var outcome = pipeline.Executor.Run(session, frame, lowering.InstructionList, new RuntimeEvaluationContext(ProcedureUri));
        return (outcome, output.Lines);
    }

    [TestMethod]
    public void AssertOnATrueExpression_CarriesOn()
    {
        var (outcome, output) = Run("Debug.Assert True", "Debug.Print \"reached\"");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        CollectionAssert.AreEqual(new[] { "reached" }, output.ToArray());
    }

    [TestMethod]
    public void AssertOnAFalseExpression_Breaks()
    {
        var (outcome, output) = Run("Debug.Assert False", "Debug.Print \"not reached\"");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Break, outcome.Kind);
        Assert.IsEmpty(output);
    }

    [TestMethod]
    public void Assert_EvaluatesItsExpression_RatherThanTestingItsPresence()
    {
        var (outcome, _) = Run("Debug.Assert 1 = 2");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Break, outcome.Kind);
    }

    [TestMethod]
    public void Assert_LetCoercesItsExpressionToBoolean()
    {
        // every VBA condition is Let-coerced to Boolean (MS-VBAL 5.5.1.2.2); 0 is False, anything else True.
        Assert.AreEqual(RuntimeExecutionOutcomeKind.Break, Run("Debug.Assert 0").Outcome.Kind);
        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, Run("Debug.Assert -1").Outcome.Kind);
    }
}
