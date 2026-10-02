using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Tests.Cli;

/// <summary>
/// The environment host runs the semantic analysis pass when it loads the code of a module, and answers <c>rdcore/host/semantics</c> with what it found: the model
/// of a module, or of every module of the workspace.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL 5.0.3 Semantic Analysis")]
public sealed class HostSemanticsTests
{
    private static string Program(params string[] lines)
        => $"Attribute VB_Name = \"Program\"\r\n{string.Join("\r\n", lines)}\r\n";

    private static readonly (string, string) Widget = ("Widget", ModuleWorkspace.ClassModule("Widget", "Public Size As Long"));

    [TestMethod]
    public async Task EveryModuleOfTheWorkspace_HasAModel_WhenNoneIsAskedFor()
    {
        var payload = await ModuleWorkspace.SemanticsAsync([Widget], Program("Public Sub Main()", "End Sub"));

        CollectionAssert.AreEquivalent(new[] { "Program", "Widget" }, payload.Modules.Select(model => model.Module.Fragment.TrimStart('#')).ToArray());
    }

    [TestMethod]
    public async Task ASingleModule_CanBeAskedFor()
    {
        var payload = await ModuleWorkspace.SemanticsAsync([Widget], Program("Public Sub Main()", "End Sub"), "program");

        Assert.AreEqual("Program", payload.Modules.Single().Module.Fragment.TrimStart('#'));
    }

    [TestMethod]
    public async Task AModuleThatIsNotThere_HasNoModel()
        => Assert.IsEmpty((await ModuleWorkspace.SemanticsAsync([Widget], Program("Public Sub Main()", "End Sub"), "Nowhere")).Modules);

    [TestMethod]
    public async Task TheModelOfAModule_SaysWhatIsWrongWithIt_EvenWhenItDoesNotLoad()
    {
        var payload = await ModuleWorkspace.SemanticsAsync([], Program("Public Total As Long", "Public Sub total()", "End Sub"));

        var model = payload.Modules.Single();
        Assert.HasCount(1, model.DeclarationErrors);
        Assert.AreEqual(RDCore.SDK.Model.Errors.VBCompileErrorId.DuplicateDeclaration, model.DeclarationErrors[0].Id);
    }

    [TestMethod]
    public async Task TheModelOfAModule_HasTheFactsOfItsExpressions_AndOfItsDeclarations()
    {
        var payload = await ModuleWorkspace.SemanticsAsync([], Program("Option Explicit", "Public Sub Main()", "Dim n As Long", "n = 5", "Debug.Print n", "End Sub"));

        var model = payload.Modules.Single();
        Assert.IsTrue(model.OptionExplicit);
        var main = model.Procedures.Single();
        Assert.IsTrue(main.IsFullyAnalyzed);
        Assert.IsTrue(main.Expressions.Any(fact => fact.Flags.HasFlag(ValueExpressionSemanticFlags.Literal)));
        Assert.IsTrue(main.Expressions.Any(fact => fact.Flags.HasFlag(ValueExpressionSemanticFlags.AssignmentTarget) && fact.Classification == ExpressionClassification.Variable));
        var n = model.Declarations.Single(declaration => declaration.Name == "n");
        Assert.AreEqual(new DeclarationReferences(1, 1, 0), n.References);
    }

    [TestMethod]
    [DataRow("vba", "Option Explicit", true)]
    [DataRow("vba", "", false)]
    [DataRow("vb6", "", false)]
    public async Task OptionExplicit_IsAFactOfALanguageThatHasIt(string language, string directive, bool stated)
    {
        var payload = await ModuleWorkspace.SemanticsAsync(
            [], Program(directive, "Public Sub Main()", "End Sub"), language: RDCore.SDK.Workspace.SupportedLanguages.Get(language));

        Assert.AreEqual(stated, payload.Modules.Single().OptionExplicit);
    }

    [TestMethod]
    public async Task OptionExplicit_IsNotIssuedForALanguageThatHasNone()
    {
        // a BASIC has no way to state it: that it is not stated is not something to say.
        var payload = await ModuleWorkspace.SemanticsAsync(
            [], Program("Public Sub Main()", "End Sub"), language: RDCore.SDK.Workspace.SupportedLanguages.BASIC);

        Assert.IsNull(payload.Modules.Single().OptionExplicit);
    }
}
