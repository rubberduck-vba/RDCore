using RDCore.External;
using RDCore.Tests.Cli;
using RDCore.Tests.External;
using System.Runtime.Versioning;

namespace RDCore.Tests.Workspace.Forms;

/// <summary>
/// A UserForm, hosted by Microsoft Forms, in an environment that refuses automation: the form, its controls of any library and what they hand out of their own
/// library are the program's own user interface, and run, although they are COM objects of the machine's; any other object is refused with error 70.
/// Inconclusive on a machine without Microsoft Forms.
/// </summary>
[TestClass]
[TestCategory("MSForms")]
[DoNotParallelize]
// Windows only past RequireMicrosoftForms(), which is inconclusive on any other platform.
[SupportedOSPlatform("windows")]
public sealed class UserFormAutomationPolicyTests
{
    private static string Program(string[] body)
        => $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n";

    private static ModuleWorkspace.WorkspaceLibraries Libraries(ExternalWorld world)
        => new(["MSForms"], FormHostingTests.Shipped, AllowAutomation: false, Outside: world);

    private static async Task<string[]> RunAsync(bool external, params string[] body)
    {
        FormHostingTests.RequireMicrosoftForms();
        var (world, apartment) = FormHostingTests.InProcess();
        using var _ = apartment;
        using var host = ExternalHosts.New();
        return await ModuleWorkspace.RunAsync([], Program(body), Libraries(external ? ExternalHosts.WorldOf(host) : world), [FormHostingTests.ExampleView()]);
    }

    [TestMethod]
    [DataRow(false, DisplayName = "in the process")]
    [DataRow(true, DisplayName = "through an external host")]
    public async Task TheFormsOwnMembers_Run(bool external)
    {
        var printed = await RunAsync(external,
            "ExampleView.Caption = \"Changed\"",
            "Debug.Print ExampleView.Caption",
            "ExampleView.Show vbModeless",
            "Debug.Print ExampleView.Visible",
            "ExampleView.Hide",
            "Unload ExampleView",
            "Debug.Print Err.Number");

        CollectionAssert.AreEqual(new[] { "Changed", "True", "0" }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    [DataRow(false, DisplayName = "in the process")]
    [DataRow(true, DisplayName = "through an external host")]
    public async Task TheDesignersControls_Run(bool external)
    {
        var printed = await RunAsync(external,
            "ExampleView.AmountBox.Text = \"12\"",
            "Debug.Print ExampleView.AmountBox.Text",
            "Unload ExampleView");

        CollectionAssert.AreEqual(new[] { "12" }, printed, string.Join(" | ", printed));
    }

    // ExampleView has 13 controls of its designer's.
    [TestMethod]
    [DataRow(false, DisplayName = "in the process")]
    [DataRow(true, DisplayName = "through an external host")]
    public async Task WhatAFormHandsOutOfItsOwnLibrary_Runs(bool external)
    {
        var printed = await RunAsync(external,
            "Debug.Print ExampleView.Controls.Count",
            "Dim c As Object",
            "Set c = ExampleView.Controls.Add(\"Forms.Label.1\", \"lblNew\")",
            "c.Caption = \"added\"",
            "Debug.Print ExampleView.Controls(\"lblNew\").Caption",
            "Dim ctl As Object, n As Long",
            "For Each ctl In ExampleView.Controls",
            "    If Len(ctl.Name) > 0 Then n = n + 1",
            "Next",
            "Debug.Print n",
            "Unload ExampleView");

        CollectionAssert.AreEqual(new[] { "13", "added", "14" }, printed, string.Join(" | ", printed));
    }

    // the form's Font is of stdole, not of Microsoft Forms: an ordinary object, whose members are refused, and whose value a form's property cannot be given.
    [TestMethod]
    public async Task AnObjectOfAnotherLibraryAFormHandsOut_IsRefused()
    {
        var printed = await RunAsync(false,
            "Dim f As Object",
            "Set f = ExampleView.Font",
            "On Error Resume Next",
            "Debug.Print f.Name",
            "Debug.Print Err.Number",
            "Err.Clear",
            "ExampleView.Caption = f",
            "Debug.Print Err.Number",
            "Err.Clear",
            "Debug.Print ExampleView.Caption",
            "Unload ExampleView");

        CollectionAssert.AreEqual(new[] { "70", "70", "ExampleView" }, printed, string.Join(" | ", printed));
    }

    // a control of another library that the form makes is the form's own, as a control its designer declares is.
    [TestMethod]
    public async Task AControlOfAnotherLibraryAdded_Runs()
    {
        var printed = await RunAsync(false,
            "On Error Resume Next",
            "ExampleView.Controls.Add \"Shell.Explorer.2\", \"web\"",
            "Debug.Print Err.Number",
            "Debug.Print ExampleView.Controls.Count",
            "Unload ExampleView");

        CollectionAssert.AreEqual(new[] { "0", "14" }, printed, string.Join(" | ", printed));
    }

    // a class only a server has, since the platform's own classes (Scripting.Dictionary) are made where no automation is allowed.
    [TestMethod]
    [DataRow("Set d = CreateObject(\"ADODB.Connection\")", DisplayName = "created")]
    [DataRow("Set d = GetObject(, \"ADODB.Connection\")", DisplayName = "obtained")]
    [DataRow("Set d = New MSForms.DataObject", DisplayName = "a class of Microsoft Forms created")]
    public async Task AnObjectOfAnotherClass_IsRefused_BesideAForm(string made)
    {
        var printed = await RunAsync(false,
            "Debug.Print ExampleView.Caption",
            "Dim d As Object",
            "On Error Resume Next",
            made,
            "Debug.Print Err.Number",
            "Debug.Print d Is Nothing",
            "Unload ExampleView");

        CollectionAssert.AreEqual(new[] { "ExampleView", "70", "True" }, printed, string.Join(" | ", printed));
    }

    // End forgets the form with the program: the program run again has a new one, the designer's, whose members run as the first one's did.
    [TestMethod]
    public async Task AfterEnd_TheFormOfTheProgramRunAgain_Runs()
    {
        FormHostingTests.RequireMicrosoftForms();
        var (world, apartment) = FormHostingTests.InProcess();
        using var _ = apartment;

        string[]? first = null;
        string[]? again = null;
        await ModuleWorkspace.InspectAsync([], Program([
                "Debug.Print ExampleView.AmountBox.Text = \"\"",
                "ExampleView.AmountBox.Text = \"12\"",
                "Debug.Print ExampleView.AmountBox.Text",
                "End"]),
            async (_, result, runAgain) =>
            {
                first = [.. result.Output.Select(line => line.Trim())];
                again = [.. (await runAgain()).Output.Select(line => line.Trim())];
            },
            libraries: Libraries(world), forms: [FormHostingTests.ExampleView()]);

        CollectionAssert.AreEqual(new[] { "True", "12" }, first, string.Join(" | ", first!));
        CollectionAssert.AreEqual(new[] { "True", "12" }, again, string.Join(" | ", again!));
    }
}
