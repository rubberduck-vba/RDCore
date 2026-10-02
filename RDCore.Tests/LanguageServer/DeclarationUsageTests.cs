using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Static;
using RDCore.SDK.Semantics.Static.Abstract;
using RDCore.SDK.Workspace;
using System.Collections.Immutable;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// The static pass counts how the declarations of a module are used by its own code: what is read, what is written to, and what nothing refers to.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.4.3 Declarations")]
public sealed class DeclarationUsageTests
{
    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly Uri Main = new UriBuilder(Root) { Fragment = "Main" }.Uri;

    // the facts of the declarations of a standard module whose source is `source`.
    private static ImmutableArray<DeclarationFact> Facts(params string[] source)
    {
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Main.bas"), $"Attribute VB_Name = \"Main\"\r\n{string.Join("\r\n", source)}\r\n");
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

        var composition = WorkspaceSymbolResolver.ComposeWithScopes(Root, [(Main, ModuleType.StdModule, parse)], new IntrinsicSymbolResolver());
        var symbols = new SyntaxTreeSymbolProvider(Root, Main, ModuleType.StdModule, parse, composition.Resolver, withImplicitDeclarations: true).ProvideSymbols().ToList();
        var members = symbols.OfType<VBTypeMemberSymbol>().Where(member => member.ParentUri.AbsoluteUri == Main.AbsoluteUri).ToList();

        var models = new List<ProcedureSemanticModel>();
        foreach (var declaration in parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>())
        {
            var procedure = members.First(member => string.Equals(member.Name, declaration.Name, StringComparison.OrdinalIgnoreCase)
                && member is VBProcedureMemberSymbol or VBFunctionMemberSymbol or VBPropertyGetMemberSymbol);
            models.Add(StatementStaticSemanticsEvaluator.Analyze(
                procedure.SemanticId, new StatementBlock([.. declaration.Children]), kind: declaration.MemberKind,
                context: new StaticEvaluationContext(composition.Resolver, composition.ScopeTree.ScopeFor(procedure.Uri))));
        }

        // a provider may state a procedure's locals and parameters as symbols of their own, as well as on the procedure.
        return DeclarationUsage.Of(DeclarationUsage.DeclaredBy(members).Concat(symbols), models);
    }

    private static DeclarationFact Fact(ImmutableArray<DeclarationFact> facts, string name) => facts.Single(fact => fact.Name == name);

    [TestMethod]
    public void ALocalThatNothingRefersTo_IsUnreferenced()
        => Assert.IsTrue(Fact(Facts("Public Sub Run()", "Dim unused As Long", "End Sub"), "unused").IsUnreferenced);

    [TestMethod]
    public void ALocalThatIsOnlyRead_IsNeverAssigned()
    {
        var fact = Fact(Facts("Public Sub Run()", "Dim x", "Dim n As Long", "x = n", "End Sub"), "n");

        Assert.AreEqual(1, fact.Reads);
        Assert.AreEqual(0, fact.Writes);
        Assert.IsTrue(fact.IsNeverAssigned);
    }

    [TestMethod]
    public void ALocalThatIsWrittenAndRead_IsCountedBoth()
    {
        var fact = Fact(Facts("Public Sub Run()", "Dim x", "Dim n As Long", "n = 1", "x = n", "End Sub"), "n");

        Assert.AreEqual(1, fact.Reads);
        Assert.AreEqual(1, fact.Writes);
        Assert.IsFalse(fact.IsNeverAssigned);
    }

    [TestMethod]
    public void AnElementOfAnArray_IsWrittenThroughTheArray()
        => Assert.AreEqual(1, Fact(Facts("Public Sub Run()", "Dim a(3) As Long", "a(1) = 5", "End Sub"), "a").Writes);

    [TestMethod]
    public void TheCounterOfALoop_IsWritten()
        => Assert.AreEqual(1, Fact(Facts("Public Sub Run()", "Dim i As Long", "For i = 1 To 3", "Next", "End Sub"), "i").Writes);

    [TestMethod]
    public void ReDim_IsAWriteToTheArray()
        => Assert.AreEqual(1, Fact(Facts("Public Sub Run()", "Dim a() As Long", "ReDim a(3)", "End Sub"), "a").Writes);

    [TestMethod]
    public void AProcedureThatIsCalled_IsRead_AndOneThatIsNotIsUnreferenced()
    {
        var facts = Facts("Private Sub Work()", "End Sub", "Private Sub Idle()", "End Sub", "Public Sub Run()", "Work", "End Sub");

        Assert.AreEqual(1, Fact(facts, "Work").Reads);
        Assert.IsTrue(Fact(facts, "Idle").IsUnreferenced);
    }

    [TestMethod]
    public void TheResultOfAFunction_IsWrittenByAssigningItsName()
        => Assert.AreEqual(1, Fact(Facts("Public Function F() As Long", "F = 3", "End Function"), "F").Writes);

    [TestMethod]
    public void AParameterThatIsNeverUsed_IsUnreferenced()
        => Assert.IsTrue(Fact(Facts("Public Sub Run(ByVal n As Long)", "End Sub"), "n").IsUnreferenced);

    [TestMethod]
    public void AVariableThatWasNeverDeclared_IsImplicit()
    {
        var facts = Facts("Public Sub Run()", "Dim x", "x = 1", "y = 2", "End Sub");

        Assert.IsTrue(Fact(facts, "y").IsImplicit);
        Assert.IsFalse(Fact(facts, "x").IsImplicit);
    }

    [TestMethod]
    public void TheAccessorsOfAProperty_AreOneDeclaration()
    {
        var facts = Facts("Private mV As Long", "Public Property Get V() As Long", "V = mV", "End Property", "Public Property Let V(ByVal n As Long)", "mV = n", "End Property");

        Assert.AreEqual(DeclarationKind.Property, Fact(facts, "V").Kind);
    }

    [TestMethod]
    public void AModuleLevelVariable_IsCountedAcrossTheProceduresThatUseIt()
    {
        var facts = Facts("Private Total As Long", "Public Sub Add()", "Total = Total + 1", "End Sub", "Public Sub Show()", "Debug.Print Total", "End Sub");
        var total = Fact(facts, "Total");

        Assert.AreEqual(1, total.Writes);
        Assert.AreEqual(2, total.Reads);
        Assert.AreEqual(AccessModifier.Private, total.Access);
    }
}
