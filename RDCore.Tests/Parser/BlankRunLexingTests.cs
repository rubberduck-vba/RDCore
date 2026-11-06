using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using RDCore.Parsing;
using RDCore.Parsing.Syntax;

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

    // a member access or an index written over a line continuation with blanks before it reads as it did when the continuation began with all of them.
    [TestMethod]
    [DataRow("Set x = obj   _\r\n        .Item")]
    [DataRow("x = obj   _\r\n        (1)")]
    [DataRow("With obj\r\n        x = .Item   _\r\n            .Value\r\n    End With")]
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
}
