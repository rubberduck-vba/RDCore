using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Workspace.Forms;
using RDCore.Tests.Cli;
using RDCore.Tests.Runtime.Libraries;

namespace RDCore.Tests.Workspace.Forms;

/// <summary>
/// A UserForm's lifecycle, as MS-VBA has it (Excel-measured, <c>oracle\s41</c>; each test names its case): what loads a form, what <c>Unload</c> does - its
/// <c>QueryClose</c>, its <c>Terminate</c>, the object it leaves - and its default instance, made again after it is unloaded. None of it needs the form's window:
/// these run on a platform that hosts no forms, and so on any.
/// </summary>
[TestClass]
[TestCategory("MSForms")]
public sealed class FormLifecycleTests
{
    private static DirectoryLibrarySource Shipped => new(new System.IO.Abstractions.FileSystem(), ShippedLibrariesTests.Symbols);

    // a form with no controls, whose event procedures print the events, as s41's LifeForm logs them
    private static readonly FormDescriptor Designer = new(
        "LifeForm", FormDesignerKind.MSForms, new FormControlClass("MSForms", "UserForm", FormDesigner.MSFormsClassId),
        new Dictionary<string, string> { ["Caption"] = "LifeForm" }, []);

    private static string LifeForm(params string[] more)
        => string.Join("\r\n", [
            "VERSION 5.00",
            "Begin {C62A69F0-16DC-11CE-9E98-00AA00574A4F} LifeForm ",
            "   Caption         =   \"LifeForm\"",
            "   OleObjectBlob   =   \"LifeForm.frx\":0000",
            "End",
            "Attribute VB_Name = \"LifeForm\"",
            "Attribute VB_GlobalNameSpace = False",
            "Attribute VB_Creatable = False",
            "Attribute VB_PredeclaredId = True",
            "Attribute VB_Exposed = False",
            "Option Explicit",
            "Public Counter As Long",
            "Private Sub UserForm_Initialize()", "    Debug.Print \"Initialize\"", "End Sub",
            "Private Sub UserForm_QueryClose(Cancel As Integer, CloseMode As Integer)", "    Debug.Print \"QueryClose\" & CloseMode", "    If CancelClose Then Cancel = True", "End Sub",
            "Private Sub UserForm_Terminate()", "    Debug.Print \"Terminate\"", "End Sub",
            "Public Sub CloseMe()", "    Unload Me", "    Debug.Print \"AfterUnloadMe\"", "End Sub",
            "Public Sub Touch()", "    Counter = Counter + 1", "End Sub",
            .. more,
            string.Empty]);

    private static Task<string[]> RunWithFormAsync(string form, params string[] body)
        => ModuleWorkspace.RunAsync(
            [],
            $"Attribute VB_Name = \"Program\"\r\nPublic CancelClose As Boolean\r\nPublic Sub Main()\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n",
            new ModuleWorkspace.WorkspaceLibraries(["MSForms"], Shipped, Outside: RDCore.External.ExternalWorld.None),
            [("LifeForm", form, Designer)]);

    private static Task<string[]> RunAsync(params string[] body) => RunWithFormAsync(LifeForm(), body);

    [TestMethod]
    [DataRow(new[] { "LifeForm.Touch", "Debug.Print LifeForm.Counter" }, new[] { "Initialize", "1" }, DisplayName = "L03 a member of its code loads the default instance")]
    [DataRow(new[] { "Dim f As LifeForm", "Debug.Print TypeName(f)" }, new[] { "Nothing" }, DisplayName = "L05 a variable of its class loads nothing")]
    [DataRow(new[] { "Dim f As LifeForm", "Set f = New LifeForm", "Debug.Print \"made\"" }, new[] { "Initialize", "made", "Terminate" },
        DisplayName = "L06 New loads it at once (and the variable going out of scope lets it go)")]
    public async Task WhatLoadsAForm_IsWhatMakesItsInstance(string[] body, string[] printed)
        => CollectionAssert.AreEqual(printed, await RunAsync(body));

    [TestMethod]
    [DataRow(new[] { "Load LifeForm", "Unload LifeForm" }, new[] { "Initialize", "QueryClose1", "Terminate" }, DisplayName = "L10 Unload: QueryClose with vbFormCode, then Terminate")]
    [DataRow(new[] { "Unload LifeForm" }, new[] { "Initialize", "QueryClose1", "Terminate" }, DisplayName = "L11 a default instance that is not loaded is loaded to be unloaded")]
    [DataRow(new[] { "LifeForm.CloseMe" }, new[] { "Initialize", "QueryClose1", "Terminate", "AfterUnloadMe" }, DisplayName = "L16 Unload Me, and the code after it")]
    [DataRow(new[] { "Dim f As LifeForm", "Set f = New LifeForm", "Set f = Nothing" }, new[] { "Initialize", "Terminate" }, DisplayName = "L14 let go of without Unload: no QueryClose")]
    public async Task Unload_AsksTheForm_ThenTerminatesIt(string[] body, string[] printed)
        => CollectionAssert.AreEqual(printed, await RunAsync(body));

    // L17: a form that cancels its QueryClose stays loaded: no Terminate.
    [TestMethod]
    public async Task AFormThatCancelsItsQueryClose_StaysLoaded()
        => CollectionAssert.AreEqual(
            new[] { "Initialize", "QueryClose1", "kept 7" },
            await RunAsync("CancelClose = True", "LifeForm.Counter = 7", "Unload LifeForm", "CancelClose = False", "Debug.Print \"kept\"; LifeForm.Counter"));

    // L12, L55: the default instance, once unloaded, is let go of: the next reference loads another, and a variable that held the first holds it still.
    [TestMethod]
    public async Task AnUnloadedDefaultInstance_IsMadeAgainByTheNextReference()
        => CollectionAssert.AreEqual(
            new[] { "Initialize", "QueryClose1", "Terminate", "Initialize", "False", "5", "0" },
            await RunAsync("Dim f As LifeForm", "Set f = LifeForm", "LifeForm.Counter = 5", "Unload LifeForm", "Debug.Print f Is LifeForm", "Debug.Print f.Counter", "Debug.Print LifeForm.Counter"));

    // L15, L53, L54: the object an Unload leaves keeps its variables; unloading it again, or letting it go, raises nothing more.
    [TestMethod]
    public async Task AnUnloadedForm_IsAnObjectStill_ThatIsTerminatedOnce()
        => CollectionAssert.AreEqual(
            new[] { "Initialize", "QueryClose1", "Terminate", "3", "0", "released" },
            await RunAsync("Dim f As LifeForm", "Set f = New LifeForm", "f.Counter = 3", "Unload f", "Debug.Print f.Counter",
                "On Error Resume Next", "Unload f", "Debug.Print Err.Number", "Set f = Nothing", "Debug.Print \"released\""));

    // L50: a form's lifecycle is the UserForm's; a Class_Initialize in its code is a procedure like any other.
    [TestMethod]
    public async Task AFormsClassInitialize_DoesNotRun()
        => CollectionAssert.AreEqual(
            new[] { "Initialize", "QueryClose1", "Terminate" },
            await RunWithFormAsync(LifeForm("Private Sub Class_Initialize()", "    Debug.Print \"ClassInitialize\"", "End Sub",
                "Private Sub Class_Terminate()", "    Debug.Print \"ClassTerminate\"", "End Sub"), "Load LifeForm", "Unload LifeForm"));

    // L18, L19: Load and Unload of an object that is no form is error 361.
    [TestMethod]
    [DataRow("Unload New Collection", DisplayName = "L18 Unload")]
    [DataRow("Load New Collection", DisplayName = "L19 Load")]
    public async Task LoadingOrUnloadingAnObjectThatIsNoForm_Is361(string statement)
        => CollectionAssert.AreEqual(new[] { "361" }, await RunAsync("On Error Resume Next", statement, "Debug.Print Err.Number"));
}
