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

    [TestMethod]
    [DataRow(new[] { "Debug.Print TypeName(UserForms); VarType(UserForms); TypeOf UserForms Is Collection" }, new[] { "Object 9 False" },
        DisplayName = "U01 an object of no class a program can name")]
    [DataRow(new[] { "Dim a As Object, b As Object", "Set a = UserForms", "Set b = UserForms", "Debug.Print a Is b; a Is UserForms" }, new[] { "TrueTrue" },
        DisplayName = "U02 the same object every time")]
    [DataRow(new[] { "Dim g As LifeForm", "Load LifeForm", "Set g = New LifeForm", "Debug.Print UserForms.Count; UserForms(0) Is LifeForm; UserForms.Item(1) Is g; UserForms(1).Name" },
        new[] { "Initialize", "Initialize", "2 TrueTrueLifeForm", "Terminate" }, DisplayName = "U03 the loaded forms, in order, from 0")]
    [DataRow(new[] { "On Error Resume Next", "Debug.Print UserForms(0).Name", "Debug.Print Err.Number" }, new[] { "9" }, DisplayName = "U04 a position that is no form's")]
    [DataRow(new[] { "On Error Resume Next", "Load LifeForm", "Debug.Print UserForms(-1).Name", "Debug.Print Err.Number", "Err.Clear", "Debug.Print UserForms(1).Name", "Debug.Print Err.Number" },
        new[] { "Initialize", "9", "9" }, DisplayName = "U17 before the first, after the last")]
    [DataRow(new[] { "On Error Resume Next", "Load LifeForm", "Debug.Print UserForms(\"LifeForm\").Name", "Debug.Print Err.Number" }, new[] { "Initialize", "13" },
        DisplayName = "U09 a name is no position")]
    [DataRow(new[] { "Load LifeForm", "Unload LifeForm", "Debug.Print UserForms.Count" }, new[] { "Initialize", "QueryClose1", "Terminate", "0" }, DisplayName = "U10 an unloaded form is not")]
    [DataRow(new[] { "Dim x As Variant", "For Each x In UserForms", "Debug.Print \"item\"", "Next", "Debug.Print \"done\"" }, new[] { "done" }, DisplayName = "U11 For Each, of none")]
    [DataRow(new[] { "Dim f As LifeForm, x As Variant", "Load LifeForm", "Set f = New LifeForm", "For Each x In UserForms", "Debug.Print x Is f", "Next" },
        new[] { "Initialize", "Initialize", "False", "True", "Terminate" }, DisplayName = "U18 For Each, in order")]
    [DataRow(new[] { "Dim f As LifeForm, g As LifeForm", "Set f = New LifeForm", "Set g = New LifeForm", "Unload f", "Debug.Print UserForms.Count; UserForms(0) Is g" },
        new[] { "Initialize", "Initialize", "QueryClose1", "Terminate", "1 True", "Terminate" }, DisplayName = "U16 an unloaded form leaves the others")]
    public async Task UserForms_IsTheLoadedForms(string[] body, string[] printed)
        => CollectionAssert.AreEqual(printed, await RunAsync(body), string.Join(" | ", await RunAsync(body)));

    [TestMethod]
    [DataRow("UserForms.Remove 0", "438", DisplayName = "U08 a member it does not have")]
    [DataRow("UserForms.Foo", "438", DisplayName = "U12 late-bound, as any member is")]
    [DataRow("Debug.Print UserForms", "450", DisplayName = "U13 its default member takes a position")]
    [DataRow("Dim c As Collection: Set c = UserForms", "13", DisplayName = "U14 no Collection")]
    [DataRow("UserForms.Add \"NoSuchForm\"", "424", DisplayName = "U06 Add of a name that is no form's")]
    public async Task UserForms_IsWhatItIs_AndNothingElse(string statement, string number)
        => CollectionAssert.AreEqual(new[] { number }, await RunAsync("On Error Resume Next", statement, "Debug.Print Err.Number"));

    // U05, U07: Add loads a new instance - not the default one - which stays loaded, held by the collection, with nothing else referring to it.
    [TestMethod]
    public async Task UserFormsAdd_LoadsANewInstance_ThatItHolds()
        => CollectionAssert.AreEqual(
            new[] { "Initialize", "LifeForm", "1", "Initialize", "False", "2" },
            await RunAsync("Debug.Print TypeName(UserForms.Add(\"LifeForm\"))", "Debug.Print UserForms.Count", "Debug.Print UserForms(0) Is LifeForm", "Debug.Print UserForms.Count"));

    // U15: a form that was unloaded is an object whose form is gone, which TypeName calls a UserForm.
    [TestMethod]
    public async Task AnUnloadedFormsTypeName_IsUserForm()
        => CollectionAssert.AreEqual(
            new[] { "Initialize", "QueryClose1", "Terminate", "0 UserForm" },
            await RunAsync("Dim f As Object", "Load LifeForm", "Set f = UserForms(0)", "Unload LifeForm", "Debug.Print UserForms.Count; TypeName(f)"));

    // L18, L19: Load and Unload of an object that is no form is error 361.
    [TestMethod]
    [DataRow("Unload New Collection", DisplayName = "L18 Unload")]
    [DataRow("Load New Collection", DisplayName = "L19 Load")]
    public async Task LoadingOrUnloadingAnObjectThatIsNoForm_Is361(string statement)
        => CollectionAssert.AreEqual(new[] { "361" }, await RunAsync("On Error Resume Next", statement, "Debug.Print Err.Number"));
}
