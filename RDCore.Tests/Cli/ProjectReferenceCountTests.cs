using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics;

namespace RDCore.Tests.Cli;

/// <summary>
/// The environment host counts the references to a declaration that every module of the project can refer to, and nothing outside the project can, over every
/// module of the project: a <c>Friend</c> procedure (MS-VBAL §5.3.1.1), and a <c>Public</c> variable or constant of a module that is
/// <c>Option Private Module</c> (MS-VBAL §5.2.1.4). Each counted reference is stated with where it is.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL 5.0.3 Semantic Analysis")]
public sealed class ProjectReferenceCountTests
{
    private static string Program(params string[] lines)
        => $"Attribute VB_Name = \"Program\"\r\n{string.Join("\r\n", lines)}\r\n";

    private static readonly (string, string) Counter = ("Counter", ModuleWorkspace.ClassModule("Counter",
        "Private mTotal As Long",
        "Friend Sub Add()", "mTotal = mTotal + 1", "End Sub",
        "Friend Function Unused() As Long", "End Function",
        "Friend Property Get Value() As Long", "Value = mTotal", "End Property",
        "Friend Property Let Value(ByVal v As Long)", "mTotal = v", "End Property"));

    private static readonly string[] UsesCounter =
    [
        "Public Sub Main()",
        "Dim c As New Counter",
        "c.Add",
        "c.Add",
        "c.Value = 3",
        "Debug.Print c.Value",
        "End Sub",
    ];

    private static async Task<ModuleSemanticsDto> ModelOfAsync(
        string name, string program, IReadOnlyList<(string Name, string Source)>? classes = null, IReadOnlyList<(string Name, string Source)>? modules = null,
        string moduleName = "")
    {
        var payload = await ModuleWorkspace.SemanticsAsync(classes ?? [Counter], program, moduleName, modules: modules);
        return payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == name);
    }

    private static DeclarationFactDto Declaration(ModuleSemanticsDto model, string name) => model.Declarations.First(declaration => declaration.Name == name);

    [TestMethod]
    public async Task AFriendProcedure_IsCountedOverEveryModuleOfTheProject()
    {
        var model = await ModelOfAsync("Counter", Program(UsesCounter));

        Assert.AreEqual(new DeclarationReferences(2, 0, 0), Declaration(model, "Add").References);
        Assert.AreEqual(new DeclarationReferences(0, 0, 0), Declaration(model, "Unused").References);
    }

    [TestMethod]
    public async Task AFriendProperty_IsCountedOverEveryAccessor()
        => Assert.AreEqual(new DeclarationReferences(1, 1, 0), Declaration(await ModelOfAsync("Counter", Program(UsesCounter)), "Value").References);

    [TestMethod]
    public async Task ACountedReference_IsStatedWithWhereItIs()
    {
        var sites = Declaration(await ModelOfAsync("Counter", Program(UsesCounter)), "Add").ReferencedFrom;

        Assert.IsNotNull(sites);
        Assert.HasCount(2, sites.Value);
        Assert.IsTrue(sites.Value.All(site => site.Kind == DeclarationReferenceKind.Read && site.Procedure is not null
            && SymbolUri.QualifiedNameOf(site.Procedure) == "Program.Main"));
        Assert.AreEqual(3, sites.Value[0].Location.Range.Start.Line, "on the line that calls it");
        Assert.IsTrue(sites.Value[0].Location.Range.Start < sites.Value[1].Location.Range.Start, "in the order they are written");
    }

    [TestMethod]
    public async Task ACallWithArguments_IsOneReference_AndAFunctionAssigningItsResult_RefersToNothing()
    {
        (string, string) twice = ("Counter", ModuleWorkspace.ClassModule("Counter",
            "Friend Function Twice(ByVal n As Long) As Long", "Twice = n * 2", "End Function",
            "Friend Function Never() As Long", "Never = 1", "End Function"));
        var model = await ModelOfAsync("Counter", Program("Public Sub Main()", "Dim c As New Counter", "Debug.Print c.Twice(2)", "End Sub"), classes: [twice]);

        Assert.AreEqual(new DeclarationReferences(1, 0, 0), Declaration(model, "Twice").References);
        Assert.AreEqual(new DeclarationReferences(0, 0, 0), Declaration(model, "Never").References);
    }

    [TestMethod]
    public async Task AFunctionThatCallsItself_IsReadByItsOwnCall()
    {
        (string, string) recursive = ("Counter", ModuleWorkspace.ClassModule("Counter",
            "Friend Function Again() As Long", "Again = Again()", "End Function"));
        var again = Declaration(await ModelOfAsync("Counter", Program("Public Sub Main()", "End Sub"), classes: [recursive]), "Again");

        Assert.AreEqual(new DeclarationReferences(1, 0, 0), again.References);
        Assert.HasCount(1, again.ReferencedFrom!.Value);
        Assert.AreEqual("Counter.Again", SymbolUri.QualifiedNameOf(again.ReferencedFrom.Value[0].Procedure!), "the site is in the function itself");
    }

    [TestMethod]
    public async Task AFriendProcedureThatAnExcludedBranchOfAnotherModuleCalls_HasNoCount()
    {
        var model = await ModelOfAsync("Counter", Program(
            "#Const FEATURE = 0",
            "Public Sub Main()",
            "Dim c As New Counter",
            "#If FEATURE Then",
            "c.Unused",
            "#Else",
            "c.Add",
            "#End If",
            "End Sub"));

        Assert.IsNull(Declaration(model, "Unused").References, "only the excluded branch calls it");
        Assert.AreEqual(new DeclarationReferences(1, 0, 0), Declaration(model, "Add").References, "the live branch's call is counted");
    }

    [TestMethod]
    public async Task AFunctionCalledAsAStatement_IsAReferenceThatDiscardsTheValueItReturns()
    {
        (string, string) twice = ("Counter", ModuleWorkspace.ClassModule("Counter",
            "Friend Function Twice(ByVal n As Long) As Long", "Twice = n * 2", "End Function"));
        var model = await ModelOfAsync("Counter",
            Program("Public Sub Main()", "Dim c As New Counter", "c.Twice 2", "Debug.Print c.Twice(2)", "End Sub"), classes: [twice]);

        CollectionAssert.AreEqual(new[] { true, false }, Declaration(model, "Twice").ReferencedFrom!.Value.Select(site => site.DiscardsResult).ToArray());
    }

    [TestMethod]
    public async Task AModuleAskedForAlone_IsCountedOverTheWholeProject()
        => Assert.AreEqual(2, Declaration(await ModelOfAsync("Counter", Program(UsesCounter), moduleName: "Counter"), "Add").References?.Reads);

    [TestMethod]
    public async Task APublicVariableOfAPrivateModule_IsCountedAcrossTheProject()
    {
        var model = await ModelOfAsync("Shared", Program("Public Sub Main()", "Total = 1", "Debug.Print Total", "End Sub"),
            modules: [("Shared", ModuleWorkspace.StandardModule("Shared", "Option Private Module", "Public Total As Long", "Public Unread As Long"))]);

        Assert.AreEqual(new DeclarationReferences(1, 1, 0), Declaration(model, "Total").References);
        Assert.AreEqual(new DeclarationReferences(0, 0, 0), Declaration(model, "Unread").References);
    }

    [TestMethod]
    public async Task APublicConstantOfAPrivateModule_IsCountedAcrossTheProject_InTheCodeAndTheDeclarationsOfEachModule()
    {
        var model = await ModelOfAsync("Shared",
            Program("Private Const Ceiling As Long = Limit + 1", "Public Sub Main()", "Debug.Print Limit, Ceiling", "End Sub"),
            modules: [("Shared", ModuleWorkspace.StandardModule("Shared", "Option Private Module",
                "Public Const Limit As Long = 10", "Public Const Twice As Long = Limit * 2", "Public Const Unread As Long = 1"))]);
        var limit = Declaration(model, "Limit");

        Assert.AreEqual(new DeclarationReferences(3, 0, 0), limit.References);
        Assert.AreEqual(2, limit.ReferencedFrom!.Value.Count(site => site.Procedure is null), "the value of a constant is in no procedure");
        Assert.AreEqual(new DeclarationReferences(0, 0, 0), Declaration(model, "Twice").References);
        Assert.AreEqual(new DeclarationReferences(0, 0, 0), Declaration(model, "Unread").References);
    }

    [TestMethod]
    public async Task APublicConstantOfAPublicModule_HasNoCount_TheProjectsThatReferenceThisOneCanReferToIt()
    {
        var model = await ModelOfAsync("Shared", Program("Public Sub Main()", "Debug.Print Limit", "End Sub"),
            modules: [("Shared", ModuleWorkspace.StandardModule("Shared", "Public Const Limit As Long = 10"))]);

        Assert.IsNull(Declaration(model, "Limit").References);
    }

    [TestMethod]
    public async Task APublicVariableOfAPublicModule_HasNoCount_TheProjectsThatReferenceThisOneCanReferToIt()
    {
        var model = await ModelOfAsync("Shared", Program("Public Sub Main()", "Total = 1", "End Sub"),
            modules: [("Shared", ModuleWorkspace.StandardModule("Shared", "Public Total As Long"))]);

        Assert.IsNull(Declaration(model, "Total").References);
        Assert.IsNull(Declaration(model, "Total").ReferencedFrom);
    }

    [TestMethod]
    public async Task APublicProcedureOfAPrivateModule_HasNoCount_AStringCanNameIt()
    {
        var model = await ModelOfAsync("Shared", Program("Public Sub Main()", "Helper", "End Sub"),
            modules: [("Shared", ModuleWorkspace.StandardModule("Shared", "Option Private Module", "Public Sub Helper()", "End Sub"))]);

        Assert.IsNull(Declaration(model, "Helper").References);
    }

    [TestMethod]
    public async Task AProjectWithAProcedureNotFullyAnalyzed_CountsNothingAcrossItsModules()
    {
        var model = await ModelOfAsync("Counter", Program(UsesCounter),
            modules: [("Broken", ModuleWorkspace.StandardModule("Broken", "Option Explicit", "Public Sub Bad()", "undeclared = 1", "End Sub"))]);

        Assert.IsNull(Declaration(model, "Add").References);
        Assert.IsNull(Declaration(model, "Add").ReferencedFrom);
    }

    [TestMethod]
    public async Task AModuleLocalCount_IsStatedWithWhereEachReferenceIs()
    {
        var model = await ModelOfAsync("Program", Program("Public Sub Main()", "Dim n As Long", "n = 1", "Debug.Print n", "End Sub"));
        var local = Declaration(model, "n");

        Assert.AreEqual(new DeclarationReferences(1, 1, 0), local.References);
        CollectionAssert.AreEqual(
            new[] { DeclarationReferenceKind.Write, DeclarationReferenceKind.Read }, local.ReferencedFrom!.Value.Select(site => site.Kind).ToArray());
    }
}
