using Antlr4.Runtime;
using Antlr4.Runtime.Atn;
using RDCore.Parsing;
using RDCore.Parsing.Syntax;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols.Operators;

namespace RDCore.Tests.Parser;

/// <summary>
/// RD-VBAL §5.4.5.8: two output clauses are written with a space, a <c>;</c> or a <c>,</c> between them, as the VBE writes them (oracle\s69). MS-VBAL
/// §5.4.5.8.1 lets an output item follow another with nothing between them.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.4.5.8 Print Statement")]
public class OutputListSeparationTests
{
    private static ModuleParseResult ParseInProcedure(string statement)
        => new ModuleParser().Parse(TestUri.TestModuleUri(), $"Public Sub DoWork()\r\n    {statement}\r\nEnd Sub\r\n");

    [TestMethod]
    [DataRow("Debug.Print 1(2)", 17)]
    [DataRow("Debug.Print \"a\"(2)", 19)]
    [DataRow("Debug.Print (1)(2)", 19)]
    [DataRow("Debug.Print Spc(1)(2)", 22)]
    [DataRow("Debug.Print Tab(1)(2)", 22)]
    [DataRow("Debug.Print \"a\"x", 19)]
    [DataRow("Debug.Print 1; 2(3)", 20)]
    [DataRow("Print #1, 1(2)", 15)]
    [DataRow("Print #1, \"a\"x", 17)]
    [DataRow("Print #1, Spc(1)Tab(2)", 20)]
    [DataRow("Write #1, 1(2)", 15)]
    [DataRow("Print 1(2)", 11)]
    public void AnOutputItemWrittenRightAfterAnother_IsOutputItemsNotSeparated_AtIt(string statement, int column)
    {
        var result = ParseInProcedure(statement);

        var error = result.SyntaxErrors.Single();
        Assert.AreEqual(VBCompileErrorId.OutputItemsNotSeparated, error.VBCompileErrorId);
        Assert.AreEqual((1, column), (error.Location.Range.Start.Line, error.Location.Range.Start.Character));
    }

    [TestMethod]
    [DataRow("Debug.Print 1 (2)")]
    [DataRow("Debug.Print 1;(2)")]
    [DataRow("Debug.Print 1; (2)")]
    [DataRow("Debug.Print 1,(2)")]
    [DataRow("Debug.Print 1 _\r\n        (2)")]
    [DataRow("Print #1, 1 (2)")]
    [DataRow("Write #1, 1 (2)")]
    public void TwoOutputItemsWithASeparatorBetweenThem_AreTwoItems(string statement)
    {
        var result = ParseInProcedure(statement);

        Assert.IsEmpty(result.SyntaxErrors, string.Join(" | ", result.SyntaxErrors.Select(error => error.Description)));
        var items = ItemsOf(result).Where(item => item.Value is not null).ToArray();
        Assert.HasCount(2, items);
        Assert.AreEqual(OperatorSymbolNames.UnaryLetCoerceOp, ((VBUnaryOperatorExpressionNode)items[1].Value!).Token);
    }

    [TestMethod]
    [DataRow("Debug.Print F(2)")]
    [DataRow("Debug.Print F(1)(2)")]
    [DataRow("Print #1, F(2)")]
    public void ANameRightBeforeParentheses_IsCalled_AsOneItem(string statement)
    {
        var result = ParseInProcedure(statement);

        Assert.IsEmpty(result.SyntaxErrors, string.Join(" | ", result.SyntaxErrors.Select(error => error.Description)));
        Assert.IsInstanceOfType<IndexExpressionNode>(ItemsOf(result).Single().Value);
    }

    // a KNOWN DIFFERENCE from MS-VBA, pinned as RD-VBA reads it today, as main did before VBC00061: the grammar allows no space before the argument list of a
    // name (indexExpr), so `F (2)` is two items, F with no argument, then (2). MS-VBA calls F with 2, and the VBE writes it `F(2)` (oracle\s70 A09).
    // fix/print-name-space-paren is to read it as the VBE does.
    [TestMethod]
    [DataRow("Debug.Print F (2)")]
    [DataRow("Print #1, F (2)")]
    public void ANameThenASpaceThenParentheses_IsTwoItems_UnlikeMSVBA(string statement)
    {
        var result = ParseInProcedure(statement);

        Assert.IsEmpty(result.SyntaxErrors, string.Join(" | ", result.SyntaxErrors.Select(error => error.Description)));
        var items = ItemsOf(result).Where(item => item.Value is not null).ToArray();
        Assert.HasCount(2, items);
        Assert.IsNotInstanceOfType<IndexExpressionNode>(items[0].Value);
    }

    // a Spc clause that is not complete is the parser's error as any other is, said with what it could not read: not the code of the grammar's predicate
    // that tells a Spc clause from a call of a name Spc.
    [TestMethod]
    public void ASpcClauseThatIsNotComplete_IsASyntaxError_OfWhatIsNotRead()
    {
        var error = ParseInProcedure("Debug.Print Spc(1").SyntaxErrors.Single();

        Assert.AreEqual(VBCompileErrorId.SyntaxError, error.VBCompileErrorId);
        Assert.AreEqual("no viable alternative at input 'Spc'", error.Verbose);
    }

    private static IEnumerable<PrintOutputItemNode> ItemsOf(ModuleParseResult result)
    {
        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        return member.Children.Single() switch
        {
            DebugPrintStatementNode print => print.Items,
            PrintStatementNode print => print.Items,
            var other => throw new AssertFailedException($"not a print statement: {other.GetType().Name}"),
        };
    }

    // SLL, which the parse tries first, reads the context of a rule as the grammar's every context of it. With an output item allowed right after an
    // expression, the `(` after a name could begin an item as well as the name's argument list: telling them apart read to the end of a nested call, once
    // for each call it is nested in. What a decision of lExpression reads ahead is now the same at any depth.
    [TestMethod]
    public void TheArgumentListOfANestedCall_IsToldFromAnOutputItem_WithoutReadingToItsEnd()
    {
        var shallow = LExpressionLookahead(20);
        var deep = LExpressionLookahead(40);

        Assert.AreEqual(shallow, deep);
        Assert.IsLessThan(5, deep);
    }

    // the most tokens a decision of lExpression read ahead, in a module-level constant of calls nested `depth` deep, with a prediction cache of its own: the
    // cache of VBAParser is the process's, shared with every other parse, and what it already holds is read ahead of nothing.
    private static int LExpressionLookahead(int depth)
    {
        var code = "Public Const X = " + string.Concat(Enumerable.Repeat("F(", depth)) + "1" + new string(')', depth) + "\r\n";
        var atn = new ATNDeserializer().Deserialize(VBAParser._serializedATN.ToCharArray());
        var parser = new VBAParser(new CommonTokenStream(new VBALexer(new AntlrInputStream(code))));
        parser.Interpreter = new ParserATNSimulator(parser, atn);

        // the profiler replaces the simulator with one of its ATN, and takes no prediction mode from it: the mode is set on the profiler.
        parser.Profile = true;
        parser.Interpreter.PredictionMode = PredictionMode.Sll;
        parser.RemoveErrorListeners();
        parser.startRule();
        Assert.AreEqual(0, parser.NumberOfSyntaxErrors);

        return (int)parser.ParseInfo.DecisionInfo
            .Where(decision => atn.decisionToState[decision.decision].ruleIndex == VBAParser.RULE_lExpression)
            .Max(decision => decision.SLL_MaxLook);
    }
}
