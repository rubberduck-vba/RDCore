using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Workspace;
using RDCore.SDK.Workspace.Forms;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Workspace.Forms;

/// <summary>
/// A Visual Basic 6.0 form's menu array: the items that share a name and each have an <c>Index</c>. The array's name is an object with <c>Count</c>, <c>LBound</c>,
/// <c>UBound</c> and a default member that takes the index of an element; <c>Load</c> and <c>Unload</c> of an element add one to the array and take one out. The
/// forms are hosted by RDForms, headless.
/// </summary>
// the active form is the UI thread's, which every RDForms window shares: a row that shows a form runs alone.
[TestClass]
[DoNotParallelize]
public sealed class VisualBasicFormMenuArrayTests
{
    // Form3's File menu holds the menu array mnuRecent, of indexes 0 and 2, after the item mnuOpen; cmd is a control array of one button. Its code reads and loads
    // elements by the array's name alone.
    private static string Form3()
        => string.Join("\r\n", [
            "VERSION 5.00",
            "Begin VB.Form Form3 ",
            "   Caption         =   \"Form3\"",
            "   ClientHeight    =   3195",
            "   ClientWidth     =   4680",
            "   Begin VB.CommandButton cmd ",
            "      Caption         =   \"One\"",
            "      Height          =   495",
            "      Index           =   0",
            "      Left            =   120",
            "      TabIndex        =   0",
            "      Top             =   120",
            "      Width           =   1215",
            "   End",
            "   Begin VB.Menu mnuFile ",
            "      Caption         =   \"&File\"",
            "      Begin VB.Menu mnuOpen ",
            "         Caption         =   \"&Open\"",
            "      End",
            "      Begin VB.Menu mnuRecent ",
            "         Caption         =   \"First\"",
            "         Index           =   0",
            "      End",
            "      Begin VB.Menu mnuRecent ",
            "         Caption         =   \"Third\"",
            "         Index           =   2",
            "      End",
            "   End",
            "End",
            "Attribute VB_Name = \"Form3\"",
            "Attribute VB_GlobalNameSpace = False",
            "Attribute VB_Creatable = False",
            "Attribute VB_PredeclaredId = True",
            "Attribute VB_Exposed = False",
            "Option Explicit",
            "Public Sub PrintOwn()", "    Debug.Print mnuRecent(2).Caption & \",\" & mnuRecent.Count", "End Sub",
            "Public Sub LoadOwn(ByVal index As Integer)", "    Load mnuRecent(index)", "End Sub",
            "Public Sub UnloadOwn(ByVal index As Integer)", "    Unload mnuRecent(index)", "End Sub",
            "Private Sub mnuRecent_Click(Index As Integer)", "    Debug.Print \"mnuRecent Click \" & Index", "End Sub",
            string.Empty]);

    private static string Program(string[] body)
        => $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n";

    private static ModuleWorkspace.WorkspaceLibraries Libraries
        => new ModuleWorkspace.WorkspaceLibraries([], VisualBasicFormLifecycleTests.Shipped) with { Outside = VisualBasicFormLifecycleTests.RDFormsWorld() };

    private static async Task<string[]> RunAsync(params string[] body)
    {
        var form3 = Form3();
        var printed = await ModuleWorkspace.RunAsync([], Program([.. body, "Unload Form3"]), Libraries, [("Form3", form3, FormDesigner.Read(form3)!)], SupportedLanguages.VB6);
        return [.. printed.Where(line => !line.StartsWith("Resize ", StringComparison.Ordinal) && line != "Paint")];
    }

    private static async Task AssertPrintsAsync(string[] expected, params string[] body)
    {
        var printed = await RunAsync(body);

        CollectionAssert.AreEqual(expected, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    public Task TheArray_CountsItsElements_FromTheLeastIndexToTheGreatest()
        => AssertPrintsAsync(["2,0,2"], "Debug.Print Form3.mnuRecent.Count & \",\" & Form3.mnuRecent.LBound & \",\" & Form3.mnuRecent.UBound");

    [TestMethod]
    public Task AnElement_IsReachedByItsIndex_AndIsTheSameObjectEachTime()
        => AssertPrintsAsync(["Third", "True,True"],
            "Debug.Print Form3.mnuRecent(2).Caption",
            "Debug.Print (Form3.mnuRecent Is Form3.mnuRecent) & \",\" & (Form3.mnuRecent(2) Is Form3.mnuRecent(2))");

    [TestMethod]
    public Task AnElement_IsAssignedItsProperties()
        => AssertPrintsAsync(["Changed"], "Form3.mnuRecent(2).Caption = \"Changed\"", "Debug.Print Form3.mnuRecent(2).Caption");

    [TestMethod]
    public Task AnIndexNoElementHas_Is340()
        => AssertPrintsAsync(["340"], "On Error Resume Next", "Debug.Print Form3.mnuRecent(1).Caption", "Debug.Print Err.Number");

    [TestMethod]
    public Task LateBound_AnElementIsReachedInOneCall()
        => AssertPrintsAsync(["Third", "2"], "Dim f As Object", "Set f = Form3", "Debug.Print f.mnuRecent(2).Caption", "Debug.Print f.mnuRecent.Count");

    [TestMethod]
    public Task TheFormsOwnCode_NamesTheArrayAlone()
        => AssertPrintsAsync(["Third,2"], "Form3.PrintOwn");

    [TestMethod]
    public Task ForEach_GoesThroughTheElements_InTheOrderOfTheirIndexes()
        => AssertPrintsAsync(["First", "Third"], "Dim m As Object", "For Each m In Form3.mnuRecent", "    Debug.Print m.Caption", "Next");
    [TestMethod]
    public Task Load_AddsAnElement_ThatCopiesTheOneOfTheLeastIndex()
        => AssertPrintsAsync(["3,0,2", "First"], "Load Form3.mnuRecent(1)",
            "Debug.Print Form3.mnuRecent.Count & \",\" & Form3.mnuRecent.LBound & \",\" & Form3.mnuRecent.UBound",
            "Debug.Print Form3.mnuRecent(1).Caption");

    [TestMethod]
    public Task Load_LetCoercesTheIndexToInteger_AndDoesNotReachTheElement()
        => AssertPrintsAsync(["5"], "Dim index As String", "index = \"5\"", "Load Form3.mnuRecent(index)", "Debug.Print Form3.mnuRecent.UBound");

    [TestMethod]
    public Task Unload_TakesOutAnElementTheProgramLoaded()
        => AssertPrintsAsync(["2,2"], "Load Form3.mnuRecent(5)", "Unload Form3.mnuRecent(5)", "Debug.Print Form3.mnuRecent.Count & \",\" & Form3.mnuRecent.UBound");

    [TestMethod]
    public Task TheFormsOwnCode_LoadsAndUnloadsByTheArraysNameAlone()
        => AssertPrintsAsync(["4", "2"], "Form3.LoadOwn 4", "Debug.Print Form3.mnuRecent.UBound", "Form3.UnloadOwn 4", "Debug.Print Form3.mnuRecent.UBound");

    [TestMethod]
    [DataRow("Load Form3.mnuRecent(0)", "360", DisplayName = "Load of an element that is loaded")]
    [DataRow("Unload Form3.mnuRecent(0)", "362", DisplayName = "Unload of an element of the form's file")]
    [DataRow("Unload Form3.mnuRecent(1)", "340", DisplayName = "Unload of an index no element has")]
    [DataRow("Load Form3.mnuRecent(-1)", "341", DisplayName = "Load of a negative index")]
    [DataRow("Load Form3.mnuRecent(40000)", "6", DisplayName = "Load of an index past Integer")]
    public Task LoadAndUnload_OfAnElement_RaiseWhatVB6Raises(string statement, string number)
        => AssertPrintsAsync([number, "2"], "On Error Resume Next", statement, "Debug.Print Err.Number", "Debug.Print Form3.mnuRecent.Count");

    [TestMethod]
    [DataRow("Load Form3.mnuOpen", DisplayName = "Load of an item that is in no array")]
    [DataRow("Unload Form3.mnuOpen", DisplayName = "Unload of an item that is in no array")]
    [DataRow("Load Form3.mnuRecent", DisplayName = "Load of the array")]
    [DataRow("Unload Form3.mnuRecent", DisplayName = "Unload of the array")]
    public Task LoadAndUnload_OfWhatIsNoElement_Are361(string statement)
        => AssertPrintsAsync(["361"], "On Error Resume Next", statement, "Debug.Print Err.Number");

    // what a user does with the first menu a form pops up: a click on the element of an index of mnuRecent, on the UI thread, once the menu is open. A form that
    // pops up nothing is hidden, which ends the program's PopupMenu, and the row fails on what was printed.
    private sealed class ElementClicked(RDCore.External.Forms.IFormHost forms, int index) : RDCore.External.Forms.IFormHost
    {
        public bool IsAvailable => forms.IsAvailable;

        public object Create(RDCore.External.Forms.FormDefinition form) => forms.Create(form);

        public void Show(object form, bool modal, object? owner = null)
        {
            forms.Show(form, modal, owner);
            OnceItPopsUp(RDCore.External.Forms.RDForms.RDFormsAutomationServer.HandleOf(form), 1000);
        }

        private void OnceItPopsUp(RDCore.External.Forms.RDForms.RDFormsObject form, int jobsLeft)
            => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (form.Form.Window.PoppedUpMenu is { IsOpen: true } popup)
                {
                    Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(popup).OfType<Avalonia.Controls.MenuItem>()
                        .Single(item => RDCore.Forms.Avalonia.Controls.VBControl.GetInstance(item) is { Name: "mnuRecent" } element && element.Index == index)
                        .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.MenuItem.ClickEvent));
                    popup.Close();
                }
                else if (jobsLeft > 0 && !form.Form.IsDestroyed)
                {
                    OnceItPopsUp(form, jobsLeft - 1);
                }
                else if (!form.Form.IsDestroyed)
                {
                    form.Form.Hide();
                }
            }, Avalonia.Threading.DispatcherPriority.Background);

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

    [TestMethod]
    [DataRow(2, DisplayName = "an element of the form's file")]
    [DataRow(1, true, DisplayName = "an element the program loaded")]
    public async Task AnElementsClick_RunsTheArraysHandler_WithItsIndex(int index, bool loaded = false)
    {
        var form3 = Form3();
        var libraries = new ModuleWorkspace.WorkspaceLibraries([], VisualBasicFormLifecycleTests.Shipped)
            with { Outside = VisualBasicFormLifecycleTests.RDFormsWorld(forms => new ElementClicked(forms, index)) };
        string[] load = loaded ? ["Load Form3.mnuRecent(1)"] : [];

        var printed = await ModuleWorkspace.RunAsync([], Program([.. load, "Form3.Show", "Form3.PopupMenu Form3.mnuFile", "Debug.Print \"back\"", "Unload Form3"]),
            libraries, [("Form3", form3, FormDesigner.Read(form3)!)], SupportedLanguages.VB6);

        CollectionAssert.AreEqual(new[] { $"mnuRecent Click {index}", "back" }, printed.Where(line => line.StartsWith("mnu", StringComparison.Ordinal) || line == "back").ToArray(),
            string.Join(" | ", printed));
    }

    [TestMethod]
    [Ignore("an array of controls other than menus has no object in RDForms: its name raises 438")]
    public Task Load_AddsAControlToAControlArray()
        => AssertPrintsAsync(["2"], "Load Form3.cmd(1)", "Debug.Print Form3.cmd.Count");
}
