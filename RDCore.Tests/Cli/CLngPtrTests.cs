using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §6.1.2.3.1.10</strong> <c>CLngPtr</c>: its argument Let-coerced to <c>LongPtr</c>, the integer a pointer is on the platform - a
/// <c>LongLong</c> on the 64-bit one these tests run on - and an Error argument's code converted the same way.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.3 Conversion")]
public sealed class CLngPtrTests
{
    private static Task<string[]> Run(params string[] lines)
        => RunAsync([], $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n{string.Join("\r\n", lines)}\r\nEnd Sub\r\n");

    [TestMethod]
    public async Task ANumber_IsConverted_ByBankersRounding()
        // 2.5 rounds to the even 2, 3.5 to 4 (MS-VBAL 5.5.1.2.1.1).
        => CollectionAssert.AreEqual(new[] { "2 4" }, await Run("Debug.Print CLngPtr(2.5) & \" \" & CLngPtr(3.5)"));

    [TestMethod]
    public async Task AValuePastTheRangeOfALong_IsALongPtrOnA64BitPlatform()
        => CollectionAssert.AreEqual(new[] { "3000000000" }, await Run("Debug.Print CLngPtr(3000000000#)"));

    [TestMethod]
    public async Task AString_IsConverted()
        => CollectionAssert.AreEqual(new[] { "42" }, await Run("Debug.Print CLngPtr(\"42\")"));

    [TestMethod]
    public async Task AnError_IsItsCode()
        => CollectionAssert.AreEqual(new[] { "13" }, await Run("Debug.Print CLngPtr(CVErr(13))"));

    [TestMethod]
    public async Task AValuePastTheRangeOfALongLong_IsAnOverflow()
        => CollectionAssert.AreEqual(new[] { "6" }, await Run(
            "On Error Resume Next",
            "Debug.Print CLngPtr(1E+30)",
            "Debug.Print Err.Number"));

    [TestMethod]
    public async Task ItsResult_IsAssignedToALongPtr()
        => CollectionAssert.AreEqual(new[] { "7" }, await Run(
            "Dim p As LongPtr",
            "p = CLngPtr(7)",
            "Debug.Print p"));
}
