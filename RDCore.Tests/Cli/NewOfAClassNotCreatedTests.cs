using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Libraries;
using RDCore.Tests.Runtime.Libraries;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §5.6.8</strong> and <strong>§5.2.3.1.4</strong>: <c>New</c> of a class its library says can be created compiles, and one it does not say it
/// of - an interface, a class with no <c>TYPEFLAG_FCANCREATE</c> - is an invalid use of <c>New</c>. <c>ErrObject</c> is one the library says can be created and
/// nothing creates: creating one raises run-time error -2147221164 <em>Class not registered</em>. Each expectation is what MS-VBA did with the same code,
/// observed in Excel.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.6.8 New Expressions")]
public sealed class NewOfAClassNotCreatedTests
{
    // a library with a class that can be created and one that cannot, as a host's object model has (Office's CommandBar and CommandBars).
    private static readonly LibraryDescription Shop = new()
    {
        Name = "Shop",
        Classes =
        [
            new ClassDescription { Name = "Basket", ProgId = "Shop.Basket", IsCreatable = true },
            new ClassDescription { Name = "Receipt", IsCreatable = false },
        ],
    };

    private static string Program(params string[] body) => string.Join("\r\n",
    [
        "Attribute VB_Name = \"Program\"",
        "Option Explicit",
        "Public Sub Main()",
        "    On Error Resume Next",
        .. body,
        "End Sub",
    ]);

    private static async Task AssertPrintsAsync(string[] expected, string measured, VBExecutionEngine engine, params string[] body)
    {
        var printed = await ModuleWorkspace.RunAsync([], Program(body), engine);
        CollectionAssert.AreEqual(expected, printed, $"{measured}: {string.Join(" | ", printed)}");
    }

    private static async Task AssertInvalidUseOfNewAsync(string measured, params string[] body)
    {
        var errors = await ModuleWorkspace.LoadErrorsAsync([], Program(body), new WorkspaceLibraries(["Shop"], new InMemoryLibrarySource(Shop)));
        Assert.HasCount(1, errors, $"{measured}: {string.Join(" / ", errors)}");
        StringAssert.Contains(errors[0], "Invalid use of New keyword", measured);
    }

    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter)]
    [DataRow(VBExecutionEngine.Compiled)]
    public async Task NewErrObject_Compiles_AndRaisesClassNotRegistered_LeavingTheVariableNothing(VBExecutionEngine engine)
        => await AssertPrintsAsync(["e-2147221164", "True", "Project1", "True"], "s62 X04", engine,
            "Dim e As VBA.ErrObject", "Set e = New VBA.ErrObject", "Debug.Print \"e\" & Err.Number",
            "Debug.Print (Err.Description = \"Class not registered\" & vbCrLf)", "Debug.Print Err.Source", "Debug.Print (e Is Nothing)");

    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter)]
    [DataRow(VBExecutionEngine.Compiled)]
    public async Task NewErrObject_RaisesAnErrorWhoseHelpContextIsMSVBAs(VBExecutionEngine engine)
        => await AssertPrintsAsync(["e-2147221164", "1000440"], "s62 X12", engine,
            "Dim e As ErrObject", "Set e = New ErrObject", "Debug.Print \"e\" & Err.Number", "Debug.Print Err.HelpContext");

    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter)]
    [DataRow(VBExecutionEngine.Compiled)]
    public async Task NewErrObject_SetToAnObject_LeavesItNothing(VBExecutionEngine engine)
        => await AssertPrintsAsync(["Nothing", "e-2147221164"], "s62 X08", engine,
            "Dim x As Object", "Set x = New ErrObject", "Debug.Print TypeName(x)", "Debug.Print \"e\" & Err.Number");

    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter)]
    [DataRow(VBExecutionEngine.Compiled)]
    public async Task AVariableDeclaredAsNewErrObject_SetToNothing_CreatesNothing(VBExecutionEngine engine)
        => await AssertPrintsAsync(["a", "b0"], "s62 X06", engine,
            "Dim x As New ErrObject", "Debug.Print \"a\"", "Set x = Nothing", "Debug.Print \"b\" & Err.Number");

    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter)]
    [DataRow(VBExecutionEngine.Compiled)]
    public async Task AVariableDeclaredAsNewErrObject_RaisesClassNotRegistered_WhenItIsReferredTo(VBExecutionEngine engine)
        => await AssertPrintsAsync(["e-2147221164", "True"], "s62 X07", engine,
            "Dim x As New ErrObject", "Debug.Print (x Is Nothing)", "Debug.Print \"e\" & Err.Number",
            "Debug.Print (Err.Description = \"Class not registered\" & vbCrLf)");

    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter)]
    [DataRow(VBExecutionEngine.Compiled)]
    public async Task AVariableDeclaredAsNewErrObject_RaisesClassNotRegistered_AtEachReference(VBExecutionEngine engine)
        => await AssertPrintsAsync(["e-2147221164", "f-2147221164"], "s62 X09", engine,
            "Dim x As New ErrObject", "Debug.Print x.Number", "Debug.Print \"e\" & Err.Number", "Err.Clear", "Debug.Print x.Number", "Debug.Print \"f\" & Err.Number");

    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter)]
    [DataRow(VBExecutionEngine.Compiled)]
    public async Task ACollectionDeclaredAsNew_IsCreated(VBExecutionEngine engine)
        => await AssertPrintsAsync(["0", "e0"], "s62 X11", engine, "Dim c As New Collection", "Debug.Print c.Count", "Debug.Print \"e\" & Err.Number");

    [TestMethod]
    public async Task NewOfAClassTheLibraryDoesNotSayCanBeCreated_IsAnInvalidUseOfNew()
    {
        await AssertInvalidUseOfNewAsync("s63 Y03", "Dim m As VBA.Match", "Set m = New VBA.Match");
        await AssertInvalidUseOfNewAsync("s63 Y08", "Dim x As New VBA.SubMatches");
        await AssertInvalidUseOfNewAsync("s62 X01, X05, s63 Y01, Y04", "Dim r As Receipt", "Set r = New Receipt");
        await AssertInvalidUseOfNewAsync("s62 X10, s63 Y02", "Dim r As New Receipt");
    }

    [TestMethod]
    public async Task NewOfAClassTheLibrarySaysCanBeCreated_Compiles()
        => Assert.IsEmpty(await ModuleWorkspace.LoadErrorsAsync([], Program(
            "Dim e As ErrObject", "Set e = New ErrObject", "Dim x As New ErrObject", "Dim b As Basket", "Set b = New Basket", "Dim c As New Basket"),
            new WorkspaceLibraries(["Shop"], new InMemoryLibrarySource(Shop))));
}
