using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A user-defined type is passed to a native function by the address of a copy of it, as MS-VBA passes it: laid out as it is in memory, its strings in the ANSI
/// code page - a fixed-length <c>String * n</c> as <em>n</em> bytes inline - and taken back into the variable's fields when the function returns. These call the
/// libraries of Windows itself, and are skipped elsewhere.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.2.3.5 External Procedure Declaration")]
[OSCondition(OperatingSystems.Windows)]
public sealed class DeclareRecordTests
{
    private static Task<string[]> Run(string declarations, params string[] lines)
        => RunAsync([], $"Attribute VB_Name = \"Program\"\r\n{declarations}\r\nPublic Sub Main()\r\n{string.Join("\r\n", lines)}\r\nEnd Sub\r\n");

    [TestMethod]
    public async Task ARecordOfNumbers_IsFilledInByTheFunction()
        => CollectionAssert.AreEqual(new[] { "True", "True" }, await Run(
            string.Join("\r\n",
                "Private Type SYSTEMTIME",
                "    wYear As Integer",
                "    wMonth As Integer",
                "    wDayOfWeek As Integer",
                "    wDay As Integer",
                "    wHour As Integer",
                "    wMinute As Integer",
                "    wSecond As Integer",
                "    wMilliseconds As Integer",
                "End Type",
                "Private Declare PtrSafe Sub GetSystemTime Lib \"kernel32\" (lpSystemTime As SYSTEMTIME)"),
            "Dim clock As SYSTEMTIME",
            "GetSystemTime clock",
            $"Debug.Print (clock.wYear = {DateTime.UtcNow.Year})",
            "Debug.Print clock.wMonth >= 1 And clock.wMonth <= 12"));

    // the classic: a fixed-length String is 128 ANSI bytes in the copy, so the size the function is told is Len of the type, 148 - as the A flavour expects.
    [TestMethod]
    [Ignore("String * n is dropped when the syntax tree is built: a fixed-length string is declared as a variable-length one")]
    public async Task AFixedLengthStringField_IsItsAnsiBytesInline_AsTheAnsiFlavourOfAFunctionExpects()
        => CollectionAssert.AreEqual(new[] { "148", "True", "2", "True" }, await Run(
            string.Join("\r\n",
                "Private Type OSVERSIONINFO",
                "    dwOSVersionInfoSize As Long",
                "    dwMajorVersion As Long",
                "    dwMinorVersion As Long",
                "    dwBuildNumber As Long",
                "    dwPlatformId As Long",
                "    szCSDVersion As String * 128",
                "End Type",
                "Private Declare PtrSafe Function GetVersionEx Lib \"kernel32\" Alias \"GetVersionExA\" (lpVersionInformation As OSVERSIONINFO) As Long"),
            "Dim info As OSVERSIONINFO",
            "info.dwOSVersionInfoSize = Len(info)",
            "Debug.Print info.dwOSVersionInfoSize",
            "Debug.Print GetVersionEx(info) <> 0",
            "Debug.Print info.dwPlatformId",
            "Debug.Print info.dwMajorVersion >= 6"));

    // a pointer field is as wide as the environment's pointers, and on a boundary as wide: the fields after it are where the function wrote them.
    [TestMethod]
    public async Task APointerField_IsOnTheBoundaryOfItsWidth_AndTheFieldsAfterItAreWhereTheFunctionWroteThem()
        => CollectionAssert.AreEqual(new[] { "4096", Environment.ProcessorCount.ToString(System.Globalization.CultureInfo.InvariantCulture), "65536" }, await Run(
            string.Join("\r\n",
                "Private Type SYSTEM_INFO",
                "    wProcessorArchitecture As Integer",
                "    wReserved As Integer",
                "    dwPageSize As Long",
                "    lpMinimumApplicationAddress As LongPtr",
                "    lpMaximumApplicationAddress As LongPtr",
                "    dwActiveProcessorMask As LongPtr",
                "    dwNumberOfProcessors As Long",
                "    dwProcessorType As Long",
                "    dwAllocationGranularity As Long",
                "    wProcessorLevel As Integer",
                "    wProcessorRevision As Integer",
                "End Type",
                "Private Declare PtrSafe Sub GetSystemInfo Lib \"kernel32\" (lpSystemInfo As SYSTEM_INFO)"),
            "Dim info As SYSTEM_INFO",
            "GetSystemInfo info",
            "Debug.Print info.dwPageSize",
            "Debug.Print info.dwNumberOfProcessors",
            "Debug.Print info.dwAllocationGranularity"));

    // a record copied into another through native memory reads back as it was written: a nested type on its own boundary, a Double on eight, a fixed-size array
    // and a fixed-length String inline - the copy is laid out the same way both ways. Inner is 16 bytes (a Byte, then a Double at 8); Outer puts it at 8, its
    // array at 24, its 4 ANSI characters at 36, and is 40 bytes in all.
    [TestMethod]
    [Ignore("an element of a fixed-size array field of a user-defined type cannot be assigned yet, and String * n is dropped when the syntax tree is built")]
    public async Task ANestedRecord_AFixedArrayAndAFixedString_AreLaidOutAsTheyAreInMemory_BothWays()
        => CollectionAssert.AreEqual(new[] { "7", "9", "2.5", "10 20 30", "duck" }, await Run(
            string.Join("\r\n",
                "Private Type Inner",
                "    Tag As Byte",
                "    Weight As Double",
                "End Type",
                "Private Type Outer",
                "    Count As Integer",
                "    Contents As Inner",
                "    Values(1 To 3) As Long",
                "    Name As String * 4",
                "End Type",
                "Private Declare PtrSafe Sub CopyMemory Lib \"kernel32\" Alias \"RtlMoveMemory\" (destination As Any, source As Any, ByVal length As LongPtr)"),
            "Dim source As Outer",
            "Dim copy As Outer",
            "source.Count = 7",
            "source.Contents.Tag = 9",
            "source.Contents.Weight = 2.5",
            "source.Values(1) = 10",
            "source.Values(2) = 20",
            "source.Values(3) = 30",
            "source.Name = \"duck\"",
            "CopyMemory copy, source, 40",
            "Debug.Print copy.Count",
            "Debug.Print copy.Contents.Tag",
            "Debug.Print copy.Contents.Weight",
            "Debug.Print copy.Values(1) & \" \" & copy.Values(2) & \" \" & copy.Values(3)",
            "Debug.Print copy.Name"));
}
