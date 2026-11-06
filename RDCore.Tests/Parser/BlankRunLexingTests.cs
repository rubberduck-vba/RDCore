using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using RDCore.Parsing;
using RDCore.Parsing.Syntax;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Errors;

namespace RDCore.Tests.Parser;

/// <summary>
/// A run of blanks is lexed once over: a line continuation is the blank right before its underscore, and the blanks before that one are <c>WS</c> of
/// their own. Begun with every blank before the underscore, it was tried at each blank of a run and read the rest of the run: the directive pass, which
/// reads a line that is no directive as blanks, took 20 s over a module with a directive and one line of 64,000 characters.
/// </summary>
[TestClass]
public class BlankRunLexingTests
{
    // the characters the lexer looks at, as many times as it looks at each: what a guard of its work counts, where a clock would count the machine.
    private sealed class LookCountingCharStream(string text) : ICharStream
    {
        private readonly AntlrInputStream _input = new(text);

        public long Looks { get; private set; }

        public int La(int i)
        {
            Looks++;
            return _input.La(i);
        }

        public void Consume() => _input.Consume();
        public int Mark() => _input.Mark();
        public void Release(int marker) => _input.Release(marker);
        public int Index => _input.Index;
        public void Seek(int index) => _input.Seek(index);
        public int Size => _input.Size;
        public string SourceName => _input.SourceName;
        public string GetText(Interval interval) => _input.GetText(interval);
    }

    // the most looks the lexer takes at each blank of a run: it takes 4, trying a WS and a continuation at each. Tried at each blank to the end of the run, a
    // continuation took as many looks as the run has blanks for each of them: 32 million for a run of 8,000.
    private const long LooksABlank = 8;

    private static (long Looks, IList<IToken> Tokens) Lex(string text)
    {
        var input = new LookCountingCharStream(text);
        var tokens = new VBALexer(input).GetAllTokens();
        return (input.Looks, tokens);
    }

    [TestMethod]
    [DataRow(' ', DisplayName = "spaces")]
    [DataRow('\t', DisplayName = "tabs")]
    public void ARunOfBlanks_IsLookedAtAFewTimesABlank(char blank)
    {
        const int length = 8_000;
        var (looks, tokens) = Lex(new string(blank, length) + "x\n");

        Assert.HasCount(length + 2, tokens, "each blank is a WS of its own, then the name and the line break");
        Assert.IsGreaterThan(0L, looks, "the lexer's looks are counted");
        Assert.IsLessThanOrEqualTo(LooksABlank * length, looks, $"{looks:n0} looks at {length:n0} blanks");
    }

    // a continuation keeps the text it is written as: the tokens of a line are its characters, the blanks before the continuation's own are WS.
    [TestMethod]
    public void TheBlanksBeforeALineContinuation_AreWS_AndItsOwnIsTheBlankRightBeforeTheUnderscore()
    {
        const string text = "x   _\r\n    .y\n";
        var (_, tokens) = Lex(text);

        Assert.AreEqual(text, string.Concat(tokens.Select(token => token.Text)));
        var continuation = tokens.Single(token => token.Type == VBALexer.LINE_CONTINUATION);
        Assert.AreEqual(" _\r\n    ", continuation.Text);
        Assert.AreEqual((1, 3), (continuation.Line, continuation.Column));
        Assert.AreEqual(2, tokens.Count(token => token.Type == VBALexer.WS), "the two blanks before the continuation's");
    }

    // a line continuation is a blank, an underscore, and the end of the line, the blanks after the underscore aside: an underscore anywhere else is none, and
    // the tokens of a line are its characters, whatever they are.
    [TestMethod]
    [DataRow("x _\r\n    y\n", 1, DisplayName = "a space before the underscore")]
    [DataRow("x\t_\r\n    y\n", 1, DisplayName = "a tab before the underscore")]
    [DataRow("x \t \t_\r\n    y\n", 1, DisplayName = "spaces and tabs before the underscore")]
    [DataRow("x _ \t\r\n    y\n", 1, DisplayName = "blanks after the underscore")]
    [DataRow("x _\n    y\n", 1, DisplayName = "a line feed alone")]
    [DataRow("x   _\r\n   _\r\n    y\n", 2, DisplayName = "a line of an underscore alone, continued")]
    [DataRow("' a comment   _\r\n    continued\n", 1, DisplayName = "a comment, continued")]
    [DataRow("a_b\n", 0, DisplayName = "an underscore in a name")]
    [DataRow("a _b\n", 0, DisplayName = "an underscore before a name")]
    [DataRow("x   _", 0, DisplayName = "an underscore at the end of the module")]
    [DataRow("x_\r\n", 0, DisplayName = "an underscore with no blank before it")]
    public void ALineContinuation_IsABlankAnUnderscoreAndTheEndOfTheLine(string text, int continuations)
    {
        var (_, tokens) = Lex(text);

        Assert.AreEqual(text, string.Concat(tokens.Select(token => token.Text)));
        Assert.AreEqual(continuations, tokens.Count(token => token.Type == VBALexer.LINE_CONTINUATION));
        Assert.IsTrue(tokens.Where(token => token.Type == VBALexer.LINE_CONTINUATION).All(token => token.Text[0] is ' ' or '\t' && token.Text[1] == '_'),
            "each continuation begins with the blank right before its underscore");
    }

    // a member access or an index written over a line continuation with blanks before it reads as it did when the continuation began with all of them.
    [TestMethod]
    [DataRow("Set x = obj   _\r\n        .Item")]
    [DataRow("x = obj   _\r\n        (1)")]
    [DataRow("With obj\r\n        x = .Item   _\r\n            .Value\r\n    End With")]
    [DataRow("On   _\r\n        Error Resume Next")]
    [DataRow("Exit\t\t_\r\n        Sub")]
    public void AnExpressionContinued_AfterSeveralBlanks_IsRead(string statement)
    {
        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), $"Public Sub DoWork()\r\n    Dim x As Variant, obj As Object\r\n    {statement}\r\nEnd Sub\r\n");

        Assert.IsEmpty(result.SyntaxErrors, string.Join(" | ", result.SyntaxErrors.Select(error => error.Description)));
    }

    // a module with a directive is read by the directive pass, as blanks but for its directives: a line of 64,000 characters is 64,000 blanks there.
    [TestMethod]
    public void AModuleWithADirectiveAndALongLine_IsReadAsFastAsItsBlanksAreLexed()
    {
        var (looks, _) = Lex(new string(' ', 64_000) + "\n");
        var result = new ModuleParser().Parse(TestUri.TestModuleUri(),
            $"#Const DEBUGMODE = 1\r\nPublic Sub DoWork()\r\n    Dim x As String\r\n    x = \"{new string('a', 64_000)}\"\r\nEnd Sub\r\n");

        Assert.IsEmpty(result.SyntaxErrors, string.Join(" | ", result.SyntaxErrors.Select(error => error.Description)));
        Assert.IsGreaterThan(0L, looks);
        Assert.IsLessThanOrEqualTo(LooksABlank * 64_000, looks, $"{looks:n0} looks at the blanks of the line");
    }

    // a parse that fails after the directive pass (a module nested deeper than the parse reads) holds its directives to the expression limit, as a module
    // that was read does: a #Const past it is an error node, and VBC00060, beside the error that says the module is nested too deeply to be read.
    [TestMethod]
    public void AFailedParse_PrunesItsDirectives()
    {
        var depth = StackDepthGuardListener.MaxRuleDepth;
        var result = new ModuleParser().Parse(TestUri.TestModuleUri(),
            $"#Const L = {new string('-', ModuleParser.MaxExpressionNesting + 1)}1\r\nPublic Const X = {new string('(', depth)}1{new string(')', depth)}\r\n");

        var errors = string.Join(" | ", result.SyntaxErrors.Select(error => $"{error.VBCompileErrorId}@{error.Location.Range.Start.Line}"));
        Assert.IsTrue(result.SyntaxErrors.Any(error => error.Verbose == SDK.Exceptions.VBCompileError_ModuleNestedTooDeeplyToRead_Verbose), errors);
        Assert.IsTrue(result.SyntaxErrors.Any(error => error.VBCompileErrorId == VBCompileErrorId.ExpressionNestedTooDeeply && error.Location.Range.Start.Line == 0), errors);
        var constant = result.PrecompilerTrivia.SelectMany(node => node.Descendants().Prepend(node)).OfType<PrecompilerConstantDeclarationNode>().Single();
        Assert.IsTrue(constant.Descendants().Any(node => node is UnbuiltExpressionTriviaNode), "the constant's value past the limit is an error node");
    }
}
