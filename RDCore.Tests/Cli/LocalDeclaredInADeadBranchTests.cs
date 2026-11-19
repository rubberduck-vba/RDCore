namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §3.4</strong>: a local declared in a branch of a <c>#If</c> that is not live is not declared. The code of that branch is not compiled, so a
/// name the live code uses that only the branch declares is a name nothing declares: a compile error under <c>Option Explicit</c>, and otherwise a variable
/// the use declares (<strong>MS-VBAL §5.6.10</strong>).
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 3.4 Conditional Compilation")]
public sealed class LocalDeclaredInADeadBranchTests
{
    [TestMethod]
    public async Task ALocalOfTheLiveBranch_IsDeclared()
    {
        var printed = await ModuleWorkspace.RunAsync([], ModuleWorkspace.StandardModule("Program",
            "Option Explicit",
            "#Const DEV = 1",
            "Public Sub Main()",
            "#If DEV Then",
            "    Dim n As Long",
            "#Else",
            "    Dim n As String",
            "#End If",
            "    n = 5",
            "    Debug.Print TypeName(n); n",
            "End Sub"));

        CollectionAssert.AreEqual(new[] { "Long 5" }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    public async Task ALocalOnlyABranchThatIsNotLiveDeclares_IsNotDefined_UnderOptionExplicit()
    {
        var errors = await ModuleWorkspace.LoadErrorsAsync([], ModuleWorkspace.StandardModule("Program",
            "Option Explicit",
            "#Const DEV = 0",
            "Public Sub Main()",
            "#If DEV Then",
            "    Dim n As Long",
            "#End If",
            "    n = 5",
            "End Sub"));

        Assert.IsTrue(errors.Any(error => error.Contains("Variable not defined", StringComparison.OrdinalIgnoreCase)), string.Join(" | ", errors));
    }

    [TestMethod]
    public async Task ALocalOnlyABranchThatIsNotLiveDeclares_IsDeclaredByItsUse_WithoutOptionExplicit()
    {
        var printed = await ModuleWorkspace.RunAsync([], ModuleWorkspace.StandardModule("Program",
            "#Const DEV = 0",
            "Public Sub Main()",
            "#If DEV Then",
            "    Dim n As String",
            "#End If",
            "    n = 5",
            "    Debug.Print TypeName(n); n",
            "End Sub"));

        CollectionAssert.AreEqual(new[] { "Integer 5" }, printed, string.Join(" | ", printed));
    }
}
