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

    // MS-VBAL 3.2.2: a line-continuation joins physical lines into one logical line, which is what a directive is (3.4). Each row is what MS-VBA printed for the
    // same module in Excel (oracle\cc-continued C01-C14, D01-D06): the module's own lines, the body of Main, and what it printed.
    [TestMethod]
    [DataRow("#Const LC = _ ;;     1", "#If LC = 1 Then ;; Debug.Print \"true\" ;; #Else ;; Debug.Print \"false\" ;; #End If ;; Debug.Print \"compiled\"", "true|compiled", DisplayName = "C01 #Const continued before its value")]
    [DataRow("#Const LC = 1 + _ ;;     1", "#If LC = 2 Then ;; Debug.Print \"true\" ;; #Else ;; Debug.Print \"false\" ;; #End If", "true", DisplayName = "C02 #Const continued in its expression")]
    [DataRow("#Const LC _ ;;     = 1", "#If LC = 1 Then ;; Debug.Print \"true\" ;; #Else ;; Debug.Print \"false\" ;; #End If", "true", DisplayName = "C03 #Const continued before =")]
    [DataRow("#Const _ ;;     LC = 1", "#If LC = 1 Then ;; Debug.Print \"true\" ;; #Else ;; Debug.Print \"false\" ;; #End If", "true", DisplayName = "C04 #Const continued before its name")]
    [DataRow("#Const LC = _ ;;     1 ;; #Const LD = 2", "#If LC + LD = 3 Then ;; Debug.Print \"true\" ;; #Else ;; Debug.Print \"false\" ;; #End If", "true", DisplayName = "C05 a #Const after a continued one")]
    [DataRow("#Const LC = 1", "#If LC = _ ;;     1 Then ;; Debug.Print \"true\" ;; #Else ;; Debug.Print \"false\" ;; #End If ;; Debug.Print \"compiled\"", "true|compiled", DisplayName = "C06 #If continued in its condition")]
    [DataRow("", "#If 1 = 1 _ ;;     Then ;; Debug.Print \"true\" ;; #Else ;; Debug.Print \"false\" ;; #End If", "true", DisplayName = "C07 #If continued before Then")]
    [DataRow("", "#If 0 Then ;; Debug.Print \"a\" ;; #ElseIf 1 = _ ;;     1 Then ;; Debug.Print \"b\" ;; #Else ;; Debug.Print \"c\" ;; #End If", "b", DisplayName = "C08 #ElseIf continued")]
    [DataRow("", "#If 0 Then ;; Debug.Print \"a\" ;; #Else ' note _ ;;     Debug.Print \"x\" ;; Debug.Print \"b\" ;; #End If", "b", DisplayName = "C09 #Else's comment continued")]
    [DataRow("", "#If 1 Then ;; Debug.Print \"a\" ;; #End If ' note _ ;;     Debug.Print \"x\" ;; Debug.Print \"b\"", "a|b", DisplayName = "C10 #End If's comment continued")]
    [DataRow("", "Dim d As Date ;; d = _ ;;     #1/2/2000# ;; Debug.Print Month(d)", "1", DisplayName = "C11 a continued line that starts with # is no directive")]
    [DataRow("#Const LC = 1 ' note _ ;;     Debug.Print \"x\"", "Debug.Print \"b\"", "b", DisplayName = "C12 #Const's comment continued")]
    [DataRow("", "# _ ;; If 1 Then ;; Debug.Print \"a\" ;; #End If", "a", DisplayName = "C13 # continued before If")]
    [DataRow("", "#If 0 Then ;; Debug.Print \"a\" & _ ;;     \"b\" ;; #End If ;; Debug.Print \"c\"", "c", DisplayName = "C14 a continued line in an excluded block")]
    [DataRow("", "#  If 1 Then ;; Debug.Print \"a\" ;; #End If ;; Debug.Print \"c\"", "a|c", DisplayName = "D01 spaces before If")]
    [DataRow("# _ ;; Const LC = 1", "#If LC = 1 Then ;; Debug.Print \"true\" ;; #Else ;; Debug.Print \"false\" ;; #End If", "true", DisplayName = "D02 # continued before Const")]
    [DataRow("#  Const LC = 1", "#If LC = 1 Then ;; Debug.Print \"true\" ;; #Else ;; Debug.Print \"false\" ;; #End If", "true", DisplayName = "D03 spaces before Const")]
    [DataRow("", "#If 1 Then ;; Debug.Print \"a\" ;; #End _ ;; If ;; Debug.Print \"c\"", "a|c", DisplayName = "D04 #End continued before If")]
    [DataRow("", "#If 0 Then ;; Debug.Print \"a\" ;; # _ ;; Else ;; Debug.Print \"b\" ;; # _ ;; End If", "b", DisplayName = "D05 # continued before Else and End If")]
    [DataRow("", "#If 0 Then ;; Debug.Print \"a\" ;; # _ ;; ElseIf 1 Then ;; Debug.Print \"b\" ;; #End If", "b", DisplayName = "D06 # continued before ElseIf")]
    public async Task AContinuedDirective_IsOneLogicalLine(string module, string body, string expected)
    {
        var source = Module([.. module.Split(" ;; ", StringSplitOptions.RemoveEmptyEntries), "Public Sub Main()", .. body.Split(" ;; "), "End Sub"]);

        CollectionAssert.AreEqual(expected.Split('|'), await ModuleWorkspace.RunAsync([], source));
    }

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
