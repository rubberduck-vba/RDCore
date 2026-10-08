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
    public async Task ANameDeclaredTwice_InTheScopeOfAModule_IsADuplicateDeclaration()
    {
        var errors = await ModuleWorkspace.LoadErrorsAsync([], Program("Public Total As Long", "Public Sub total()", "End Sub"));

        Assert.HasCount(1, errors);
        StringAssert.Contains(errors[0], "declared more than once");
    }

    [TestMethod]
    [DataRow("Public Total As Long", "Public Total As Long")]
    [DataRow("Public Total As Long", "Public Total As String")]
    [DataRow("Public Total As Long", "Public Const Total = 1")]
    [DataRow("Public Sub Work()\r\nEnd Sub", "Public Sub Work()\r\nEnd Sub")]
    [DataRow("Public Sub Work()\r\nEnd Sub", "Public Function WORK() As Long\r\nEnd Function")]
    [DataRow("Public Property Get V() As Long\r\nEnd Property", "Public Property Get V() As Long\r\nEnd Property")]
    public async Task ANameDeclaredTwice_AsTheSameKindOfThing_IsADuplicateDeclaration_Too(string first, string second)
    {
        var errors = await ModuleWorkspace.LoadErrorsAsync([], Program(first, second));

        Assert.HasCount(1, errors);
        StringAssert.Contains(errors[0], "declared more than once");
    }

    [TestMethod]
    public async Task AnEnumAndAPropertyOfTheSameName_AreEachDeclaredOnce_TheyAreNamesOfDifferentThings()
    {
        // the shape of a real module (IPlayer.cls of OOPBattleship): `Property Get PlayerType() As PlayerType`.
        var errors = await ModuleWorkspace.LoadErrorsAsync(
            [("IPlayer", ModuleWorkspace.ClassModule("IPlayer",
                "Public Enum PlayerType", "HumanControlled", "ComputerControlled", "End Enum",
                "Public Property Get PlayerType() As PlayerType", "End Property"))],
            Program());

        CollectionAssert.AreEqual(Array.Empty<string>(), errors);
    }

    [TestMethod]
    public async Task TwoTypesOfTheSameName_AreADuplicateDeclaration()
    {
        var errors = await ModuleWorkspace.LoadErrorsAsync(
            [], Program("Public Enum Kind", "A", "End Enum", "Public Type Kind", "X As Long", "End Type"));

        Assert.HasCount(1, errors);
        StringAssert.Contains(errors[0], "declared more than once");
    }

    [TestMethod]
    public async Task ANameDeclaredInEachBranchOfAConditionalCompilationBlock_IsDeclaredOnce_WhateverTheBranchesAreWorth()
    {
        // VBA7 and Mac are constants nothing here defines: the block is not evaluated, and what it declares is still one declaration of each name.
        var errors = await ModuleWorkspace.LoadErrorsAsync([], Program(
            "#If VBA7 Then",
            "Public Declare PtrSafe Sub Sleep Lib \"kernel32\" (ByVal ms As Long)",
            "#Else",
            "Public Declare Sub Sleep Lib \"kernel32\" (ByVal ms As Long)",
            "#End If",
            "#If Mac Then",
            "Public Total As Long",
            "#ElseIf VBA7 Then",
            "Public Total As LongLong",
            "#Else",
            "Public Total As Integer",
            "#End If"));

        CollectionAssert.AreEqual(Array.Empty<string>(), errors);
    }

    [TestMethod]
    public async Task ANameDeclaredTwiceInOneBranch_IsADuplicateDeclaration_EvenWhenTheOtherBranchDeclaresItToo()
    {
        var errors = await ModuleWorkspace.LoadErrorsAsync([], Program(
            "#If Mac Then", "Public Total As Long", "Public Total As Long", "#Else", "Public Total As Integer", "#End If"));

        Assert.HasCount(1, errors);
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
