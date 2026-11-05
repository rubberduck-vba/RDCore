using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.Symbols;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Parser;

/// <summary>
/// The depth of an expression RD-VBA reads (<strong>RD-VBAL §2.6.1</strong>): an expression nested in at most 2,500 others is read, loaded and run, as deep as MS-VBA
/// runs it (recorded in Excel, oracle\s65: 2,000 nested parentheses, 1,000 nested calls, a chain of 2,500 terms); a deeper one is the syntax error
/// <see cref="VBCompileErrorId.ExpressionNestedTooDeeply"/>, where MS-VBA crashes the host.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL 2.6.1 Syntax Errors")]
public sealed class ExpressionNestingTests
{
    private static string Parenthesized(int depth) => new string('(', depth) + "1" + new string(')', depth);

    private static string NestedAbs(int depth) => string.Concat(Enumerable.Repeat("Abs(", depth)) + "-1" + new string(')', depth);

    private static string Chain(int terms) => string.Join(" + ", Enumerable.Repeat("1", terms));

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
    public void TwoExpressionsPastTheLimit_AreEachReported()
    {
        var past = Parenthesized(ModuleParser.MaxExpressionNesting + 1);
        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), $"Public Sub Main()\r\n    Dim x As Variant\r\n    x = {past}\r\n    x = {past}\r\nEnd Sub\r\n");

        Assert.AreEqual(2, result.SyntaxErrors.Count(error => error.ErrorId == (int)VBCompileErrorId.ExpressionNestedTooDeeply), Describe(result));
    }

    // what MS-VBA runs, RD-VBA runs: on the test's own thread, which is no larger than a thread of the language server.
    [TestMethod]
    public async Task TwoThousandNestedParentheses_Run()
        => Assert.AreEqual("1", await RunAsync(Parenthesized(2000)));

    [TestMethod]
    public async Task AThousandNestedCalls_Run()
        => Assert.AreEqual("1", await RunAsync(NestedAbs(1000)));

    [TestMethod]
    public async Task AChainOf2500Terms_Runs()
        => Assert.AreEqual("2500", await RunAsync(Chain(2500)));

    // runs work on a thread with a 256 KB stack, a quarter of a thread's default and as small as one the language server or an extension may ask on, and says
    // what it threw, if anything. A module past the limit is still declared and walked there: its error is the one to report, not a crash.
    private static Exception? OnASmallStack(Action work)
    {
        Exception? thrown = null;
        var thread = new Thread(() =>
        {
            try
            {
                work();
            }
            catch (Exception exception)
            {
                thrown = exception;
            }
        }, 256 * 1024);
        thread.Start();
        thread.Join();
        return thrown;
    }

    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly Uri Main = new UriBuilder(Root) { Fragment = "Main" }.Uri;

    [TestMethod]
    public void AModuleWithAnExpressionPastTheLimit_DeclaresWhatItDeclares()
    {
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Main.bas"),
            $"Attribute VB_Name = \"Main\"\r\nPublic Sub Main()\r\n    Dim x As Variant\r\n    x = {Chain(40_000)}\r\nEnd Sub\r\n");
        Assert.IsTrue(parse.SyntaxErrors.Any(error => error.ErrorId == (int)VBCompileErrorId.ExpressionNestedTooDeeply), Describe(parse));

        string[] declared = [];
        var thrown = OnASmallStack(() =>
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
        var parse = Parse(Chain(40_000));

        var count = 0;
        var thrown = OnASmallStack(() => count = parse.SyntaxTree!.Descendants().Count(node => node is LiteralExpressionNode));

        Assert.IsNull(thrown, thrown?.ToString());
        Assert.AreEqual(40_000, count);
    }
}
