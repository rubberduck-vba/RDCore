using RDCore.Parsing;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Platform.Protocol;
using System.Text;

namespace RDCore.Tests.Model.AST;

/// <summary>
/// A syntax tree is written and read with the same stack however deep it is (<see cref="SyntaxNodeJson"/>): a chain of additions 40 KB long, and 250 nested
/// parentheses, which overflowed the parse server's stack writing them and the debug adapter's reading them.
/// </summary>
[TestClass]
public sealed class DeepSyntaxTreeJsonTests
{
    private static readonly Uri Uri = TestUri.TestModuleUri();

    // a stack a quarter of the size of a thread's on Windows: a writer or a reader whose depth grows with the tree's overflows it at these depths.
    private const int SmallStack = 256 * 1024;

    private static string AdditionChain(int bytes)
    {
        var chain = new StringBuilder("1");
        while (chain.Length < bytes)
        {
            chain.Append(" + 1");
        }

        return chain.ToString();
    }

    [TestMethod]
    [DataRow("chain", DisplayName = "a chain of additions 40 KB long")]
    [DataRow("parentheses", DisplayName = "250 nested parentheses")]
    public void ADeepTree_RoundTripsThroughThePlatformsJson_OnASmallStack(string shape)
    {
        var expression = shape == "chain" ? AdditionChain(40 * 1024) : new string('(', 250) + "1" + new string(')', 250);
        var module = ParseTree($"Sub Test()\r\n    x = {expression}\r\nEnd Sub\r\n");

        string? json = null;
        string? again = null;
        Exception? failed = null;
        var thread = new Thread(() =>
        {
            try
            {
                json = PlatformJson.Serialize<SyntaxNode>(module);
                again = PlatformJson.Serialize(PlatformJson.Deserialize<SyntaxNode>(json));
            }
            catch (Exception exception)
            {
                failed = exception;
            }
        }, SmallStack);
        thread.Start();
        thread.Join();

        Assert.IsNull(failed, failed?.ToString());
        Assert.AreEqual(json, again);
    }

    // the tree of the module, which a parse has whether or not an expression of it is nested deeper than RD-VBA reads: the chain is (VBC00060), and its tree
    // crosses the wire with the error.
    private static ModuleNode ParseTree(string source)
    {
        var result = new ModuleParser().Parse(Uri, source);
        Assert.IsTrue(result.SyntaxErrors.All(error => error.ErrorId == (int)VBCompileErrorId.ExpressionNestedTooDeeply),
            string.Join(", ", result.SyntaxErrors.Select(e => e.Verbose)));
        return result.SyntaxTree!;
    }
}
