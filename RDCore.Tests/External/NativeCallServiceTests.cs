using RDCore.External.Native;
using RDCore.External.Protocol;
using System.Globalization;

namespace RDCore.Tests.External;

/// <summary>
/// The external host's side of a <c>Declare</c>: a call arrives described - which function, and how each argument is passed - and what comes back is what it
/// returned and what it left in the arguments that are variables. These call the C library of the platform they run on.
/// </summary>
[TestClass]
[TestCategory("External host")]
public sealed class NativeCallServiceTests
{
    // a function every platform has, under the name of its own C library.
    private static string CLibrary => OperatingSystem.IsWindows() ? "msvcrt" : OperatingSystem.IsMacOS() ? "libc.dylib" : "libc.so.6";

    private static NativeArgument Pass(NativeSlot slot, object? value, bool byReference = false, bool writesBack = false)
        => new() { Slot = slot, ByReference = byReference, Value = ExternalValues.ToWire(value, _ => 0), WritesBack = writesBack };

    [TestMethod]
    public void AStringByValue_IsPassedAsAnAnsiCopy_AndWhatTheFunctionReturnsIsItsNumber()
    {
        var service = new NativeCallService(UnsupportedNativePlatform.Instance);

        var result = service.Call(new NativeCallParams
        {
            Library = CLibrary, EntryPoint = "strlen", Returns = NativeSlot.Pointer, Arguments = [Pass(NativeSlot.AnsiBuffer, "quack")], AnsiCodePage = 1252,
        });

        Assert.AreEqual(NativeLookup.Found, result.Lookup);
        Assert.AreEqual(5L, ExternalValues.FromWire(result.Returned, _ => new object()));
    }

    [TestMethod]
    public void ABufferTheFunctionWritesInto_IsWhatItLeft_TheLengthItWas()
    {
        var service = new NativeCallService(UnsupportedNativePlatform.Instance);

        // strcpy(destination, source): the function writes "duck" and its terminator over the first five of the destination's eight characters.
        var result = service.Call(new NativeCallParams
        {
            Library = CLibrary, EntryPoint = "strcpy", Returns = NativeSlot.Pointer,
            Arguments = [Pass(NativeSlot.AnsiBuffer, "........", writesBack: true), Pass(NativeSlot.AnsiBuffer, "duck")],
            AnsiCodePage = 1252,
        });

        Assert.AreEqual("duck\0...", ExternalValues.FromWire(result.Written[0], _ => new object()));
        Assert.AreEqual(ExternalValueKind.Empty, result.Written[1].Kind, "an argument that does not write back says nothing");
    }

    [TestMethod]
    public void ANumberByReference_IsTheNumberTheFunctionLeftAtItsAddress()
    {
        var service = new NativeCallService(UnsupportedNativePlatform.Instance);

        // time(&seconds): the function writes the time at the address it is given, and returns it.
        var result = service.Call(new NativeCallParams
        {
            Library = CLibrary, EntryPoint = OperatingSystem.IsWindows() ? "_time64" : "time", Returns = NativeSlot.Int64,
            Arguments = [Pass(NativeSlot.Int64, 0L, byReference: true, writesBack: true)],
            AnsiCodePage = 1252,
        });

        var written = Convert.ToInt64(ExternalValues.FromWire(result.Written[0], _ => new object()), CultureInfo.InvariantCulture);
        Assert.IsGreaterThan(1_700_000_000L, written);
        Assert.AreEqual(written, ExternalValues.FromWire(result.Returned, _ => new object()));
    }

    [TestMethod]
    public void ALibraryThatIsNotThere_AndAFunctionItDoesNotExport_AreToldApart()
    {
        var service = new NativeCallService(UnsupportedNativePlatform.Instance);

        Assert.AreEqual(NativeLookup.NoLibrary, service.Call(new NativeCallParams { Library = "no-such-library-quack", EntryPoint = "quack" }).Lookup);
        Assert.AreEqual(NativeLookup.NoEntryPoint, service.Call(new NativeCallParams { Library = CLibrary, EntryPoint = "no_such_function_quack" }).Lookup);
    }

    [TestMethod]
    public void OnAPlatformWithNoBstrs_AByRefString_CannotBePassed_AndNoOrdinalIsFound()
    {
        var service = new NativeCallService(UnsupportedNativePlatform.Instance);

        var byReference = service.Call(new NativeCallParams
        {
            Library = CLibrary, EntryPoint = "strlen", Returns = NativeSlot.Pointer, Arguments = [Pass(NativeSlot.AnsiBstr, "quack", byReference: true)],
        });
        var ordinal = service.Call(new NativeCallParams { Library = CLibrary, EntryPoint = "#1" });

        Assert.AreEqual(NativeLookup.Unsupported, byReference.Lookup);
        Assert.AreEqual(NativeLookup.NoEntryPoint, ordinal.Lookup);
    }
}
