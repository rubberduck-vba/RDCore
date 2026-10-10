using RDCore.SDK.Runtime.Libraries;
using System.IO.Abstractions;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A program that references a library runs against the real thing: the objects of <c>Scripting</c> are the machine's own, created, called and let go of
/// through the whole pipeline. These need the servers of a Windows machine, and are skipped everywhere else.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §2.3.1.2 References")]
[OSCondition(OperatingSystems.Windows)]
public sealed class AutomationEndToEndTests
{
    private static WorkspaceLibraries Scripting
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RDCore.slnx")))
            {
                directory = directory.Parent;
            }

            return new(["Scripting"], new DirectoryLibrarySource(new FileSystem(), Path.Combine(directory!.FullName, "Symbols")));
        }
    }

    private static Task<string[]> Run(params string[] lines)
        => RunAsync([], $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n{string.Join("\r\n", lines)}\r\nEnd Sub\r\n", Scripting);

    [TestMethod]
    public async Task ADictionary_IsCreated_AddedTo_AndRead()
        => CollectionAssert.AreEqual(new[] { "2", "10", "True", "False" }, await Run(
            "Dim d As New Scripting.Dictionary",
            "d.Add \"a\", 10",
            "d.Add \"b\", 20",
            "Debug.Print d.Count",
            "Debug.Print d.Item(\"a\")",
            "Debug.Print d.Exists(\"b\")",
            "Debug.Print d.Exists(\"c\")"));

    // the key and the item are Variants passed by reference, given the loop's Long: the dictionary holds Longs (MS-VBAL §5.3.1.11).
    [TestMethod]
    public async Task ADictionary_IsFilledWithTheVariablesOfALoop()
        => CollectionAssert.AreEqual(new[] { "3", "6" }, await Run(
            "Dim d As New Scripting.Dictionary",
            "Dim i As Long",
            "Dim s As Long",
            "For i = 1 To 3",
            "d.Add i, i",
            "Next",
            "For i = 1 To 3",
            "s = s + d.Item(i)",
            "Next",
            "Debug.Print d.Count",
            "Debug.Print s"));

    /// <summary>
    /// An event Excel raises while a call is made is handled inside the call: the handler of a workbook's <c>BeforeClose</c> cancels it by setting an argument Excel passed
    /// by reference, and Excel finds it set when the handler returns. Skipped on a machine that does not have Excel.
    /// </summary>
    [TestMethod]
    public async Task AnEventOfExcel_IsHandledByTheProgram_AndTheHandlerAnswersIt()
    {
        if (Type.GetTypeFromProgID("Excel.Application") is null)
        {
            Assert.Inconclusive("Excel is not installed on this machine.");
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RDCore.slnx")))
        {
            directory = directory.Parent;
        }

        var libraries = new WorkspaceLibraries(["Excel"], new DirectoryLibrarySource(new FileSystem(), Path.Combine(directory!.FullName, "Symbols")));
        var watcher = ClassModule(
            "Watcher",
            "Private WithEvents Book As Excel.Workbook",
            "Private WithEvents App As Excel.Application",
            "Public Sub Watch(ByVal workbook As Excel.Workbook, ByVal application As Excel.Application)",
            "    Set Book = workbook",
            "    Set App = application",
            "End Sub",
            "Public Sub Unwatch()",
            "    Set Book = Nothing",
            "    Set App = Nothing",
            "End Sub",
            "Private Sub Book_BeforeClose(Cancel As Boolean)",
            "    Debug.Print \"closing\"",
            "    Cancel = True",
            "End Sub",
            "Private Sub App_NewWorkbook(ByVal Wb As Excel.Workbook)",
            "    Debug.Print \"new workbook \" & Wb.Worksheets.Count",
            "End Sub");

        var output = await RunAsync([("Watcher", watcher)], "Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n" + string.Join("\r\n",
            "Dim app As New Excel.Application",
            "app.SheetsInNewWorkbook = 2",
            "Dim book As Excel.Workbook",
            "Set book = app.Workbooks.Add",
            "Dim spy As New Watcher",
            "spy.Watch book, app",
            "Dim other As Excel.Workbook",
            "Set other = app.Workbooks.Add",
            "book.Close False",
            "Debug.Print app.Workbooks.Count",
            "spy.Unwatch",
            "book.Close False",
            "other.Close False",
            "Debug.Print app.Workbooks.Count",
            "app.Quit") + "\r\nEnd Sub\r\n", libraries);

        // the new workbook was announced, the close was asked for and cancelled by the handler (so both workbooks are still open), and once the variables let go
        // of Excel's objects the close goes through.
        CollectionAssert.AreEqual(new[] { "new workbook 2", "closing", "2", "0" }, output);
    }

    /// <summary>
    /// The object model of a host application, from the sidelines: an Excel of its own is started, a workbook is made, cells are written and read, an enumeration
    /// member is an argument, and Excel is let go of. Skipped on a machine that does not have Excel.
    /// </summary>
    [TestMethod]
    public async Task Excel_IsAutomated_FromTheSidelines()
    {
        if (Type.GetTypeFromProgID("Excel.Application") is null)
        {
            Assert.Inconclusive("Excel is not installed on this machine.");
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RDCore.slnx")))
        {
            directory = directory.Parent;
        }

        var libraries = new WorkspaceLibraries(["Excel"], new DirectoryLibrarySource(new FileSystem(), Path.Combine(directory!.FullName, "Symbols")));
        var output = await RunAsync([], "Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n" + string.Join("\r\n",
            "Dim app As New Excel.Application",
            "Dim book As Excel.Workbook",
            "app.SheetsInNewWorkbook = 3",
            "Set book = app.Workbooks.Add",
            "Dim sheet As Excel.Worksheet",
            "Set sheet = book.Worksheets(1)",
            "sheet.Range(\"A1\").Value = 42",
            "sheet.Range(\"A2\").Value = \"hi\"",
            "Debug.Print sheet.Range(\"A1\").Value + 1",
            "Debug.Print sheet.Cells(2, 1).Value",
            "Debug.Print sheet.Range(\"A1\").End(xlDown).Address",
            "Dim cell As Excel.Range",
            "For Each cell In sheet.Range(\"A1:A2\")",
            "Debug.Print cell.Address",
            "Next",
            "Dim page As Excel.Worksheet",
            "For Each page In book.Worksheets",
            "Debug.Print page.Index",
            "Next",
            "book.Close False",
            "app.Quit") + "\r\nEnd Sub\r\n", libraries);

        CollectionAssert.AreEqual(new[] { "43", "hi", "$A$2", "$A$1", "$A$2", "1", "2", "3" }, output);
    }

    [TestMethod]
    public async Task ForEach_OverADictionary_VisitsItsKeys_AndOverItsKeysAndItems_ItsArrays()
        => CollectionAssert.AreEqual(new[] { "a", "b", "a", "b", "1", "2" }, await Run(
            "Dim d As New Scripting.Dictionary",
            "d.Add \"a\", 1",
            "d.Add \"b\", 2",
            "Dim v As Variant",
            "For Each v In d",
            "Debug.Print v",
            "Next",
            "For Each v In d.Keys",
            "Debug.Print v",
            "Next",
            "For Each v In d.Items",
            "Debug.Print v",
            "Next"));

    [TestMethod]
    public async Task ADictionary_IsCreatedByNew_AndAnItemIsAssigned()
        => CollectionAssert.AreEqual(new[] { "7" }, await Run(
            "Dim d As Scripting.Dictionary",
            "Set d = New Scripting.Dictionary",
            "d.Item(\"k\") = 7",
            "Debug.Print d(\"k\")"));

    [TestMethod]
    public async Task TheErrorOfAServer_IsAnErrorTheProgramCanHandle()
        => CollectionAssert.AreEqual(new[] { "457" }, await Run(
            "Dim d As New Scripting.Dictionary",
            "d.Add \"a\", 1",
            "On Error Resume Next",
            "d.Add \"a\", 2",
            "Debug.Print Err.Number"));
}
