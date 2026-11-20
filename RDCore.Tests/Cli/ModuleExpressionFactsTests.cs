namespace RDCore.Tests.Cli;

/// <summary>
/// The expressions of a module's declarations, outside any procedure, are stated with the module's model: the value of a constant
/// (<strong>MS-VBAL §5.2.3.2</strong>) and the bounds of an array (<strong>MS-VBAL §5.2.3.1.3</strong>), each name bound to what it refers to, in the
/// module or in another, as an expression of a procedure's body is.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL 5.0.3 Semantic Analysis")]
public sealed class ModuleExpressionFactsTests
{
    private static string Program(params string[] lines)
        => $"Attribute VB_Name = \"Program\"\r\n{string.Join("\r\n", lines)}\r\n";

    // `Globals.N%` and `N%`, outside any procedure: both refer to the constant of the other module.
    [TestMethod]
    public async Task AReferenceOutsideAProcedure_ToAConstantOfAnotherModule_IsBoundToIt()
    {
        var payload = await ModuleWorkspace.SemanticsAsync([],
            Program("Private Const C = Globals.N% + 1", "Private Items(1 To N%) As Long", "Public Sub Work()", "    Debug.Print C, Items(1)", "End Sub"),
            modules: [("Globals", ModuleWorkspace.StandardModule("Globals", "Public Const N% = 3"))]);
        var program = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Program");
        var constant = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Globals")
            .Declarations.Single(declaration => declaration.Name == "N").Symbol;

        Assert.IsNotNull(program.Expressions, "the module states no expression outside its procedures");
        var lines = program.Expressions.Value.Where(fact => fact.Binding == constant).Select(fact => fact.Location.Range.Start.Line).Distinct().Order();
        CollectionAssert.AreEqual(new[] { 1, 2 }, lines.ToArray());
    }

    [TestMethod]
    public async Task AModuleWhoseDeclarationsHaveNoExpression_StatesNone()
        => Assert.IsNull((await ModuleWorkspace.SemanticsAsync([], Program("Private Total As Long", "Public Sub Work()", "    Total = 1", "End Sub")))
            .Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Program").Expressions);
}
