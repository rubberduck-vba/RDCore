namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §5.3.1.11</strong> a public variable of an object is a variable: a <c>ByRef</c> parameter whose argument is one is a second name for it, and what the
/// callee writes through the parameter is what the object's variable holds.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.3.1.11 Procedure Invocation Argument Processing")]
public sealed class ByRefFieldArgumentTests
{
    private static Task<string[]> RunAsync(string procedures, params string[] body)
        => ArrayStatementTargetTests.RunProgramAsync(string.Empty, procedures, body);

    private const string Bump = "Public Sub Bump(ByRef n As Long)\r\nn = n + 1\r\nEnd Sub";

    [TestMethod]
    public async Task APublicVariableOfAnObject_PassedByRef_IsWrittenThroughTheParameter()
        => CollectionAssert.AreEqual(new[] { "2" },
            await RunAsync(Bump, "b.Count = 1", "Bump b.Count", "Debug.Print b.Count"));

    [TestMethod]
    public async Task TheSameVariable_PassedTwice_IsTheSameStorageTwice()
        => CollectionAssert.AreEqual(new[] { "3" },
            await RunAsync(Bump, "b.Count = 1", "Bump b.Count", "Bump b.Count", "Debug.Print b.Count"));

    [TestMethod]
    public async Task APublicVariableInAWithBlock_PassedByRef_IsWrittenThroughTheParameter()
        => CollectionAssert.AreEqual(new[] { "6" },
            await RunAsync(Bump, "b.Count = 5", "With b", "Bump .Count", "End With", "Debug.Print b.Count"));

    [TestMethod]
    public async Task APublicVariableOfAnotherType_PassedByRef_IsStillACopy()
        => CollectionAssert.AreEqual(new[] { "1" },
            await RunAsync("Public Sub Bump2(ByRef n As Integer)\r\nn = n + 1\r\nEnd Sub", "b.Count = 1", "Bump2 b.Count", "Debug.Print b.Count"));

    [TestMethod]
    public async Task APublicVariableOfAnObject_PassedToAVariantParameter_IsWrittenThroughIt()
        => CollectionAssert.AreEqual(new[] { "8" },
            await RunAsync("Public Sub Set8(ByRef v As Variant)\r\nv = 8\r\nEnd Sub", "b.Count = 1", "Set8 b.Count", "Debug.Print b.Count"));

    [TestMethod]
    public async Task AnExpressionThatIsNotAVariable_PassedByRef_IsStillACopy()
        => CollectionAssert.AreEqual(new[] { "1" },
            await RunAsync(Bump, "b.Count = 1", "Bump (b.Count)", "Debug.Print b.Count"));
}
