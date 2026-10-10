using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>Declare</c>d procedure is a function of a native library, called the way MS-VBA calls it (<strong>MS-VBAL §5.2.3.5</strong>): numbers as the numbers
/// they are, strings as ANSI, by value or by the address of the variable, and <c>Err.LastDllError</c> after each call. These call the libraries of
/// Windows itself, and are skipped elsewhere.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.2.3.5 External Procedure Declaration")]
[OSCondition(OperatingSystems.Windows)]
public sealed class DeclareTests
{
    private static Task<string[]> Run(string declarations, params string[] lines)
        => RunAsync([], $"Attribute VB_Name = \"Program\"\r\n{declarations}\r\nPublic Sub Main()\r\n{string.Join("\r\n", lines)}\r\nEnd Sub\r\n");

    // the function runs in the external host, a process of its own - not the one that owns the session, which a function declared wrongly would take down.
    [TestMethod]
    public async Task AFunctionThatTakesNothing_IsCalled_InAProcessOfItsOwn_AndWhatItReturnsIsALong()
    {
        var output = await Run(
            "Private Declare PtrSafe Function GetCurrentProcessId Lib \"kernel32\" () As Long",
            "Debug.Print GetCurrentProcessId");

        var caller = int.Parse(output.Single(), System.Globalization.CultureInfo.InvariantCulture);
        Assert.AreNotEqual(Environment.ProcessId, caller);
        using var process = System.Diagnostics.Process.GetProcessById(caller);
        Assert.AreEqual("rdc", process.ProcessName);
    }

    [TestMethod]
    public async Task ASub_IsCalledWithANumberByValue()
        => CollectionAssert.AreEqual(new[] { "slept" }, await Run(
            "Private Declare PtrSafe Sub Sleep Lib \"kernel32\" (ByVal dwMilliseconds As Long)",
            "Sleep 1",
            "Debug.Print \"slept\""));

    [TestMethod]
    public async Task AStringByValue_IsPassedAsANSI_UnderItsAlias()
        => CollectionAssert.AreEqual(new[] { "5" }, await Run(
            "Private Declare PtrSafe Function StringLength Lib \"kernel32\" Alias \"lstrlenA\" (ByVal text As String) As Long",
            "Debug.Print StringLength(\"hello\")"));

    [TestMethod]
    public async Task AStringBufferPassedByValue_TakesWhatTheFunctionWroteIntoIt()
        // every process of Windows has OS=Windows_NT: the function writes it and its terminator over the first eleven of the buffer's twelve characters, and
        // the buffer keeps its length.
        => CollectionAssert.AreEqual(new[] { "10", "12", "Windows_NT\0" }, await Run(
            "Private Declare PtrSafe Function GetEnvironmentVariable Lib \"kernel32\" Alias \"GetEnvironmentVariableA\" (ByVal lpName As String, ByVal lpBuffer As String, ByVal nSize As Long) As Long",
            "Dim buffer As String",
            "buffer = \"            \"",
            "Dim length As Long",
            "length = GetEnvironmentVariable(\"OS\", buffer, 12)",
            "Debug.Print length",
            "Debug.Print Len(buffer)",
            "Debug.Print buffer"));

    [TestMethod]
    public async Task ANumberByReference_IsTheVariableTheFunctionWritesTo()
        => CollectionAssert.AreEqual(new[] { "True", "True" }, await Run(
            "Private Declare PtrSafe Function QueryPerformanceFrequency Lib \"kernel32\" (ByRef frequency As LongLong) As Long",
            "Dim frequency As LongLong",
            "Debug.Print QueryPerformanceFrequency(frequency) <> 0",
            "Debug.Print frequency > 0"));

    [TestMethod]
    public async Task ACurrencyByReference_IsItsScaled64Bits_AsTheIdiomHasIt()
        => CollectionAssert.AreEqual(new[] { "True" }, await Run(
            "Private Declare PtrSafe Function QueryPerformanceCounter Lib \"kernel32\" (ByRef counter As Currency) As Long",
            "Dim counter As Currency",
            "QueryPerformanceCounter counter",
            "Debug.Print counter > 0"));

    [TestMethod]
    public async Task AnArgumentAsAny_IsPassedAsWhatItIs()
        => CollectionAssert.AreEqual(new[] { "5" }, await Run(
            "Private Declare PtrSafe Function StringLength Lib \"kernel32\" Alias \"lstrlenA\" (ByVal text As Any) As Long",
            "Debug.Print StringLength(\"quack\")"));

    [TestMethod]
    public async Task AfterACall_LastDllErrorIsTheCodeTheFunctionLeft()
        => CollectionAssert.AreEqual(new[] { "0", "6" }, await Run(
            "Private Declare PtrSafe Function CloseHandle Lib \"kernel32\" (ByVal handle As LongPtr) As Long",
            "Debug.Print CloseHandle(12345)",
            "Debug.Print Err.LastDllError"));

    [TestMethod]
    public async Task VbNullString_IsANullPointer_AndAZeroLengthStringIsNot()
        => CollectionAssert.AreEqual(new[] { "0", "0" }, await Run(
            // lstrlenA counts the characters before the terminator, and answers 0 for a null pointer rather than reading through it.
            "Private Declare PtrSafe Function StringLength Lib \"kernel32\" Alias \"lstrlenA\" (ByVal text As String) As Long",
            "Debug.Print StringLength(vbNullString)",
            "Debug.Print StringLength(\"\")"));

    [TestMethod]
    public async Task AnOrdinalAlias_NamesTheFunctionByItsNumber()
    {
        // the ordinal of lstrlenA in this machine's kernel32, found the way a program written against it would have found it.
        var ordinal = OrdinalOf("kernel32.dll", "lstrlenA");

        CollectionAssert.AreEqual(new[] { "5" }, await Run(
            $"Private Declare PtrSafe Function StringLength Lib \"kernel32\" Alias \"#{ordinal}\" (ByVal text As String) As Long",
            "Debug.Print StringLength(\"hello\")"));
    }

    // reads the export directory of a loaded module: the ordinal of a named export is its position in the ordinal table plus the base.
    private static int OrdinalOf(string module, string export)
    {
        var handle = System.Runtime.InteropServices.NativeLibrary.Load(module);
        var peHeader = System.Runtime.InteropServices.Marshal.ReadInt32(handle, 0x3C);
        var directory = System.Runtime.InteropServices.Marshal.ReadInt32(handle, peHeader + (IntPtr.Size == 8 ? 0x88 : 0x78));
        var ordinalBase = System.Runtime.InteropServices.Marshal.ReadInt32(handle, directory + 0x10);
        var names = System.Runtime.InteropServices.Marshal.ReadInt32(handle, directory + 0x18);
        var nameTable = System.Runtime.InteropServices.Marshal.ReadInt32(handle, directory + 0x20);
        var ordinalTable = System.Runtime.InteropServices.Marshal.ReadInt32(handle, directory + 0x24);
        for (var index = 0; index < names; index++)
        {
            var name = System.Runtime.InteropServices.Marshal.PtrToStringAnsi(handle + System.Runtime.InteropServices.Marshal.ReadInt32(handle, nameTable + (index * 4)));
            if (name == export)
            {
                return ordinalBase + System.Runtime.InteropServices.Marshal.ReadInt16(handle, ordinalTable + (index * 2));
            }
        }

        throw new InvalidOperationException($"{module} does not export {export}.");
    }

    [TestMethod]
    public async Task ALibraryThatIsNotThere_IsFileNotFound_AndAFunctionItDoesNotExport_IsNoEntryPoint()
        => CollectionAssert.AreEqual(new[] { "53", "453" }, await Run(
            "Private Declare PtrSafe Function Nothing1 Lib \"no-such-library-quack\" () As Long\r\nPrivate Declare PtrSafe Function Nothing2 Lib \"kernel32\" Alias \"NoSuchFunctionQuack\" () As Long",
            "On Error Resume Next",
            "Debug.Print Nothing1",
            "Debug.Print Err.Number",
            "Err.Clear",
            "Debug.Print Nothing2",
            "Debug.Print Err.Number"));

    // what MS-VBA would not survive: a function declared wrongly - here, told to copy from an address that is no memory at all - takes down the process that
    // called it. That is the external host, not the one that owns the session: the program is told, as error 49, and goes on, and the next call starts another.
    [TestMethod]
    public async Task AFunctionThatTakesDownTheProcessThatCalledIt_IsBadDllCallingConvention_AndTheProgramGoesOn()
    {
        using var host = RDCore.Tests.External.ExternalHosts.New();
        var libraries = new WorkspaceLibraries([], new RDCore.Tests.Runtime.Libraries.InMemoryLibrarySource(), Outside: RDCore.Tests.External.ExternalHosts.WorldOf(host));

        var output = await RunAsync([], "Attribute VB_Name = \"Program\"\r\n" + string.Join("\r\n",
            "Private Declare PtrSafe Sub CopyMemory Lib \"kernel32\" Alias \"RtlMoveMemory\" (ByVal destination As LongPtr, ByVal source As LongPtr, ByVal length As LongPtr)",
            "Private Declare PtrSafe Function StringLength Lib \"kernel32\" Alias \"lstrlenA\" (ByVal text As String) As Long",
            "Public Sub Main()",
            "On Error Resume Next",
            "CopyMemory 0, 8, 8",
            "Debug.Print Err.Number",
            "Err.Clear",
            "Debug.Print StringLength(\"quack\")",
            "End Sub") + "\r\n", libraries);

        CollectionAssert.AreEqual(new[] { "49", "5" }, output);
    }
}
