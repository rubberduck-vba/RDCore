using RDCore.SDK.Workspace;
using RDCore.SDK.Workspace.Forms;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Workspace.Forms;

/// <summary>
/// A Visual Basic 6.0 form, hosted by RDForms headless, in an environment that refuses automation: the form, its designer's controls and what they hand out of
/// the VB library are the program's own user interface, and run; creating or obtaining any other object is refused with error 70.
/// </summary>
[TestClass]
public sealed class VisualBasicFormAutomationPolicyTests
{
    // Label1 and Command1, in the file's order.
    private static string Form1() => string.Join("\r\n", [
        "VERSION 5.00",
        "Begin VB.Form Form1 ",
        "   Caption         =   \"Form1\"",
        "   ClientHeight    =   3195",
        "   Begin VB.Label Label1 ",
        "      Caption         =   \"Label1\"",
        "      Height          =   255",
        "      Left            =   120",
        "      Top             =   720",
        "      Width           =   1215",
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
        string.Empty]);

    private static ModuleWorkspace.WorkspaceLibraries Libraries
        => new ModuleWorkspace.WorkspaceLibraries([], VisualBasicFormLifecycleTests.Shipped, AllowAutomation: false)
        {
            Outside = VisualBasicFormLifecycleTests.RDFormsWorld(),
        };

    private static string Program(string[] body) => string.Join("\r\n", [
        "Attribute VB_Name = \"Program\"",
        "Public Sub Main()",
        .. body,
        "End Sub",
        string.Empty]);

    private static (string, string, FormDescriptor)[] Forms()
    {
        var form1 = Form1();
        return [("Form1", form1, FormDesigner.Read(form1)!)];
    }

    private static Task<string[]> RunAsync(params string[] body)
        => ModuleWorkspace.RunAsync([], Program(body), Libraries, Forms(), SupportedLanguages.VB6);

    [TestMethod]
    public async Task TheFormsOwnMembers_Run()
    {
        var printed = await RunAsync(
            "Form1.Caption = \"Changed\"",
            "Debug.Print Form1.Caption",
            "Form1.Hide",
            "Debug.Print Form1.Visible",
            "Unload Form1",
            "Debug.Print Err.Number");

        CollectionAssert.AreEqual(new[] { "Changed", "False", "0" }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    public async Task TheDesignersControls_Run()
    {
        var printed = await RunAsync(
            "Form1.Label1.Caption = \"set\"",
            "Debug.Print Form1.Label1.Caption",
            "Debug.Print Form1.Command1.Caption",
            "Unload Form1");

        CollectionAssert.AreEqual(new[] { "set", "Command1" }, printed, string.Join(" | ", printed));
    }

    // the Controls collection is of no library's class, and the controls it holds and adds are of VB's.
    [TestMethod]
    public async Task WhatAFormHandsOutOfItsOwnLibrary_Runs()
    {
        var printed = await RunAsync(
            "Debug.Print Form1.Controls.Count",
            "Dim c As Object",
            "Set c = Form1.Controls.Add(\"VB.Label\", \"lblNew\")",
            "c.Caption = \"added\"",
            "Debug.Print Form1.Controls(\"lblNew\").Caption",
            "Dim ctl As Object",
            "For Each ctl In Form1.Controls",
            "    Debug.Print ctl.Name",
            "Next",
            "Unload Form1");

        CollectionAssert.AreEqual(new[] { "2", "added", "Label1", "Command1", "lblNew" }, printed, string.Join(" | ", printed));
    }

    // a form's property can hold nothing automation made: the object to store is refused where it is made. The class is one only a server has.
    [TestMethod]
    public async Task AnObjectStoredInAFormsProperty_IsRefused()
    {
        var printed = await RunAsync(
            "On Error Resume Next",
            "Form1.Tag = CreateObject(\"ADODB.Connection\")",
            "Debug.Print Err.Number",
            "Debug.Print Len(Form1.Tag)",
            "Unload Form1");

        CollectionAssert.AreEqual(new[] { "70", "0" }, printed, string.Join(" | ", printed));
    }

    // a class only a server has, since the platform's own classes (Scripting.Dictionary) are made where no automation is allowed.
    [TestMethod]
    [DataRow("Set d = CreateObject(\"ADODB.Connection\")", DisplayName = "created")]
    [DataRow("Set d = GetObject(, \"ADODB.Connection\")", DisplayName = "obtained")]
    public async Task AnObjectOfAnotherClass_IsRefused_BesideAForm(string made)
    {
        var printed = await RunAsync(
            "Debug.Print Form1.Caption",
            "Dim d As Object",
            "On Error Resume Next",
            made,
            "Debug.Print Err.Number",
            "Debug.Print d Is Nothing",
            "Unload Form1");

        CollectionAssert.AreEqual(new[] { "Form1", "70", "True" }, printed, string.Join(" | ", printed));
    }

    // End forgets the form with the program: the program run again has a new one, the designer's, whose members run as the first one's did.
    [TestMethod]
    public async Task AfterEnd_TheFormOfTheProgramRunAgain_Runs()
    {
        string[]? again = null;
        string[]? first = null;
        await ModuleWorkspace.InspectAsync([], Program([
                "Debug.Print Form1.Label1.Caption",
                "Form1.Label1.Caption = \"set\"",
                "Debug.Print Form1.Label1.Caption",
                "End"]),
            async (_, result, runAgain) =>
            {
                first = [.. result.Output.Select(line => line.Trim())];
                again = [.. (await runAgain()).Output.Select(line => line.Trim())];
            },
            libraries: Libraries, language: SupportedLanguages.VB6, forms: Forms());

        CollectionAssert.AreEqual(new[] { "Label1", "set" }, first, string.Join(" | ", first!));
        CollectionAssert.AreEqual(new[] { "Label1", "set" }, again, string.Join(" | ", again!));
    }
}
