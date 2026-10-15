using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A variable or a field of an Enum holds a <c>Long</c> (<strong>MS-VBAL §5.2.3.4</strong>), and is stored as one: in a record's memory image, which
/// <c>LSet</c> copies one record over another through (<strong>§5.4.3.6</strong>), and in a file, which <c>Put</c> writes and <c>Get</c> reads
/// (<strong>§5.4.5.11</strong>, <strong>§5.4.5.12</strong>). Run through <see cref="ModuleWorkspace"/>, so the declared types are the ones the host makes.
/// </summary>
/// <remarks>
/// <c>VbDayOfWeek</c> is the standard library's Enum, and <c>Colour</c> is the <c>Program</c> module's own.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.2.3.4 Enum Declarations")]
public sealed class EnumRecordStorageTests
{
    // Open reaches the file system itself, so each test has a file of its own in the temporary folder, and removes it. A program that stopped before its
    // Close leaves the file open, and then it stays, so that the failure is the one reported.
    private static async Task<string[]> WithDataFileAsync(Func<string, Task<string[]>> run)
    {
        var path = Path.Combine(Path.GetTempPath(), $"rdcore-enum-records-{Guid.NewGuid():N}.bin");
        try
        {
            return await run(path);
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

    private const string Colour = "Private Enum Colour\r\ncRed = 1\r\ncBlue = 2\r\nEnd Enum";

    private static string Program(string declarations, params string[] body)
        => $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n{declarations}\r\nPublic Sub Main()\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n";

    private static string Types(params string[] types)
        => string.Join("\r\n", types.Select((fields, index) => $"Private Type T{index + 1}\r\n{fields}\r\nEnd Type"));

    #region LSet

    [TestMethod]
    [DataRow("e As VbDayOfWeek", "e", "n As Long", "n", DisplayName = "A field of an Enum, into a Long field")]
    [DataRow("n As Long", "n", "e As VbDayOfWeek", "e", DisplayName = "A Long field, into a field of an Enum")]
    [DataRow("e As VbDayOfWeek", "e", "f As VbDayOfWeek", "f", DisplayName = "A field of an Enum, into a field of the Enum")]
    [DataRow("b As Byte\r\ne As VbDayOfWeek", "e", "b As Byte\r\nn As Long", "n", DisplayName = "A field of an Enum after a Byte, into a Long field after a Byte")]
    [DataRow("e As Colour", "e", "n As Long", "n", DisplayName = "A field of the workspace's Enum, into a Long field")]
    [DataRow("n As Long", "n", "e As Colour", "e", DisplayName = "A Long field, into a field of the workspace's Enum")]
    [DataRow("e As Colour", "e", "f As Colour", "f", DisplayName = "A field of the workspace's Enum, into a field of the Enum")]
    public async Task LSetBetweenRecords_CopiesAFieldOfAnEnum_AsTheLongItHolds(string sourceFields, string source, string destinationFields, string destination)
    {
        // the record's image had no bytes for a field of an Enum: what was copied out of one, or into one, was 0.
        CollectionAssert.AreEqual(new[] { "7" }, await RunAsync([], Program($"{Colour}\r\n{Types(sourceFields, destinationFields)}",
            "Dim a As T1", "Dim b As T2", $"a.{source} = 7", "LSet b = a", $"Debug.Print b.{destination}")));
    }

    #endregion

    #region Put and Get

    [TestMethod]
    public async Task AVariableOfAnEnum_PutThenGet_IsTheLongItHeld()
        // Get read no bytes for an Enum, and raised error 62, Input past end of file.
        => CollectionAssert.AreEqual(new[] { "2" }, await WithDataFileAsync(file => RunAsync([], Program("",
            "Dim d As VbDayOfWeek", "Dim e As VbDayOfWeek", "d = 2", $"Open \"{file}\" For Binary As #1", "Put #1, 1, d", "Get #1, 1, e", "Close #1",
            "Debug.Print e"))));

    [TestMethod]
    public async Task ARecordWithAFieldOfAnEnum_PutThenGet_IsTheLongsItHeld()
        => CollectionAssert.AreEqual(new[] { "3", "5" }, await WithDataFileAsync(file => RunAsync([], Program(Types("e As VbDayOfWeek\r\nn As Long"),
            "Dim a As T1", "Dim b As T1", "a.e = 3", "a.n = 5", $"Open \"{file}\" For Binary As #1", "Put #1, 1, a", "Get #1, 1, b", "Close #1",
            "Debug.Print b.e", "Debug.Print b.n"))));

    [TestMethod]
    [DataRow("VbDayOfWeek", DisplayName = "A library's Enum")]
    [DataRow("Colour", DisplayName = "The workspace's Enum")]
    public async Task ALongWritten_IsReadIntoAVariableOfAnEnum_AsTheLongItIs(string type)
        => CollectionAssert.AreEqual(new[] { "2" }, await WithDataFileAsync(file => RunAsync([], Program(Colour,
            "Dim n As Long", $"Dim d As {type}", "n = 2", $"Open \"{file}\" For Binary As #1", "Put #1, 1, n", "Get #1, 1, d", "Close #1",
            "Debug.Print d"))));

    [TestMethod]
    [DataRow("VbDayOfWeek", DisplayName = "A library's Enum")]
    [DataRow("Colour", DisplayName = "The workspace's Enum")]
    public async Task ALongWritten_IsReadIntoARecordsFieldOfAnEnum_AsTheLongItIs(string type)
        => CollectionAssert.AreEqual(new[] { "3" }, await WithDataFileAsync(file => RunAsync([], Program($"{Colour}\r\n{Types($"e As {type}")}",
            "Dim n As Long", "Dim a As T1", "n = 3", $"Open \"{file}\" For Binary As #1", "Put #1, 1, n", "Get #1, 1, a", "Close #1",
            "Debug.Print a.e"))));

    #endregion

    [TestMethod]
    public async Task LenOfARecordWithFieldsOfAnEnum_CountsFourBytesForEach()
        // Len is the size the record is written in: four bytes for each field of an Enum, and two for the Integer, with no padding (LenB has it).
        => CollectionAssert.AreEqual(new[] { "4", "10", "12" }, await RunAsync([], Program(Types("e As VbDayOfWeek", "e As VbDayOfWeek\r\ni As Integer\r\nf As VbDayOfWeek"),
            "Dim a As T1", "Dim b As T2", "Debug.Print Len(a)", "Debug.Print Len(b)", "Debug.Print LenB(b)")));
}
