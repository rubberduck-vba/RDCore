namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §3.4</strong> conditional compilation, as the platform runs it: a module's own <c>#Const</c> directives bind constants its conditions
/// can name, and a condition that is not a number is an error of the module rather than a branch nobody can choose.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 3.4 Conditional Compilation")]
public sealed class ConditionalCompilationTests
{
    private static string Module(params string[] lines)
        => $"Attribute VB_Name = \"Program\"\r\n{string.Join("\r\n", lines)}\r\n";

    private static string Chosen(params string[] directives)
        => Module([.. directives, "Public Sub Main()", "#If Flag Then", "Debug.Print \"yes\"", "#Else", "Debug.Print \"no\"", "#End If", "End Sub"]);

    [TestMethod]
    [DataRow("#Const Flag = 1", "yes")]
    [DataRow("#Const Flag = -1", "yes")]
    [DataRow("#Const Flag = 0", "no")]
    [DataRow("#Const Flag = True", "yes")]
    [DataRow("#Const Flag = False", "no")]
    [DataRow("#Const Flag = 2 - 2", "no")]
    [DataRow("#Const Flag% = 1", "yes")]
    public async Task AModulesConst_ChoosesTheBranch(string directive, string expected)
        => CollectionAssert.AreEqual(new[] { expected }, await ModuleWorkspace.RunAsync([], Chosen(directive)));

    [TestMethod]
    public async Task AnUndefinedConstant_IsZero_NotAnError()
        => CollectionAssert.AreEqual(new[] { "no" }, await ModuleWorkspace.RunAsync([], Chosen()));

    [TestMethod]
    public async Task AConst_BindsInTheWholeModule_WhereverItIsWritten()
    {
        var source = Module("Public Sub Main()", "#If Flag Then", "Debug.Print \"yes\"", "#Else", "Debug.Print \"no\"", "#End If", "End Sub", "#Const Flag = 1");

        CollectionAssert.AreEqual(new[] { "yes" }, await ModuleWorkspace.RunAsync([], source));
    }

    [TestMethod]
    public async Task AConst_IsProcessedInAnExcludedBlockToo()
    {
        // §3.4.1: "All <cc-const> directives are processed including those contained in excluded blocks".
        var source = Chosen("#If 0 Then", "#Const Flag = 1", "#End If");

        CollectionAssert.AreEqual(new[] { "yes" }, await ModuleWorkspace.RunAsync([], source));
    }

    [TestMethod]
    public async Task AConst_CanNameAnother()
    {
        var source = Chosen("#Const First = 2", "#Const Flag = First + 1 = 3");

        CollectionAssert.AreEqual(new[] { "yes" }, await ModuleWorkspace.RunAsync([], source));
    }

    [TestMethod]
    public async Task AConstThatNamesItself_IsWhatTheNameMeansWithoutIt()
    {
        // it has nothing else to be: an undefined constant is 0.
        var source = Chosen("#Const Flag = Flag + 1");

        CollectionAssert.AreEqual(new[] { "yes" }, await ModuleWorkspace.RunAsync([], source));
    }

    [TestMethod]
    public async Task AModulesConst_ShadowsTheProjectLevelOneOfTheSameName()
    {
        // Win64 is -1 in the environment these tests are composed in.
        var source = Module(
            "#Const Win64 = 0", "Public Sub Main()", "#If Win64 Then", "Debug.Print \"project\"", "#Else", "Debug.Print \"module\"", "#End If", "End Sub");

        CollectionAssert.AreEqual(new[] { "module" }, await ModuleWorkspace.RunAsync([], source));
    }

    [TestMethod]
    public async Task ATwiceDeclaredConst_IsADuplicateDeclaration()
    {
        var errors = await ModuleWorkspace.LoadErrorsAsync([], Chosen("#Const Flag = 1", "#Const FLAG = 0"));

        Assert.HasCount(1, errors);
        StringAssert.Contains(errors[0], "declared more than once");
    }

    [TestMethod]
    [DataRow("#If Nothing Then", "Invalid use of object")]
    [DataRow("#If Empty Then", "Type mismatch")]
    [DataRow("#If \"a\" Then", "Type mismatch")]
    [DataRow("#If Null Then", "Type mismatch")]
    public async Task AConditionThatIsNotANumberOrABoolean_IsACompileError(string condition, string error)
    {
        var errors = await ModuleWorkspace.LoadErrorsAsync([], Module("Public Sub Main()", condition, "Debug.Print \"x\"", "#End If", "End Sub"));

        Assert.HasCount(1, errors);
        StringAssert.Contains(errors[0], error);
    }

    [TestMethod]
    public async Task AConstThatIsNotANumber_IsACompileError_AtTheConst()
    {
        var errors = await ModuleWorkspace.LoadErrorsAsync([], Chosen("#Const Flag = Nothing"));

        Assert.IsNotEmpty(errors);
        StringAssert.Contains(errors[0], "Invalid use of object");
    }
}
