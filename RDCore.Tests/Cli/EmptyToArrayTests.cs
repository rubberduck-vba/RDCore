using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// MS-VBAL §3.3.5.2: the <c>Empty</c> literal has a declared type of <c>Variant</c>, and MS-VBAL §5.5.1.1 makes a <c>Variant</c> valid to Let-coerce to an
/// array: <c>a = Empty</c> compiles, and raises 13 when it runs (MS-VBAL §5.5.1.2.7; Excel, <c>oracle\s46</c> F17a-b).
/// </summary>
[TestClass]
public sealed class EmptyToArrayTests
{
    [TestMethod]
    [DataRow("Function FE() As Integer() ;; FE = Empty ;; End Function", "Dim r ;; r = FE()", DisplayName = "F17a the result of a function")]
    [DataRow("", "Dim a() As Integer ;; a = Empty", DisplayName = "F17b a variable")]
    [DataRow("", "Dim a() As Integer ;; a = Null", DisplayName = "s48 J01 Null to a variable")]
    [DataRow("Function FN() As Integer() ;; FN = Null ;; End Function", "Dim r ;; r = FN()", DisplayName = "s48 J02 Null to the result of a function")]
    public async Task EmptyAssignedToAnArray_Compiles_AndRaises13(string declarations, string body)
    {
        var program = "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n" + declarations.Replace(" ;; ", "\r\n")
            + "\r\nPublic Sub Main()\r\nOn Error Resume Next\r\n" + body.Replace(" ;; ", "\r\n") + "\r\nDebug.Print Err.Number\r\nEnd Sub\r\n";

        Assert.IsEmpty(await LoadErrorsAsync([], program));
        Assert.AreEqual("13", string.Join("/", await RunAsync([], program)));
    }
}
