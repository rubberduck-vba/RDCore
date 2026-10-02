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
/// The static pass counts the references to the declarations of a module, and states a count only when it is the whole truth: when nothing outside the code
/// analyzed can refer to the declaration, and the code that could was analyzed completely.
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

    private static DeclarationReferences References(ImmutableArray<DeclarationFact> facts, string name)
    {
        var references = Fact(facts, name).References;
        Assert.IsNotNull(references, $"'{name}' has references that are known");
        return references.Value;
    }

    // ---- what is counted ----

    [TestMethod]
    public void ALocalThatNothingRefersTo_HasNoReferences()
        => Assert.AreEqual(default, References(Facts("Public Sub Run()", "Dim unused As Long", "End Sub"), "unused"));

    [TestMethod]
    public void ALocalThatIsOnlyRead_IsReadAndNotWritten()
        => Assert.AreEqual(new DeclarationReferences(1, 0, 0), References(Facts("Public Sub Run()", "Dim x", "Dim n As Long", "x = n", "End Sub"), "n"));

    [TestMethod]
    public void ALocalThatIsWrittenAndRead_IsCountedBoth()
        => Assert.AreEqual(new DeclarationReferences(1, 1, 0), References(Facts("Public Sub Run()", "Dim x", "Dim n As Long", "n = 1", "x = n", "End Sub"), "n"));

    [TestMethod]
    public void AnElementOfAnArray_IsWrittenThroughTheArray()
        => Assert.AreEqual(1, References(Facts("Public Sub Run()", "Dim a(3) As Long", "a(1) = 5", "End Sub"), "a").Writes);

    [TestMethod]
    public void TheCounterOfALoop_IsWritten()
        => Assert.AreEqual(1, References(Facts("Public Sub Run()", "Dim i As Long", "For i = 1 To 3", "Next", "End Sub"), "i").Writes);

    [TestMethod]
    public void ReDim_IsAWriteToTheArray()
        => Assert.AreEqual(1, References(Facts("Public Sub Run()", "Dim a() As Long", "ReDim a(3)", "End Sub"), "a").Writes);

    [TestMethod]
    public void AVariablePassedToAProcedure_IsPassedAsAnArgument_WhichMayWriteToItThroughByRef()
    {
        var facts = Facts("Private Sub Work(p As Long)", "End Sub", "Public Sub Run()", "Dim n As Long", "Work n", "End Sub");

        Assert.AreEqual(new DeclarationReferences(0, 0, 1), References(facts, "n"));
    }

    [TestMethod]
    public void AnArgumentWrittenWithByVal_IsRead_ForItCanNotBeWrittenTo()
    {
        var facts = Facts("Private Sub Work(ByVal n As Long)", "End Sub", "Public Sub Run()", "Dim local As Long", "Work ByVal local", "End Sub");

        Assert.AreEqual(new DeclarationReferences(1, 0, 0), References(facts, "local"));
    }

    [TestMethod]
    public void AnIndexOfAnArray_IsNoArgument()
        => Assert.AreEqual(new DeclarationReferences(1, 0, 0), References(Facts("Public Sub Run()", "Dim a(3) As Long", "Dim i As Long", "a(i) = 1", "End Sub"), "i"));

    [TestMethod]
    public void AParameterThatIsNeverUsed_HasNoReferences()
        => Assert.AreEqual(default, References(Facts("Public Sub Run(ByVal n As Long)", "End Sub"), "n"));

    [TestMethod]
    public void AVariableThatWasNeverDeclared_IsImplicit()
    {
        var facts = Facts("Public Sub Run()", "Dim x", "x = 1", "y = 2", "End Sub");

        Assert.IsTrue(Fact(facts, "y").IsImplicit);
        Assert.IsFalse(Fact(facts, "x").IsImplicit);
        Assert.AreEqual(1, References(facts, "y").Writes);
    }

    [TestMethod]
    public void APrivateModuleVariable_IsCountedAcrossTheProceduresThatUseIt()
    {
        var facts = Facts("Private Total As Long", "Public Sub Add()", "Total = Total + 1", "End Sub", "Public Sub Show()", "Debug.Print Total", "End Sub");

        Assert.AreEqual(new DeclarationReferences(2, 1, 0), References(facts, "Total"));
        Assert.AreEqual(AccessModifier.Private, Fact(facts, "Total").Access);
    }

    // ---- what is not known is not stated ----

    [TestMethod]
    public void APublicVariable_HasNoCount_ForAnotherModuleCanReferToIt()
        => Assert.IsNull(Fact(Facts("Public Total As Long", "Public Sub Run()", "End Sub"), "Total").References);

    [TestMethod]
    [DataRow("Private Sub Work()\r\nEnd Sub", "Work")]
    [DataRow("Public Sub Work()\r\nEnd Sub", "Work")]
    [DataRow("Public Property Get Work() As Long\r\nEnd Property", "Work")]
    public void AProcedureAPropertyOrAnEvent_HasNoCount_ForItIsCalledByMoreThanWhatNamesIt(string declaration, string name)
        => Assert.IsNull(Fact(Facts(declaration, "Public Sub Run()", "Work", "End Sub"), name).References);

    [TestMethod]
    public void TheAccessorsOfAProperty_AreOneDeclaration()
    {
        var facts = Facts("Private mV As Long", "Public Property Get V() As Long", "V = mV", "End Property", "Public Property Let V(ByVal n As Long)", "mV = n", "End Property");

        Assert.AreEqual(DeclarationKind.Property, Fact(facts, "V").Kind);
    }

    [TestMethod]
    public void ALocalOfAProcedureWithAnError_HasNoCount_ForWhatIsAfterTheErrorWasNotLookedAt()
        => Assert.IsNull(Fact(Facts("Option Explicit", "Public Sub Run()", "Dim x", "Dim n As Long", "x = Undeclared + n", "End Sub"), "n").References);

    [TestMethod]
    public void AModuleVariable_HasNoCount_WhenAnyProcedureOfTheModuleHasAnError()
    {
        var facts = Facts("Option Explicit", "Private Total As Long", "Public Sub Show()", "Debug.Print Total", "End Sub", "Public Sub Broken()", "Dim x", "x = Undeclared", "End Sub");

        Assert.IsNull(Fact(facts, "Total").References);
    }

    [TestMethod]
    public void AConstant_HasNoCount_ForItIsAlsoReferredToByDeclarations()
        // the bounds of an array, and the value of another constant, are expressions of declarations: not evaluated as those of a procedure are.
        => Assert.IsNull(Fact(Facts("Private Const Size As Long = 3", "Public Sub Run()", "Dim a(1 To Size) As Long", "End Sub"), "Size").References);

    [TestMethod]
    public void AVariableInTheBoundsOfAnArray_IsNeverStatedAsNeverRead()
    {
        // not valid VBA, which wants a constant expression: but the pass does not say so, and a count that left the reference out would be a lie.
        var references = Fact(Facts("Public Sub Run()", "Dim n As Long", "Dim a(1 To n) As Long", "End Sub"), "n").References;

        Assert.IsTrue(references is null || references.Value.Reads == 1);
    }
}
