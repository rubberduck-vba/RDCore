using RDCore.SDK.Runtime.Abstract.Execution;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// The bytes a <c>Put</c> of a record writes, field by field, and the record a <c>Get</c> of them reads back (<strong>MS-VBAL §5.4.5.11</strong>,
/// <strong>§5.4.5.12</strong>): byte for byte what MS-VBA writes (recorded in Excel), so that a file either one wrote is a file the other reads.
/// </summary>
/// <remarks>
/// Each row writes a record to a file of its own, reads every byte of the file back one <c>Byte</c> at a time, then <c>Get</c>s the record into another
/// variable. In a <c>Random</c> file the bytes after a record and before the next are the file's own, zeros.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.4.5.11 Put")]
[TestCategory("MS-VBAL 5.4.5.12 Get")]
public sealed class RecordFileBytesTests
{
    private const string Types =
        "Private Type TA\r\n    N As Integer\r\n    A(1 To 3) As Integer\r\nEnd Type\r\n" +
        "Private Type TR\r\n    N As Integer\r\n    A() As Integer\r\nEnd Type\r\n" +
        "Private Type TS\r\n    S As String * 4\r\n    N As Integer\r\nEnd Type\r\n" +
        "Private Type TV\r\n    V As Variant\r\n    N As Integer\r\nEnd Type\r\n" +
        "Private Type TVS\r\n    S As String\r\n    N As Integer\r\nEnd Type\r\n" +
        "Private Type TF2\r\n    A(1 To 2, 1 To 3) As Integer\r\nEnd Type\r\n" +
        "Private Type TR2\r\n    A() As Integer\r\nEnd Type\r\n" +
        "Private Type TFS\r\n    A(1 To 2) As String\r\n    N As Integer\r\nEnd Type\r\n" +
        "Private Type TRS\r\n    A() As String\r\n    N As Integer\r\nEnd Type\r\n" +
        "Private Type TFV\r\n    A(1 To 2) As Variant\r\n    N As Integer\r\nEnd Type\r\n" +
        "Private Type TRV\r\n    A() As Variant\r\n    N As Integer\r\nEnd Type\r\n" +
        "Private Type TFX\r\n    A(1 To 2) As String * 3\r\n    N As Integer\r\nEnd Type\r\n" +
        "Private Type TIn\r\n    N As Integer\r\n    S As String\r\nEnd Type\r\n" +
        "Private Type TFU\r\n    A(1 To 2) As TIn\r\nEnd Type\r\n" +
        "Private Type TRB\r\n    A() As Byte\r\n    N As Integer\r\nEnd Type\r\n";

    private const string Dump =
        "Private Function Dump(ByVal Path As String) As String\r\n" +
        "    Dim F As Integer, B As Byte, I As Long, S As String\r\n" +
        "    F = FreeFile\r\n" +
        "    Open Path For Binary Access Read As #F\r\n" +
        "    For I = 1 To LOF(F)\r\n" +
        "        Get #F, I, B\r\n" +
        "        S = S & Right$(\"0\" & Hex$(B), 2)\r\n" +
        "    Next\r\n" +
        "    Close #F\r\n" +
        "    Dump = S\r\n" +
        "End Function\r\n";

    // open is the mode and, for a Random file, its record length: "Random Len = 20" opens `For Random As #1 Len = 20`.
    private static string Program(string file, string open, string declare, string fill, string io, string back)
        => "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n" + Types + Dump +
           $"Public Sub Main()\r\n    Dim X As {declare}, Y As {declare}, I As Integer, J As Integer\r\n" +
           string.Join("\r\n", fill.Split(";;").Select(line => "    " + line.Trim())) + "\r\n" +
           $"    Open \"{file}\" For {open.Split(" Len ")[0]} As #1{(open.Contains(" Len ") ? " Len " + open.Split(" Len ")[1] : string.Empty)}\r\n" +
           string.Join("\r\n", io.Split(";;").Select(line => "    " + line.Trim())) + "\r\n" +
           "    Close #1\r\n" +
           $"    Debug.Print \"dump \" & Dump(\"{file}\")\r\n" +
           $"    Debug.Print \"back \" & {back}\r\n" +
           "End Sub\r\n";

    // each engine writes a file of its own, in the temporary folder, and removes it.
    private static async Task AssertInBothEnginesAsync(Func<string, string> program, params string[] expected)
    {
        foreach (var engine in new[] { VBExecutionEngine.Interpreter, VBExecutionEngine.Compiled })
        {
            var path = Path.Combine(Path.GetTempPath(), $"rdcore-record-bytes-{Guid.NewGuid():N}.bin");
            try
            {
                var output = await RunAsync([], program(path), engine);
                CollectionAssert.AreEqual(expected, output, $"{engine}: {string.Join(" | ", output)}");
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

    private const string Write = "Put #1, 1, X ;; Get #1, 1, Y";

    // a fixed-length String has no two-byte prefix (MS-VBAL §5.4.5.11's Fixed-length String row) in Random mode either: it had one, so Get read the
    // prefix as text and the field after it past the record.
    [TestMethod]
    [DataRow("Random Len = 20", DisplayName = "Random")]
    [DataRow("Binary", DisplayName = "Binary")]
    public async Task AFixedLengthStringField_IsItsCharacters(string open)
        => await AssertInBothEnginesAsync(file => Program(file, open, "TS", "X.S = \"ab\" ;; X.N = 7", Write, "\"[\" & Y.S & \"] \" & Y.N"),
            "dump 616220200700", "back [ab  ] 7");

    // a String field has its two-byte length in Binary mode as in Random: a record's String is not the variable whose current length says how much
    // of the file is its own.
    [TestMethod]
    [DataRow("Random Len = 20", DisplayName = "Random")]
    [DataRow("Binary", DisplayName = "Binary")]
    public async Task AStringField_IsItsLength_ThenItsCharacters(string open)
        => await AssertInBothEnginesAsync(file => Program(file, open, "TVS", "X.S = \"hey\" ;; X.N = 7", Write, "\"[\" & Y.S & \"] \" & Y.N"),
            "dump 03006865790700", "back [hey] 7");

    // a Variant field read by Get is a Variant still: the record written after it has the field's type descriptor, which it dropped.
    [TestMethod]
    public async Task AVariantFieldReadByGet_IsWrittenWithItsDescriptor_Random()
        => await AssertInBothEnginesAsync(file => Program(file, "Random Len = 20", "TV", "X.V = CInt(5) ;; X.N = 7", "Put #1, 1, X ;; Get #1, 1, Y ;; Put #1, 2, Y",
            "VarType(Y.V) & \" \" & Y.V & \" \" & Y.N"),
            "dump 0200050007000000000000000000000000000000020005000700", "back 2 5 7");

    [TestMethod]
    public async Task AVariantFieldReadByGet_IsWrittenWithItsDescriptor_Binary()
        => await AssertInBothEnginesAsync(file => Program(file, "Binary", "TV", "X.V = CInt(5) ;; X.N = 7", "Put #1, 1, X ;; Get #1, 1, Y ;; Put #1, , Y",
            "VarType(Y.V) & \" \" & Y.V & \" \" & Y.N"),
            "dump 020005000700020005000700", "back 2 5 7");

    [TestMethod]
    public async Task AVariantFieldHoldingAString_IsItsDescriptor_ItsLength_ThenItsCharacters()
        => await AssertInBothEnginesAsync(file => Program(file, "Random Len = 20", "TV", "X.V = \"hi\" ;; X.N = 7", "Put #1, 1, X ;; Get #1, 1, Y ;; Put #1, 2, Y",
            "VarType(Y.V) & \" \" & Y.V & \" \" & Y.N"),
            "dump 08000200686907000000000000000000000000000800020068690700", "back 8 hi 7");

    // a String a Variant holds has its two-byte length in Binary mode too, in a field or not: it had none, so Get read no character.
    [TestMethod]
    public async Task AVariantFieldHoldingAString_HasItsLength_InBinaryMode()
        => await AssertInBothEnginesAsync(file => Program(file, "Binary", "TV", "X.V = \"hi\" ;; X.N = 7", Write, "VarType(Y.V) & \" [\" & Y.V & \"] \" & Y.N"),
            "dump 0800020068690700", "back 8 [hi] 7");

    [TestMethod]
    public async Task AVariantHoldingAString_HasItsLength_InBinaryMode()
        => await AssertInBothEnginesAsync(file => Program(file, "Binary", "Variant", "X = \"hi\"", Write, "VarType(Y) & \" [\" & Y & \"]\""),
            "dump 080002006869", "back 8 [hi]");
}
