using Antlr4.Runtime;
using Antlr4.Runtime.Atn;
using Antlr4.Runtime.Tree;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RDCore.Parsing;
using RDCore.Parsing.Syntax;
using RDCore.ParseServer.Handlers;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server.Configuration;

namespace RDCore.Tests.Parser;

/// <summary>
/// A parse that is no longer wanted stops: its text has changed since it was asked for, or whoever asked gave up. It stops at the next rule the parser
/// enters, and throws <see cref="OperationCanceledException"/>: a request for it is answered as cancelled (JSON-RPC's -32800), not with a failed parse.
/// </summary>
[TestClass]
public sealed class ParseCancellationTests
{
    private static readonly Uri Uri = TestUri.TestModuleUri();

    // a module long enough that its parse takes a while: a procedure of many statements, many times.
    private static string LongModule()
        => string.Concat(Enumerable.Range(0, 200).Select(index =>
            $"Public Sub P{index}()\r\n" + string.Concat(Enumerable.Range(0, 50).Select(line => $"    Debug.Print {line} + {index} * 2\r\n")) + "End Sub\r\n"));

    [TestMethod]
    public void AParse_WhoseTokenIsCancelled_ThrowsThatItWasCancelled()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        _ = Assert.ThrowsExactly<OperationCanceledException>(() => new ModuleParser().Parse(Uri, "Public Sub Foo()\r\nEnd Sub\r\n", token: cancelled.Token));
    }

    // counts the rules the parser enters, and cancels the token as it enters the one it is told of.
    private sealed class CancellingAtRule(int rule, CancellationTokenSource cancellation) : IParseTreeListener
    {
        public int Entered { get; private set; }

        public void EnterEveryRule(ParserRuleContext context)
        {
            if (++Entered == rule)
            {
                cancellation.Cancel();
            }
        }

        public void ExitEveryRule(ParserRuleContext context) { }

        public void VisitTerminal(ITerminalNode node) { }

        public void VisitErrorNode(IErrorNode node) { }
    }

    // a parse cancelled as it runs stops at the next rule it enters. The token is cancelled at a rule the parse is known to reach, by a listener that counts
    // them, rather than after a time: what a clock would decide differs from one machine to the next, and a warm parse can end before a timer fires.
    [TestMethod]
    public void AParse_CancelledAsItRuns_StopsAtTheRuleItIsCancelledAt()
    {
        const int Rule = 1_000;
        using var cancellation = new CancellationTokenSource();
        var counting = new CancellingAtRule(Rule, cancellation);
        var parser = new VBAParser(new CommonTokenStream(new VBALexer(new AntlrInputStream(LongModule()))));
        parser.Interpreter.PredictionMode = PredictionMode.Sll;
        parser.RemoveErrorListeners();
        parser.AddParseListener(counting);
        parser.AddParseListener(new CancellationListener(cancellation.Token));

        var thrown = Assert.Throws<OperationCanceledException>(() => parser.startRule());

        Assert.AreEqual(cancellation.Token, thrown.CancellationToken);
        Assert.AreEqual(Rule, counting.Entered, "the parse stopped at the rule its token was cancelled at, not at its end");
    }

    [TestMethod]
    [DataRow("Public Sub Foo()\r\n    Debug.Print 1\r\nEnd Sub\r\n", DisplayName = "a module that parses")]
    [DataRow("#If VBA7 Then\r\nPublic Sub Foo()\r\n#Else\r\nPublic Sub Foo(ByVal x As Long)\r\n#End If\r\nEnd Sub\r\n", DisplayName = "a split header")]
    public void AParse_WhoseTokenIsNeverCancelled_IsTheParseWithNone(string source)
    {
        using var never = new CancellationTokenSource();

        var with = new ModuleParser().Parse(Uri, source, token: never.Token);
        var without = new ModuleParser().Parse(Uri, source);

        Assert.AreEqual(PlatformJsonEnvelope.Of(without).Json, PlatformJsonEnvelope.Of(with).Json);
    }

    // ANTLR's own ParseCanceledException, which SLL bails with, is an OperationCanceledException too. With a token that can be cancelled, a failed SLL pass
    // still falls back to LL, whose recovery is what reports the error: were the fallback skipped, the parse would throw, or say nothing of the line.
    [TestMethod]
    public void AParse_WhoseMainSllPassFails_WithATokenThatCanBeCancelled_IsStillRecoveredByLl()
    {
        using var never = new CancellationTokenSource();

        var result = new ModuleParser().Parse(Uri, "Public Sub Foo()\r\n    x = = 1\r\nEnd Sub\r\n", token: never.Token);

        Assert.IsTrue(result.SyntaxErrors.Any(error => error.Verbose?.Contains("no viable alternative at input 'x = ='", StringComparison.Ordinal) == true),
            string.Join("; ", result.SyntaxErrors.Select(error => error.Verbose)));
    }

    // the directive pass the same: a stray #End If fails its SLL pass, and its LL pass reads it. Were that fallback skipped, the directive trivia would be
    // forfeited.
    [TestMethod]
    public void AParse_WhoseDirectiveSllPassFails_WithATokenThatCanBeCancelled_IsStillReadByLl()
    {
        using var never = new CancellationTokenSource();
        const string source = "#If VBA7 Then\r\nPrivate Const A = 1\r\n#End If\r\n#End If\r\nPrivate Const B = 1\r\n";

        var with = new ModuleParser().Parse(Uri, source, token: never.Token);
        var without = new ModuleParser().Parse(Uri, source);

        Assert.IsNotEmpty(with.PrecompilerTrivia, "the directive pass read the module");
        Assert.AreEqual(PlatformJsonEnvelope.Of(without).Json, PlatformJsonEnvelope.Of(with).Json);
    }

    // the request is answered as cancelled (JSON-RPC's -32800, by the transport), and not with a parse that failed.
    [TestMethod]
    public async Task AParseRequest_TheClientCancelled_IsCancelled_RatherThanAnsweredWithAFailedParse()
    {
        var handler = new ParseFullDocumentHandler(new ModuleParser(), NullLogger<ParseFullDocumentHandler>.Instance, Options.Create(new SdkServerOptions()));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        _ = await Assert.ThrowsAsync<OperationCanceledException>(
            () => handler.Handle(new ParseDocumentParams { DocumentUri = Uri, Fragment = LongModule() }, cancelled.Token));
    }
}
