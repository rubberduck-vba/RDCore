using RDCore.SDK.Runtime.Abstract.Execution;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// MS-VBAL §5.4.4.1, §5.4.2.4: an error in the collection of a <c>For Each</c> statement, under <c>On Error Resume Next</c>, goes on at the statement
/// following it, as MS-VBA has it (observed in Excel). When its <c>Next</c> follows the header on the header's line, that statement is the one past the
/// <c>Next</c>, which does not run: the control variable keeps its value, and Err the header's error. Otherwise the <c>Next</c> runs, finds no loop, and
/// raises error 92, the control variable emptied.
/// </summary>
[TestClass]
public sealed class ForEachHeaderResumeNextTests
{
    private static async Task<string> Run(string loop, VBExecutionEngine engine)
    {
        var program = "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\n"
            + "Dim v As Variant, o As Object, c As Collection, x As Variant, e As Variant, n As Long, n2 As Long\r\nv = 5\r\nOn Error Resume Next\r\n"
            + string.Join("\r\n", loop.Split(" ;; "))
            + "\r\nDebug.Print n & \"|\" & TypeName(v) & \"|\" & v & \"|\" & Err.Number & \" n2=\" & n2\r\nEnd Sub\r\n";
        return string.Join("/", await RunAsync([], program, engine));
    }

    [TestMethod]
    [DataRow("For Each v In o: Next: n2 = 7", "0|Integer|5|424 n2=7", DisplayName = "an object variable of Nothing")]
    [DataRow("For Each v In o: Next v: n2 = 7", "0|Integer|5|424 n2=7", DisplayName = "a Next that names the variable")]
    [DataRow("For Each v In o: Next ;; n2 = 7", "0|Integer|5|424 n2=7", DisplayName = "nothing after the Next on its line")]
    [DataRow("For Each v In c: Next", "0|Integer|5|424 n2=0", DisplayName = "a Collection variable of Nothing")]
    [DataRow("Set x = Nothing ;; For Each v In x: Next: n2 = 7", "0|Integer|5|424 n2=7", DisplayName = "a Variant that holds Nothing")]
    [DataRow("For Each v In e: Next: n2 = 7", "0|Integer|5|13 n2=7", DisplayName = "a Variant that holds Empty")]
    [DataRow("For Each v In o: Next: For Each v In o: n = n + 1: Next: n2 = 7", "1|Empty||92 n2=7", DisplayName = "a second loop on the line, with a body")]
    public async Task AnErrorInTheHeader_OfALoopWhoseNextFollowsItOnItsLine_GoesOnPastTheNext(string loop, string expected)
    {
        Assert.AreEqual(expected, await Run(loop, VBExecutionEngine.Interpreter));
        Assert.AreEqual(expected, await Run(loop, VBExecutionEngine.Compiled));
    }

    [TestMethod]
    [DataRow("For Each v In o ;; Next ;; n2 = 7", "0|Empty||92 n2=7", DisplayName = "a Next on the next line")]
    [DataRow("For Each v In o ;; ' nothing ;; Next ;; n2 = 7", "0|Empty||92 n2=7", DisplayName = "a comment for a body")]
    [DataRow("For Each v In o: n = n + 1: Next", "1|Empty||92 n2=0", DisplayName = "a body on the line")]
    [DataRow("For Each v In o: n = n + 1 ;; Next ;; n2 = 7", "1|Empty||92 n2=7", DisplayName = "a body on the line, the Next on the next")]
    [DataRow("Set x = Nothing ;; For Each v In x ;; n = n + 1 ;; Next ;; n2 = 7", "1|Empty||92 n2=7", DisplayName = "a Variant of Nothing, over lines")]
    public async Task AnErrorInTheHeader_OfAnyOtherLoop_RunsItsNext_WhichIsError92(string loop, string expected)
    {
        Assert.AreEqual(expected, await Run(loop, VBExecutionEngine.Interpreter));
        Assert.AreEqual(expected, await Run(loop, VBExecutionEngine.Compiled));
    }
}
