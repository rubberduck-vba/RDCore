using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>Declare</c> names a native library of the platform the program runs on, whichever that is: on Linux, the C library.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.2.3.5 External Procedure Declaration")]
[OSCondition(OperatingSystems.Linux)]
public sealed class DeclareOnLinuxTests
{
    // the function runs in the external host, a process of its own.
    [TestMethod]
    public async Task AFunctionOfTheCLibrary_IsCalled_InAProcessOfItsOwn()
    {
        var output = await RunAsync([],
            "Attribute VB_Name = \"Program\"\r\nPrivate Declare PtrSafe Function getpid Lib \"libc.so.6\" () As Long\r\nPublic Sub Main()\r\nDebug.Print getpid\r\nEnd Sub\r\n");

        var caller = int.Parse(output.Single(), System.Globalization.CultureInfo.InvariantCulture);
        Assert.AreNotEqual(Environment.ProcessId, caller);
        using var process = System.Diagnostics.Process.GetProcessById(caller);
        Assert.AreEqual("rdc", process.ProcessName);
    }

    // a record is passed by the address of a copy of it, and what the function wrote there is the variable's fields: a timeval is two longs, which on a 64-bit
    // Linux are 64 bits each.
    [TestMethod]
    public async Task ARecord_IsFilledInByTheFunction()
        => CollectionAssert.AreEqual(new[] { "True", "True" }, await RunAsync([], "Attribute VB_Name = \"Program\"\r\n" + string.Join("\r\n",
            "Private Type TimeValue",
            "    Seconds As LongLong",
            "    Microseconds As LongLong",
            "End Type",
            "Private Declare PtrSafe Function gettimeofday Lib \"libc.so.6\" (value As TimeValue, ByVal zone As LongPtr) As Long",
            "Public Sub Main()",
            "Dim now As TimeValue",
            "Debug.Print gettimeofday(now, 0) = 0",
            "Debug.Print now.Seconds > 1700000000",
            "End Sub") + "\r\n"));

    // a function that ends the process that called it is the external host's end, not the session's: the program is told, as error 49, and goes on.
    [TestMethod]
    public async Task AFunctionThatEndsTheProcessThatCalledIt_IsBadDllCallingConvention_AndTheProgramGoesOn()
    {
        using var host = RDCore.Tests.External.ExternalHosts.New();
        var libraries = new WorkspaceLibraries([], new RDCore.Tests.Runtime.Libraries.InMemoryLibrarySource(), Outside: RDCore.Tests.External.ExternalHosts.WorldOf(host));

        var output = await RunAsync([], "Attribute VB_Name = \"Program\"\r\n" + string.Join("\r\n",
            "Private Declare PtrSafe Sub abort Lib \"libc.so.6\" ()",
            "Private Declare PtrSafe Function getpid Lib \"libc.so.6\" () As Long",
            "Public Sub Main()",
            "On Error Resume Next",
            "abort",
            "Debug.Print Err.Number",
            "Err.Clear",
            "Debug.Print getpid > 0",
            "End Sub") + "\r\n", libraries);

        CollectionAssert.AreEqual(new[] { "49", "True" }, output);
    }
}
