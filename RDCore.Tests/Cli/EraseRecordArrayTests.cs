using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Cli;

/// <summary>
/// <c>Erase</c> of a fixed-size array of records (<strong>MS-VBAL §5.4.3.4</strong>): each element is reset to what a declared variable of the record type
/// starts as, its fixed-size array fields with their bounds, and each element a record of its own.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.4.3.4 Erase Statement")]
public sealed class EraseRecordArrayTests
{
    // body: the lines of Main, ";;" between them.
    private static string Program(string body)
        => string.Join("\r\n",
            "Attribute VB_Name = \"Program\"",
            "Option Explicit",
            "Private Type RecA",
            "    Tag As Long",
            "    Items(1 To 3) As Long",
            "End Type",
            "Private Type RecB",
            "    Inner As RecA",
            "    Pairs(1 To 2) As RecA",
            "End Type",
            "Private Type Holder",
            "    L(1 To 2) As RecA",
            "End Type",
            "Private M(1 To 2) As RecA",
            "Private H As Holder",
            "Public Sub Main()",
            string.Join("\r\n", body.Split(";;").Select(line => "    " + line.Trim())),
            "End Sub") + "\r\n";

    // after the Erase, one element is written: the other reads its own fields, at their defaults.
    [TestMethod]
    [DataRow("Dim L(1 To 2) As RecA ;; L(1).Items(2) = 5 ;; L(2).Tag = 3 ;; Erase L ;; L(1).Tag = 1 ;; L(1).Items(1) = 9 ;; " +
        "Debug.Print L(1).Items(2) & \"|\" & L(2).Tag & \"|\" & L(2).Items(1) & \"|\" & UBound(L(2).Items)", "0|0|0|3", DisplayName = "a local")]
    [DataRow("M(1).Items(2) = 5 ;; M(2).Tag = 3 ;; Erase M ;; M(1).Tag = 1 ;; M(1).Items(1) = 9 ;; " +
        "Debug.Print M(1).Items(2) & \"|\" & M(2).Tag & \"|\" & M(2).Items(1) & \"|\" & UBound(M(2).Items)", "0|0|0|3", DisplayName = "a module-level array")]
    [DataRow("H.L(1).Items(2) = 5 ;; H.L(2).Tag = 3 ;; Erase H.L ;; H.L(1).Tag = 1 ;; H.L(1).Items(1) = 9 ;; " +
        "Debug.Print H.L(1).Items(2) & \"|\" & H.L(2).Tag & \"|\" & H.L(2).Items(1) & \"|\" & UBound(H.L(2).Items)", "0|0|0|3",
        DisplayName = "a field of a record")]
    [DataRow("Dim N(1 To 2) As RecB ;; N(1).Inner.Items(2) = 4 ;; N(2).Pairs(2).Items(3) = 6 ;; N(2).Pairs(1).Tag = 2 ;; Erase N ;; " +
        "N(1).Pairs(1).Items(1) = 9 ;; Debug.Print N(1).Inner.Items(2) & \"|\" & N(2).Pairs(2).Items(3) & \"|\" & N(2).Pairs(1).Tag & \"|\" & " +
        "N(2).Pairs(1).Items(1) & \"|\" & UBound(N(1).Pairs(2).Items)", "0|0|0|0|3", DisplayName = "nested fixed-size array fields")]
    public async Task AnErasedArrayOfRecords_ResetsEachElement_ItsFixedSizeArrayFieldsKeepingTheirBounds(string body, string expected)
    {
        foreach (var engine in new[] { VBExecutionEngine.Interpreter, VBExecutionEngine.Compiled })
        {
            var output = await ModuleWorkspace.RunAsync([], Program(body), engine);

            Assert.AreEqual(expected, string.Join(" | ", output), engine.ToString());
        }
    }
}
