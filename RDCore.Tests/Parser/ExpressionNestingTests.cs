using Antlr4.Runtime;
using Antlr4.Runtime.Atn;
using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.Parsing.Syntax;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Platform;
using RDCore.Symbols;
using RDCore.Tests.Cli;
using RDCore.Runtime.LanguageCore.Errors;

namespace RDCore.Tests.Parser;

/// <summary>
/// The depth of an expression RD-VBA reads (<strong>RD-VBAL §2.6.1</strong>): an expression nested in at most 2,500 others is read, loaded and run, as deep as MS-VBA
/// runs it (observed in Excel: 2,000 nested parentheses, 1,000 nested calls, a chain of 2,500 terms); a deeper one is the syntax error
/// <see cref="VBCompileErrorId.ExpressionNestedTooDeeply"/>, where MS-VBA crashes the host.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL 2.6.1 Syntax Errors")]
public sealed class ExpressionNestingTests
{
    private static string Parenthesized(int depth) => new string('(', depth) + "1" + new string(')', depth);

    private static string NestedAbs(int depth) => string.Concat(Enumerable.Repeat("Abs(", depth)) + "-1" + new string(')', depth);

    internal static string Chain(int terms) => string.Join(" + ", Enumerable.Repeat("1", terms));

    // the shortest chain past the limit: its first term is nested in 2,501 additions.
    internal static string ChainPastTheLimit() => Chain(ModuleParser.MaxExpressionNesting + 2);

    // a chain in parentheses, whose first term is nested in `levels` expressions: the parentheses and every addition but one, as deep as in `levels` parentheses.
    // A chain doesn't nest the parse, which reads it in a fraction of the time it reads as many parentheses.
    private static string ParenthesizedChain(int levels) => "(" + Chain(levels) + ")";

    private static ModuleParseResult Parse(string expression)
        => new ModuleParser().Parse(TestUri.TestModuleUri(), $"Public Sub Main()\r\n    Dim x As Variant\r\n    x = {expression}\r\nEnd Sub\r\n");

    private static string Describe(ModuleParseResult result)
        => string.Join("; ", result.SyntaxErrors.Select(error => $"{error.ErrorId} {error.Location.Range.Start.Line}:{error.Location.Range.Start.Character} {error.Verbose}"));

    private static async Task<string> RunAsync(string expression)
        => string.Join(" ", await ModuleWorkspace.RunAsync([], $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n    Debug.Print {expression}\r\nEnd Sub\r\n"));

    [TestMethod]
    public void AnExpressionNestedIn2500Others_IsRead()
    {
        // the literal is nested in the 2,500 parentheses around it.
        var result = Parse(Parenthesized(ModuleParser.MaxExpressionNesting));

        Assert.IsTrue(result.IsSuccess, Describe(result));
        Assert.IsNotNull(result.SyntaxTree);
        Assert.IsFalse(result.SyntaxTree.Descendants().OfType<UnbuiltExpressionTriviaNode>().Any(), "nothing of a module at the limit is kept as text");
        Assert.IsTrue(result.SyntaxTree.Descendants().OfType<LiteralExpressionNode>().Any(), "the literal the parentheses are around");
    }

    [TestMethod]
    public void AnExpressionNestedIn2501Others_IsExpressionNestedTooDeeply_AtIt()
    {
        var result = Parse(Parenthesized(ModuleParser.MaxExpressionNesting + 1));

        var error = Assert.ContainsSingle(result.SyntaxErrors, Describe(result));
        Assert.AreEqual((VBCompileErrorId.ExpressionNestedTooDeeply, 2, 4 + ModuleParser.MaxExpressionNesting + 1 + 4),
            ((VBCompileErrorId)error.ErrorId, error.Location.Range.Start.Line, error.Location.Range.Start.Character), Describe(result));
    }

    [TestMethod]
    public void AChainPastTheLimit_IsExpressionNestedTooDeeply_Once()
    {
        // the first term of a chain of additions is nested in every addition but none: 2,502 terms are 2,501 additions.
        var result = Parse(Chain(ModuleParser.MaxExpressionNesting + 2));

        Assert.AreEqual(VBCompileErrorId.ExpressionNestedTooDeeply, (VBCompileErrorId)Assert.ContainsSingle(result.SyntaxErrors, Describe(result)).ErrorId);
    }

    [TestMethod]
    public void AChainOf2501Terms_IsRead()
    {
        // its first term is nested in 2,500 additions.
        var result = Parse(Chain(ModuleParser.MaxExpressionNesting + 1));

        Assert.IsTrue(result.IsSuccess, Describe(result));
    }

    // the states the parser's prediction keeps for the choices of a rule, once an expression nested in `depth` calls is read with a prediction cache of its own:
    // the cache of VBAParser is the process's, shared with every other parse.
    private static int PredictionStatesOf(string rule, int depth)
    {
        var atn = new ATNDeserializer().Deserialize(VBAParser._serializedATN.ToCharArray());
        var parser = new VBAParser(new CommonTokenStream(new VBALexer(new AntlrInputStream(NestedAbs(depth))))) { ErrorHandler = new BailErrorStrategy() };
        parser.Interpreter = new ParserATNSimulator(parser, atn) { PredictionMode = PredictionMode.Sll };
        parser.RemoveErrorListeners();
        parser.expression();

        var ruleIndex = Array.IndexOf(parser.RuleNames, rule);
        return atn.decisionToDFA.Where(dfa => dfa.atnStartState.ruleIndex == ruleIndex).Sum(dfa => dfa.states.Count);
    }

    // an argument is told from the bounds of a ReDim (`x(1 To n)`) by what follows its expression, not by reading the expression first: read for each call it
    // is nested in, a nested call was read once for each level, and 2,500 of them took minutes to parse.
    [TestMethod]
    public void AnArgument_IsToldFromBounds_WithoutReadingItForEachCallItIsNestedIn()
        => Assert.AreEqual(PredictionStatesOf("argumentExpression", 100), PredictionStatesOf("argumentExpression", 200));

    // how many rules the parse is inside at most.
    private sealed class RuleDepth : VBAParserBaseListener
    {
        private int _current;

        public int Max { get; private set; }

        public override void EnterEveryRule(ParserRuleContext context) => Max = Math.Max(Max, ++_current);

        public override void ExitEveryRule(ParserRuleContext context) => _current--;
    }

    // the deepest the parse of a procedure goes, the {0} of its statement nested `depth` times in `open` and as many closing parentheses.
    private static int RuleDepthOf(string statement, string open, int depth)
    {
        var nested = string.Concat(Enumerable.Repeat(open, depth)) + "1" + new string(')', depth);
        var parser = new VBAParser(new CommonTokenStream(new VBALexer(new AntlrInputStream($"Public Sub Main()\r\n    {string.Format(statement, nested)}\r\nEnd Sub\r\n"))))
        {
            ErrorHandler = new BailErrorStrategy(),
        };
        parser.Interpreter.PredictionMode = PredictionMode.Sll;
        parser.RemoveErrorListeners();
        var depthOf = new RuleDepth();
        parser.AddParseListener(depthOf);
        LargeStackThread.Run(() => parser.startRule());
        return depthOf.Max;
    }

    // the ceiling of the parse (StackDepthGuardListener.MaxRuleDepth) is twice as deep as the deepest expression RD-VBA reads: a call nests the parse the
    // deepest of all the forms, 6 rules a level, and 2,500 nested calls are about 15,000 rules deep. The rules a level are counted between 100 and 200 levels.
    [TestMethod]
    [DataRow("x = {0}", "Abs(")]
    [DataRow("x = {0}", "o.F(")]
    [DataRow("x = {0}", "F(a:=")]
    [DataRow("Call F({0})", "G(")]
    public void TheDeepestExpressionRDVBAReads_IsReadAtHalfTheCeilingOfTheParse_OrLess(string statement, string open)
    {
        var (at100, at200) = (RuleDepthOf(statement, open, 100), RuleDepthOf(statement, open, 200));
        var atTheLimit = at200 + (int)Math.Ceiling((at200 - at100) / 100.0 * (ModuleParser.MaxExpressionNesting - 200));

        Assert.IsLessThanOrEqualTo(StackDepthGuardListener.MaxRuleDepth / 2, atTheLimit, $"{at100} rules at 100 levels, {at200} at 200");
    }

    // a statement, the {0} in it a parenthesized chain, and the levels of nesting it writes around it: a call's argument is nested in the call, but neither
    // passing it by name or ByVal nor printing it nests it in anything more. The parentheses after a bare call's name group its one argument, a level of their
    // own: `F (x)` nests x as `Call F((x))` does.
    [TestMethod]
    [DataRow("x = {0}", 0)]
    [DataRow("Dim y(1 To {0}) As Long", 0)]
    [DataRow("Dim y({0} To 5) As Long", 0)]
    [DataRow("Debug.Print {0}", 0)]
    [DataRow("Debug.Print 1; {0};", 0)]
    [DataRow("Debug.Print Spc({0}); 1", 0)]
    [DataRow("Debug.Print Tab({0}); 1", 0)]
    [DataRow("Call F({0})", 1)]
    [DataRow("Call F(a:={0})", 1)]
    [DataRow("Call F(ByVal {0})", 1)]
    [DataRow("x = F(1, a:={0})", 1)]
    [DataRow("F {0}", 1)]
    [DataRow("F ({0})", 2)]
    [DataRow("Call F(({0}))", 2)]
    [DataRow("F 1, {0}", 1)]
    [DataRow("F a:={0}", 1)]
    [DataRow("x = TypeOf {0} Is Object", 1)]
    public void AnExpressionAtTheLimit_IsRead_AndOnePast_IsExpressionNestedTooDeeply(string statement, int levels)
    {
        ModuleParseResult ParseWith(int nesting)
            => new ModuleParser().Parse(TestUri.TestModuleUri(), $"Public Sub Main()\r\n    {string.Format(statement, ParenthesizedChain(nesting))}\r\nEnd Sub\r\n");

        var atTheLimit = ParseWith(ModuleParser.MaxExpressionNesting - levels);
        var pastIt = ParseWith(ModuleParser.MaxExpressionNesting - levels + 1);

        Assert.IsTrue(atTheLimit.IsSuccess, Describe(atTheLimit));
        Assert.AreEqual(VBCompileErrorId.ExpressionNestedTooDeeply, (VBCompileErrorId)Assert.ContainsSingle(pastIt.SyntaxErrors, Describe(pastIt)).ErrorId);
    }

    // each link of a fluent chain is a member access and an index expression, and its owner is nested in both: b is nested in 2,500 expressions at 1,250 links.
    [TestMethod]
    [DataRow(ModuleParser.MaxExpressionNesting / 2, true)]
    [DataRow(ModuleParser.MaxExpressionNesting / 2 + 1, false)]
    public void AFluentChain_IsNestedTwoLevelsALink(int links, bool read)
    {
        var result = Parse("b" + string.Concat(Enumerable.Repeat(".Add(1)", links)));

        if (read)
        {
            Assert.IsTrue(result.IsSuccess, Describe(result));
        }
        else
        {
            Assert.AreEqual(VBCompileErrorId.ExpressionNestedTooDeeply, (VBCompileErrorId)Assert.ContainsSingle(result.SyntaxErrors, Describe(result)).ErrorId);
        }
    }

    [TestMethod]
    public void TwoArgumentsOfABareCallPastTheLimit_AreReportedOnce_AsInACallWithCall()
    {
        var past = ParenthesizedChain(ModuleParser.MaxExpressionNesting);
        ModuleParseResult ParseWith(string statement)
            => new ModuleParser().Parse(TestUri.TestModuleUri(), $"Public Sub Main()\r\n    {statement}\r\nEnd Sub\r\n");

        var bare = ParseWith($"F {past}, {past}");
        var withCall = ParseWith($"Call F({past}, {past})");

        Assert.AreEqual(1, bare.SyntaxErrors.Count(error => error.ErrorId == (int)VBCompileErrorId.ExpressionNestedTooDeeply), Describe(bare));
        Assert.AreEqual(1, withCall.SyntaxErrors.Count(error => error.ErrorId == (int)VBCompileErrorId.ExpressionNestedTooDeeply), Describe(withCall));
    }

    [TestMethod]
    public void TwoExpressionsPastTheLimit_AreEachReported()
    {
        var past = ParenthesizedChain(ModuleParser.MaxExpressionNesting + 1);
        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), $"Public Sub Main()\r\n    Dim x As Variant\r\n    x = {past}\r\n    x = {past}\r\nEnd Sub\r\n");

        Assert.AreEqual(2, result.SyntaxErrors.Count(error => error.ErrorId == (int)VBCompileErrorId.ExpressionNestedTooDeeply), Describe(result));
    }

    // a directive's expression is read by the directive pass, a parse of its own, and has the same limit: the 1 of `-…-1` is nested in as many negations as
    // there are minus signs. A chain of negations is a fraction of a second for the directive pass to read this deep, where a chain of additions is seconds.
    [TestMethod]
    [DataRow("#If {0} Then\r\nx = 1\r\n#End If")]
    [DataRow("#If False Then\r\n#ElseIf {0} Then\r\nx = 1\r\n#End If")]
    [DataRow("#Const LIMIT = {0}")]
    public void ADirectivesExpressionAtTheLimit_IsRead_AndOnePast_IsExpressionNestedTooDeeply_Once(string directive)
    {
        ModuleParseResult ParseWith(int negations)
            => new ModuleParser().Parse(TestUri.TestModuleUri(), $"Public Sub Main()\r\n{string.Format(directive, new string('-', negations) + "1")}\r\nEnd Sub\r\n");

        var atTheLimit = ParseWith(ModuleParser.MaxExpressionNesting);
        var pastIt = ParseWith(ModuleParser.MaxExpressionNesting + 1);

        Assert.IsTrue(atTheLimit.IsSuccess, Describe(atTheLimit));
        var error = Assert.ContainsSingle(pastIt.SyntaxErrors, Describe(pastIt));
        Assert.AreEqual((VBCompileErrorId.ExpressionNestedTooDeeply, directive.StartsWith("#If False") ? 2 : 1), ((VBCompileErrorId)error.ErrorId, error.Location.Range.Start.Line));
        Assert.IsTrue(pastIt.PrecompilerTrivia.SelectMany(node => node.Descendants()).Any(node => node is UnbuiltExpressionTriviaNode), "what is past the limit is an error node");
    }

    // what MS-VBA runs, RD-VBA runs: on the test's own thread, which is no larger than a thread of the language server.
    [TestMethod]
    public async Task TwoThousandNestedParentheses_Run()
        => Assert.AreEqual("1", await RunAsync(Parenthesized(2000)));

    // a nested call costs its parse more than a parenthesis does, level for level: a few hundred calls are run, as deep as parentheses are, and how deep calls are
    // read is the count of parentheses and calls alike (AnExpressionAtTheLimit_IsRead_AndOnePast_IsExpressionNestedTooDeeply).
    [TestMethod]
    public async Task NestedCalls_Run()
        => Assert.AreEqual("1", await RunAsync(NestedAbs(250)));

    [TestMethod]
    public async Task AChainOf2500Terms_Runs()
        => Assert.AreEqual("2500", await RunAsync(Chain(2500)));

    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly Uri Main = new UriBuilder(Root) { Fragment = "Main" }.Uri;

    [TestMethod]
    public void AModuleWithAnExpressionPastTheLimit_DeclaresWhatItDeclares()
    {
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Main.bas"),
            $"Attribute VB_Name = \"Main\"\r\nPublic Sub Main()\r\n    Dim x As Variant\r\n    x = {ChainPastTheLimit()}\r\nEnd Sub\r\n");
        Assert.IsTrue(parse.SyntaxErrors.Any(error => error.ErrorId == (int)VBCompileErrorId.ExpressionNestedTooDeeply), Describe(parse));

        string[] declared = [];
        var thrown = SmallStack.Run(() =>
        {
            var composition = WorkspaceSymbolResolver.ComposeWithScopes(Root, [(Main, ModuleType.StdModule, parse)], new IntrinsicSymbolResolver());
            declared = [.. new SyntaxTreeSymbolProvider(Root, Main, ModuleType.StdModule, parse, composition.Resolver, withImplicitDeclarations: true)
                .ProvideSymbols().Select(symbol => symbol.Name)];
        });

        Assert.IsNull(thrown, thrown?.ToString());
        CollectionAssert.IsSubsetOf(new[] { "Main", "x" }, declared);
    }

    [TestMethod]
    public void TheNodesOfAModuleWithAnExpressionPastTheLimit_AreWalked()
    {
        var parse = Parse(ChainPastTheLimit());

        var (literals, pruned) = (0, 0);
        var thrown = SmallStack.Run(() =>
        {
            literals = parse.SyntaxTree!.Descendants().Count(node => node is LiteralExpressionNode);
            pruned = parse.SyntaxTree!.Descendants().Count(node => node is UnbuiltExpressionTriviaNode);
        });

        // the right operand of each of the 2,500 outer additions, and both operands of the addition nested in 2,500 others, which are past the limit.
        Assert.IsNull(thrown, thrown?.ToString());
        Assert.AreEqual((ModuleParser.MaxExpressionNesting, 2), (literals, pruned));
    }
}
