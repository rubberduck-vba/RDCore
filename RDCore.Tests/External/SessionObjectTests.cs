using RDCore.External;
using RDCore.External.Automation;
using RDCore.External.Native;
using RDCore.External.Windows.Automation;
using RDCore.SDK.Runtime.Libraries;
using RDCore.Tests.Cli;
using RDCore.Tests.Runtime.Libraries;
using System.Globalization;
using System.Runtime.InteropServices;

namespace RDCore.Tests.External;

/// <summary>
/// The program's own objects handed to a COM server, as MS-VBA hands them: an object of a class module, or the standard library's <c>Collection</c>, is held by
/// the server - <c>d.Add "key", New Widget</c> - is the same object when it comes back (<c>d("key") Is w</c>), and is called as itself. In the process, on one
/// apartment, and through an external host, as <c>rdc</c> runs a program.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.5.2 Object References")]
public sealed class SessionObjectTests
{
    private static DirectoryLibrarySource Shipped => new(new System.IO.Abstractions.FileSystem(), ShippedLibrariesTests.Symbols);

    private static readonly (string Name, string Source) Widget = ("Widget", ModuleWorkspace.ClassModule("Widget",
        "Option Explicit",
        "Public Value As Long",
        "Public Function Twice() As Long",
        "    Twice = Value * 2",
        "End Function"));

    private static string Program(params string[] body)
        => ModuleWorkspace.StandardModule("Program", ["Option Explicit", "Public Sub Main()", .. body, "End Sub"]);

    private static async Task<string[]> RunAsync(bool external, params string[] body)
    {
        using var apartment = new ComApartment();
        using var host = external ? ExternalHosts.New() : null;
        var world = host is null ? new ExternalWorld(new ComAutomationServer(apartment), UnavailableNativeLibraryHost.Instance) : ExternalHosts.WorldOf(host);
        return await ModuleWorkspace.RunAsync([Widget], Program(body), new ModuleWorkspace.WorkspaceLibraries(["Scripting"], Shipped, Outside: world));
    }

    // what answers for the objects a test hands a server: it records the calls and the releases, and answers each call with what it is told to.
    private sealed class RecordingHost(Func<SessionObjectCall, object?> answer) : ISessionObjectHost
    {
        public List<SessionObjectCall> Calls { get; } = [];

        public System.Collections.Concurrent.ConcurrentQueue<(string SessionObject, int Crossings)> Releases { get; } = new();

        public object? Invoke(SessionObjectCall call)
        {
            Calls.Add(call);
            return answer(call);
        }

        public void Release(string sessionObject, int crossings) => Releases.Enqueue((sessionObject, crossings));
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetIDsOfNames(IntPtr self, ref Guid riid, IntPtr names, int count, int locale, IntPtr identifiers);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int Invoke(IntPtr self, int member, ref Guid riid, int locale, ushort flags, ref DispatchParameters parameters, IntPtr result, IntPtr exception, IntPtr argumentError);

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatchParameters
    {
        public IntPtr Arguments;
        public IntPtr NamedArgumentIds;
        public int ArgumentCount;
        public int NamedArgumentCount;
    }

    // calls an object through its IDispatch, as a COM server does: by a name, or by a dispatch identifier when the name is null; DISPATCH_METHOD | PROPERTYGET.
    private static (int Status, object? Result) CallThroughDispatch(object target, string? name, int dispatchId, params object?[] arguments)
    {
        var dispatch = Marshal.GetIDispatchForObject(target);
        try
        {
            var table = Marshal.ReadIntPtr(dispatch);
            var noInterface = Guid.Empty;
            if (name is not null)
            {
                var names = Marshal.AllocCoTaskMem(IntPtr.Size);
                var text = Marshal.StringToCoTaskMemUni(name);
                var identifiers = Marshal.AllocCoTaskMem(sizeof(int));
                Marshal.WriteIntPtr(names, text);
                _ = Marshal.GetDelegateForFunctionPointer<GetIDsOfNames>(Marshal.ReadIntPtr(table, 5 * IntPtr.Size))(dispatch, ref noInterface, names, 1, 0, identifiers);
                dispatchId = Marshal.ReadInt32(identifiers);
                Marshal.FreeCoTaskMem(identifiers);
                Marshal.FreeCoTaskMem(text);
                Marshal.FreeCoTaskMem(names);
            }

            // the arguments are given last first.
            var size = IntPtr.Size == 8 ? 24 : 16;
            var variants = Marshal.AllocCoTaskMem(Math.Max(1, arguments.Length) * size);
            for (var index = 0; index < arguments.Length; index++)
            {
                Marshal.GetNativeVariantForObject(arguments[arguments.Length - 1 - index], variants + (index * size));
            }

            var result = Marshal.AllocCoTaskMem(size);
            Marshal.GetNativeVariantForObject<object?>(null, result);
            var parameters = new DispatchParameters { Arguments = variants, ArgumentCount = arguments.Length };
            var status = Marshal.GetDelegateForFunctionPointer<Invoke>(Marshal.ReadIntPtr(table, 6 * IntPtr.Size))(
                dispatch, dispatchId, ref noInterface, 0, 3, ref parameters, result, IntPtr.Zero, IntPtr.Zero);
            var returned = Marshal.GetObjectForNativeVariant(result);
            Marshal.FreeCoTaskMem(result);
            Marshal.FreeCoTaskMem(variants);
            return (status, returned);
        }
        finally
        {
            _ = Marshal.Release(dispatch);
        }
    }

    // the stand-in a server holds answers its IDispatch by calling the session: by the name the server asked for, by the default member's 0; and tells the session
    // when nothing holds it any more, with the crossings it was made or found for.
    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void TheStandInAServerHolds_IsCalledThroughItsDispatch_AndLetGoOf()
    {
        using var apartment = new ComApartment();
        using var server = new ComAutomationServer(apartment);
        var host = new RecordingHost(call => call.Name is "Twice" ? (int)call.Arguments[0]! * 2 : "default");

        HoldAndCall(server, host);
        for (var attempt = 0; attempt < 20 && host.Releases.IsEmpty; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        CollectionAssert.AreEqual(new[] { ("object-1", 2) }, host.Releases.ToArray());
    }

    // a frame of its own, so that nothing of the test's holds the stand-in once the dictionary lets go of it.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void HoldAndCall(ComAutomationServer server, RecordingHost host)
    {
        var dictionary = server.CreateObject("Scripting.Dictionary");
        _ = server.Invoke(dictionary, "Add", AutomationInvocation.Method, ["k", new SessionObjectReference("object-1", host)], [false, false], CultureInfo.InvariantCulture);
        _ = server.Invoke(dictionary, "Add", AutomationInvocation.Method, ["again", new SessionObjectReference("object-1", host)], [false, false], CultureInfo.InvariantCulture);
        var standIn = server.Invoke(dictionary, "Item", AutomationInvocation.Get, ["k"], [false], CultureInfo.InvariantCulture);

        Assert.IsInstanceOfType<ISessionObjectStandIn>(standIn);
        Assert.AreEqual("object-1", ((ISessionObjectStandIn)standIn!).SessionObject);
        Assert.AreSame(standIn, server.Invoke(dictionary, "Item", AutomationInvocation.Get, ["again"], [false], CultureInfo.InvariantCulture), "one object, one stand-in");
        Assert.AreEqual((0, (object?)42), CallThroughDispatch(standIn!, "Twice", 0, 21));
        Assert.AreEqual((0, (object?)"default"), CallThroughDispatch(standIn!, null, 0));
        Assert.AreEqual("Twice", host.Calls[0].Name);
        Assert.IsNull(host.Calls[1].Name, "the default member is called by its identifier");
        Assert.AreEqual(0, host.Calls[1].DispatchId);

        _ = server.Invoke(dictionary, "RemoveAll", AutomationInvocation.Method, [], [], CultureInfo.InvariantCulture);
        server.Release(dictionary);
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    [DataRow(false, DisplayName = "in the process")]
    [DataRow(true, DisplayName = "through an external host")]
    public async Task AnObjectOfTheProgram_HeldByAServer_IsItselfWhenItComesBack(bool external)
    {
        var printed = await RunAsync(external,
            "Dim d As New Scripting.Dictionary",
            "Dim w As New Widget",
            "w.Value = 21",
            "d.Add \"w\", w",
            "Debug.Print TypeName(d(\"w\"))",
            "Debug.Print d(\"w\") Is w",
            "Debug.Print d(\"w\").Twice",
            "d(\"w\").Value = 5",
            "Debug.Print w.Value",
            "Dim c As New Collection",
            "c.Add \"one\"",
            "d.Add \"c\", c",
            "Debug.Print d(\"c\").Count",
            "Debug.Print d(\"c\")(1)",
            "Debug.Print d.Items()(0) Is w",
            "d.Add w, \"keyed by the object\"",
            "Debug.Print d(w)",
            "Debug.Print d.Exists(w)");

        CollectionAssert.AreEqual(new[] { "Widget", "True", "42", "5", "1", "one", "True", "keyed by the object", "True" }, printed, string.Join(" | ", printed));
    }
}
