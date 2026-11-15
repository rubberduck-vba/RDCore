using RDCore.SDK.Workspace;
using RDCore.SDK.Workspace.Forms;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Workspace.Forms;

/// <summary>
/// The <c>Value</c> of a Visual Basic 6.0 <c>CommandButton</c>, its default member: set to <c>True</c>, it raises the button's <c>Click</c> at once, whether or
/// not the button is enabled or shown; <c>Click</c> reads it <c>True</c>, and it is <c>False</c> again after. The value assigned is let-coerced to <c>Boolean</c>.
/// </summary>
[TestClass]
public sealed class VisualBasicFormCommandButtonValueTests
{
    // Command1's Click prints its Value; a Click raised again from inside it nests, to a depth of two.
    private static string Form1() => string.Join("\r\n", [
        "VERSION 5.00",
        "Begin VB.Form Form1 ",
        "   Caption         =   \"Form1\"",
        "   ClientHeight    =   3195",
        "   ClientWidth     =   4680",
        "   Begin VB.CommandButton Command1 ",
        "      Caption         =   \"Command1\"",
        "      TabIndex        =   0",
        "   End",
        "End",
        "Attribute VB_Name = \"Form1\"",
        "Attribute VB_GlobalNameSpace = False",
        "Attribute VB_Creatable = False",
        "Attribute VB_PredeclaredId = True",
        "Attribute VB_Exposed = False",
        "Option Explicit",
        "Public Nest As Boolean",
        "Private depth As Integer",
        "Private Sub Command1_Click()",
        "    depth = depth + 1",
        "    Debug.Print \"Command1_Click \" & depth & \" \" & Command1.Value",
        "    If Nest And depth < 2 Then Command1.Value = True",
        "    Debug.Print \"after \" & depth",
        "    depth = depth - 1",
        "End Sub",
        string.Empty]);

    private static ModuleWorkspace.WorkspaceLibraries Libraries
        => new ModuleWorkspace.WorkspaceLibraries([], VisualBasicFormLifecycleTests.Shipped) with { Outside = VisualBasicFormLifecycleTests.RDFormsWorld() };

    private static Task<string[]> RunAsync(params string[] body)
    {
        var form1 = Form1();
        var program = string.Join("\r\n", [
            "Attribute VB_Name = \"Program\"",
            "Public Sub Main()",
            .. body.Select(line => "    " + line),
            "    Debug.Print \"Value \" & Form1.Command1.Value",
            "    Unload Form1",
            "End Sub",
            string.Empty]);
        return ModuleWorkspace.RunAsync([], program, Libraries, [("Form1", form1, FormDesigner.Read(form1)!)], SupportedLanguages.VB6);
    }

    [TestMethod]
    [DataRow("Form1.Command1.Value = True", DisplayName = "Value = True")]
    [DataRow("Form1.Command1.Value = 5", DisplayName = "a number, not zero")]
    [DataRow("Dim o As Object: Set o = Form1.Command1: o.Value = True", DisplayName = "late-bound")]
    public async Task ValueSetToTrue_RaisesClick_AndIsFalseAfter(string set)
    {
        var printed = await RunAsync("Load Form1", set);

        CollectionAssert.AreEqual(new[] { "Command1_Click 1 True", "after 1", "Value False" }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    [DataRow("Form1.Command1 = True", DisplayName = "early-bound")]
    [DataRow("Dim o As Object: Set o = Form1.Command1: o = True", DisplayName = "late-bound")]
    public async Task TheDefaultMemberSetToTrue_RaisesClick(string set)
    {
        var printed = await RunAsync("Load Form1", set);

        CollectionAssert.AreEqual(new[] { "Command1_Click 1 True", "after 1", "Value False" }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    [DataRow("Form1.Command1.Enabled = False", DisplayName = "disabled")]
    [DataRow("Form1.Show: Form1.Command1.Visible = False", DisplayName = "hidden")]
    [DataRow("Form1.Show: DoEvents", DisplayName = "shown")]
    public async Task ValueSetToTrue_RaisesClick_WhateverTheButtonsState(string state)
    {
        var printed = await RunAsync("Load Form1", state, "Form1.Command1.Value = True");

        CollectionAssert.AreEqual(new[] { "Command1_Click 1 True", "after 1", "Value False" }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    public async Task ValueSetToFalse_RaisesNothing()
    {
        var printed = await RunAsync("Load Form1", "Form1.Command1.Value = False");

        CollectionAssert.AreEqual(new[] { "Value False" }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    public async Task ValueSetToTrue_InsideItsClick_RaisesClickAgain_Nested()
    {
        var printed = await RunAsync("Load Form1", "Form1.Nest = True", "Form1.Command1.Value = True");

        CollectionAssert.AreEqual(new[] { "Command1_Click 1 True", "Command1_Click 2 True", "after 2", "after 1", "Value False" }, printed,
            string.Join(" | ", printed));
    }

    [TestMethod]
    public async Task ValueSetToAStringThatIsNotANumber_IsTypeMismatch()
    {
        var printed = await RunAsync("Load Form1", "On Error Resume Next", "Form1.Command1.Value = \"x\"", "Debug.Print Err.Number", "On Error GoTo 0");

        CollectionAssert.AreEqual(new[] { "13", "Value False" }, printed, string.Join(" | ", printed));
    }
}
