using Antlr4.Runtime;
using Antlr4.Runtime.Atn;
using RDCore.Parsing;
using RDCore.Parsing.Syntax;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols.Operators;
using RDCore.Runtime.LanguageCore.Errors;

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
    [DataRow("Debug.Print Spc(2)-1", 22)]
    [DataRow("Debug.Print Tab-1", 19)]
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
    [DataRow("Debug.Print \"a\" (2)")]
    [DataRow("Debug.Print (1) (2)")]
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

    // MS-VBAL §3.3 makes the blanks before a token part of it, so `F (2)` is the index expression F(2) as much as `F(2)` is; in a list to print, MS-VBAL also
    // reads it as two items, F, then (2). MS-VBA calls F with 2, and the VBE writes it `F(2)`.
    [TestMethod]
    [DataRow("Debug.Print F (2)")]
    [DataRow("Debug.Print F    (2)")]
    [DataRow("Debug.Print F\t(2)")]
    [DataRow("Debug.Print obj.F (2)")]
    [DataRow("Debug.Print [F] (2)")]
    [DataRow("Debug.Print F$ (2)")]
    [DataRow("Debug.Print F (1, 2)")]
    [DataRow("Debug.Print F (1) (2)")]
    [DataRow("Debug.Print F(1) (2)")]
    [DataRow("Print #1, F (2)")]
    [DataRow("Write #1, F (2)")]
    [DataRow("obj.Print F (2)")]
    public void ANameThenBlanksThenParentheses_IsCalled_AsOneItem(string statement)
    {
        var result = ParseInProcedure(statement);

        Assert.IsEmpty(result.SyntaxErrors, string.Join(" | ", result.SyntaxErrors.Select(error => error.Description)));
        Assert.IsInstanceOfType<IndexExpressionNode>(ItemsOf(result).Single().Value);
    }

    // a name that is an operand of an output item is called as well: the blanks separate no items there either.
    [TestMethod]
    [DataRow("Debug.Print x + F (2) * 3", typeof(VBBinaryOperatorExpressionNode))]
    [DataRow("Debug.Print -F (2)", typeof(VBUnaryOperatorExpressionNode))]
    public void ANameThenBlanksThenParentheses_InAnOperandOfAnItem_IsCalled(string statement, Type item)
    {
        var result = ParseInProcedure(statement);

        Assert.IsEmpty(result.SyntaxErrors, string.Join(" | ", result.SyntaxErrors.Select(error => error.Description)));
        var value = ItemsOf(result).Single().Value!;
        Assert.IsInstanceOfType(value, item);
        Assert.IsTrue(value.Descendants().OfType<IndexExpressionNode>().Any(index => index.Arguments.Length == 1), "F is called with 2");
    }

    // what is called after blanks is one item of the list, and what follows its separator another.
    [TestMethod]
    public void ANameThenBlanksThenParentheses_ThenASeparator_IsOneItemOfTwo()
    {
        var items = ItemsOf(ParseInProcedure("Debug.Print F (2); 3")).Where(item => item.Value is not null).ToArray();

        Assert.HasCount(2, items);
        Assert.IsInstanceOfType<IndexExpressionNode>(items[0].Value);
        Assert.IsInstanceOfType<LiteralExpressionNode>(items[1].Value);
    }

    // right after what a call statement calls, blanks before parentheses begin its arguments: `F (2)` is F called with the argument (2), which is
    // parenthesized, and not with 2.
    [TestMethod]
    [DataRow("F (2)")]
    [DataRow("obj.F (2)")]
    [DataRow("F\t(2)")]
    public void ANameThenBlanksThenParentheses_AsACallStatement_IsCalledWithAParenthesizedArgument(string statement)
    {
        var result = ParseInProcedure(statement);

        Assert.IsEmpty(result.SyntaxErrors, string.Join(" | ", result.SyntaxErrors.Select(error => error.Description)));
        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var call = member.Children.OfType<CallStatementNode>().Single();
        Assert.IsNotInstanceOfType<IndexExpressionNode>(call.Callee);
        Assert.AreEqual(OperatorSymbolNames.UnaryLetCoerceOp, ((VBUnaryOperatorExpressionNode)call.Arguments.Single()).Token);
    }

    // MS-VBAL §3.3 and MS-VBA read blanks before an argument list in any expression: the VBE writes `x = F (2)` as `x = F(2)`. So they are in a list to
    // print within the arguments of a call, in parentheses, and in a Spc or Tab clause.
    [TestMethod]
    [DataRow("x = F (2)")]
    [DataRow("Set x = F (2)")]
    [DataRow("Call F (2)")]
    [DataRow("x = a!b (2)")]
    [DataRow("Foo (F (2))")]
    [DataRow("Foo F (2)")]
    [DataRow("Foo 1, F (2)")]
    [DataRow("If F (2) Then x = 1")]
    [DataRow("For i = F (2) To 3: Next")]
    [DataRow("Do While F (2): Loop")]
    [DataRow("Debug.Assert F (2)")]
    [DataRow("Const c = F (2)")]
    [DataRow("Debug.Print F (G (2))")]
    [DataRow("Debug.Print (F (2))")]
    [DataRow("Debug.Print Spc (F (2))")]
    [DataRow("Debug.Print Tab (F (2))")]
    public void ANameThenBlanksThenParentheses_InAnyExpression_IsCalled(string statement)
    {
        var result = ParseInProcedure(statement);

        Assert.IsEmpty(result.SyntaxErrors, string.Join(" | ", result.SyntaxErrors.Select(error => error.Description)));
        Assert.IsTrue(result.SyntaxTree!.Descendants().OfType<IndexExpressionNode>().Any(index => index.Arguments.Length == 1), "F is called with 2");
    }

    // so is the target of an assignment, and `F (1) = 2` assigns to F(1), as MS-VBA reads it: it is no call statement, whose argument would be (1) = 2.
    [TestMethod]
    [DataRow("F (1) = 2")]
    [DataRow("F (1)= 2")]
    [DataRow("obj.F (1) = 2")]
    [DataRow("Me.F (1) = 2")]
    [DataRow("x.Item\t(1) = 2")]
    [DataRow("Let F (1) = 2")]
    [DataRow("Set F (1) = obj")]
    [DataRow("F (1).G = 2")]
    [DataRow("F (1) = 2: G")]
    [DataRow("If x Then F (1) = 2")]
    [DataRow("With obj\r\n        .F (1) = 2\r\n    End With")]
    public void ANameThenBlanksThenParentheses_ThenAnEqualsSign_IsAssignedTo(string statement)
    {
        var result = ParseInProcedure(statement);

        Assert.IsEmpty(result.SyntaxErrors, string.Join(" | ", result.SyntaxErrors.Select(error => error.Description)));
        Assert.IsNotNull(result.SyntaxTree);
        var assignment = result.SyntaxTree.Descendants().OfType<AssignmentStatementNode>().Single();
        Assert.IsTrue(assignment.Target.Descendants().Prepend(assignment.Target).OfType<IndexExpressionNode>().Any(index => index.Arguments.Length == 1),
            "the target is F(1)");
        Assert.IsFalse(result.SyntaxTree.Descendants().OfType<CallStatementNode>().Any(call => call.Callee is SimpleNameExpressionNode { IdentifierName: "F" }),
            "F is no call statement");
    }

    // at the start of an output clause, Tab and Spc( are a clause, never a name an expression begins with: what follows the clause is a second item, or
    // nothing the list can hold. MS-VBA refuses a clause before `&`, kept as typed, with "Compile error: Syntax error" (Excel, oracle\s71 B01-B03, B09).
    [TestMethod]
    [DataRow("Debug.Print Spc(2) & \"x\"")]
    [DataRow("Debug.Print Tab(2) & \"x\"")]
    [DataRow("Debug.Print Tab & \"x\"")]
    [DataRow("Debug.Print Spc(2)&\"x\"")]
    [DataRow("Print #1, Spc(2) & \"x\"")]
    public void ATabOrSpcClauseBeforeAnOperator_IsASyntaxError(string statement)
    {
        var result = ParseInProcedure(statement);

        Assert.IsNotEmpty(result.SyntaxErrors, statement);
        Assert.IsTrue(result.SyntaxErrors.All(error => error.VBCompileErrorId == VBCompileErrorId.SyntaxError),
            string.Join(" | ", result.SyntaxErrors.Select(error => $"{error.VBCompileErrorId}: {error.Description}")));
    }

    // a clause, then a negation with a space before it, is two items, as MS-VBA reads it: the VBE writes `Spc(2); -1` and prints -1 after the clause (Excel,
    // oracle\s71 B04, B06). Without the space (`Spc(2)-1`), the negation is written right after the clause, which is VBC00061 (B07, B08).
    [TestMethod]
    [DataRow("Debug.Print Spc(2) - 1")]
    [DataRow("Debug.Print Tab - 1")]
    [DataRow("Print #1, Tab(2) - 1")]
    public void ATabOrSpcClauseThenASpaceThenANegation_IsTwoItems(string statement)
    {
        var result = ParseInProcedure(statement);

        Assert.IsEmpty(result.SyntaxErrors, string.Join(" | ", result.SyntaxErrors.Select(error => error.Description)));
        var items = ItemsOf(result).Where(item => item.Value is not null).ToArray();
        Assert.HasCount(2, items);
        Assert.IsTrue(items[0].Value is PrintSpcClauseNode or PrintTabClauseNode, items[0].Value!.GetType().Name);
        Assert.IsInstanceOfType<LiteralExpressionNode>(items[1].Value, "-1, a literal, and no subtraction of the clause");
    }

    // a clause, then a unary + with a space before it, is two items too, as MS-VBA reads it: the VBE writes `Tab(2); 1`, and prints 1 after the clause. The +
    // is erased, and is no addition to the clause.
    [TestMethod]
    [DataRow("Debug.Print Tab(2) + 1")]
    [DataRow("Debug.Print Spc(2) + 1")]
    [DataRow("Print #1, Tab + 1")]
    public void ATabOrSpcClauseThenASpaceThenAUnaryPlus_IsTwoItems(string statement)
    {
        var result = ParseInProcedure(statement);

        Assert.IsEmpty(result.SyntaxErrors, string.Join(" | ", result.SyntaxErrors.Select(error => error.Description)));
        var items = ItemsOf(result).Where(item => item.Value is not null).ToArray();
        Assert.HasCount(2, items);
        Assert.IsTrue(items[0].Value is PrintSpcClauseNode or PrintTabClauseNode, items[0].Value!.GetType().Name);
        Assert.IsInstanceOfType<LiteralExpressionNode>(items[1].Value, "1, a literal, and no addition to the clause");
        Assert.ContainsSingle(result.Trivia.OfType<ErasedPlusTriviaNode>());
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
            CallStatementNode { Callee: ObjectPrintExpressionNode print } => print.Items,
            var other => throw new AssertFailedException($"not a print statement: {other.GetType().Name}"),
        };
    }

    // SLL, which the parse tries first, reads the context of a rule as the grammar's every context of it. With an output item allowed right after an
    // expression, the `(` after a name could begin an item as well as the name's argument list: telling them apart read to the end of a nested call, once
    // for each call it is nested in. What a decision of lExpression reads ahead is now the same at any depth, and what every decision reads together is in
    // proportion to the depth.
    [TestMethod]
    public void TheArgumentListOfANestedCall_IsToldFromAnOutputItem_WithoutReadingToItsEnd()
    {
        var shallow = LExpressionLookahead("Public Const X = " + NestedCalls(20) + "\r\n");
        var deep = LExpressionLookahead("Public Const X = " + NestedCalls(40) + "\r\n");
        var shallowTotal = Lookahead("Public Const X = " + NestedCalls(20) + "\r\n");
        var deepTotal = Lookahead("Public Const X = " + NestedCalls(40) + "\r\n");

        Assert.IsGreaterThan(0, shallow);
        Assert.AreEqual(shallow, deep);
        Assert.IsLessThan(5, deep);
        Assert.IsGreaterThan(0L, shallowTotal);
        Assert.IsLessThanOrEqualTo(2 * shallowTotal, deepTotal, $"{shallowTotal} tokens read ahead at a depth of 20, {deepTotal} at 40");
    }

    // a call statement whose argument is parenthesized is told from an assignment to an index (`Foo (1) = 2`) at the end of the statement, which the
    // prediction of the statement reads to once: as far again in a statement twice as long, and no further.
    [TestMethod]
    public void ACallStatementWithAParenthesizedArgument_IsReadAheadOnce()
    {
        var shallow = Lookahead(InProcedure("Foo (" + NestedCalls(20) + ")"));
        var deep = Lookahead(InProcedure("Foo (" + NestedCalls(40) + ")"));
        var fewTerms = Lookahead(InProcedure("Foo (1)" + string.Concat(Enumerable.Repeat(" + (1)", 20)) + " + 1"));
        var manyTerms = Lookahead(InProcedure("Foo (1)" + string.Concat(Enumerable.Repeat(" + (1)", 40)) + " + 1"));

        Assert.IsGreaterThan(0L, shallow);
        Assert.IsLessThanOrEqualTo(2 * shallow, deep, $"{shallow} tokens read ahead with 20 nested calls, {deep} with 40");
        Assert.IsGreaterThan(0L, fewTerms);
        Assert.IsLessThanOrEqualTo(2 * fewTerms, manyTerms, $"{fewTerms} tokens read ahead with 20 terms, {manyTerms} with 40");
    }

    // a name called after blanks in a list to print is read ahead to the end of its own argument list, and no further: as far for the last name of a list
    // as for the first.
    [TestMethod]
    public void NamesCalledAfterBlanks_AreEachReadAheadToTheEndOfTheirArguments()
    {
        var few = Lookahead(InProcedure("Debug.Print " + string.Join("; ", Enumerable.Repeat("F (x)", 20))));
        var many = Lookahead(InProcedure("Debug.Print " + string.Join("; ", Enumerable.Repeat("F (x)", 40))));

        Assert.IsGreaterThan(0L, few);
        Assert.IsLessThanOrEqualTo(2 * few, many, $"{few} tokens read ahead for 20 names, {many} for 40");
    }

    // SLL reads the context of a rule as the grammar's every context of it, and after a name, blanks then `(` can begin the argument of a call statement or a
    // second output item as well as the name's own arguments: the prediction at each call reads to the end of its arguments, which hold every call nested
    // in it. A chain of calls each written with blanks is read ahead as the square of its depth: four times as far at twice the depth, and no further.
    [TestMethod]
    [DataRow("x = {0}")]
    [DataRow("Debug.Print {0}")]
    [DataRow("Foo ({0})")]
    public void CallsNestedAfterBlanks_AreEachReadAheadToTheEndOfTheirArguments(string statement)
    {
        var shallow = Lookahead(InProcedure(string.Format(statement, NestedCalls(20, "F ("))));
        var deep = Lookahead(InProcedure(string.Format(statement, NestedCalls(40, "F ("))));

        Assert.IsGreaterThan(0L, shallow);
        Assert.IsLessThanOrEqualTo(4 * shallow, deep, $"{shallow} tokens read ahead at a depth of 20, {deep} at 40");
    }

    // the forms the VBE writes, with no blanks before an argument list, are read ahead in proportion to their length: twice as far at twice the depth, and
    // no further.
    [TestMethod]
    [DataRow("x = {0}", "F(")]
    [DataRow("Debug.Print {0}", "F(")]
    [DataRow("Foo {0}", "F(")]
    [DataRow("Foo ({0})", "F(")]
    [DataRow("Call Foo({0})", "F(")]
    [DataRow("If {0} Then x = 1", "F(")]
    [DataRow("x = {0}", "(")]
    [DataRow("Line ({0}, 0)-(1, 1)", "F(")]
    [DataRow("Me.Line (0, {0}) - (1, 1)", "F(")]
    public void CallsNestedAsTheVBEWritesThem_AreReadAheadInProportionToTheirDepth(string statement, string call)
    {
        var shallow = Lookahead(InProcedure(string.Format(statement, NestedCalls(20, call))));
        var deep = Lookahead(InProcedure(string.Format(statement, NestedCalls(40, call))));

        Assert.IsGreaterThan(0L, shallow);
        Assert.IsLessThanOrEqualTo(2 * shallow, deep, $"{shallow} tokens read ahead at a depth of 20, {deep} at 40");
    }

    // as above, for a chain of the links the VBE writes.
    [TestMethod]
    [DataRow("x = a", "(1).b", "")]
    [DataRow("x = a", ".b(1)", "")]
    [DataRow("a", "(1).b", " = 2")]
    [DataRow("x = 1", " + F(1)", "")]
    [DataRow("Debug.Print 1", "; F(1)", "")]
    [DataRow("Foo 1", ", F(1)", "")]
    public void ChainsAsTheVBEWritesThem_AreReadAheadInProportionToTheirLength(string head, string link, string tail)
    {
        var shallow = Lookahead(InProcedure(head + string.Concat(Enumerable.Repeat(link, 20)) + tail));
        var deep = Lookahead(InProcedure(head + string.Concat(Enumerable.Repeat(link, 40)) + tail));

        Assert.IsGreaterThan(0L, shallow);
        Assert.IsLessThanOrEqualTo(2 * shallow, deep, $"{shallow} tokens read ahead with 20 links, {deep} with 40");
    }

    private static string NestedCalls(int depth, string call = "F(") => string.Concat(Enumerable.Repeat(call, depth)) + "1" + new string(')', depth);

    private static string InProcedure(string statement) => $"Public Sub DoWork()\r\n    {statement}\r\nEnd Sub\r\n";

    // the most tokens a decision of lExpression read ahead in a module.
    private static int LExpressionLookahead(string code)
        => (int)Decisions(code).Where(decision => decision.Rule == VBAParser.RULE_lExpression).Max(decision => decision.Info.SLL_MaxLook);

    // the tokens every decision read ahead in a module, together.
    private static long Lookahead(string code)
        => Decisions(code).Sum(decision => decision.Info.SLL_TotalLook);

    // the decisions in a parse of a module that parses, each with the rule it is a decision of, with a prediction cache of its own: the cache of VBAParser
    // is the process's, shared with every other parse, and what it already holds is read ahead of nothing.
    private static IEnumerable<(int Rule, DecisionInfo Info)> Decisions(string code)
    {
        var atn = new ATNDeserializer().Deserialize(VBAParser._serializedATN.ToCharArray());
        var parser = new VBAParser(new CommonTokenStream(new VBALexer(new AntlrInputStream(code))));
        parser.Interpreter = new ParserATNSimulator(parser, atn);

        // the profiler replaces the simulator with one of its ATN, and takes no prediction mode from it: the mode is set on the profiler.
        parser.Profile = true;
        parser.Interpreter.PredictionMode = PredictionMode.Sll;
        parser.RemoveErrorListeners();
        parser.startRule();
        Assert.AreEqual(0, parser.NumberOfSyntaxErrors, "syntax errors");

        return parser.ParseInfo.DecisionInfo.Select(decision => (atn.decisionToState[decision.decision].ruleIndex, decision)).ToArray();
    }
}
