using RDCore.External;
using RDCore.External.Automation;
using RDCore.External.Forms;
using RDCore.External.Native;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Workspace.Forms;
using RDCore.Tests.Cli;
using RDCore.Tests.Runtime.Libraries;
using System.Globalization;

namespace RDCore.Tests.Workspace.Forms;

/// <summary>
/// An object a UserForm hands out, in an environment that refuses automation, when the form is a COM object of a server that describes no class of it: an
/// object of no library's class is the platform's only when the form host made it, which a COM server never says, so it is refused with error 70. A stand-in
/// server and form host hand it out: these run on any platform.
/// </summary>
[TestClass]
public sealed class FormHostObjectPolicyTests
{
    private static DirectoryLibrarySource Shipped => new(new System.IO.Abstractions.FileSystem(), ShippedLibrariesTests.Symbols);

    private static readonly FormDescriptor Designer = new(
        "Window", FormDesignerKind.MSForms, new FormControlClass("MSForms", "UserForm", FormDesigner.MSFormsClassId),
        new Dictionary<string, string> { ["Caption"] = "Window" }, []);

    private static readonly string Window = string.Join("\r\n", [
        "VERSION 5.00",
        "Begin {C62A69F0-16DC-11CE-9E98-00AA00574A4F} Window ",
        "   Caption         =   \"Window\"",
        "   OleObjectBlob   =   \"Window.frx\":0000",
        "End",
        "Attribute VB_Name = \"Window\"",
        "Attribute VB_GlobalNameSpace = False",
        "Attribute VB_Creatable = False",
        "Attribute VB_PredeclaredId = True",
        "Attribute VB_Exposed = False",
        "Option Explicit",
        string.Empty]);

    // the form's Controls, of the class its server says: Microsoft Forms' runs, and one of no library's class is refused.
    [TestMethod]
    [DataRow("MSForms.Controls", new[] { "3", "0" }, DisplayName = "of a class of Microsoft Forms")]
    [DataRow(null, new[] { "70" }, DisplayName = "of no class its server describes")]
    public async Task AnObjectAUserFormHandsOut_IsTheFormsOnlyByItsClass(string? controlsClass, string[] expected)
    {
        var server = new StandInServer(controlsClass);
        var world = new ExternalWorld(server, UnavailableNativeLibraryHost.Instance) { Forms = new StandInForms() };
        var program = ModuleWorkspace.StandardModule(
            "Program",
            "Public Sub Main()",
            "    Dim c As Object",
            "    Set c = Window.Controls",
            "    On Error Resume Next",
            "    Debug.Print c.Count",
            "    Debug.Print Err.Number",
            "End Sub");

        var output = await ModuleWorkspace.RunAsync([], program,
            new ModuleWorkspace.WorkspaceLibraries(["MSForms"], Shipped, AllowAutomation: false, Outside: world), [("Window", Window, Designer)]);

        CollectionAssert.AreEqual(expected, output, string.Join(" | ", output));
    }

    // a server of COM objects that describes no class of a form and says what class its Controls is of: Controls counts three.
    private sealed class StandInServer(string? controlsClass) : IAutomationServer
    {
        private readonly object _controls = new();

        public bool IsAvailable => true;

        public object CreateObject(string progId) => throw new AutomationException(unchecked((int)0x800401F3));

        public object? Invoke(object target, string member, AutomationInvocation invocation, object?[] arguments, bool[] byReference, CultureInfo culture)
            => (ReferenceEquals(target, _controls), member) switch
            {
                (false, "Controls") => _controls,
                (true, "Count") => 3,
                _ => throw new AutomationException(unchecked((int)0x80020006)),
            };

        public string? ClassNameOf(object target) => ReferenceEquals(target, _controls) ? controlsClass : null;

        public bool MoveNext(object enumerator, out object? current)
        {
            current = null;
            return false;
        }

        public void Reset(object enumerator)
        {
        }

        public void Advise(object source, IAutomationEventSink sink)
        {
        }

        public void Unadvise(object source)
        {
        }

        public void Release(object handle)
        {
        }
    }

    // forms with no window.
    private sealed class StandInForms : IFormHost
    {
        public bool IsAvailable => true;

        public object Create(FormDefinition form) => new object();

        public void Show(object form, bool modal, object? owner = null)
        {
        }

        public void Hide(object form)
        {
        }

        public bool IsShown(object form) => false;

        public string GetCaption(object form) => string.Empty;

        public void SetCaption(object form, string caption)
        {
        }

        public FormBounds GetBounds(object form) => default;

        public void SetBounds(object form, FormBounds bounds)
        {
        }

        public void Leave(object form)
        {
        }

        public void Destroy(object form)
        {
        }

        public void Hold(bool running)
        {
        }

        public void DoEvents()
        {
        }
    }
}
