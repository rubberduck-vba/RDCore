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
    [DataRow("P (e = CVErr(5))", "True|", DisplayName = "E02 =")]
    [DataRow("P (e = f)", "True|", DisplayName = "E04 = of two variables")]
    [DataRow("P (e < CVErr(6))", "True|", DisplayName = "E02 <")]
    [DataRow("P (e <> CVErr(5))", "False|", DisplayName = "E02 <>")]
    [DataRow("Select Case e ;; Case CVErr(5) ;; P \"match\" ;; Case Else ;; P \"else\" ;; End Select", "match|", DisplayName = "E04 Select Case")]
    public async Task AnErrorComparedWithAnError_IsComparedByItsCode(string body, string printed)
        => Assert.AreEqual(printed + "e0|", await Run(body + " ;; P \"e\" & Err.Number"));

    [TestMethod]
    [DataRow("P (e = 5)", DisplayName = "E02 = a number")]
    [DataRow("P (e = \"Error 5\")", DisplayName = "E02 = a String")]
    [DataRow("P (e = Empty)", DisplayName = "E02 = Empty")]
    [DataRow("P Len(e)", DisplayName = "E01 Len")]
    [DataRow("P LenB(e)", DisplayName = "E01 LenB")]
    [DataRow("P Str(e)", DisplayName = "E01 Str")]
    [DataRow("P Len(CVErr(2042))", DisplayName = "E01 Len of an Excel error")]
    [DataRow("P StrComp(e, f)", DisplayName = "E04 StrComp")]
    [DataRow("P (e + 1)", DisplayName = "E03 +")]
    public async Task AnErrorWhereItIsNoValue_IsError13(string statement)
        => Assert.AreEqual("e13|", await Run(statement + " ;; P \"e\" & Err.Number"));

    [TestMethod]
    public async Task CStrOfAnError_IsItsText()
        => Assert.AreEqual("Error 5|e0|", await Run("P CStr(e) ;; P \"e\" & Err.Number"));
}
