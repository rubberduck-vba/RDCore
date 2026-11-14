using RDCore.Diagnostics;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Workspace;
using RDCore.SDK.Workspace.Forms;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Workspace.Forms;

/// <summary>
/// The <c>Controls</c> collection of a Visual Basic 6.0 form hosted by RDForms: its <c>Count</c>, its items by index and by name, its order, and the controls a
/// program adds and removes at run time, with the errors and the events VB6 has (measured in VB6, native and p-code alike).
/// </summary>
[TestClass]
public sealed class VisualBasicFormRunTimeControlsTests
{
    // Frame1 and Command1, in the file's order; a WithEvents variable whose handlers print what they handle, and a handler named for a control added at run time,
    // which nothing runs.
    private static string Form1() => string.Join("\r\n", [
        "VERSION 5.00",
        "Begin VB.Form Form1 ",
        "   Caption         =   \"Form1\"",
        "   ClientHeight    =   3195",
        "   Begin VB.Frame Frame1 ",
        "      Caption         =   \"Frame1\"",
        "      Height          =   1500",
        "      Left            =   2400",
        "      TabIndex        =   1",
        "      Top             =   120",
        "      Width           =   1800",
        "   End",
        "   Begin VB.CommandButton Command1 ",
        "      Caption         =   \"Command1\"",
        "      Height          =   495",
        "      Left            =   120",
        "      TabIndex        =   0",
        "      Top             =   120",
        "      Width           =   1215",
        "   End",
        "End",
        "Attribute VB_Name = \"Form1\"",
        "Attribute VB_GlobalNameSpace = False",
        "Attribute VB_Creatable = False",
        "Attribute VB_PredeclaredId = True",
        "Attribute VB_Exposed = False",
        "Option Explicit",
        "Public WithEvents btn As VB.CommandButton",
        "Public Sub AddButton()",
        "    Set btn = Controls.Add(\"VB.CommandButton\", \"cmdNew\")",
        "End Sub",
        "Private Sub btn_Click()", "    Debug.Print \"btn_Click\"", "End Sub",
        "Private Sub btn_GotFocus()", "    Debug.Print \"btn_GotFocus\"", "End Sub",
        "Private Sub cmdNew_Click()", "    Debug.Print \"cmdNew_Click\"", "End Sub",
        string.Empty]);

    private static ModuleWorkspace.WorkspaceLibraries Libraries
        => new ModuleWorkspace.WorkspaceLibraries([], VisualBasicFormLifecycleTests.Shipped) with { Outside = VisualBasicFormLifecycleTests.RDFormsWorld() };

    // Order prints the names of the collection's controls, by index; Errs the error of the statement before it, and clears it.
    private static string Program(string[] body) => string.Join("\r\n", [
        "Attribute VB_Name = \"Program\"",
        "Private Function Order() As String",
        "    Dim s As String, i As Integer",
        "    For i = 0 To Form1.Controls.Count - 1",
        "        s = s & IIf(i > 0, \",\", \"\") & Form1.Controls(i).Name",
        "    Next",
        "    Order = s",
        "End Function",
        "Private Sub Errs()",
        "    Debug.Print Err.Number & \" \" & Replace(Replace(Err.Description, vbCr, \"\"), vbLf, \"|\")",
        "    Err.Clear",
        "End Sub",
        "Public Sub Main()",
        .. body,
        "End Sub",
        string.Empty]);

    private static Task<string[]> RunAsync(params string[] body)
    {
        var form1 = Form1();
        return ModuleWorkspace.RunAsync([], Program(body), Libraries, [("Form1", form1, FormDesigner.Read(form1)!)], SupportedLanguages.VB6);
    }

    [TestMethod]
    public async Task TheCollection_HoldsTheFilesControls_ThenThoseAdded_InTheOrderAdded()
    {
        var printed = await RunAsync(
            "Load Form1",
            "Debug.Print Form1.Controls.Count & \" \" & Order()",
            "Dim c As Object",
            "Set c = Form1.Controls.Add(\"VB.CommandButton\", \"cmdNew\")",
            "Debug.Print Form1.Controls.Count & \" \" & Order()",
            "Dim ctl As Object",
            "For Each ctl In Form1.Controls",
            "    Debug.Print ctl.Name",
            "Next",
            "Debug.Print Form1.Controls(\"CMDNEW\") Is c",
            "Unload Form1");

        CollectionAssert.AreEqual(new[] { "2 Frame1,Command1", "3 Frame1,Command1,cmdNew", "Frame1", "Command1", "cmdNew", "True" }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    public async Task AControlAdded_IsHidden_At0_0_1200By480_AndTakesTheNextTabIndex()
    {
        var printed = await RunAsync(
            "Load Form1",
            "Dim c As Object",
            "Set c = Form1.Controls.Add(\"VB.CommandButton\", \"cmdNew\")",
            "Debug.Print c.Name & \" \" & c.Visible & \" \" & c.Left & \" \" & c.Top & \" \" & c.Width & \" \" & c.Height & \" \" & c.TabIndex & \" \" & c.Enabled & \" [\" & c.Caption & \"]\"",
            "Set c = Form1.Controls.Add(\"VB.CommandButton\", \"cmdIn\", Form1.Frame1)",
            "Debug.Print c.Name & \" \" & c.TabIndex",
            "Set c = Form1.Controls.Add(\"VB.TextBox\", \"txtNew\")",
            "Debug.Print c.Name & \" \" & c.TabIndex & \" [\" & c.Text & \"]\"",
            "Unload Form1");

        CollectionAssert.AreEqual(new[] { "cmdNew False 0 0 1200 480 2 True []", "cmdIn 3", "txtNew 4 []" }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    [DataRow("Form1.Controls.Add \"VB.CommandButton\", \"cmdNew\"", "727 There is already a control with the name 'cmdNew'", DisplayName = "727, the same name")]
    [DataRow("Form1.Controls.Add \"VB.CommandButton\", \"CMDNEW\"", "727 There is already a control with the name 'CMDNEW'", DisplayName = "727, the same name in another case")]
    [DataRow("Form1.Controls.Add \"VB.CommandButton\", \"Command1\"", "727 There is already a control with the name 'Command1'", DisplayName = "727, a name of the file's")]
    [DataRow("Form1.Controls.Add \"VB.Bogus\", \"x1\"", "711 Invalid class string.|Looking for object with ProgID: VB.Bogus", DisplayName = "711, an unknown class")]
    [DataRow("Form1.Controls.Add \"VB.CommandButton\", \"\"", "728 '' is not a legal control name", DisplayName = "728, an empty name")]
    [DataRow("Form1.Controls.Add \"VB.CommandButton\", \"cmdIn2\", Form1.Controls(\"cmdNew\")", "721 'CommandButton' is not a valid control container", DisplayName = "721, into a button")]
    [DataRow("Form1.Controls.Add \"VB.Menu\", \"mnuNew\"", "720 'Menu' is not a valid control type", DisplayName = "720, a menu")]
    [DataRow("Form1.Controls.Add \"VB.Form\", \"frmNew\"", "720 'Form' is not a valid control type", DisplayName = "720, a form")]
    public async Task AnAddTheFormRefuses_IsVB6sError(string add, string expected)
    {
        var printed = await RunAsync("Load Form1", "Form1.Controls.Add \"VB.CommandButton\", \"cmdNew\"", "On Error Resume Next", add, "Errs",
            "Debug.Print Form1.Controls.Count", "Unload Form1");

        CollectionAssert.AreEqual(new[] { expected, "3" }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    public async Task AControlAddedToAWithEventsVariable_RaisesItsEvents_ToTheVariablesHandlers()
    {
        var printed = await RunAsync(
            "Form1.Show",
            "DoEvents",
            "Form1.AddButton",
            "Form1.btn.Visible = True",
            "Form1.btn.SetFocus",
            "DoEvents",
            "Debug.Print \"done\"",
            "Unload Form1");

        CollectionAssert.AreEqual(new[] { "btn_GotFocus", "done" }, printed, string.Join(" | ", printed));
    }

    // a control Controls.Add returns is an Object, which Set-assigns to a WithEvents variable of the control's class: no compile error, and nothing an
    // analyzer reports, on the variable or on the assignment. The variable is Public for the other tests, which read it from outside the form, and that it
    // could be a property is reported of any Public field.
    [TestMethod]
    public async Task SetOfAWithEventsVariable_ToWhatControlsAddReturns_IsValid()
    {
        var form1 = Form1();
        var payload = await ModuleWorkspace.SemanticsAsync([], Program(["    Form1.AddButton"]), language: SupportedLanguages.VB6, libraries: Libraries,
            forms: [("Form1", form1, FormDesigner.Read(form1)!)]);
        var errors = payload.Modules.SelectMany(module => module.DeclarationErrors.Concat(module.Procedures.SelectMany(procedure => procedure.CompileErrors)))
            .Select(error => $"{error.Location.Range.Start.Line}: {error.Verbose}").ToArray();
        Assert.IsEmpty(errors, string.Join(" | ", errors));

        var lines = form1.Split("\r\n");
        var declared = Array.FindIndex(lines, line => line.StartsWith("Public WithEvents btn", StringComparison.Ordinal));
        var assigned = Array.FindIndex(lines, line => line.Contains("Controls.Add", StringComparison.Ordinal));
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Form1");
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Form1.frm"));
        var context = new RDCore.SDK.Extensibility.Diagnostics.ModuleAnalysisContext(uri, new RDCore.Parsing.ModuleParser().Parse(uri, form1), model);
        var findings = typeof(RDCore.Diagnostics.Analyzers.UnmatchedEventHandlerAnalyzer).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(RDCore.SDK.Extensibility.Diagnostics.IModuleAnalyzer).IsAssignableFrom(type)
                && type.GetConstructor(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
                    Type.EmptyTypes) is not null)
            .Select(type => (RDCore.SDK.Extensibility.Diagnostics.IModuleAnalyzer)Activator.CreateInstance(type, nonPublic: true)!)
            .SelectMany(analyzer => analyzer.Analyze(context))
            .Where(finding => finding.Code != RDCoreDiagnosticId.EncapsulatePublicField.ToCode())
            // a line counted from 0 or from 1: either is the line of the variable or of the assignment.
            .Where(finding => finding.Range.Start.Line is var line && (line - declared is 0 or 1 || line - assigned is 0 or 1))
            .Select(finding => $"{finding.Range.Start.Line}: {finding.Message}").ToArray();
        Assert.IsEmpty(findings, string.Join(" | ", findings));
    }

    [TestMethod]
    public async Task AControlAddedToAWithEventsVariable_RaisesItsClick_ToTheVariablesHandlerOnly()
    {
        var printed = await RunAsync(
            "Form1.Show",
            "DoEvents",
            "Form1.AddButton",
            "Form1.btn.Visible = True",
            "Form1.btn.Value = True",
            "DoEvents",
            "Debug.Print \"done\"",
            "Unload Form1");

        CollectionAssert.AreEqual(new[] { "btn_Click", "done" }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    public async Task Remove_TakesTheControlOut_AndItsObject_Is735()
    {
        var printed = await RunAsync(
            "Load Form1",
            "Form1.AddButton",
            "Dim c As Object",
            "Set c = Form1.btn",
            "Form1.Controls.Remove \"cmdNew\"",
            "Debug.Print Form1.Controls.Count & \" \" & Order()",
            "Debug.Print (Form1.btn Is Nothing) & \" \" & (c Is Nothing)",
            "On Error Resume Next",
            "Debug.Print c.Name",
            "Errs",
            "c.Caption = \"x\"",
            "Errs",
            "c.SetFocus",
            "Errs",
            "Unload Form1");

        CollectionAssert.AreEqual(new[]
        {
            "2 Frame1,Command1",
            "False False",
            "735 The control is no longer usable because it has been removed from the controls collection",
            "735 The control is no longer usable because it has been removed from the controls collection",
            "735 The control is no longer usable because it has been removed from the controls collection",
        }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    [DataRow("Form1.Controls.Remove \"cmdNew\"", "730 Control 'cmdNew' not found", DisplayName = "730, removed twice")]
    [DataRow("Form1.Controls.Remove \"nothere\"", "730 Control 'nothere' not found", DisplayName = "730, a name the form has not")]
    [DataRow("Form1.Controls.Remove \"Command1\"", "729 Controls.Remove can only remove controls added with Controls.Add", DisplayName = "729, a control of the file")]
    [DataRow("Form1.Controls.Remove 0", "729 Controls.Remove can only remove controls added with Controls.Add", DisplayName = "729, by index 0")]
    public async Task ARemoveTheFormRefuses_IsVB6sError(string remove, string expected)
    {
        var printed = await RunAsync("Load Form1", "Form1.Controls.Add \"VB.CommandButton\", \"cmdNew\"", "Form1.Controls.Remove \"cmdNew\"", "On Error Resume Next",
            remove, "Errs", "Unload Form1");

        CollectionAssert.AreEqual(new[] { expected }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    public async Task Remove_ByObject_AndOfAContainer_TakesWhatItHolds()
    {
        var printed = await RunAsync(
            "Load Form1",
            "Form1.Controls.Add \"VB.Label\", \"lblNew\"",
            "Form1.Controls.Remove Form1.Controls(\"lblNew\")",
            "Debug.Print Order()",
            "Form1.Controls.Add \"VB.Frame\", \"fraNew\"",
            "Form1.Controls.Add \"VB.CommandButton\", \"cmdInNew\", Form1.Controls(\"fraNew\")",
            "Debug.Print Order()",
            "Form1.Controls.Remove \"fraNew\"",
            "Debug.Print Order()",
            "On Error Resume Next",
            "Debug.Print TypeName(Form1.Controls(\"cmdInNew\"))",
            "Errs",
            "Unload Form1");

        CollectionAssert.AreEqual(new[] { "Frame1,Command1", "Frame1,Command1,fraNew,cmdInNew", "Frame1,Command1", "730 Control 'cmdInNew' not found" },
            printed, string.Join(" | ", printed));
    }

    [TestMethod]
    public async Task Remove_ClosesUpTheTabIndexes_AndANameCanBeAddedAgain()
    {
        var printed = await RunAsync(
            "Load Form1",
            "Form1.Controls.Add \"VB.CommandButton\", \"cmdNew\"",
            "Form1.Controls.Add \"VB.CommandButton\", \"cmdIn\", Form1.Frame1",
            "Form1.Controls.Add \"VB.TextBox\", \"txtNew\"",
            "Form1.Controls.Remove \"cmdNew\"",
            "Form1.Controls.Add \"VB.CommandButton\", \"cmdNew\"",
            "Debug.Print Order()",
            "Debug.Print Form1.Command1.TabIndex & \" \" & Form1.Frame1.TabIndex & \" \" & Form1.Controls(\"cmdIn\").TabIndex & \" \" & Form1.Controls(\"txtNew\").TabIndex & \" \" & Form1.Controls(\"cmdNew\").TabIndex",
            "Unload Form1");

        CollectionAssert.AreEqual(new[] { "Frame1,Command1,cmdIn,txtNew,cmdNew", "0 1 2 3 4" }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    public async Task UnloadThenLoad_LeavesOnlyTheFilesControls()
    {
        var printed = await RunAsync(
            "Load Form1",
            "Form1.AddButton",
            "Form1.Controls.Add \"VB.TextBox\", \"txtNew\"",
            "Debug.Print Form1.Controls.Count",
            "Unload Form1",
            "Load Form1",
            "Debug.Print Form1.Controls.Count & \" \" & Order()",
            "Form1.AddButton",
            "Debug.Print Order()",
            "Unload Form1");

        CollectionAssert.AreEqual(new[] { "4", "2 Frame1,Command1", "Frame1,Command1,cmdNew" }, printed, string.Join(" | ", printed));
    }
}
