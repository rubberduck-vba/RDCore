using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <c>StrPtr</c>: the address of the characters of a <c>String</c> in the session's memory, as <strong>MS-VBA</strong> gives it (recorded in Excel, oracle\s17
/// S rows) - <c>0</c> for <c>vbNullString</c>, the characters of the very <c>String</c> it is passed, the same until another is assigned, and a copy's for a
/// copy.
/// </summary>
[TestClass]
public sealed class StrPtrTests
{
    private static readonly (string Name, string Source) Globals = ("Globals", StandardModule("Globals",
        "Option Explicit",
        "Public Type Mixed", "    B As Byte", "    S As String", "    V As Variant", "    F As String * 3", "End Type",
        "Public Type Names", "    B As Byte", "    N(2) As String", "End Type",
        "Public Sub ByValStr(ByVal s As String, ByRef addr As LongPtr)", "    addr = StrPtr(s)", "End Sub",
        "Public Sub ByRefStr(ByRef s As String, ByRef addr As LongPtr)", "    addr = StrPtr(s)", "End Sub"));

    private static string Program(string body)
        => "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPrivate acc As String\r\nPrivate Sub P(ByVal v As Variant)\r\n    acc = acc & CStr(v) & \"|\"\r\nEnd Sub\r\n"
            + $"Public Sub Main()\r\nOn Error Resume Next\r\n{string.Join("\r\n", body.Split(" ;; "))}\r\nDebug.Print acc\r\nEnd Sub\r\n";

    private static async Task<string> RunAsync(string body)
        => string.Join(" ", await ModuleWorkspace.RunAsync([], [Globals], Program(body)));

    [TestMethod]
    [DataRow("P StrPtr(vbNullString) ;; P (StrPtr(\"\") <> 0) ;; Dim s As String ;; P StrPtr(s)", "0|True|0|", DisplayName = "S01 vbNullString is 0, a zero-length String is not")]
    [DataRow("Dim s As String, s0 As LongPtr ;; s = \"abc\" ;; s0 = StrPtr(s) ;; P (StrPtr(s) = s0) ;; s = \"abc\" ;; P (StrPtr(s) = s0)", "True|False|",
        DisplayName = "S02 the same until another String is assigned, the same text too")]
    [DataRow("Dim s As String, t As String ;; s = \"abc\" ;; t = s ;; P (StrPtr(t) = StrPtr(s))", "False|", DisplayName = "S03 an assignment copies the characters")]
    // the caller's characters are asked for first, as MS-VBA's exist from the assignment on: a copy freed by the call is not then where they come to be.
    [DataRow("Dim s As String, s0 As LongPtr, addr As LongPtr ;; s = \"abc\" ;; s0 = StrPtr(s) ;; ByValStr s, addr ;; P (addr = s0) ;; ByRefStr s, addr ;; P (addr = s0)", "False|True|",
        DisplayName = "S06 a ByVal parameter is a copy, a ByRef one the variable")]
    [DataRow("Dim s As String, s0 As LongPtr ;; s = \"abc\" ;; s0 = StrPtr(s) ;; s = s & \"\" ;; P (StrPtr(s) = s0)", "False|", DisplayName = "S07 a concatenation is another String")]
    [DataRow("Dim v As Variant ;; v = \"abc\" ;; P (StrPtr(v) <> 0) ;; P (StrPtr(v) = StrPtr(v))", "True|True|", DisplayName = "S08 a Variant holding a String")]
    [DataRow("Dim a(1) As String ;; a(0) = \"x\" ;; P (StrPtr(a(0)) <> 0) ;; P (StrPtr(a(0)) = StrPtr(a(0))) ;; P StrPtr(a(1))", "True|True|0|",
        DisplayName = "S10 an element, and one nothing was assigned to")]
    [DataRow("Dim r As Mixed ;; r.S = \"hey\" ;; P (StrPtr(r.S) <> 0) ;; P (StrPtr(r.S) = StrPtr(r.S))", "True|True|",
        DisplayName = "S11 a field of a record")]
    [DataRow("Dim r As Names, s0 As LongPtr ;; r.N(1) = \"q\" ;; s0 = StrPtr(r.N(1)) ;; P (s0 <> 0) ;; P (StrPtr(r.N(1)) = s0) ;; P StrPtr(r.N(0)) ;; Mid$(r.N(1), 1, 1) = \"z\" ;; P (StrPtr(r.N(1)) = s0)",
        "True|True|0|True|", DisplayName = "an element of a fixed-size array field")]
    [DataRow("Dim s As String, t As String ;; s = \"a\" ;; t = \"b\" ;; P (StrPtr(s) <> StrPtr(t))", "True|", DisplayName = "two Strings, two addresses")]
    [DataRow("P TypeName(StrPtr(\"abc\")) ;; P (StrPtr(\"abc\") <> 0)", "LongLong|True|", DisplayName = "a LongPtr, and a value is a temporary")]
    [DataRow("Dim n As Long, f As String * 3, r As Mixed ;; n = 5 ;; P (StrPtr(n) <> 0) ;; P (StrPtr(f) <> 0) ;; P (StrPtr(r.F) <> 0)", "True|True|True|",
        DisplayName = "what is no String is passed as a String made of it")]
    public async Task StrPtr_IsTheAddressOfTheCharacters(string body, string expected)
        => Assert.AreEqual(expected, await RunAsync(body));

    // S07: the Mid statement writes the characters where they are.
    [TestMethod]
    public async Task TheMidStatement_WritesTheCharactersInPlace()
        => Assert.AreEqual("True|zbc|", await RunAsync("Dim s As String, s0 As LongPtr ;; s = \"abc\" ;; s0 = StrPtr(s) ;; Mid$(s, 1, 1) = \"z\" ;; P (StrPtr(s) = s0) ;; P s"));

    // S02: any assignment makes another String, of the String itself too.
    [TestMethod]
    public async Task AssigningAStringItself_MovesItsCharacters()
        => Assert.AreEqual("False|", await RunAsync("Dim s As String, s0 As LongPtr ;; s = \"abc\" ;; s0 = StrPtr(s) ;; s = s ;; P (StrPtr(s) = s0)"));
}
