using NSubstitute;
using RDCore.Parsing;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics.Instructions;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Tests.Runtime.Execution;

/// <summary>
/// <c>Debug.Print</c> end to end: real source, really parsed, really lowered, really executed, with
/// the output read back off the session's own output channel. The formatting rules asserted here are
/// <strong>MS-VBAL §5.4.5.8</strong>'s, down to the spaces a numeric value is padded with.
/// </summary>
[TestClass]
public sealed class PrintOutputEvaluatorTests
{
    private static readonly Uri ProcedureUri = TestUri.TestSubProcUri();
    private static readonly SyntaxNodeId NodeId = new(ProcedureUri.AbsolutePath, [1]);

    private sealed class Provider(Symbol[] symbols) : ISymbolProvider
    {
        public IEnumerable<Symbol> ProvideSymbols() => symbols;
    }

    /// <summary>
    /// Runs <paramref name="body"/> as the body of a procedure and returns everything it printed.
    /// </summary>
    private static IReadOnlyList<string> Print(params string[] body)
    {
        var output = new RuntimeOutputBuffer();
        var session = RuntimeSessionComposer.Compose(
            new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false), [], [new Provider([])], output);

        var pipeline = RuntimeExecutionPipeline.Create(
            session, new Dictionary<SemanticId, InstructionList>(), Substitute.For<IVerboseMessageBuilder>());

        var frame = session.Symbols.CreateFrame(NodeId, new StaticSymbol("Foo", SymbolKindExt.Procedure, VBVoidType.TypeInfo));
        session.CallStack.TryPush(frame);

        var outcome = pipeline.Executor.Run(session, frame, Lower(body), new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind,
            "the body was expected to run to completion");
        return output.Lines;
    }

    private static InstructionList Lower(params string[] procedureBody)
    {
        var source = $"Sub Foo()\r\n{string.Join("\r\n", procedureBody)}\r\nEnd Sub\r\n";
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), source);
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

        var member = parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var result = InstructionListLowering.Lower(new StatementBlock([.. member.Children]));
        Assert.IsEmpty(result.Errors, string.Join("; ", result.Errors.Select(error => error.Verbose)));
        return result.InstructionList;
    }

    [TestMethod]
    public void AStringLiteral_IsPrintedAsIs()
        => CollectionAssert.AreEqual(new[] { "hello" }, Print("Debug.Print \"hello\"").ToArray());

    [TestMethod]
    public void ANumericValue_IsPaddedWithASpaceOnEachSide()
        // "...Let-coerced to String with a space character inserted as the first and the last character".
        => CollectionAssert.AreEqual(new[] { " 42 " }, Print("Debug.Print 42").ToArray());

    [TestMethod]
    public void ABooleanValue_IsTrueOrFalse_NotItsNumericValue()
        => CollectionAssert.AreEqual(new[] { "True", "False" },
            Print("Debug.Print True", "Debug.Print False").ToArray());

    [TestMethod]
    public void NoOutputList_WritesABlankLine()
        => CollectionAssert.AreEqual(new[] { "" }, Print("Debug.Print").ToArray());

    [TestMethod]
    public void EachPrintStatement_EndsItsOwnLine()
        => CollectionAssert.AreEqual(new[] { "a", "b" },
            Print("Debug.Print \"a\"", "Debug.Print \"b\"").ToArray());

    [TestMethod]
    public void ATrailingSemicolon_LeavesTheLineOpenForTheNextStatement()
        => CollectionAssert.AreEqual(new[] { "ab" },
            Print("Debug.Print \"a\";", "Debug.Print \"b\"").ToArray());

    [TestMethod]
    public void ASemicolonBetweenItems_JoinsThemWithNothingInBetween()
        => CollectionAssert.AreEqual(new[] { "ab" }, Print("Debug.Print \"a\"; \"b\"").ToArray());

    [TestMethod]
    public void ACommaBetweenItems_AdvancesToTheNextFourteenColumnPrintZone()
        => CollectionAssert.AreEqual(new[] { "a" + new string(' ', 13) + "b" },
            Print("Debug.Print \"a\", \"b\"").ToArray());

    [TestMethod]
    public void ACommaAtAZoneBoundary_StillAdvancesAWholeZone()
    {
        // "...the print zone is advanced even if the current position is already at the beginning of
        // a print zone" - fourteen characters, then a comma, lands in the third zone, not the second.
        var fourteen = new string('x', 14);

        CollectionAssert.AreEqual(new[] { fourteen + new string(' ', 14) + "b" },
            Print($"Debug.Print \"{fourteen}\", \"b\"").ToArray());
    }

    [TestMethod]
    public void Spc_WritesThatManySpaces()
        => CollectionAssert.AreEqual(new[] { "   x" }, Print("Debug.Print Spc(3); \"x\"").ToArray());

    [TestMethod]
    public void Tab_MovesToThatColumn()
        // column 10, one-based: nine spaces before it.
        => CollectionAssert.AreEqual(new[] { new string(' ', 9) + "x" },
            Print("Debug.Print Tab(10); \"x\"").ToArray());

    [TestMethod]
    public void BareTab_AdvancesToTheNextPrintZone()
        => CollectionAssert.AreEqual(new[] { "a" + new string(' ', 13) + "b" },
            Print("Debug.Print \"a\"; Tab; \"b\"").ToArray());

    [TestMethod]
    public void AnExpression_IsEvaluatedBeforeItIsPrinted()
        => CollectionAssert.AreEqual(new[] { " 7 " }, Print("Debug.Print 3 + 4").ToArray());
}
