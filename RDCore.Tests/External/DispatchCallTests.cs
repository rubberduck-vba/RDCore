using RDCore.External.Automation;
using RDCore.External.Windows.Automation;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RDCore.Tests.External;

/// <summary>
/// A late-bound call to an object of a COM server: the dispatch identifiers of the names of its class are asked once, and the class is known by the type
/// information the object gives, which every object of the class on the apartment shares.
/// </summary>
[TestClass]
[SupportedOSPlatform("windows")]
public sealed class DispatchCallTests
{
    // IDispatch, declared only as far as its type information: its first two members.
    [ComImport]
    [Guid("00020400-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITypeInformationSource
    {
        [PreserveSig]
        int GetTypeInfoCount(out int count);

        [PreserveSig]
        int GetTypeInfo(int index, int locale, out IntPtr typeInfo);
    }

    // the runtime keeps one wrapper of a COM object for the whole process, found by the object's address, which another apartment can be given for an object
    // made at that address once the first is gone; when an apartment ends, the wrappers made on it are let go of. A wrapper of the type information that the
    // call left behind is one that a call on another apartment could be given, and find already let go of.
    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void ACall_LeavesNoWrapperOfTheTypeInformationOfItsObject()
    {
        using var apartment = new ComApartment();
        using var server = new ComAutomationServer(apartment);

        var dictionary = server.CreateObject("Scripting.Dictionary");
        _ = server.Invoke(dictionary, "Add", AutomationInvocation.Method, ["k", 1], [false, false], CultureInfo.InvariantCulture);
        var count = server.Invoke(dictionary, "Count", AutomationInvocation.Get, [], [], CultureInfo.InvariantCulture);
        var held = apartment.Invoke(() =>
        {
            Assert.AreEqual(0, ((ITypeInformationSource)dictionary).GetTypeInfo(0, 0, out var typeInfo));
            try
            {
                // what is left of the process's wrapper once this one is let go of: none, when the wrapper is made here.
                return Marshal.ReleaseComObject(Marshal.GetObjectForIUnknown(typeInfo));
            }
            finally
            {
                _ = Marshal.Release(typeInfo);
            }
        });
        server.Release(dictionary);

        Assert.AreEqual(1, count);
        Assert.AreEqual(0, held, "the call let go of the wrapper it made of the type information");
    }
}
