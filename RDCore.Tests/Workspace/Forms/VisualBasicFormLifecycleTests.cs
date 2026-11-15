using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Workspace;
using RDCore.SDK.Workspace.Forms;
using RDCore.Tests.Cli;
using RDCore.Tests.Runtime.Libraries;

namespace RDCore.Tests.Workspace.Forms;

/// <summary>
/// A Visual Basic 6.0 form's lifecycle, as VB6 has it (measured in VB6): the use of its name makes its instance, and only a member of its form loads it;
/// <c>Unload</c> keeps the instance, its variables and its name, and its <c>Terminate</c> waits for its last reference; <c>Show</c>'s style and the modal
/// form's errors. The forms are hosted by RDForms, headless.
/// </summary>
[TestClass]
public sealed class VisualBasicFormLifecycleTests
{
    internal static DirectoryLibrarySource Shipped => new(new System.IO.Abstractions.FileSystem(), ShippedLibrariesTests.Symbols);

    // Form2 prints its events, and the number of loaded forms after each when it counts; what its Resize does while it is shown is what its Probe says.
    private static string Form2(bool counting = false)
    {
        string[] counted = counting ? ["    Debug.Print \"count \" & Forms.Count"] : [];
        return Designed("Form2", [
            "Public pub As Integer",
            "Public Probe As String",
            "Public CancelQuery As Integer",
            "Public CancelUnload As Integer",
            "Public UnloadInLoad As Boolean",
            "Public Sub Foo()", "    Debug.Print \"Foo\"", "End Sub",
            "Private Sub Form_Initialize()", "    Debug.Print \"Initialize\"", .. counted, "    If FailInitialize Then Debug.Print 1 \\ pub", "End Sub",
            "Private Sub Form_Load()", "    Debug.Print \"Load\"", .. counted, "    If UnloadInLoad Then", "        Unload Me", "        Debug.Print \"rest of Load\"", "    End If",
            "    If FailLoad Then Debug.Print 1 \\ pub", "End Sub",
            "Private Sub Form_QueryUnload(Cancel As Integer, UnloadMode As Integer)", "    Debug.Print \"QueryUnload \" & UnloadMode", .. counted, "    Cancel = CancelQuery", "End Sub",
            "Private Sub Form_Unload(Cancel As Integer)", "    Debug.Print \"Unload\"", .. counted, "    Cancel = CancelUnload", "End Sub",
            "Private Sub Form_Terminate()", "    Debug.Print \"Terminate\"", "End Sub",
            "Private Sub Form_Resize()",
            "    Dim probing As String",
            "    probing = Probe",
            "    Probe = \"\"",
            "    On Error Resume Next",
            "    Select Case probing",
            "        Case \"hide\"",
            "            Me.Hide",
            "        Case \"show\"",
            "            Me.Show",
            "            Debug.Print \"show \" & Err.Number & \" \" & Err.Description",
            "            Me.Hide",
            "        Case \"show modal\"",
            "            Me.Show vbModal",
            "            Debug.Print \"show modal \" & Err.Number & \" \" & Err.Description",
            "            Me.Hide",
            "        Case \"hide Form3\"",
            "            Form3.Hide",
            "            Debug.Print \"hide Form3 \" & Err.Number & \" \" & Err.Description",
            "            Me.Hide",
            "    End Select",
            "End Sub"]);
    }

    // Form3 has no handlers: it is the other form a modal Form2 is shown over.
    private static string Form3() => Designed("Form3", []);

    private static string Designed(string name, string[] body)
        => string.Join("\r\n", [
            "VERSION 5.00",
            $"Begin VB.Form {name} ",
            $"   Caption         =   \"{name}\"",
            "   ClientHeight    =   3195",
            "   Begin VB.TextBox Text1 ",
            "      TabIndex        =   0",
            "      Text            =   \"Text1\"",
            "   End",
            "End",
            $"Attribute VB_Name = \"{name}\"",
            "Attribute VB_GlobalNameSpace = False",
            "Attribute VB_Creatable = False",
            "Attribute VB_PredeclaredId = True",
            "Attribute VB_Exposed = False",
            "Option Explicit",
            .. body,
            string.Empty]);

    // the outside world of an external host that hosts its VB6 forms with RDForms, headless, and has no other server; its forms are seen through the given host.
    internal static RDCore.External.ExternalWorld RDFormsWorld(Func<RDCore.External.Forms.IFormHost, RDCore.External.Forms.IFormHost>? seen = null)
    {
        var (automation, forms) = RDCore.External.Forms.RDForms.RDFormsHosting.Compose(
            RDCore.External.Automation.UnavailableAutomationServer.Instance,
            RDCore.External.Forms.UnavailableFormHost.Instance,
            new RDCore.External.Forms.RDForms.FormsOptions { VB = RDCore.External.Forms.RDForms.VisualBasicFormsHost.RDForms, Headless = true });
        return new RDCore.External.ExternalWorld(automation, RDCore.External.Native.UnavailableNativeLibraryHost.Instance) { Forms = seen?.Invoke(forms) ?? forms };
    }

    // the forms of a world, which record each form shown and the form its Show gave as its owner, by their names.
    private sealed class OwnersRecorded(RDCore.External.Forms.IFormHost forms) : RDCore.External.Forms.IFormHost
    {
        private readonly Dictionary<object, string> _names = new(ReferenceEqualityComparer.Instance);

        public List<(string Form, string? Owner)> Shown { get; } = [];

        public bool IsAvailable => forms.IsAvailable;

        public object Create(RDCore.External.Forms.FormDefinition form)
        {
            var created = forms.Create(form);
            _names[created] = form.Name;
            return created;
        }

        public void Show(object form, bool modal, object? owner = null)
        {
            Shown.Add((_names[form], owner is null ? null : _names[owner]));
            forms.Show(form, modal, owner);
        }

        public void Hide(object form) => forms.Hide(form);

        public bool IsShown(object form) => forms.IsShown(form);

        public string GetCaption(object form) => forms.GetCaption(form);

        public void SetCaption(object form, string caption) => forms.SetCaption(form, caption);

        public RDCore.External.Forms.FormBounds GetBounds(object form) => forms.GetBounds(form);

        public void SetBounds(object form, RDCore.External.Forms.FormBounds bounds) => forms.SetBounds(form, bounds);

        public void Leave(object form) => forms.Leave(form);

        public void Destroy(object form) => forms.Destroy(form);

        public void Hold(bool running) => forms.Hold(running);

        public void DoEvents() => forms.DoEvents();
    }

    private static string Program(string[] body)
        => $"Attribute VB_Name = \"Program\"\r\nPublic FailInitialize As Boolean\r\nPublic FailLoad As Boolean\r\nPublic Sub Main()\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n";

    private static ModuleWorkspace.WorkspaceLibraries Libraries => new ModuleWorkspace.WorkspaceLibraries([], Shipped) with { Outside = RDFormsWorld() };

    private static (string, string, FormDescriptor)[] Forms(bool counting = false)
    {
        var form2 = Form2(counting);
        var form3 = Form3();
        return [("Form2", form2, FormDesigner.Read(form2)!), ("Form3", form3, FormDesigner.Read(form3)!)];
    }

    private static Task<string[]> RunAsync(params string[] body) => RunAsync(counting: false, body);

    private static Task<string[]> RunAsync(bool counting, params string[] body)
        => ModuleWorkspace.RunAsync([], Program(body), Libraries, Forms(counting), SupportedLanguages.VB6);

    [TestMethod]
    [DataRow(new[] { "Form2.pub = 3" }, new[] { "Initialize" }, DisplayName = "a Public variable makes the instance and does not load it")]
    [DataRow(new[] { "Debug.Print Form2.pub" }, new[] { "Initialize", "0" }, DisplayName = "reading a Public variable does not load it")]
    [DataRow(new[] { "Form2.Foo" }, new[] { "Initialize", "Foo" }, DisplayName = "a Public Sub runs without loading it")]
    [DataRow(new[] { "Dim o As Object", "Set o = Form2", "Debug.Print TypeName(o)" }, new[] { "Initialize", "Form2" }, DisplayName = "a reference to it does not load it")]
    [DataRow(new[] { "Debug.Print Form2.Caption", "Unload Form2" }, new[] { "Initialize", "Load", "Form2", "QueryUnload 1", "Unload" },
        DisplayName = "a member of its form loads it")]
    [DataRow(new[] { "Debug.Print Form2.Text1.Text", "Unload Form2" }, new[] { "Initialize", "Load", "Text1", "QueryUnload 1", "Unload" },
        DisplayName = "a control loads it")]
    [DataRow(new[] { "Load Form2", "Load Form2", "Debug.Print Form2.Visible", "Unload Form2" }, new[] { "Initialize", "Load", "False", "QueryUnload 1", "Unload" },
        DisplayName = "Load loads it once, hidden")]
    [DataRow(new[] { "Form2.Hide", "Debug.Print Form2.Visible", "Unload Form2" }, new[] { "Initialize", "Load", "False", "QueryUnload 1", "Unload" },
        DisplayName = "Hide loads it")]
    [DataRow(new[] { "Dim f As Form2", "Set f = New Form2", "Debug.Print \"made\"", "f.Caption = \"second\"", "Unload f", "Set f = Nothing", "Debug.Print \"after\"" },
        new[] { "Initialize", "made", "Load", "QueryUnload 1", "Unload", "Terminate", "after" },
        DisplayName = "New makes an instance that its form's first member loads")]
    public async Task WhatLoadsAForm_IsAMemberOfItsForm(string[] body, string[] printed)
        => CollectionAssert.AreEqual(printed, await RunAsync(body));

    [TestMethod]
    [DataRow(new[] { "Form2.pub = 7", "Load Form2", "Unload Form2", "Debug.Print Form2.pub", "Debug.Print Form2.Caption", "Unload Form2" },
        new[] { "Initialize", "Load", "QueryUnload 1", "Unload", "7", "Load", "Form2", "QueryUnload 1", "Unload" },
        DisplayName = "Unload keeps the instance and its variables, and a member of its form loads it again, with no Initialize")]
    [DataRow(new[] { "Load Form2", "Unload Form2", "Unload Form2" }, new[] { "Initialize", "Load", "QueryUnload 1", "Unload" },
        DisplayName = "an Unload of a form that was unloaded does nothing")]
    [DataRow(new[] { "Unload Form2", "Debug.Print \"unloaded\"" }, new[] { "Initialize", "unloaded" },
        DisplayName = "an Unload of a form that was never loaded does nothing but make its instance")]
    [DataRow(new[] { "Form2.CancelQuery = 1", "Load Form2", "Unload Form2", "Form2.CancelQuery = 0", "Unload Form2" },
        new[] { "Initialize", "Load", "QueryUnload 1", "QueryUnload 1", "Unload" },
        DisplayName = "QueryUnload's Cancel keeps it loaded")]
    [DataRow(new[] { "Form2.CancelUnload = 1", "Load Form2", "Unload Form2", "Form2.CancelUnload = 0", "Unload Form2" },
        new[] { "Initialize", "Load", "QueryUnload 1", "Unload", "QueryUnload 1", "Unload" },
        DisplayName = "Unload's Cancel keeps it loaded")]
    public async Task Unload_AsksTheForm_AndKeepsItsInstance(string[] body, string[] printed)
        => CollectionAssert.AreEqual(printed, await RunAsync(body));

    [TestMethod]
    [DataRow(new[] { "Load Form2", "Unload Form2", "Set Form2 = Nothing", "Debug.Print \"after\"" },
        new[] { "Initialize", "Load", "QueryUnload 1", "Unload", "Terminate", "after" },
        DisplayName = "its Terminate comes at its last reference, after its Unload")]
    [DataRow(new[] { "Form2.pub = 1", "Set Form2 = Nothing", "Debug.Print \"after\"" }, new[] { "Initialize", "Terminate", "after" },
        DisplayName = "a form never loaded terminates at its last reference")]
    [DataRow(new[] { "Dim f As Object", "Set f = Form2", "Load Form2", "Set Form2 = Nothing", "Debug.Print \"named\"", "Unload f", "Debug.Print \"unloaded\"", "Set f = Nothing", "Debug.Print \"after\"" },
        new[] { "Initialize", "Load", "named", "QueryUnload 1", "Unload", "unloaded", "Terminate", "after" },
        DisplayName = "a loaded form is held apart from its name, and its Terminate waits for its last reference")]
    [DataRow(new[] { "Load Form2", "Unload Form2", "Set Form2 = Nothing", "Debug.Print Form2.pub" }, new[] { "Initialize", "Load", "QueryUnload 1", "Unload", "Terminate", "Initialize", "0" },
        DisplayName = "its name makes a new instance after its last reference")]
    public async Task Terminate_ComesAtTheLastReference(string[] body, string[] printed)
        => CollectionAssert.AreEqual(printed, await RunAsync(body));

    [TestMethod]
    [DataRow("Load Form2", DisplayName = "under Load")]
    [DataRow("Form2.Show", DisplayName = "under Show")]
    [DataRow("Form2.Show vbModal", DisplayName = "under a modal Show")]
    [DataRow("Debug.Print Form2.Caption", DisplayName = "under a member of its form")]
    [DataRow("Form2.Hide", DisplayName = "under Hide")]
    public async Task ALoadThatUnloadsTheForm_FailsTheUse_With364(string use)
    {
        var printed = await RunAsync("On Error Resume Next", "Form2.UnloadInLoad = True", use, "Debug.Print Err.Number & \" \" & Err.Description");

        CollectionAssert.AreEqual(new[] { "Initialize", "Load", "QueryUnload 1", "Unload", "rest of Load", "364 Object was unloaded" }, printed);
    }

    [TestMethod]
    [DataRow("0", DisplayName = "0")]
    [DataRow("0.4", DisplayName = "0.4 converts as CInt")]
    [DataRow("vbModeless", DisplayName = "vbModeless")]
    public async Task ShowsStyle_Modeless_ShowsItAndReturns(string style)
    {
        var printed = await RunAsync($"Form2.Show {style}", "Debug.Print Form2.Visible", "Unload Form2");

        CollectionAssert.AreEqual(new[] { "Initialize", "Load", "True", "QueryUnload 1", "Unload" }, printed);
    }

    [TestMethod]
    [DataRow("1", DisplayName = "1")]
    [DataRow("\"1\"", DisplayName = "\"1\" converts as CInt")]
    [DataRow("0.6", DisplayName = "0.6 converts as CInt")]
    [DataRow("vbModal", DisplayName = "vbModal")]
    public async Task ShowsStyle_Modal_WaitsUntilItIsHidden(string style)
    {
        // a modeless Show while the form is modal fails: what its Resize finds says the Show it is inside is modal.
        var printed = await RunAsync("Form2.Probe = \"show\"", $"Form2.Show {style}", "Debug.Print \"returned \" & Form2.Visible", "Unload Form2");

        CollectionAssert.AreEqual(
            new[] { "Initialize", "Load", "show 401 Can't show non-modal form when modal form is displayed", "returned False", "QueryUnload 1", "Unload" }, printed);
    }

    [TestMethod]
    [DataRow("2", DisplayName = "2")]
    [DataRow("-1", DisplayName = "-1")]
    [DataRow("True", DisplayName = "True")]
    [DataRow("1.5", DisplayName = "1.5 converts as CInt, to 2")]
    public async Task ShowsStyle_OtherThanZeroOrOne_Is5_AfterTheLoad(string style)
    {
        var printed = await RunAsync("On Error Resume Next", $"Form2.Show {style}", "Debug.Print Err.Number", "Debug.Print Form2.Visible", "Unload Form2");

        CollectionAssert.AreEqual(new[] { "Initialize", "Load", "5", "False", "QueryUnload 1", "Unload" }, printed);
    }

    [TestMethod]
    public async Task AModalShow_OfTheModalForm_Is405()
    {
        var printed = await RunAsync("Form2.Probe = \"show modal\"", "Form2.Show vbModal", "Unload Form2");

        CollectionAssert.AreEqual(new[] { "Initialize", "Load", "show modal 405 Unable to show modal form within this context", "QueryUnload 1", "Unload" }, printed);
    }

    [TestMethod]
    public async Task AHide_OfAFormThatIsNotTheTopmostModalForm_Is402()
    {
        var printed = await RunAsync("Form3.Show", "Form2.Probe = \"hide Form3\"", "Form2.Show vbModal", "Debug.Print Form3.Visible", "Unload Form2", "Unload Form3");

        CollectionAssert.AreEqual(
            new[] { "Initialize", "Load", "hide Form3 402 Must close or hide topmost modal form first", "True", "QueryUnload 1", "Unload" }, printed);
    }

    [TestMethod]
    [DataRow("Form2.Show vbModeless, Form3", "Form3", DisplayName = "the owner form it is given")]
    [DataRow("Form2.Show vbModeless", null, DisplayName = "none, when it is given none")]
    public async Task TheOwnerFormOfAShow_IsGivenToItsFormHost(string show, string? owner)
    {
        OwnersRecorded? recorded = null;
        var libraries = new ModuleWorkspace.WorkspaceLibraries([], Shipped) with { Outside = RDFormsWorld(forms => recorded = new OwnersRecorded(forms)) };

        await ModuleWorkspace.RunAsync([], Program(["Form3.Show", show, "Unload Form2", "Unload Form3"]), libraries, Forms(), SupportedLanguages.VB6);

        CollectionAssert.AreEqual(new (string, string?)[] { ("Form3", null), ("Form2", owner) }, recorded!.Shown);
    }

    [TestMethod]
    public async Task End_RaisesNoUnloadAndNoTerminate()
    {
        ExecuteResult? ended = null;
        await ModuleWorkspace.InspectAsync([], Program(["Load Form2", "Form2.Show", "End"]), (_, result, _) =>
        {
            ended = result;
            return Task.CompletedTask;
        }, libraries: Libraries, language: SupportedLanguages.VB6, forms: Forms());

        Assert.AreEqual(ExecutionOutcome.Halted, ended!.Outcome, ended.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "Initialize", "Load" }, ended.Output.Select(line => line.Trim()).ToArray());
    }

    [TestMethod]
    [DataRow(new[] { "Load Form3", "Load Form2", "Debug.Print Forms.Count & Forms(0).Caption & Forms(1).Caption", "Unload Form2", "Unload Form3" },
        new[] { "Initialize", "Load", "2Form3Form2", "QueryUnload 1", "Unload" }, DisplayName = "the loaded forms, in the order they loaded")]
    [DataRow(new[] { "Form2.pub = 1", "Dim f As Form2", "Set f = New Form2", "Debug.Print Forms.Count" }, new[] { "Initialize", "Initialize", "0", "Terminate" },
        DisplayName = "an instance that is not loaded is not one of them")]
    [DataRow(new[] { "Load Form2", "Load Form3", "Unload Form2", "Load Form2", "Debug.Print Forms(0).Caption & Forms(1).Caption", "Unload Form2", "Unload Form3" },
        new[] { "Initialize", "Load", "QueryUnload 1", "Unload", "Load", "Form3Form2", "QueryUnload 1", "Unload" },
        DisplayName = "an unload takes a form out, and a load puts it at the end")]
    [DataRow(new[] { "Load Form2", "Debug.Print TypeName(Forms) & \" \" & TypeName(Forms.Count) & \" \" & TypeName(Forms(0))", "Unload Form2" },
        new[] { "Initialize", "Load", "Object Integer Form2", "QueryUnload 1", "Unload" }, DisplayName = "what it is, its Count, and a form of it")]
    public async Task TheFormsCollection_HoldsTheLoadedForms(string[] body, string[] printed)
        => CollectionAssert.AreEqual(printed, await RunAsync(body));

    [TestMethod]
    [DataRow("Forms.Count", DisplayName = "unqualified")]
    [DataRow("VB.Forms.Count", DisplayName = "qualified with its library")]
    [DataRow("VB.Global.Forms.Count", DisplayName = "qualified with its library and its module")]
    public async Task TheFormsCollection_IsAGlobalMemberOfTheVBLibrary(string count)
        => CollectionAssert.AreEqual(new[] { "Initialize", "Load", "1", "QueryUnload 1", "Unload" }, await RunAsync("Load Form2", $"Debug.Print {count}", "Unload Form2"));

    [TestMethod]
    [DataRow("Forms(\"0\").Caption", "0|Form2", DisplayName = "a String that is a number is the position")]
    [DataRow("Forms(0.4).Caption", "0|Form2", DisplayName = "the position converts as CInt")]
    [DataRow("Forms.Item(0).Caption", "0|Form2", DisplayName = "Item is the default member")]
    [DataRow("Forms(Forms.Count).Caption", "9|", DisplayName = "Count is out of range")]
    [DataRow("Forms(-1).Caption", "9|", DisplayName = "-1 is out of range")]
    [DataRow("Forms(\"Form2\").Caption", "13|", DisplayName = "a name is a type mismatch: there are no keys")]
    [DataRow("Forms(Null).Caption", "13|", DisplayName = "Null is a type mismatch")]
    [DataRow("Forms(70000).Caption", "6|", DisplayName = "an index too large for an Integer overflows")]
    public async Task TheFormsCollectionsIndex_ConvertsAsCInt(string read, string printed)
    {
        var lines = await RunAsync("Load Form2", "On Error Resume Next", "Dim x As String", $"x = {read}", "Debug.Print Err.Number & \"|\" & x", "Unload Form2");

        CollectionAssert.AreEqual(new[] { "Initialize", "Load", printed, "QueryUnload 1", "Unload" }, lines);
    }

    [TestMethod]
    public async Task TheFormsCollection_LetWithoutSet_Is450()
    {
        var printed = await RunAsync("On Error Resume Next", "Dim v As Variant", "v = Forms", "Debug.Print Err.Number & \" \" & IsEmpty(v)");

        CollectionAssert.AreEqual(new[] { "450 True" }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    public async Task AForm_IsOneOfTheLoadedForms_FromItsLoad_ToTheEndOfItsUnload()
    {
        var printed = await RunAsync(counting: true, "Load Form2", "Unload Form2");

        CollectionAssert.AreEqual(new[] { "Initialize", "count 0", "Load", "count 1", "QueryUnload 1", "count 1", "Unload", "count 1" }, printed);
    }
    [TestMethod]
    [DataRow(new[] { "Dim f As Object", "For Each f In Forms", "Unload f", "Next", "Debug.Print Forms.Count & \" \" & (f Is Nothing)" },
        new[] { "Initialize", "Load", "QueryUnload 1", "Unload", "0 True" }, DisplayName = "For Each unloads every form")]
    [DataRow(new[] { "Dim i As Integer", "For i = Forms.Count - 1 To 0 Step -1", "Unload Forms(i)", "Next", "Debug.Print Forms.Count" },
        new[] { "Initialize", "Load", "QueryUnload 1", "Unload", "0" }, DisplayName = "counting down unloads every form")]
    [DataRow(new[] { "On Error Resume Next", "Dim i As Integer", "For i = 0 To Forms.Count - 1", "Unload Forms(i)", "Next", "Debug.Print Err.Number & \" \" & Forms.Count & Forms(0).Caption", "Unload Form3" },
        new[] { "Initialize", "Load", "QueryUnload 1", "Unload", "9 1Form3" }, DisplayName = "counting up skips a form, then is out of range")]
    public async Task UnloadingEveryForm_TheIdiomsProgramsUse(string[] body, string[] printed)
        => CollectionAssert.AreEqual(printed, await RunAsync(["Load Form2", "Load Form3", .. body]));

    [TestMethod]
    public async Task AnErrorInInitialize_IsTheCallers()
    {
        var printed = await RunAsync("On Error GoTo h", "FailInitialize = True", "Form2.pub = 1", "Exit Sub", "h:", "Debug.Print \"caught \" & Err.Number");

        CollectionAssert.AreEqual(new[] { "Initialize", "caught 11" }, printed);
    }

    [TestMethod]
    [Ignore("ending a VB6 run on an error in Form_Load, whatever handler the caller has, is not implemented")]
    public async Task AnErrorInLoad_EndsTheRun_WhateverHandlerTheCallerHas()
    {
        var printed = await RunAsync("On Error GoTo h", "FailLoad = True", "Load Form2", "Exit Sub", "h:", "Debug.Print \"caught \" & Err.Number");

        CollectionAssert.AreEqual(new[] { "Initialize", "Load" }, printed);
    }

    [TestMethod]
    [Ignore("the end of a VB6 run, which terminates each form's instance that is left, is not implemented")]
    public async Task TheRunsEnd_TerminatesTheInstancesThatAreLeft()
    {
        var printed = await RunAsync("Form2.pub = 1");

        CollectionAssert.AreEqual(new[] { "Initialize", "Terminate" }, printed);
    }
}
