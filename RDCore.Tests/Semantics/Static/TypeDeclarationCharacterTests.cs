using RDCore.Tests.Cli;
using RDCore.SDK.Model.Errors;

namespace RDCore.Tests.Semantics.Static;

/// <summary>
/// A type-declaration character says the type of the name it is written on. A name declared as one type and written with the character of another is an
/// error at that name; and <c>x$</c> and <c>x%</c> are both <c>x</c>, which can be declared once in its scope.
/// </summary>
[TestClass]
public sealed class TypeDeclarationCharacterTests
{
    private static async Task<(VBCompileErrorId Id, int Line, int Character)[]> ErrorsOfAsync(params string[] body)
    {
        var payload = await ModuleWorkspace.SemanticsAsync([], $"Public Sub Main()\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n");
        var model = payload.Modules.Single();

        return [.. model.DeclarationErrors.Concat(model.Procedures.SelectMany(procedure => procedure.CompileErrors))
            .Select(error => (error.Id, error.Location.Range.Start.Line, error.Location.Range.Start.Character))];
    }

    // ---- a character that is not the type ----

    [TestMethod]
    [DataRow("Dim x$", "x% = 42")]
    [DataRow("Dim x As String", "x% = 42")]
    [DataRow("Dim x As Long", "x$ = \"a\"")]
    [DataRow("Dim x$", "x& = 42")]
    [DataRow("Dim x$", "Debug.Print x%")]
    public async Task ANameWrittenWithTheCharacterOfAnotherType_IsAnErrorAtTheName(string declaration, string use)
    {
        var errors = await ErrorsOfAsync(declaration, use);

        var error = Assert.ContainsSingle(errors);
        Assert.AreEqual(VBCompileErrorId.TypeDeclarationCharacterDoesNotMatch, error.Id);
        Assert.AreEqual(2, error.Line);
        Assert.AreEqual(use.IndexOf('x'), error.Character, "at the name, and not at the statement it is in");
    }

    [TestMethod]
    [DataRow("Dim x$", "x$ = \"a\"")]
    [DataRow("Dim x%", "x% = 1")]
    [DataRow("Dim x%", "x = 1")]
    [DataRow("Dim x As Long", "x& = 1")]
    [DataRow("Dim x As String", "x$ = \"a\"")]
    public async Task ANameWrittenWithTheCharacterOfItsType_OrWithNone_IsFine(string declaration, string use)
        => Assert.IsEmpty(await ErrorsOfAsync(declaration, use));

    [TestMethod]
    public async Task AMemberOfTheLibraryNamedWithTheCharacter_IsNotADeclarationOfAnIdentifier()
        => Assert.IsEmpty(await ErrorsOfAsync("Dim n%", "n = 255", "Debug.Print Hex$(n)", "Debug.Print Hex(n)"));

    [TestMethod]
    public async Task ANameThatIsDeclaredByBeingUsed_IsOfTheTypeItWasWrittenWith()
    {
        Assert.IsEmpty(await ErrorsOfAsync("y$ = \"a\"", "Debug.Print y$"));

        var errors = await ErrorsOfAsync("y$ = \"a\"", "y% = 1");
        Assert.AreEqual(VBCompileErrorId.TypeDeclarationCharacterDoesNotMatch, Assert.ContainsSingle(errors).Id);
    }

    // ---- a name declared once ----

    [TestMethod]
    [DataRow("Dim x$", "Dim x%")]
    [DataRow("Dim x As Long", "Dim x As String")]
    [DataRow("Dim X As Long", "Dim x As Long")]
    [DataRow("Dim x As Long", "Static x As Long")]
    [DataRow("Dim x As Long", "Const x = 1")]
    public async Task ANameDeclaredTwiceInAProcedure_IsADuplicateDeclaration_AtTheSecond(string first, string second)
    {
        var errors = await ErrorsOfAsync(first, second);

        var error = Assert.ContainsSingle(errors);
        Assert.AreEqual(VBCompileErrorId.DuplicateDeclaration, error.Id);
        Assert.AreEqual(2, error.Line, "the second declaration, on the third line of the module");
    }

    [TestMethod]
    public async Task ANameDeclaredInsideABlock_IsDeclaredInTheProcedure()
    {
        var errors = await ErrorsOfAsync("Dim x As Long", "If True Then", "Dim x As String", "End If");

        Assert.AreEqual(VBCompileErrorId.DuplicateDeclaration, Assert.ContainsSingle(errors).Id);
    }

    [TestMethod]
    public async Task ANameDeclaredInEachBranchOfAConditionalCompilationBlock_IsDeclaredOnce()
        => Assert.IsEmpty(await ErrorsOfAsync("#Const Flag = 1", "#If Flag Then", "Dim Temp As Long", "#Else", "Dim Temp As Double", "#End If", "Temp = 1"));

    [TestMethod]
    public async Task ANameDeclaredInEachBranchOfABlockThatCannotBeEvaluated_IsDeclaredOnce_Too()
    {
        // a condition that fails to evaluate says which branch is dead of none of them, and the branches are alternatives all the same.
        var errors = await ErrorsOfAsync("#If 1 / 0 Then", "Dim Temp As Long", "#Else", "Dim Temp As Double", "#End If");

        Assert.IsEmpty(errors);
    }

    [TestMethod]
    public async Task ANameDeclaredTwiceInTheLiveBranch_IsADuplicate_WhateverTheOtherBranchDoes()
    {
        var payload = await ModuleWorkspace.SemanticsAsync([], string.Join("\r\n",
            "Public Sub Main()", "#If 1 Then", "Dim Temp As Long", "Dim Temp As Long", "#Else", "Dim Temp As Double", "#End If", "End Sub", string.Empty));

        var model = payload.Modules.Single();
        var error = Assert.ContainsSingle(model.DeclarationErrors.Concat(model.Procedures.SelectMany(procedure => procedure.CompileErrors)).ToArray());
        Assert.AreEqual(VBCompileErrorId.DuplicateDeclaration, error.Id);
    }

    [TestMethod]
    public async Task ANameDeclaredTwiceInABranchThatIsNotCompiled_IsNotAnError()
    {
        // an undefined conditional-compilation constant is 0 (MS-VBAL §3.4.2): the branch is excluded, and removed before the language sees it.
        var errors = await ErrorsOfAsync("#If NothingDefinesThis Then", "Dim Temp As Long", "Dim Temp As Long", "#End If");

        Assert.IsEmpty(errors);
    }

    [TestMethod]
    public async Task ADifferentName_OrTheSameNameInAnotherProcedure_IsNoDuplicate()
    {
        var payload = await ModuleWorkspace.SemanticsAsync([], string.Join("\r\n",
            "Public Sub Main()", "Dim x As Long", "Dim y As Long", "End Sub",
            "Public Sub Other()", "Dim x As Long", "End Sub", string.Empty));

        var model = payload.Modules.Single();
        Assert.IsEmpty(model.DeclarationErrors.Concat(model.Procedures.SelectMany(procedure => procedure.CompileErrors)).ToArray());
    }

    [TestMethod]
    public async Task AParameterAndALocalOfOneName_AreADuplicateDeclaration()
    {
        var payload = await ModuleWorkspace.SemanticsAsync([], string.Join("\r\n", "Public Sub Main(ByVal x As Long)", "Dim x As String", "End Sub", string.Empty));

        var model = payload.Modules.Single();
        var error = model.Procedures.SelectMany(procedure => procedure.CompileErrors).Single();
        Assert.AreEqual(VBCompileErrorId.DuplicateDeclaration, error.Id);
    }
}
