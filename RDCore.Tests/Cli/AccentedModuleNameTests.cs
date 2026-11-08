using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A module named with a letter that is not ASCII - <c>Modèle</c>, the common case in a French project - is the module its name says, wherever the name is
/// read from its address: the address escapes it (<c>#Mod%C3%A8le</c>), and the name it is found, compared and shown by is the one its code was written with.
/// </summary>
[TestClass]
public sealed class AccentedModuleNameTests
{
    private static string Program(params string[] lines)
        => $"Attribute VB_Name = \"Program\"\r\n{string.Join("\r\n", lines)}\r\n";

    [TestMethod]
    public async Task TheCodeOfAClassNamedWithAnAccent_ComparesStrings_InItsOwnModulesMode()
    {
        var modele = ("Modèle", ClassModule("Modèle", "Option Compare Text", "Public Function Same() As Boolean", "Same = (\"a\" = \"A\")", "End Function"));

        var output = await RunAsync([modele], Program("Private m As New Modèle", "Public Sub Main()", "Debug.Print m.Same", "End Sub"));

        CollectionAssert.AreEqual(new[] { "True" }, output);
    }

    [TestMethod]
    public async Task AnArrayOfAModuleNamedWithAnAccent_ThatOmitsItsLowerBound_StartsAtItsOptionBase()
    {
        var modele = ("Modèle", StandardModule("Modèle", "Option Base 1", "Public Tableau(3) As Long"));

        var output = await RunAsync([], [modele], Program("Public Sub Main()", "Debug.Print LBound(Tableau)", "End Sub"));

        CollectionAssert.AreEqual(new[] { "1" }, output);
    }

    [TestMethod]
    public async Task Me_InAClassNamedWithAnAccent_IsAnInstanceOfIt()
    {
        var modele = ("Modèle", ClassModule("Modèle", "Public Function Moi() As Modèle", "Set Moi = Me", "End Function"));
        var program = Program("Public Sub Main()", "Dim m As New Modèle", "Debug.Print m.Moi Is m", "End Sub");

        var errors = await LoadErrorsAsync([modele], program);
        var output = await RunAsync([modele], program);

        Assert.IsEmpty(errors, string.Join("\n", errors));
        CollectionAssert.AreEqual(new[] { "True" }, output);
    }

    // the sweep evaluates a class's procedures against an instance of the class: what it states of Me is that instance's.
    [TestMethod]
    public async Task TheSweepOfAClassNamedWithAnAccent_EvaluatesItsCode_AgainstAnInstanceOfIt()
    {
        var modele = ("Modèle", ClassModule("Modèle", "Option Compare Text", "Public Nom As String", "Public Function Same() As Boolean", "Same = (Me.Nom = \"\")", "End Function"));

        var model = await ModelAsync([modele], Program("Public Sub Main()", "End Sub"), "Modèle");

        var facts = model.Procedures.Single().Runtime;
        Assert.IsNotNull(facts, "the module was loaded, so its code was evaluated");
        Assert.IsNotEmpty(facts.Operations);
    }

    [TestMethod]
    public async Task TheSemanticsOfAModuleNamedWithAnAccent_AreAnsweredWhenItIsAskedForByName()
    {
        var modele = ("Modèle", ClassModule("Modèle", "Public Sub Travailler()", "End Sub"));

        var payload = await SemanticsAsync([modele], Program("Public Sub Main()", "End Sub"), moduleName: "Modèle");

        Assert.AreEqual("Modèle", SDK.Model.Symbols.SymbolUri.ModuleNameOf(payload.Modules.Single().Module));
    }

    // the language server's composition: the module is named as its code names it, and a name qualified by it resolves to its member.
    [TestMethod]
    public void InTheLanguageServersComposition_AModuleNamedWithAnAccent_IsFoundByItsName_AndItsMembersThroughIt()
    {
        var root = new Uri(Path.Combine(Root, "accents") + Path.DirectorySeparatorChar);
        var moduleUri = SDK.Model.Symbols.SymbolUri.Module(root, "Modèle");
        var parse = new RDCore.Parsing.ModuleParser().Parse(new Uri(Path.Combine(Root, "accents", "Modèle.bas")),
            StandardModule("Modèle", "Public Function Foo() As Long", "End Function"));

        var resolver = RDCore.Symbols.WorkspaceSymbolResolver.Compose(root, [(moduleUri, SDK.Model.AST.Declarations.ModuleType.StdModule, parse)],
            new RDCore.Symbols.IntrinsicSymbolResolver(), "Projet");
        var module = resolver.ResolveValue("Modèle", SDK.Model.Symbols.Abstract.ScopeKind.Global, moduleUri).Symbol;
        var foo = resolver.ResolveValue("Foo", SDK.Model.Symbols.Abstract.ScopeKind.Global, moduleUri).Symbol;

        Assert.IsInstanceOfType<SDK.Model.Symbols.VBStandardModuleSymbol>(module);
        Assert.AreEqual("Modèle", module.Name);
        Assert.AreEqual(moduleUri, module.Uri);
        Assert.AreEqual(moduleUri.AbsoluteUri, foo!.ParentUri.AbsoluteUri);
    }

    [TestMethod]
    public async Task AnAmbiguousName_NamesTheModulesThatDeclareIt_AsTheirCodeDoes()
    {
        var modele = ("Modèle", StandardModule("Modèle", "Public Total As Long"));
        var donnees = ("Données", StandardModule("Données", "Public Total As Long"));

        var errors = await LoadErrorsAsync([], [modele, donnees], Program("Public Sub Main()", "Debug.Print Total", "End Sub"));

        var ambiguous = Assert.ContainsSingle(errors);
        StringAssert.Contains(ambiguous, "Modèle");
        StringAssert.Contains(ambiguous, "Données");
    }

    // an edit made while the program waits names the procedure it is of after the module, as the module's code names it.
    [TestMethod]
    public async Task AnEditOfAModuleNamedWithAnAccent_WhileTheProgramWaits_NamesItsProcedureAfterTheModule()
    {
        var modele = StandardModule("Modèle", "Public Sub Calculer()", "    Stop", "End Sub", "Public Sub Autre()", "End Sub");
        var program = Program("Public Sub Main()", "    Modèle.Calculer", "End Sub");
        DebugEditStatusResult? status = null;
        await InspectWithModulesAsync([("Modèle", modele)], program, async (provider, stopped, runAgain) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, stopped.Outcome, stopped.ErrorMessage);
            _ = await ReloadOnlyAsync(provider, ["Modèle", "Program"],
                ("Modèle", ModuleType.StdModule, StandardModule("Modèle", "Public Sub Autre()", "End Sub")),
                ("Program", ModuleType.StdModule, Program("Public Sub Main()", "    Modèle.Autre", "End Sub")));
            status = provider.Execution.EditStatus();
        }, debug: true);

        var listed = string.Join("; ", status!.Edits.Select(edit => $"{edit.Name}: {edit.Reason}"));
        Assert.IsTrue(status.Edits.Any(edit => edit.Name == "Modèle.Calculer"), listed);
        Assert.IsFalse(status.Edits.Any(edit => edit.Name.Contains('%', StringComparison.Ordinal)), listed);
    }

    // a member qualified by the module's name: the composition names the module as its code does.
    [TestMethod]
    public async Task AProcedureOfAModuleNamedWithAnAccent_IsCalled_QualifiedByTheModulesName()
    {
        var modele = ("Modèle", StandardModule("Modèle", "Public Function Foo() As Long", "Foo = 42", "End Function"));
        var program = Program("Option Explicit", "Public Sub Main()", "Debug.Print Modèle.Foo", "End Sub");

        var errors = await LoadErrorsAsync([], [modele], program);
        var output = await RunAsync([], [modele], program);

        Assert.IsEmpty(errors, string.Join("\n", errors));
        CollectionAssert.AreEqual(new[] { "42" }, output);
    }
}
