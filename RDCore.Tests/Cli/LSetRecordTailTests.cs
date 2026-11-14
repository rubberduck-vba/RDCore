using RDCore.SDK.Runtime.Abstract.Execution;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <c>LSet</c> of a record onto a record of another size (<strong>MS-VBAL §5.4.3.6</strong>, whose copy is implementation-defined): MS-VBA copies the bytes
/// both records have and leaves the rest of the destination as it was, a field the copy reaches only part of included (observed in Excel).
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.4.3.6 LSet Statement")]
public sealed class LSetRecordTailTests
{
    private static string Program(params string[] body)
        => "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n" +
            "Private Type TI\r\n    I As Integer\r\nEnd Type\r\n" +
            "Private Type TL\r\n    L As Long\r\nEnd Type\r\n" +
            "Private Type TII\r\n    A As Integer\r\n    B As Integer\r\nEnd Type\r\n" +
            "Private Type TD\r\n    D As Double\r\nEnd Type\r\n" +
            "Private Type TS4\r\n    S As String * 4\r\nEnd Type\r\n" +
            "Private Type TIL\r\n    I As Integer\r\n    L As Long\r\nEnd Type\r\n" +
            "Private Type TB3\r\n    X As Byte\r\n    Y As Byte\r\n    Z As Byte\r\nEnd Type\r\n" +
            "Private Type TNest\r\n    N As TIL\r\n    Tail As Integer\r\nEnd Type\r\n" +
            "Public Sub Main()\r\n" + string.Join("\r\n", body) + "\r\nEnd Sub\r\n";

    private static async Task Expect(string[] expected, string program, VBExecutionEngine engine)
        => Assert.AreEqual(string.Join(" | ", expected), string.Join(" | ", await RunAsync([], program, engine)));

    // the low two bytes of the Long are the Integer's, and its high two bytes are what they were.
    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter, DisplayName = "Interpreter")]
    [DataRow(VBExecutionEngine.Compiled, DisplayName = "Compiled")]
    public async Task AShorterRecord_LSetOntoALong_KeepsTheBytesItDoesNotReach(VBExecutionEngine engine)
        => await Expect(["L 12340102", "L 102", "L FFFF 65535"], Program(
            "    Dim a As TI, b As TL",
            "    b.L = &H12345678",
            "    a.I = &H102",
            "    LSet b = a",
            "    Debug.Print \"L \" & Hex(b.L)",
            "    b.L = 0",
            "    LSet b = a",
            "    Debug.Print \"L \" & Hex(b.L)",
            "    a.I = -1",
            "    LSet b = a",
            "    Debug.Print \"L \" & Hex(b.L) & \" \" & b.L"), engine);

    // a field the copy does not reach at all is what it was.
    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter, DisplayName = "Interpreter")]
    [DataRow(VBExecutionEngine.Compiled, DisplayName = "Compiled")]
    public async Task AShorterRecord_LSetOntoTwoFields_LeavesTheSecondAsItWas(VBExecutionEngine engine)
        => await Expect(["A 5 B 22"], Program(
            "    Dim a As TI, t As TII",
            "    t.A = 11: t.B = 22",
            "    a.I = 5",
            "    LSet t = a",
            "    Debug.Print \"A \" & t.A & \" B \" & t.B"), engine);

    // the low four bytes of the Double are the Long's, and its high four bytes are what they were: 1.5 and 7 in its last bits.
    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter, DisplayName = "Interpreter")]
    [DataRow(VBExecutionEngine.Compiled, DisplayName = "Compiled")]
    public async Task AShorterRecord_LSetOntoADouble_KeepsItsHighBytes(VBExecutionEngine engine)
        => await Expect(["D 1.5"], Program(
            "    Dim a As TL, d As TD",
            "    d.D = 1.5",
            "    a.L = 7",
            "    LSet d = a",
            "    Debug.Print \"D \" & d.D"), engine);

    // a Long of -1 over the low four bytes of 1.5 changes it in its last digits.
    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter, DisplayName = "Interpreter")]
    [DataRow(VBExecutionEngine.Compiled, DisplayName = "Compiled")]
    public async Task AShorterRecord_LSetOntoADouble_ChangesOnlyItsLowBytes(VBExecutionEngine engine)
        => await Expect(["D 1.50000095367432 False"], Program(
            "    Dim a As TL, d As TD",
            "    d.D = 1.5",
            "    a.L = -1",
            "    LSet d = a",
            "    Debug.Print \"D \" & d.D & \" \" & (d.D = 1.5)"), engine);

    // a fixed-length String field is two bytes a character: an Integer of 65 is its first character, an A, and the others are what they were.
    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter, DisplayName = "Interpreter")]
    [DataRow(VBExecutionEngine.Compiled, DisplayName = "Compiled")]
    public async Task AShorterRecord_LSetOntoAFixedLengthString_ReplacesItsFirstCharacter(VBExecutionEngine engine)
        => await Expect(["[Axyz] 4"], Program(
            "    Dim a As TI, s As TS4",
            "    s.S = \"wxyz\"",
            "    a.I = 65",
            "    LSet s = a",
            "    Debug.Print \"[\" & s.S & \"] \" & Len(s.S)"), engine);

    // a field past the end of the source is what it was, after the padding the source's bytes reach, in a nested record too, and a Byte field after two
    // Bytes of an Integer.
    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter, DisplayName = "Interpreter")]
    [DataRow(VBExecutionEngine.Compiled, DisplayName = "Compiled")]
    public async Task AFieldPastTheEndOfTheSource_IsWhatItWas(VBExecutionEngine engine)
        => await Expect(["3 12345678", "2 1 9", "3 12345678 33"], Program(
            "    Dim a As TL, t As TIL, i As TI, b As TB3, n As TNest",
            "    t.I = 11: t.L = &H12345678",
            "    a.L = &H10003",
            "    LSet t = a",
            "    Debug.Print t.I & \" \" & Hex(t.L)",
            "    b.X = 7: b.Y = 8: b.Z = 9",
            "    i.I = &H102",
            "    LSet b = i",
            "    Debug.Print b.X & \" \" & b.Y & \" \" & b.Z",
            "    n.N.I = 11: n.N.L = &H12345678: n.Tail = 33",
            "    LSet n = a",
            "    Debug.Print n.N.I & \" \" & Hex(n.N.L) & \" \" & n.Tail"), engine);

    // a longer record gives the destination the bytes it has room for: RD-VBA and MS-VBA agree.
    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter, DisplayName = "Interpreter")]
    [DataRow(VBExecutionEngine.Compiled, DisplayName = "Compiled")]
    public async Task ALongerRecord_LSetOntoAnInteger_GivesItTheLowBytes(VBExecutionEngine engine)
        => await Expect(["I 3"], Program(
            "    Dim a As TL, t As TI",
            "    a.L = &H10003",
            "    LSet t = a",
            "    Debug.Print \"I \" & t.I"), engine);
}
