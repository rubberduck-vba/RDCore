using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>Get</c> statement reads into a variable (<strong>MS-VBAL §5.4.5.12</strong>), and the result of the function or the <c>Property Get</c> it is
/// written in is one, its function result variable (<strong>MS-VBAL §5.3.1</strong>): the name of the procedure, inside it, is its result.
/// </summary>
[TestClass]
public sealed class GetIntoProcedureResultTests
{
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), $"rdcore-get-result-{Guid.NewGuid():N}");
    private static readonly string File = Path.Combine(Folder, "data.bin");

    [ClassInitialize]
    public static void WriteFile(TestContext _)
    {
        Directory.CreateDirectory(Folder);
        System.IO.File.WriteAllBytes(File, [0x2A, 0x01, 0x02, 0x03, 0x04, 0x05]);
    }

    [ClassCleanup]
    public static void DeleteFile() => Directory.Delete(Folder, recursive: true);

    [TestMethod]
    public async Task AGet_ReadsIntoTheResultOfTheFunctionItIsWrittenIn()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            $"    Debug.Print ByteAt(\"{File}\", 0); ByteAt(\"{File}\", 3)",
            "End Sub",
            "Private Function ByteAt(ByVal path As String, ByVal at As Long) As Byte",
            "    Dim f As Integer",
            "    f = FreeFile",
            "    Open path For Binary Access Read As #f",
            "    Get #f, at + 1, ByteAt",
            "    Close #f",
            "End Function"), expected: "Completed | 42  3 |");

    // a Binary-mode String reads as many characters as the variable holds: the function's result, as much as any other.
    [TestMethod]
    public async Task AGet_ReadsAsMuchIntoAStringResultAsItHolds()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            $"    Debug.Print Len(Head(\"{File}\")); Asc(Mid$(Head(\"{File}\"), 3, 1))",
            "End Sub",
            "Private Function Head(ByVal path As String) As String",
            "    Dim f As Integer",
            "    f = FreeFile",
            "    Open path For Binary Access Read As #f",
            "    Head = Space$(3)",
            "    Get #f, 1, Head",
            "    Close #f",
            "End Function"), expected: "Completed | 3  2 |");

    [TestMethod]
    public async Task AGet_FillsARecordResult()
        => await AssertAlikeAsync(Program(
            "Private Type TPair",
            "    A As Byte",
            "    B As Integer",
            "End Type",
            "Public Sub Main()",
            $"    Dim p As TPair: p = Pair(\"{File}\")",
            "    Debug.Print p.A; p.B",
            "End Sub",
            "Private Function Pair(ByVal path As String) As TPair",
            "    Dim f As Integer",
            "    f = FreeFile",
            "    Open path For Binary Access Read As #f",
            "    Get #f, 1, Pair",
            "    Close #f",
            "End Function"), expected: "Completed | 42  513 |");

    [TestMethod]
    public async Task AGet_ReadsIntoTheResultOfThePropertyGetItIsWrittenIn()
        => await AssertAlikeAsync(Program(
            $"Private Const P As String = \"{File}\"",
            "Public Sub Main()",
            "    Debug.Print Second",
            "End Sub",
            "Private Property Get Second() As Byte",
            "    Dim f As Integer",
            "    f = FreeFile",
            "    Open P For Binary Access Read As #f",
            "    Get #f, 2, Second",
            "    Close #f",
            "End Property"), expected: "Completed | 1 |");
}
