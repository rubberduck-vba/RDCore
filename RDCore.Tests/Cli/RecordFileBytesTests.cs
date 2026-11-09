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

    // a fixed array field is its elements, one after the other, with nothing before them: a Put that wrote none would leave Get reading past the end of the file (62).
    [TestMethod]
    [DataRow("Random Len = 20", DisplayName = "Random")]
    [DataRow("Binary", DisplayName = "Binary")]
    public async Task AFixedArrayField_IsItsElements(string open)
        => await AssertInBothEnginesAsync(file => Program(file, open, "TA", "X.N = 1 ;; X.A(1) = 2 ;; X.A(2) = 3 ;; X.A(3) = 4", Write,
            "Y.N & \" \" & Y.A(1) & Y.A(2) & Y.A(3)"),
            "dump 0100020003000400", "back 1 234");

    // a resizable array field is how many dimensions it has, then each dimension's count and lower bound, then its elements, in either mode.
    [TestMethod]
    [DataRow("Random Len = 30", DisplayName = "Random")]
    [DataRow("Binary", DisplayName = "Binary")]
    public async Task AResizableArrayField_IsItsDimensions_ThenItsElements(string open)
        => await AssertInBothEnginesAsync(file => Program(file, open, "TR", "X.N = 1 ;; ReDim X.A(1 To 2) ;; X.A(1) = 5 ;; X.A(2) = 6", Write,
            "Y.N & \" \" & LBound(Y.A) & UBound(Y.A) & \" \" & Y.A(1) & Y.A(2)"),
            "dump 01000100020000000100000005000600", "back 1 12 56");

    [TestMethod]
    public async Task AResizableArrayFieldThatHasNoElements_IsNoDimension()
        => await AssertInBothEnginesAsync(file => Program(file, "Random Len = 30", "TR", "X.N = 1", Write, "Y.N"),
            "dump 01000000", "back 1");

    // a fixed-length String has no two-byte prefix (MS-VBAL §5.4.5.11's Fixed-length String row) in Random mode either: with one, Get would read the
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

    // a Variant field read by Get is a Variant still: the record written after it has the field's type descriptor.
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

    // a String a Variant holds has its two-byte length in Binary mode too, in a field or not: without it, Get would read no character.
    [TestMethod]
    public async Task AVariantFieldHoldingAString_HasItsLength_InBinaryMode()
        => await AssertInBothEnginesAsync(file => Program(file, "Binary", "TV", "X.V = \"hi\" ;; X.N = 7", Write, "VarType(Y.V) & \" [\" & Y.V & \"] \" & Y.N"),
            "dump 0800020068690700", "back 8 [hi] 7");

    [TestMethod]
    public async Task AVariantHoldingAString_HasItsLength_InBinaryMode()
        => await AssertInBothEnginesAsync(file => Program(file, "Binary", "Variant", "X = \"hi\"", Write, "VarType(Y) & \" [\" & Y & \"]\""),
            "dump 080002006869", "back 8 [hi]");

    // a two-dimension array field is its elements with the first subscript varying fastest, the order the array holds them in.
    [TestMethod]
    public async Task ATwoDimensionFixedArrayField_IsItsElements_FirstSubscriptFastest()
        => await AssertInBothEnginesAsync(file => Program(file, "Binary", "TF2", "For I = 1 To 2: For J = 1 To 3: X.A(I, J) = I * 10 + J: Next: Next", Write,
            "Y.A(1, 1) & \" \" & Y.A(2, 1) & \" \" & Y.A(1, 3) & \" \" & Y.A(2, 3)"),
            "dump 0B0015000C0016000D001700", "back 11 21 13 23");

    // the bounds of a resizable array field come last dimension first, as a SAFEARRAY lists them: (1 To 2, 0 To 1) is 2 from 0, then 2 from 1.
    [TestMethod]
    public async Task ATwoDimensionResizableArrayField_HasItsLastDimensionFirst()
        => await AssertInBothEnginesAsync(file => Program(file, "Binary", "TR2", "ReDim X.A(1 To 2, 0 To 1) ;; For I = 1 To 2: For J = 0 To 1: X.A(I, J) = I * 10 + J: Next: Next", Write,
            "LBound(Y.A, 1) & UBound(Y.A, 1) & LBound(Y.A, 2) & UBound(Y.A, 2) & \" \" & Y.A(1, 0) & \" \" & Y.A(2, 0) & \" \" & Y.A(2, 1)"),
            "dump 0200020000000000000002000000010000000A0014000B001500", "back 1201 10 20 21");

    // Get gives a resizable array field the bounds the record has, whatever bounds the field had.
    [TestMethod]
    public async Task AResizableArrayFieldReadByGet_HasTheBoundsOfTheRecord()
        => await AssertInBothEnginesAsync(file => Program(file, "Binary", "TR2", "ReDim X.A(1 To 2) ;; X.A(1) = 5 ;; X.A(2) = 6 ;; ReDim Y.A(5 To 9)", Write,
            "LBound(Y.A) & UBound(Y.A) & \" \" & Y.A(1) & Y.A(2)"),
            "dump 0100020000000100000005000600", "back 12 56");

    // ... and a record whose array has no dimension leaves the field with none: LBound of it is error 9.
    [TestMethod]
    public async Task AResizableArrayFieldReadByGet_FromOneThatHasNoElements_HasNone()
        => await AssertInBothEnginesAsync(file => Program(file, "Binary", "TRS", "X.N = 7 ;; ReDim Y.A(1 To 3)",
            "Put #1, 1, X ;; Get #1, 1, Y ;; On Error Resume Next ;; Debug.Print \"lb \" & LBound(Y.A) ;; Debug.Print \"err \" & Err.Number ;; On Error GoTo 0", "Y.N"),
            "err 9", "dump 00000700", "back 7");

    // an element of an array field is written as a field of its type is: a String has its length, a Variant its descriptor, a String * n its characters
    // padded or cut to its length, and a record its own fields.
    [TestMethod]
    [DataRow("Random Len = 40", DisplayName = "Random")]
    [DataRow("Binary", DisplayName = "Binary")]
    public async Task AStringElementOfAnArrayField_IsItsLength_ThenItsCharacters(string open)
        => await AssertInBothEnginesAsync(file => Program(file, open, "TFS", "X.A(1) = \"a\" ;; X.A(2) = \"bc\" ;; X.N = 7", Write,
            "\"[\" & Y.A(1) & \"][\" & Y.A(2) & \"] \" & Y.N"),
            "dump 010061020062630700", "back [a][bc] 7");

    [TestMethod]
    public async Task AStringElementOfAResizableArrayField_IsItsLength_ThenItsCharacters()
        => await AssertInBothEnginesAsync(file => Program(file, "Binary", "TRS", "ReDim X.A(0 To 1) ;; X.A(0) = \"a\" ;; X.A(1) = \"bc\" ;; X.N = 7", Write,
            "LBound(Y.A) & UBound(Y.A) & \" [\" & Y.A(0) & \"][\" & Y.A(1) & \"] \" & Y.N"),
            "dump 01000200000000000000010061020062630700", "back 01 [a][bc] 7");

    [TestMethod]
    public async Task AVariantElementOfAnArrayField_IsItsDescriptor_ThenItsValue()
        => await AssertInBothEnginesAsync(file => Program(file, "Binary", "TFV", "X.A(1) = CInt(5) ;; X.A(2) = \"x\" ;; X.N = 7", Write,
            "VarType(Y.A(1)) & \" \" & Y.A(1) & \" \" & VarType(Y.A(2)) & \" \" & Y.A(2) & \" \" & Y.N"),
            "dump 0200050008000100780700", "back 2 5 8 x 7");

    [TestMethod]
    public async Task AVariantElementOfAResizableArrayField_IsItsDescriptor_ThenItsValue()
        => await AssertInBothEnginesAsync(file => Program(file, "Random Len = 40", "TRV", "ReDim X.A(1 To 2) ;; X.A(1) = CLng(5) ;; X.A(2) = Empty ;; X.N = 7", Write,
            "LBound(Y.A) & UBound(Y.A) & \" \" & VarType(Y.A(1)) & \" \" & Y.A(1) & \" \" & VarType(Y.A(2)) & \" \" & Y.N"),
            "dump 0100020000000100000003000500000000000700", "back 12 3 5 0 7");

    [TestMethod]
    public async Task AFixedLengthStringElementOfAnArrayField_IsItsCharacters_PaddedOrCut()
        => await AssertInBothEnginesAsync(file => Program(file, "Binary", "TFX", "X.A(1) = \"a\" ;; X.A(2) = \"bcde\" ;; X.N = 7", Write,
            "\"[\" & Y.A(1) & \"][\" & Y.A(2) & \"] \" & Y.N"),
            "dump 6120206263640700", "back [a  ][bcd] 7");

    [TestMethod]
    public async Task ARecordElementOfAnArrayField_IsItsFields()
        => await AssertInBothEnginesAsync(file => Program(file, "Binary", "TFU", "X.A(1).N = 1 ;; X.A(1).S = \"a\" ;; X.A(2).N = 2 ;; X.A(2).S = \"bc\"", Write,
            "Y.A(1).N & \"[\" & Y.A(1).S & \"] \" & Y.A(2).N & \"[\" & Y.A(2).S & \"]\""),
            "dump 0100010061020002006263", "back 1[a] 2[bc]");

    [TestMethod]
    public async Task AByteArrayField_IsItsDimensions_ThenItsBytes()
        => await AssertInBothEnginesAsync(file => Program(file, "Binary", "TRB", "ReDim X.A(0 To 2) ;; X.A(0) = 1 ;; X.A(1) = 2 ;; X.A(2) = 255 ;; X.N = 7", Write,
            "LBound(Y.A) & UBound(Y.A) & \" \" & Y.A(0) & \" \" & Y.A(2) & \" \" & Y.N"),
            "dump 010003000000000000000102FF0700", "back 02 1 255 7");

    // a Variant field that holds an array is described as VarType describes it, vbArray + vbVariant (&H200C), then is the array with its bounds.
    [TestMethod]
    public async Task AVariantFieldHoldingAnArray_IsItsVarType_ThenTheArray()
        => await AssertInBothEnginesAsync(file => Program(file, "Binary", "TV", "X.V = Array(CInt(5), CInt(6)) ;; X.N = 7", Write,
            "VarType(Y.V) & \" \" & LBound(Y.V) & UBound(Y.V) & \" \" & Y.N"),
            "dump 0C200100020000000000000002000500020006000700", "back 8204 01 7");
}
