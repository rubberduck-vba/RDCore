namespace RDCore.Tests.Cli;

/// <summary>
/// A module is valid when what it declares is, as well as every procedure of it: a module whose procedures are all fine is not loaded when it declares a
/// name twice, or an interface it does not implement.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.2 Module Structure")]
public sealed class DeclarationErrorTests
{
    private static string Program(params string[] lines)
        => $"Attribute VB_Name = \"Program\"\r\n{string.Join("\r\n", lines)}\r\nPublic Sub Main()\r\nDebug.Print \"ran\"\r\nEnd Sub\r\n";

    private static readonly (string, string) IShape = ("IShape", ModuleWorkspace.ClassModule("IShape", "Public Function Area() As Double", "End Function"));

    [TestMethod]
    public async Task AValidModule_Loads()
        => CollectionAssert.AreEqual(Array.Empty<string>(), await ModuleWorkspace.LoadErrorsAsync([], Program("Public Total As Long")));

    [TestMethod]
    public async Task ANameDeclaredTwice_InTheScopeOfAModule_IsAnAmbiguousName()
    {
        // MS-VBA: "Ambiguous name detected", whatever the two declarations are (Excel-verified); "Duplicate declaration in current scope" is a procedure's.
        var errors = await ModuleWorkspace.LoadErrorsAsync([], Program("Public Total As Long", "Public Sub total()", "End Sub"));

        Assert.HasCount(1, errors);
        StringAssert.Contains(errors[0], RDCore.Runtime.LanguageCore.Exceptions.VBCompileError_AmbiguousName);
        StringAssert.Contains(errors[0], "declared more than once");
    }

    [TestMethod]
    [DataRow("Public Total As Long", "Public Total As Long")]
    [DataRow("Public Total As Long", "Public Total As String")]
    [DataRow("Public Total As Long", "Public Const Total = 1")]
    [DataRow("Public Sub Work()\r\nEnd Sub", "Public Sub Work()\r\nEnd Sub")]
    [DataRow("Public Sub Work()\r\nEnd Sub", "Public Function WORK() As Long\r\nEnd Function")]
    [DataRow("Public Property Get V() As Long\r\nEnd Property", "Public Property Get V() As Long\r\nEnd Property")]
    public async Task ANameDeclaredTwice_AsTheSameKindOfThing_IsAnAmbiguousName_Too(string first, string second)
    {
        var errors = await ModuleWorkspace.LoadErrorsAsync([], Program(first, second));

        Assert.HasCount(1, errors);
        StringAssert.Contains(errors[0], "declared more than once");
    }

    [TestMethod]
    public async Task AnEnumAndAPropertyOfTheSameName_AreEachDeclaredOnce_TheyAreNamesOfDifferentThings()
    {
        // the shape of a real project's module: `Property Get PlayerType() As PlayerType`.
        var errors = await ModuleWorkspace.LoadErrorsAsync(
            [("IPlayer", ModuleWorkspace.ClassModule("IPlayer",
                "Public Enum PlayerType", "HumanControlled", "ComputerControlled", "End Enum",
                "Public Property Get PlayerType() As PlayerType", "End Property"))],
            Program());

        CollectionAssert.AreEqual(Array.Empty<string>(), errors);
    }

    [TestMethod]
    public async Task AMemberOfTwoEnums_IsAnAmbiguousName_NamedAfterEachEnum()
    {
        var errors = await ModuleWorkspace.LoadErrorsAsync([], Program(
            "Public Enum A", "Red", "End Enum", "Public Enum B", "Red", "End Enum", "Public Sub Paint()", "Dim x As Long", "x = Red", "End Sub"));

        var error = Assert.ContainsSingle(errors);
        StringAssert.Contains(error, "Program.A, Program.B");
        Assert.DoesNotContain("]", error, "an address's suffix is not shown");
    }

    [TestMethod]
    public async Task TwoTypesOfTheSameName_AreAnAmbiguousName()
    {
        var errors = await ModuleWorkspace.LoadErrorsAsync(
            [], Program("Public Enum Kind", "A", "End Enum", "Public Type Kind", "X As Long", "End Type"));

        Assert.HasCount(1, errors);
        StringAssert.Contains(errors[0], "declared more than once");
    }

    [TestMethod]
    public async Task ANameDeclaredInEachBranchOfAConditionalCompilationBlock_IsDeclaredOnce_WhateverTheBranchesAreWorth()
    {
        // what the branches of a block declare is one declaration of each name, whichever branch compiles.
        var errors = await ModuleWorkspace.LoadErrorsAsync([], Program(
            "#If NothingDefinesThis Then",
            "Public Declare PtrSafe Sub Sleep Lib \"kernel32\" (ByVal ms As Long)",
            "#Else",
            "Public Declare Sub Sleep Lib \"kernel32\" (ByVal ms As Long)",
            "#End If",
            "#If NorThis Then",
            "Public Total As Long",
            "#ElseIf NorThisEither Then",
            "Public Total As LongLong",
            "#Else",
            "Public Total As Integer",
            "#End If"));

        CollectionAssert.AreEqual(Array.Empty<string>(), errors);
    }

    [TestMethod]
    public async Task ANameDeclaredTwiceInOneBranch_IsAnAmbiguousName_EvenWhenTheOtherBranchDeclaresItToo()
    {
        // the branch that states it twice is the live one: a branch that is not live is not part of the module.
        var errors = await ModuleWorkspace.LoadErrorsAsync([], Program(
            "#If NothingDefinesThis Then", "Public Total As Integer", "#Else", "Public Total As Long", "Public Total As Long", "#End If"));

        StringAssert.Contains(Assert.ContainsSingle(errors), "declared more than once");
    }

    [TestMethod]
    [DataRow("#If 0 Then", null, false, DisplayName = "the If branch, not live")]
    [DataRow("#If 1 Then", null, true, DisplayName = "the If branch, live")]
    [DataRow("#If 1 Then", "#Else", false, DisplayName = "the Else branch, not live")]
    [DataRow("#If 0 Then", "#Else", true, DisplayName = "the Else branch, live")]
    public async Task ANameDeclaredOutsideABlock_AndInOneOfItsBranches_IsAnAmbiguousName_OnlyWhenThatBranchIsLive(string block, string? branch, bool live)
    {
        // MS-VBAL §3.4: only the live branch of a block is part of the module, so the declaration in another is not part of it. The rows follow the
        // specification: no run of MS-VBA records a module that declares the name outside the block and in a branch that is not live.
        var lines = new List<string> { "Public Total As Long", block };
        lines.AddRange(branch is null ? ["Public Total As Long"] : ["Public Other As Long", branch, "Public Total As Long"]);
        lines.Add("#End If");

        var errors = await ModuleWorkspace.LoadErrorsAsync([], Program([.. lines]));

        if (live)
        {
            StringAssert.Contains(Assert.ContainsSingle(errors), "declared more than once");
        }
        else
        {
            CollectionAssert.AreEqual(Array.Empty<string>(), errors);
        }
    }

    [TestMethod]
    public async Task AModule_ThatNamesOneDefinedAfterIt_IsLoaded_ForTheCodeIsCheckedOnceEveryModuleIsDefined()
    {
        // A comes before B in the workspace, and uses it.
        var a = ("A", ModuleWorkspace.ClassModule("A", "Public Function Make() As B", "Set Make = New B", "End Function"));
        var b = ("B", ModuleWorkspace.ClassModule("B", "Public Size As Long"));

        CollectionAssert.AreEqual(Array.Empty<string>(), await ModuleWorkspace.LoadErrorsAsync([a, b], Program()));
        CollectionAssert.AreEqual(new[] { "ran" }, await ModuleWorkspace.RunAsync([a, b], Program()));
    }

    [TestMethod]
    public async Task TheAccessorsOfAProperty_AreTheOneDeclarationOfIt()
        => CollectionAssert.AreEqual(Array.Empty<string>(), await ModuleWorkspace.LoadErrorsAsync(
            [("Box", ModuleWorkspace.ClassModule("Box", "Private mV As Long",
                "Public Property Get V() As Long", "V = mV", "End Property",
                "Public Property Let V(ByVal n As Long)", "mV = n", "End Property"))],
            Program()));

    [TestMethod]
    public async Task AClass_ThatDoesNotImplementWhatItsInterfaceDeclares_IsNotLoaded()
    {
        var square = ("Square", ModuleWorkspace.ClassModule("Square", "Implements IShape"));

        var errors = await ModuleWorkspace.LoadErrorsAsync([IShape, square], Program());

        Assert.IsNotEmpty(errors);
    }

    [TestMethod]
    public async Task AClass_ThatImplementsWhatItsInterfaceDeclares_Loads()
    {
        var square = ("Square", ModuleWorkspace.ClassModule("Square", "Implements IShape",
            "Private Function IShape_Area() As Double", "IShape_Area = 4", "End Function"));

        CollectionAssert.AreEqual(Array.Empty<string>(), await ModuleWorkspace.LoadErrorsAsync([IShape, square], Program()));
    }
}
