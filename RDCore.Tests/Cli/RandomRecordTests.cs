using RDCore.SDK.Runtime.Abstract.Execution;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// The records of a <c>Random</c> file (<strong>MS-VBAL §5.4.5.11</strong>, <strong>§5.4.5.12</strong>): a <c>Put</c> or a <c>Get</c> leaves the
/// file-pointer-position at the next record, however much of its own it used; <c>EOF</c> is whether the file ends inside the record read; a channel opened
/// with no <c>Len</c> has records of 128 bytes. Each row is what MS-VBA does (recorded in Excel, 64-bit).
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.4.5.11 Put")]
[TestCategory("MS-VBAL 5.4.5.12 Get")]
public sealed class RandomRecordTests
{
    // body: the lines of Main, ";;" between them; F is the file's path.
    private static string Program(string file, string body)
        => "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n" +
           "Public Sub Main()\r\n    On Error Resume Next\r\n    Dim gi As Integer, gl As Long, gs As String\r\n" +
           $"    Dim F As String: F = \"{file}\"\r\n" +
           string.Join("\r\n", body.Split(";;").Select(line => "    " + line.Trim())) + "\r\n" +
           "End Sub\r\n";

    private static async Task AssertInBothEnginesAsync(string body, string expected)
    {
        foreach (var engine in new[] { VBExecutionEngine.Interpreter, VBExecutionEngine.Compiled })
        {
            var path = Path.Combine(Path.GetTempPath(), $"rdcore-random-records-{Guid.NewGuid():N}.bin");
            try
            {
                var output = await RunAsync([], Program(path, body), engine);
                Assert.AreEqual(expected, string.Join(" | ", output), engine.ToString());
            }
            finally
            {
                try
                {
                    File.Delete(path);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    private const string Where = "Debug.Print \"loc \" & Loc(1) & \" seek \" & Seek(1) & \" lof \" & LOF(1)";

    [TestMethod]
    [DataRow("Open F For Random As #1 Len = 64 ;; gi = 7 ;; Put #1, , gi ;; Put #1, , gi ;; " + Where, "loc 2 seek 3 lof 66", DisplayName = "two Puts")]
    [DataRow("Open F For Random As #1 Len = 64 ;; gi = 5 ;; Put #1, 3, gi ;; " + Where, "loc 3 seek 4 lof 130", DisplayName = "a Put of record 3")]
    [DataRow("Open F For Random As #1 ;; gi = 7 ;; Put #1, , gi ;; Put #1, , gi ;; " + Where, "loc 2 seek 3 lof 130", DisplayName = "no Len")]
    [DataRow("Open F For Random As #1 ;; gs = String$(126, \"a\") ;; Put #1, 1, gs ;; Put #1, , gs ;; Debug.Print Err.Number ;; " + Where,
        "0 | loc 2 seek 3 lof 256", DisplayName = "no Len, records of 128 bytes")]
    public async Task APut_LeavesTheFileAtTheNextRecord(string body, string expected)
        => await AssertInBothEnginesAsync(body, expected);

    [TestMethod]
    public async Task AGet_LeavesTheFileAtTheNextRecord_AndIsAtTheEndOfTheFileAfterTheLastRecord()
        => await AssertInBothEnginesAsync(
            "Open F For Random As #1 Len = 4 ;; gl = 1 ;; Put #1, 1, gl ;; gl = 2 ;; Put #1, 2, gl ;; Close #1 ;; Open F For Random As #1 Len = 4 ;; " +
            "Get #1, 1, gl ;; Debug.Print gl & \" \" & EOF(1) & \" loc \" & Loc(1) & \" seek \" & Seek(1) ;; " +
            "Get #1, , gl ;; Debug.Print gl & \" \" & EOF(1) & \" loc \" & Loc(1) & \" seek \" & Seek(1)",
            "1 False loc 1 seek 2 | 2 False loc 2 seek 3");

    // the file ends inside the record: the Integer read of it is all there, and EOF is True all the same.
    [TestMethod]
    public async Task AGetOfARecordTheFileEndsInside_IsAtTheEndOfTheFile()
        => await AssertInBothEnginesAsync(
            "Open F For Random As #1 Len = 64 ;; gi = 7 ;; Put #1, 1, gi ;; Seek #1, 1 ;; gi = 0 ;; Get #1, , gi ;; " +
            "Debug.Print gi & \" \" & EOF(1) & \" loc \" & Loc(1) & \" seek \" & Seek(1)",
            "7 True loc 1 seek 2");

    [TestMethod]
    public async Task ARecordLongerThan128Bytes_OfAChannelWithNoLen_IsBadRecordLength()
        => await AssertInBothEnginesAsync(
            "Open F For Random As #1 ;; gs = String$(200, \"a\") ;; Put #1, 1, gs ;; Debug.Print CStr(Err.Number)", "59");
}
