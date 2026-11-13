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
/// A hosted form that a class holds in a <c>WithEvents</c> variable: closing its window runs its <c>QueryClose</c> and unloads it, whether the variable was assigned
/// before the form was hosted or after, and whether it still holds the form. A stand-in server and form host close the window as a modal <c>Show</c> waits, as the
/// close button does: these run on any platform.
/// </summary>
[TestClass]
public sealed class FormHeldWithEventsTests
{
    private static DirectoryLibrarySource Shipped => new(new System.IO.Abstractions.FileSystem(), ShippedLibrariesTests.Symbols);

    private static readonly FormDescriptor Designer = new(
        "Window", FormDesignerKind.MSForms, new FormControlClass("MSForms", "UserForm", FormDesigner.MSFormsClassId),
        new Dictionary<string, string> { ["Caption"] = "Window" }, []);

    // a form that tells its holder it closed; its Initialize hosts it at once (Me.Caption) when the program says so, as a control added there does.
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
        "Public Event Closed()",
        "Private Sub UserForm_Initialize()", "    Debug.Print \"Initialize\"", "    If HostedAtOnce Then Me.Caption = \"Held\"", "End Sub",
        "Private Sub UserForm_QueryClose(Cancel As Integer, CloseMode As Integer)", "    Debug.Print \"QueryClose \" & CloseMode", "    RaiseEvent Closed", "End Sub",
        string.Empty]);

    private static readonly (string Name, string Source)[] Classes =
    [
        ("Owner", ModuleWorkspace.ClassModule(
            "Owner",
            "Private WithEvents Held As Window",
            "Public Sub Run(ByVal letGo As Boolean)",
            "    Set Held = Window",
            "    If letGo Then Set Held = Nothing",
            "    Window.Show",
            "    Debug.Print \"shown\"",
            "End Sub",
            "Private Sub Held_Closed()",
            "    Debug.Print \"Closed\"",
            "End Sub")),
    ];

    [TestMethod]
    [DataRow(false, false, new[] { "Initialize", "QueryClose 0", "Closed", "shown" }, DisplayName = "assigned before the form is hosted")]
    [DataRow(true, false, new[] { "Initialize", "QueryClose 0", "Closed", "shown" }, DisplayName = "assigned once the form is hosted")]
    [DataRow(false, true, new[] { "Initialize", "QueryClose 0", "shown" }, DisplayName = "assigned before the form is hosted, then let go of")]
    [DataRow(true, true, new[] { "Initialize", "QueryClose 0", "shown" }, DisplayName = "assigned once the form is hosted, then let go of")]
    public async Task ClosingTheWindow_OfAFormHeldWithEvents_RunsItsQueryClose(bool hostedAtOnce, bool letGo, string[] printed)
    {
        var server = new StandInServer();
        var world = new ExternalWorld(server, UnavailableNativeLibraryHost.Instance) { Forms = new StandInForms(server) };
        var program = ModuleWorkspace.StandardModule(
            "Program",
            "Public HostedAtOnce As Boolean",
            "Public Sub Main()",
            $"    HostedAtOnce = {hostedAtOnce}",
            "    Dim o As New Owner",
            $"    o.Run {letGo}",
            "End Sub");

        var output = await ModuleWorkspace.RunAsync(Classes, program, new ModuleWorkspace.WorkspaceLibraries(["MSForms"], Shipped, Outside: world), [("Window", Window, Designer)]);

        CollectionAssert.AreEqual(printed, output, string.Join(" | ", output));
        Assert.AreEqual(1, server.Advised.Count, "the form is listened to once");
    }

    // a server whose objects raise nothing of their own and answer no member: the forms of StandInForms are its objects. It keeps the sink it was last given for an
    // object, as a server in another process does.
    private sealed class StandInServer : IAutomationServer
    {
        private readonly Dictionary<object, IAutomationEventSink> _sinks = new(ReferenceEqualityComparer.Instance);

        public List<object> Advised { get; } = [];

        public bool IsAvailable => true;

        // what a form's window raises, while the call that shows it waits.
        public bool Raise(object form, string name)
        {
            if (!_sinks.TryGetValue(form, out var sink))
            {
                return false;
            }

            sink.OnEvent(new AutomationEvent(name, [], isSynchronous: true, _ => { }));
            return true;
        }

        public object CreateObject(string progId) => throw new AutomationException(unchecked((int)0x800401F3));

        public object? Invoke(object target, string member, AutomationInvocation invocation, object?[] arguments, bool[] byReference, CultureInfo culture)
            => throw new AutomationException(unchecked((int)0x80020006));

        public string? ClassNameOf(object target) => null;

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
            _sinks[source] = sink;
            Advised.Add(source);
        }

        public void Unadvise(object source) => _ = _sinks.Remove(source);

        public void Release(object handle)
        {
        }
    }

    // forms with no window: showing one closes it, as its close button does, and one nobody listens to is hidden.
    private sealed class StandInForms(StandInServer server) : IFormHost
    {
        private readonly Dictionary<object, string> _captions = new(ReferenceEqualityComparer.Instance);

        public bool IsAvailable => true;

        public object Create(FormDefinition form) => new object();

        public void Show(object form, bool modal) => _ = server.Raise(form, FormWindowEvents.Close);

        public void Hide(object form)
        {
        }

        public bool IsShown(object form) => false;

        public string GetCaption(object form) => _captions.GetValueOrDefault(form, string.Empty);

        public void SetCaption(object form, string caption) => _captions[form] = caption;

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
