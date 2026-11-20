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
/// server and form host hand it out, so the tests run on any platform.
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

    // a control of another library on the form, one its designer declares and one a Controls.Add of the form makes, is the form's own; what one hands out of
    // yet another library is an ordinary object, and so is an object of the control's class made outside a form.
    [TestMethod]
    [DataRow("Set b = Window.Browser", new[] { "0", "about:blank", "0" }, DisplayName = "declared by the designer")]
    [DataRow("Set b = Window.Controls.Add(\"Shell.Explorer.2\", \"Added\")", new[] { "0", "about:blank", "0" }, DisplayName = "made by Controls.Add")]
    [DataRow("Set b = Window.Browser.Document", new[] { "0", "70" }, DisplayName = "of another library, handed out by one")]
    [DataRow("Set b = CreateObject(\"Shell.Explorer.2\")", new[] { "70", "91" }, DisplayName = "made outside a form")]
    public async Task AControlOfAnotherLibrary_IsTheFormsWhereverTheFormMadeIt(string obtained, string[] expected)
    {
        var designer = Designer with { Controls = [new FormControlDescriptor("Browser", new FormControlClass("SHDocVw", "WebBrowser"), null, null, [])] };
        var world = new ExternalWorld(new BrowserServer(), UnavailableNativeLibraryHost.Instance) { Forms = new StandInForms() };
        var libraries = new InMemoryLibrarySource([.. Shipped.Names.SelectMany(Shipped.DescriptionsOf), InternetControls]);
        var program = ModuleWorkspace.StandardModule(
            "Program",
            "Public Sub Main()",
            "    Dim b As Object",
            "    On Error Resume Next",
            "    " + obtained,
            "    Debug.Print Err.Number",
            "    Err.Clear",
            "    Debug.Print b.LocationURL",
            "    Debug.Print Err.Number",
            "End Sub");

        var output = await ModuleWorkspace.RunAsync([], program,
            new ModuleWorkspace.WorkspaceLibraries(["MSForms", "SHDocVw"], libraries, AllowAutomation: false, Outside: world), [("Window", Window, designer)]);

        CollectionAssert.AreEqual(expected, output, string.Join(" | ", output));
    }

    // the library of the WebBrowser control, which a project with one on a form references: its LocationURL, its Document of MSHTML, and an event, which a
    // control has for the form to handle.
    private static LibraryDescription InternetControls { get; } = new()
    {
        Name = "SHDocVw",
        Classes =
        [
            new ClassDescription
            {
                Name = "WebBrowser",
                ProgId = "Shell.Explorer.2",
                Members =
                [
                    new MemberDescription { Name = "LocationURL", Kind = MemberKind.PropertyGet, Type = "String" },
                    new MemberDescription { Name = "Document", Kind = MemberKind.PropertyGet, Type = "Object" },
                ],
                Events = [new EventDescription { Name = "DownloadComplete" }],
            },
        ],
    };

    // a server of COM objects whose form holds a WebBrowser and whose Controls.Add makes one: each is at about:blank, and its Document is of MSHTML.
    private sealed class BrowserServer : IAutomationServer
    {
        private readonly object _controls = new();
        private readonly object _browser = new();
        private readonly object _added = new();
        private readonly object _document = new();

        public bool IsAvailable => true;

        public object CreateObject(string progId) => new object();

        public object? Invoke(object target, string member, AutomationInvocation invocation, object?[] arguments, bool[] byReference, CultureInfo culture)
            => member switch
            {
                "Controls" => _controls,
                "Browser" => _browser,
                "Add" when ReferenceEquals(target, _controls) => _added,
                "LocationURL" when ReferenceEquals(target, _browser) || ReferenceEquals(target, _added) => "about:blank",
                "Document" when ReferenceEquals(target, _browser) => _document,
                _ => throw new AutomationException(unchecked((int)0x80020006)),
            };

        public string? ClassNameOf(object target)
            => ReferenceEquals(target, _controls) ? "MSForms.Controls"
            : ReferenceEquals(target, _browser) || ReferenceEquals(target, _added) ? "SHDocVw.WebBrowser"
            : ReferenceEquals(target, _document) ? "MSHTML.HTMLDocument"
            : null;

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
