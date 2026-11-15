using RDCore.SDK.Runtime.Abstract.Execution;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>Put</c> and a <c>Get</c> whose data is an array variable, and a <c>Get</c> that runs past the end of the file (<strong>MS-VBAL §5.4.5.11</strong>,
/// <strong>§5.4.5.12</strong>): byte for byte what MS-VBA writes and reads (recorded in Excel, 64-bit).
/// </summary>
/// <remarks>
/// Each row has a folder of its own, with <c>six.bin</c> in it: the six bytes <c>2A 01 02 03 04 05</c>. <c>Dump</c> is every byte of a file, in hex.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.4.5.11 Put")]
[TestCategory("MS-VBAL 5.4.5.12 Get")]
public sealed class ArrayRecordTests
{
    private const string Helpers =
        "Private Type TP\r\n    A As Long\r\n    B As String\r\n    C As Integer\r\nEnd Type\r\n" +
        "Private Function Arr(ByRef a() As Byte) As String\r\n" +
        "    On Error Resume Next\r\n" +
        "    Dim i As Long, t As String, lb As Long: lb = -99: lb = LBound(a)\r\n" +
        "    If lb = -99 Then Arr = \"(unallocated)\": Exit Function\r\n" +
        "    t = \"(\" & LBound(a) & \"..\" & UBound(a) & \")\"\r\n" +
        "    For i = LBound(a) To UBound(a): t = t & \" \" & Right$(\"0\" & Hex$(a(i)), 2): Next\r\n" +
        "    Arr = t\r\n" +
        "End Function\r\n" +
        "Private Function Hx(ByVal s As String) As String\r\n" +
        "    Dim i As Long, t As String\r\n" +
        "    For i = 1 To Len(s): t = t & Right$(\"0\" & Hex$(Asc(Mid$(s, i, 1))), 2): Next\r\n" +
        "    Hx = t\r\n" +
        "End Function\r\n" +
        "Private Function Dump(ByVal path As String) As String\r\n" +
        "    Dim f As Integer, x As Byte, i As Long, t As String\r\n" +
        "    f = FreeFile\r\n" +
        "    Open path For Binary Access Read As #f\r\n" +
        "    For i = 1 To LOF(f): Get #f, i, x: t = t & Right$(\"0\" & Hex$(x), 2): Next\r\n" +
        "    Close #f\r\n" +
        "    Dump = \"[\" & t & \"]\"\r\n" +
        "End Function\r\n";

    private const string Declarations =
        "    Dim b() As Byte, bf(0 To 2) As Byte, n() As Integer, s() As String, v() As Variant, b2() As Byte, l() As Long\r\n" +
        "    Dim gl As Long, gb As Byte, gs As String, gv As Variant, gi As Integer, t As TP\r\n";

    // body: the lines of Main, ";;" between them, with O the folder (and its separator).
    private static string Program(string folder, string body)
        => "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n" + Helpers +
           "Public Sub Main()\r\n" + Declarations +
           $"    Dim O As String: O = \"{folder}\"\r\n" +
           string.Join("\r\n", body.Split(";;").Select(line => "    " + line.Trim())) + "\r\n" +
           "End Sub\r\n";

    private static async Task AssertInBothEnginesAsync(string body, string expected)
    {
        foreach (var engine in new[] { VBExecutionEngine.Interpreter, VBExecutionEngine.Compiled })
        {
            var folder = Path.Combine(Path.GetTempPath(), $"rdcore-array-records-{Guid.NewGuid():N}");
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "six.bin"), [0x2A, 0x01, 0x02, 0x03, 0x04, 0x05]);
            try
            {
                var output = await RunAsync([], Program(folder + Path.DirectorySeparatorChar, body), engine);
                Assert.AreEqual(expected, string.Join(" | ", output), engine.ToString());
            }
            finally
            {
                try
                {
                    Directory.Delete(folder, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    private const string Six = "Open O & \"six.bin\" For Binary As #1";

    // a Binary Get reads into the array as it is sized, its first subscript varying fastest, and an array that was never sized reads nothing.
    [TestMethod]
    [DataRow("ReDim b(0 To 3) ;; " + Six + " ;; Get #1, 1, b ;; Debug.Print Arr(b) & \" seek \" & Seek(1)", "(0..3) 2A 01 02 03 seek 5",
        DisplayName = "dynamic Byte")]
    [DataRow("Erase b ;; " + Six + " ;; Get #1, 1, b ;; Debug.Print Arr(b) & \" seek \" & Seek(1)", "(unallocated) seek 1", DisplayName = "never sized")]
    [DataRow(Six + " ;; Get #1, 2, bf ;; Debug.Print bf(0) & \" \" & bf(1) & \" \" & bf(2) & \" seek \" & Seek(1)", "1 2 3 seek 5", DisplayName = "fixed")]
    [DataRow("ReDim n(1 To 2) ;; " + Six + " ;; Get #1, 1, n ;; Debug.Print n(1) & \" \" & n(2) & \" seek \" & Seek(1)", "298 770 seek 5",
        DisplayName = "Integer")]
    [DataRow("ReDim b2(0 To 1, 0 To 1) ;; " + Six + " ;; Get #1, 1, b2 ;; Debug.Print b2(0, 0) & \" \" & b2(1, 0) & \" \" & b2(0, 1) & \" \" & b2(1, 1)",
        "42 1 2 3", DisplayName = "2-D")]
    [DataRow("ReDim b(0 To 1) ;; " + Six + " ;; Get #1, , b ;; Get #1, , b ;; Debug.Print Arr(b) & \" seek \" & Seek(1)", "(0..1) 02 03 seek 5",
        DisplayName = "two Gets")]
    public async Task ABinaryGet_ReadsIntoTheArrayAsItIsSized(string body, string expected)
        => await AssertInBothEnginesAsync(body, expected);

    // each element is read as an element of an array field is: a String with its two-byte length, a Variant with its type descriptor.
    [TestMethod]
    [DataRow("ReDim s(0 To 1) ;; s(0) = \"ab\" ;; s(1) = \"c\" ;; Open O & \"p.bin\" For Binary As #1 ;; Put #1, 1, s ;; ReDim s(0 To 1) ;; Get #1, 1, s ;; " +
        "Debug.Print \"[\" & s(0) & \"] [\" & s(1) & \"] seek \" & Seek(1) ;; Close #1 ;; Debug.Print Dump(O & \"p.bin\")",
        "[ab] [c] seek 8 | [02006162010063]", DisplayName = "String")]
    [DataRow("ReDim v(0 To 1) ;; v(0) = CByte(3) ;; v(1) = \"x\" ;; Open O & \"p.bin\" For Binary As #1 ;; Put #1, 1, v ;; ReDim v(0 To 1) ;; Get #1, 1, v ;; " +
        "Debug.Print TypeName(v(0)) & v(0) & \" \" & TypeName(v(1)) & v(1) & \" seek \" & Seek(1) ;; Close #1 ;; Debug.Print Dump(O & \"p.bin\")",
        "Byte3 Stringx seek 9 | [1100030800010078]", DisplayName = "Variant")]
    public async Task ABinaryArrayElement_IsWrittenAndReadAsAnElementOfAField(string body, string expected)
        => await AssertInBothEnginesAsync(body, expected);

    // a Binary Put writes the elements and nothing else, a fixed-size array's and a resizable one's alike; an array that was never sized writes nothing.
    [TestMethod]
    [DataRow("ReDim b(1 To 3) ;; b(1) = 7 ;; b(2) = 8 ;; b(3) = 9", "b", "[070809] seek 4", DisplayName = "dynamic Byte")]
    [DataRow("bf(0) = 4 ;; bf(1) = 5 ;; bf(2) = 6", "bf", "[040506] seek 4", DisplayName = "fixed")]
    [DataRow("Erase b", "b", "[] seek 1", DisplayName = "never sized")]
    [DataRow("ReDim n(0 To 1) ;; n(0) = 258 ;; n(1) = -1", "n", "[0201FFFF] seek 5", DisplayName = "Integer")]
    [DataRow("ReDim b2(0 To 1, 1 To 2) ;; b2(0, 1) = 1 ;; b2(1, 1) = 2 ;; b2(0, 2) = 3 ;; b2(1, 2) = 4", "b2", "[01020304] seek 5", DisplayName = "2-D")]
    public async Task ABinaryPut_WritesTheElements(string fill, string array, string expected)
        => await AssertInBothEnginesAsync(
            $"{fill} ;; Open O & \"p.bin\" For Binary As #1 ;; Put #1, 1, {array} ;; Dim at As Long: at = Seek(1) ;; Close #1 ;; " +
            "Debug.Print Dump(O & \"p.bin\") & \" seek \" & at", expected);

    // a Random Put writes a resizable array's number of dimensions, then the count and lower bound of each, the last dimension first, then its
    // elements: the format of an array field. A fixed-size array is its elements.
    [TestMethod]
    [DataRow("ReDim b(1 To 3) ;; b(1) = 7 ;; b(2) = 8 ;; b(3) = 9", "b", "[01000300000001000000070809]", DisplayName = "dynamic Byte")]
    [DataRow("bf(0) = 4 ;; bf(1) = 5 ;; bf(2) = 6", "bf", "[040506]", DisplayName = "fixed")]
    [DataRow("Erase b", "b", "[0000]", DisplayName = "never sized")]
    [DataRow("ReDim b2(0 To 1, 1 To 2) ;; b2(0, 1) = 1 ;; b2(1, 1) = 2 ;; b2(0, 2) = 3 ;; b2(1, 2) = 4", "b2", "[02000200000001000000020000000000000001020304]",
        DisplayName = "2-D")]
    [DataRow("ReDim b2(0 To 2, 5 To 6) ;; b2(2, 6) = 9", "b2", "[020002000000050000000300000000000000000000000009]", DisplayName = "2-D bounds")]
    [DataRow("ReDim s(1 To 2) ;; s(1) = \"ab\" ;; s(2) = \"c\"", "s", "[0100020000000100000002006162010063]", DisplayName = "String")]
    [DataRow("ReDim v(0 To 1) ;; v(0) = CByte(3) ;; v(1) = \"x\"", "v", "[010002000000000000001100030800010078]", DisplayName = "Variant")]
    public async Task ARandomPut_WritesAResizableArraysBounds_ThenItsElements(string fill, string array, string expected)
        => await AssertInBothEnginesAsync(
            $"{fill} ;; Open O & \"r.bin\" For Random As #1 Len = 64 ;; Put #1, 1, {array} ;; Close #1 ;; Debug.Print Dump(O & \"r.bin\")", expected);

    // a Random Get sizes a resizable array to the bounds its record gives, whatever it was sized to, and reads the elements as the array's own type.
    [TestMethod]
    [DataRow("Erase b ;; Get #1, 1, b ;; Debug.Print Arr(b)", "(1..3) 07 08 09", DisplayName = "never sized")]
    [DataRow("ReDim b(0 To 7) ;; Get #1, 1, b ;; Debug.Print Arr(b)", "(1..3) 07 08 09", DisplayName = "sized otherwise")]
    [DataRow("ReDim l(0 To 0) ;; Get #1, 1, l ;; Debug.Print LBound(l) & \"..\" & UBound(l) & \" \" & Hex$(l(1)) & \" \" & l(2) & \" \" & l(3)",
        "1..3 90807 0 0", DisplayName = "Long")]
    public async Task ARandomGet_SizesAResizableArrayToItsRecord(string read, string expected)
        => await AssertInBothEnginesAsync(
            "ReDim b(1 To 3) ;; b(1) = 7 ;; b(2) = 8 ;; b(3) = 9 ;; Open O & \"r.bin\" For Random As #1 Len = 64 ;; Put #1, 1, b ;; " + read, expected);

    [TestMethod]
    [DataRow("ReDim b2(0 To 1, 1 To 2) ;; b2(1, 2) = 4", "Debug.Print LBound(b2, 1) & \"..\" & UBound(b2, 1) & \",\" & LBound(b2, 2) & \"..\" & UBound(b2, 2) & \" \" & b2(1, 2)",
        "0..1,1..2 4", DisplayName = "2-D")]
    [DataRow("ReDim b2(0 To 2, 5 To 6) ;; b2(2, 6) = 9", "Debug.Print LBound(b2, 1) & \"..\" & UBound(b2, 1) & \",\" & LBound(b2, 2) & \"..\" & UBound(b2, 2) & \" \" & b2(2, 6)",
        "0..2,5..6 9", DisplayName = "2-D bounds")]
    [DataRow("ReDim v(0 To 1) ;; v(0) = CByte(3) ;; v(1) = \"x\"", "Debug.Print LBound(v) & \"..\" & UBound(v) & \" \" & TypeName(v(0)) & v(0) & \" \" & TypeName(v(1)) & v(1)",
        "0..1 Byte3 Stringx", DisplayName = "Variant")]
    public async Task ARandomPutThenGet_ReadsTheArrayBack(string fill, string print, string expected)
    {
        var array = fill.Contains("b2") ? "b2" : "v";
        await AssertInBothEnginesAsync(
            $"{fill} ;; Open O & \"r.bin\" For Random As #1 Len = 64 ;; Put #1, 1, {array} ;; Erase {array} ;; Get #1, 1, {array} ;; {print}", expected);
    }

    [TestMethod]
    public async Task ARandomGet_ReadsAFixedArrayAsItIsSized()
        => await AssertInBothEnginesAsync(
            "bf(0) = 4 ;; bf(1) = 5 ;; bf(2) = 6 ;; Open O & \"r.bin\" For Random As #1 Len = 64 ;; Put #1, 1, bf ;; Erase bf ;; Get #1, 1, bf ;; " +
            "Debug.Print bf(0) & \" \" & bf(1) & \" \" & bf(2)", "4 5 6");

    // a record longer than the channel's records is not written at all, an array's as a String's.
    [TestMethod]
    [DataRow("ReDim b(1 To 70)", "b", DisplayName = "array")]
    [DataRow("gs = \"abcdefghij\"", "gs", DisplayName = "String")]
    public async Task ARandomPutLongerThanTheRecord_IsBadRecordLength_AndWritesNothing(string fill, string data)
        => await AssertInBothEnginesAsync(
            $"{fill} ;; On Error Resume Next ;; Open O & \"r.bin\" For Random As #1 Len = {(data == "gs" ? 8 : 64)} ;; Put #1, 1, {data} ;; " +
            "Debug.Print CStr(Err.Number) ;; Close #1 ;; Debug.Print Dump(O & \"r.bin\")", "59 | []");

    // a Get past the end of the file is no error: each byte the file does not have reads a zero, the position moves past them all, and EOF is True.
    [TestMethod]
    [DataRow("ReDim b(0 To 9) ;; " + Six + " ;; Get #1, 1, b ;; Debug.Print Arr(b) & \" seek \" & Seek(1) & \" \" & EOF(1)",
        "(0..9) 2A 01 02 03 04 05 00 00 00 00 seek 11 True", DisplayName = "array")]
    [DataRow(Six + " ;; Get #1, 5, bf ;; Debug.Print bf(0) & \" \" & bf(1) & \" \" & bf(2) & \" seek \" & Seek(1) & \" \" & EOF(1)", "4 5 0 seek 8 True",
        DisplayName = "fixed array")]
    [DataRow("gl = -1 ;; " + Six + " ;; Get #1, 5, gl ;; Debug.Print Hex$(gl) & \" seek \" & Seek(1) & \" \" & EOF(1)", "504 seek 9 True", DisplayName = "Long")]
    [DataRow("gb = 99 ;; " + Six + " ;; Get #1, 20, gb ;; Debug.Print gb & \" seek \" & Seek(1)", "0 seek 21", DisplayName = "Byte")]
    [DataRow("gs = \"xyz\" ;; " + Six + " ;; Get #1, 5, gs ;; Debug.Print Hx(gs) & \" seek \" & Seek(1)", "040500 seek 8", DisplayName = "String")]
    [DataRow("gv = 5 ;; " + Six + " ;; Get #1, 20, gv ;; Debug.Print TypeName(gv) & \" seek \" & Seek(1)", "Empty seek 22", DisplayName = "Variant")]
    [DataRow("t.A = -1 ;; t.B = \"q\" ;; t.C = 9 ;; " + Six + " ;; Get #1, 5, t ;; Debug.Print Hex$(t.A) & \" \" & Len(t.B) & \" \" & t.C & \" seek \" & Seek(1) & \" \" & EOF(1)",
        "504 0 0 seek 13 True", DisplayName = "record")]
    [DataRow("ReDim s(0 To 1) ;; s(0) = \"ab\" ;; " + Six + " ;; Get #1, 1, s ;; Debug.Print Len(s(0)) & \" \" & Left$(Hx(s(0)), 8) & \" \" & Len(s(1)) & \" seek \" & Seek(1)",
        "298 02030405 0 seek 303", DisplayName = "String elements")]
    [DataRow("gi = 7 ;; Open O & \"r.bin\" For Random As #1 Len = 64 ;; Put #1, 1, gi ;; Get #1, 3, gi ;; Debug.Print gi & \" \" & EOF(1)", "0 True",
        DisplayName = "Random")]
    public async Task AGetPastTheEndOfTheFile_ReadsZeros(string body, string expected)
        => await AssertInBothEnginesAsync(body, expected);

    [TestMethod]
    public async Task AGetToTheExactEndOfTheFile_IsNotPastIt()
        => await AssertInBothEnginesAsync(
            "gi = 7 ;; " + Six + " ;; Get #1, 5, gi ;; Debug.Print Hex$(gi) & \" seek \" & Seek(1) & \" \" & EOF(1)", "504 seek 7 False");
}
