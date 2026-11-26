using RDCore.SDK.Runtime.Libraries;
using System.IO.Abstractions;
using System.Runtime.Versioning;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A program that references a library runs against the real thing: the objects of a server of the machine, ADO's and Excel's, are created, called and let
/// go of through the whole pipeline. These need the servers of a Windows machine, and are skipped everywhere else.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §2.3.1.2 References")]
[OSCondition(OperatingSystems.Windows)]
[SupportedOSPlatform("windows")]
public sealed class AutomationEndToEndTests
{
    private static WorkspaceLibraries Adodb
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RDCore.slnx")))
            {
                directory = directory.Parent;
            }

            return new(["ADODB"], new DirectoryLibrarySource(new FileSystem(), Path.Combine(directory!.FullName, "Symbols")));
        }
    }

    private static Task<string[]> Run(params string[] lines)
        => RunAsync([], $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n{string.Join("\r\n", lines)}\r\nEnd Sub\r\n", Adodb);

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

    // ADO's error for a field it has not: 0x800A0CC1, of the control facility, whose number is the error's (MS-VBA's 3265).
    [TestMethod]
    public async Task TheErrorOfAServer_IsAnErrorTheProgramCanHandle()
        => CollectionAssert.AreEqual(new[] { (0x800A0CC1 & 0xFFFF).ToString(System.Globalization.CultureInfo.InvariantCulture) }, await Run(
            "Dim r As New ADODB.Recordset",
            "Dim f As ADODB.Field",
            "On Error Resume Next",
            "Set f = r.Fields(\"missing\")",
            "Debug.Print Err.Number"));

    /// <summary>
    /// A Let of an object to the <c>Value</c> of an Excel range passes the object, early bound and late bound, and Excel decides: <c>Nothing</c> is its error
    /// 1004, and another range gives its value. Skipped on a machine that does not have Excel.
    /// </summary>
    [TestMethod]
    public async Task AnObjectLetToTheValueOfARange_IsReadByExcel()
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
            "Dim app As Object",
            "Set app = CreateObject(\"Excel.Application\")",
            "On Error Resume Next",
            "Dim book As Object",
            "Set book = app.Workbooks.Add",
            "Dim sheet As Object",
            "Set sheet = book.Worksheets(1)",
            "sheet.Range(\"A1\").Value = 1",
            "sheet.Range(\"B1\").Value = Nothing",
            "Debug.Print \"late Nothing \" & Err.Number",
            "Err.Clear",
            "sheet.Range(\"C1\").Value = sheet.Range(\"A1\")",
            "Debug.Print \"late range \" & Err.Number & \" \" & sheet.Range(\"C1\").Value",
            "Err.Clear",
            "Dim early As Excel.Worksheet",
            "Set early = sheet",
            "early.Range(\"D1\").Value = Nothing",
            "Debug.Print \"early Nothing \" & Err.Number",
            "Err.Clear",
            "early.Range(\"E1\").Value = early.Range(\"A1\")",
            "Debug.Print \"early range \" & Err.Number & \" \" & early.Range(\"E1\").Value",
            "Err.Clear",
            "book.Close False",
            "app.Quit") + "\r\nEnd Sub\r\n", libraries);

        CollectionAssert.AreEqual(new[] { "late Nothing 1004", "late range 0 1", "early Nothing 1004", "early range 0 1" }, output, string.Join("\n", output));
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    /// <summary>
    /// The code of a worksheet's module, moved out of its host as RDC01005 says to move it: a class holds a sheet of an Excel the program started in a
    /// <c>WithEvents</c> variable of <c>Excel.Worksheet</c>, and its <c>AutomationSheet_Change</c> handles what <c>Worksheet_Change</c> handled. Skipped on a
    /// machine that does not have Excel. Excel is quit by the program, also when it fails; the one it started is stopped after if it did not quit, and no other.
    /// </summary>
    [TestMethod]
    public async Task ASheetOfAnExcelTheProgramStarted_HeldInAWithEventsVariable_HasItsChangesHandled()
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
        var view = ClassModule(
            "GameSheetView",
            "Private WithEvents AutomationSheet As Excel.Worksheet",
            "Public Sub Attach(ByVal sheet As Excel.Worksheet)",
            "    Set AutomationSheet = sheet",
            "End Sub",
            "Public Sub Detach()",
            "    Set AutomationSheet = Nothing",
            "End Sub",
            "Private Sub AutomationSheet_Change(ByVal Target As Excel.Range)",
            "    Debug.Print \"changed \" & Target.Address",
            "End Sub");

        string[] output = [];
        try
        {
            output = await RunAsync([("GameSheetView", view)], "Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n" + string.Join("\r\n",
                "Dim app As Excel.Application",
                "Set app = New Excel.Application",
                "Debug.Print app.Hwnd",
                "On Error GoTo Done",
                "Dim book As Excel.Workbook",
                "Set book = app.Workbooks.Add",
                "Dim view As GameSheetView",
                "Set view = New GameSheetView",
                "view.Attach book.Worksheets(1)",
                "book.Worksheets(1).Range(\"A1\").Value = 5",
                "view.Detach",
                "book.Close False",
                "Done:",
                "app.Quit") + "\r\nEnd Sub\r\n", libraries);

            CollectionAssert.AreEqual(new[] { "changed $A$1" }, output.Skip(1).ToArray(), string.Join("\n", output));
        }
        finally
        {
            StopExcel(output.FirstOrDefault());
        }
    }

    /// <summary>
    /// An Error crosses to Excel and back as MS-VBA's does, each row observed in Excel 16.0: a cell given <c>CVErr(2007)</c> shows
    /// <c>#DIV/0!</c>, an array holding <c>CVErr(2042)</c> puts <c>#N/A</c> in its cell, and a <c>#DIV/0!</c> cell reads back as an Error. Skipped on a machine
    /// that does not have Excel.
    /// </summary>
    [TestMethod]
    [DataRow(new[] { "sheet.Range(\"H1\").Value = CVErr(2007)", "Debug.Print sheet.Range(\"H1\").Text" }, "#DIV/0!", DisplayName = "CVErr(2007) into a cell")]
    [DataRow(new[] { "Dim a(1 To 1, 1 To 2) As Variant", "a(1, 1) = CVErr(2042): a(1, 2) = 1", "sheet.Range(\"H1:I1\").Value = a",
        "Debug.Print sheet.Range(\"H1\").Text & \" \" & TypeName(sheet.Range(\"I1\").Value) & \" \" & sheet.Range(\"I1\").Value" }, "#N/A Double 1",
        DisplayName = "a Variant array holding an Error, into a range")]
    [DataRow(new[] { "sheet.Range(\"D5\").Formula = \"=1/0\"", "Dim v As Variant", "v = sheet.Range(\"D5\").Value", "Debug.Print IsError(v) & \" \" & CLng(v)" },
        "True 2007",
        IgnoreMessage = "a #DIV/0! cell read back: RD-VBA reads the Long -2146826281 where MS-VBA reads Error 2007, because the .NET late binder turns a VT_ERROR result into a number and RD-VBA does not call IDispatch itself",
        DisplayName = "a #DIV/0! cell read back")]
    public async Task AnError_CrossesToExcelAndBack_AsMSVBAsDoes(string[] lines, string expected)
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
        string[] output = [];
        try
        {
            output = await RunAsync([], "Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n" + string.Join("\r\n", [
                "Dim app As Excel.Application",
                "Set app = New Excel.Application",
                "Debug.Print app.Hwnd",
                "On Error GoTo Done",
                "Dim book As Excel.Workbook",
                "Set book = app.Workbooks.Add",
                "Dim sheet As Excel.Worksheet",
                "Set sheet = book.Worksheets(1)",
                .. lines,
                "book.Close False",
                "Done:",
                "If Err.Number <> 0 Then Debug.Print \"error \" & Err.Number",
                "app.Quit"]) + "\r\nEnd Sub\r\n", libraries);

            CollectionAssert.AreEqual(new[] { expected }, output.Skip(1).ToArray(), string.Join("\n", output));
        }
        finally
        {
            StopExcel(output.FirstOrDefault());
        }
    }

    // the Excel a test started, by its window, if it is still there once the program quit it: never another, which another program may be using.
    private static void StopExcel(string? hwnd)
    {
        if (hwnd is null || !long.TryParse(hwnd.Trim(), out var handle) || GetWindowThreadProcessId(new IntPtr(handle), out var processId) == 0 || processId == 0)
        {
            return;
        }

        try
        {
            using var excel = System.Diagnostics.Process.GetProcessById((int)processId);
            if (!excel.WaitForExit(10_000) && excel.ProcessName.Equals("EXCEL", StringComparison.OrdinalIgnoreCase))
            {
                excel.Kill();
            }
        }
        catch (ArgumentException)
        {
            // it has quit.
        }
    }
}
