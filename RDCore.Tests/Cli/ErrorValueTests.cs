using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// An Error value (<c>CVErr</c>) as MS-VBA has it: compared with another Error by its code (<strong>MS-VBAL §5.6.9.5</strong>), and a type mismatch to
/// <c>Len</c>, <c>LenB</c> and <c>Str</c>, where <c>CStr</c> writes it as <c>Error 5</c> (observed in Excel).
/// </summary>
[TestClass]
public sealed class ErrorValueTests
{
    private static async Task<string> Run(string body)
        => (await RunAsync([], "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPrivate acc As String\r\n"
            + "Private Sub P(ByVal v As Variant)\r\n    acc = acc & CStr(v) & \"|\"\r\nEnd Sub\r\n"
            + $"Public Sub Main()\r\nOn Error Resume Next\r\nDim e As Variant, f As Variant\r\ne = CVErr(5)\r\nf = CVErr(5)\r\n{string.Join("\r\n", body.Split(" ;; "))}\r\nDebug.Print acc\r\nEnd Sub\r\n")).Last();

    [TestMethod]
    [DataRow("P (e = CVErr(5))", "True|", DisplayName = "=")]
    [DataRow("P (e = f)", "True|", DisplayName = "= of two variables")]
    [DataRow("P (e < CVErr(6))", "True|", DisplayName = "<")]
    [DataRow("P (e <> CVErr(5))", "False|", DisplayName = "<>")]
    [DataRow("Select Case e ;; Case CVErr(5) ;; P \"match\" ;; Case Else ;; P \"else\" ;; End Select", "match|", DisplayName = "Select Case")]
    public async Task AnErrorComparedWithAnError_IsComparedByItsCode(string body, string printed)
        => Assert.AreEqual(printed + "e0|", await Run(body + " ;; P \"e\" & Err.Number"));

    [TestMethod]
    [DataRow("P (e = 5)", DisplayName = "= a number")]
    [DataRow("P (e = \"Error 5\")", DisplayName = "= a String")]
    [DataRow("P (e = Empty)", DisplayName = "= Empty")]
    [DataRow("P Len(e)", DisplayName = "Len")]
    [DataRow("P LenB(e)", DisplayName = "LenB")]
    [DataRow("P Str(e)", DisplayName = "Str")]
    [DataRow("P Len(CVErr(2042))", DisplayName = "Len of an Excel error")]
    [DataRow("P StrComp(e, f)", DisplayName = "StrComp")]
    [DataRow("P (e + 1)", DisplayName = "+")]
    public async Task AnErrorWhereItIsNoValue_IsError13(string statement)
        => Assert.AreEqual("e13|", await Run(statement + " ;; P \"e\" & Err.Number"));

    [TestMethod]
    public async Task CStrOfAnError_IsItsText()
        => Assert.AreEqual("Error 5|e0|", await Run("P CStr(e) ;; P \"e\" & Err.Number"));
}
