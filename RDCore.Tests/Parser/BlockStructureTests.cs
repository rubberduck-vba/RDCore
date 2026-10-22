using RDCore.Parsing;
using RDCore.SDK.Model.Errors;

namespace RDCore.Tests.Parser;

/// <summary>
/// A procedure whose blocks do not match is a syntax error of MS-VBA's own words - <c>Next without For</c>, <c>Block If without End If</c> - at the
/// statement that does not fit (Excel-verified), rather than what the grammar expected where it could no longer parse.
/// </summary>
[TestClass]
public sealed class BlockStructureTests
{
    private static readonly Uri Module = new("file://rdcore-test/Blocks.bas");

    private static IReadOnlyList<VBSyntaxErrorInfo> ErrorsOf(string body)
        => new ModuleParser().Parse(Module, $"Attribute VB_Name = \"Blocks\"\r\nPublic Sub Main()\r\n{body.Replace(" ;; ", "\r\n")}\r\nEnd Sub\r\n").SyntaxErrors;

    [TestMethod]
    [DataRow("Dim i As Long ;; Next", VBCompileErrorId.NextWithoutFor)]
    [DataRow("End If", VBCompileErrorId.EndIfWithoutBlockIf)]
    [DataRow("Loop", VBCompileErrorId.LoopWithoutDo)]
    [DataRow("Dim x As Long ;; If x = 1 Then ;; x = 2", VBCompileErrorId.BlockIfWithoutEndIf)]
    [DataRow("Dim i As Long ;; For i = 1 To 2 ;; Debug.Print i", VBCompileErrorId.ForWithoutNext)]
    [DataRow("Do ;; Debug.Print 1", VBCompileErrorId.DoWithoutLoop)]
    [DataRow("Dim x As Long ;; Select Case x ;; Case 1", VBCompileErrorId.SelectCaseWithoutEndSelect)]
    [DataRow("Dim c As New Collection ;; With c", VBCompileErrorId.ExpectedEndWith)]
    [DataRow("End With", VBCompileErrorId.EndWithWithoutWith)]
    [DataRow("Case 1", VBCompileErrorId.CaseWithoutSelectCase)]
    [DataRow("Else", VBCompileErrorId.ElseWithoutIf)]
    [DataRow("Dim i As Long ;; For i = 1 To 2 ;; If i = 1 Then ;; Next", VBCompileErrorId.NextWithoutFor)]
    public void ABlockThatDoesNotMatch_IsMsVbasError(string body, VBCompileErrorId expected)
    {
        var errors = ErrorsOf(body);

        Assert.HasCount(1, errors, string.Join(" | ", errors.Select(error => $"{error.VBCompileErrorId}: {error.Verbose}")));
        Assert.AreEqual(expected, errors[0].VBCompileErrorId);
    }

    [TestMethod]
    [DataRow("Dim i As Long, j As Long ;; For i = 1 To 2 ;; For j = 1 To 2 ;; Next j, i", DisplayName = "a Next of two loops")]
    [DataRow("Dim x As Long ;; If x = 1 Then x = 2 Else x = 3", DisplayName = "a single-line If with an Else")]
    [DataRow("Dim x As Long ;; If x = 1 Then x = 2: x = 3 ;; Do ;; Loop While x = 0", DisplayName = "a single-line If with statements after a colon")]
    [DataRow("Dim x As Long ;; Select Case x ;; Case 1 ;; Case Else ;; End Select ;; With New Collection ;; End With", DisplayName = "Select Case and With")]
    [DataRow("Dim x As Long ;; If x = 1 Then ' a comment ;; ElseIf x = 2 Then ;; Else ;; End If", DisplayName = "a block If with a comment after its Then")]
    public void BlocksThatMatch_AreNoError(string body)
        => Assert.IsEmpty(ErrorsOf(body), string.Join(" | ", ErrorsOf(body).Select(error => $"{error.VBCompileErrorId}: {error.Verbose}")));

    // what is not a block error is the grammar's to say, as it always was.
    [TestMethod]
    public void ASyntaxErrorThatIsNoBlockError_IsTheGrammars()
        => Assert.IsTrue(ErrorsOf("Dim x As Long ;; x = = 1").All(error => error.VBCompileErrorId == VBCompileErrorId.SyntaxError));
}
