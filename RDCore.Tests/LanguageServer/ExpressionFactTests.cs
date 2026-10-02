using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Semantics.Static;
using RDCore.SDK.Semantics.Static.Abstract;
using RDCore.SDK.Workspace;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// The static pass says what it found out about each expression of a procedure: what it names, what it is bound to, and how it is written
/// (<strong>MS-VBAL §5.6.1</strong>).
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.6 Expressions")]
public sealed class ExpressionFactTests
{
    private static readonly Uri Root = new("file://rdcore-test");

    private static Uri ModuleUri(string name) => new UriBuilder(Root) { Fragment = name }.Uri;

    private static (Uri Uri, ModuleType ModuleType, ModuleParseResult Parse) Class(string name, params string[] body)
        => (ModuleUri(name), ModuleType.ClassModule, new ModuleParser().Parse(
            new Uri($"file:///c:/ws/{name}.cls"), $"Attribute VB_Name = \"{name}\"\r\n{string.Join("\r\n", body)}\r\n"));

    private static readonly (Uri Uri, ModuleType ModuleType, ModuleParseResult Parse) Widget = Class("Widget", "Public Size As Long");

    private static readonly (Uri Uri, ModuleType ModuleType, ModuleParseResult Parse) Table = Class("Table",
        "Public Property Get Item(ByVal i As Long) As Long", "Attribute Item.VB_UserMemId = 0", "Item = i", "End Property");

    // the model of Main.Run, whose body is `body`, in a module that also has `moduleLevel` declarations.
    private static (ProcedureSemanticModel Model, MemberDeclarationNode Run, WorkspaceComposition Composition) Analyze(
        string[] moduleLevel, string[] body, params (Uri Uri, ModuleType ModuleType, ModuleParseResult Parse)[] modules)
    {
        var main = (ModuleUri("Main"), ModuleType.StdModule, new ModuleParser().Parse(
            new Uri("file:///c:/ws/Main.bas"),
            $"Attribute VB_Name = \"Main\"\r\nOption Explicit\r\n{string.Join("\r\n", moduleLevel)}\r\nSub Run()\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n"));
        Assert.IsTrue(main.Item3.IsSuccess, string.Join("; ", main.Item3.SyntaxErrors.Select(error => error.Verbose)));
        var composition = WorkspaceSymbolResolver.ComposeWithScopes(Root, [.. modules, main], new IntrinsicSymbolResolver());

        Assert.IsTrue(composition.ScopeTree.TryGetScope(ModuleUri("Main.Run"), out var scope));
        var run = main.Item3.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single(member => member.Name == "Run");
        var model = StatementStaticSemanticsEvaluator.Analyze(
            new SemanticId(ModuleUri("Main.Run")), new StatementBlock([.. run.Children]), kind: run.MemberKind,
            context: new StaticEvaluationContext(composition.Resolver, scope));
        return (model, run, composition);
    }

    // the fact of the value of the last assignment of the body.
    private static ExpressionFact LastValue(string[] moduleLevel, string[] body, params (Uri Uri, ModuleType ModuleType, ModuleParseResult Parse)[] modules)
    {
        var (model, run, _) = Analyze(moduleLevel, body, modules);
        Assert.IsTrue(model.IsValid, string.Join("; ", model.CompileErrors.Select(error => error.Verbose)));
        return model.Expressions[run.Children.OfType<AssignmentStatementNode>().Last().Value.Identity];
    }

    [TestMethod]
    public void ALiteral_IsAValue_WrittenAsOne()
    {
        var fact = LastValue([], ["Dim x", "x = 5"]);

        Assert.AreEqual(ExpressionClassification.Value, fact.Classification);
        Assert.AreEqual(ValueExpressionSemanticFlags.Literal, fact.Flags);
        Assert.IsNull(fact.Binding);
        Assert.IsNotNull(fact.DeclaredType);
    }

    [TestMethod]
    public void ALocalVariable_IsAVariable_BoundToItsSymbol()
    {
        var fact = LastValue([], ["Dim x", "Dim n As Long", "x = n"]);

        Assert.AreEqual(ExpressionClassification.Variable, fact.Classification);
        Assert.AreEqual(VBLongType.TypeInfo, fact.DeclaredType);
        Assert.AreEqual(ModuleUri("Main.Run.n").AbsoluteUri, fact.Binding!.Value.Uri.AbsoluteUri, ignoreCase: true);
        Assert.AreEqual((ValueExpressionSemanticFlags)0, fact.Flags);
    }

    [TestMethod]
    public void ANameWrittenInAnotherCaseThanItsDeclaration_IsAFact()
        => Assert.IsTrue(LastValue([], ["Dim x", "Dim Total As Long", "x = TOTAL"]).Flags.HasFlag(ValueExpressionSemanticFlags.CaseMismatch));

    [TestMethod]
    public void ANameWrittenAsItIsDeclared_IsNoMismatch()
        => Assert.IsFalse(LastValue([], ["Dim x", "Dim Total As Long", "x = Total"]).Flags.HasFlag(ValueExpressionSemanticFlags.CaseMismatch));

    [TestMethod]
    public void AConstant_IsAConstant()
        => Assert.AreEqual(ExpressionClassification.Constant, LastValue(["Public Const Limit As Long = 9"], ["Dim x", "x = Limit"]).Classification);

    [TestMethod]
    public void AFunctionNamedWithNoArgumentList_IsACall()
    {
        var fact = LastValue(["Public Function F() As Long", "End Function"], ["Dim x", "x = F"]);

        Assert.AreEqual(ExpressionClassification.Function, fact.Classification);
        Assert.IsTrue(fact.Flags.HasFlag(ValueExpressionSemanticFlags.ProcedureCall));
        Assert.IsNotNull(fact.Binding);
    }

    [TestMethod]
    public void AnIndexExpressionOnAFunction_IsACallOfIt()
    {
        var fact = LastValue(["Public Function G(ByVal n As Long) As Long", "End Function"], ["Dim x", "x = G(1)"]);

        Assert.AreEqual(ExpressionClassification.Value, fact.Classification);
        Assert.IsTrue(fact.Flags.HasFlag(ValueExpressionSemanticFlags.ProcedureCall));
        Assert.IsNotNull(fact.Binding);
    }

    [TestMethod]
    public void AMemberOfAClass_IsBoundToTheMember()
    {
        var fact = LastValue([], ["Dim x", "Dim w As Widget", "x = w.Size"], Widget);

        Assert.AreEqual(ExpressionClassification.Variable, fact.Classification);
        Assert.AreEqual(VBLongType.TypeInfo, fact.DeclaredType);
        Assert.IsNotNull(fact.Binding);
    }

    [TestMethod]
    public void AMemberOfAnObjectOfNoParticularClass_IsLateBound()
    {
        var fact = LastValue([], ["Dim x", "Dim o As Object", "x = o.Anything"]);

        Assert.AreEqual(ExpressionClassification.UnboundMember, fact.Classification);
        Assert.IsTrue(fact.Flags.HasFlag(ValueExpressionSemanticFlags.LateBound));
        Assert.IsNull(fact.Binding);
    }

    [TestMethod]
    public void IndexingAnObject_IsACallOfItsDefaultMember()
    {
        var fact = LastValue([], ["Dim x", "Dim t As Table", "x = t(3)"], Table);

        Assert.IsTrue(fact.Flags.HasFlag(ValueExpressionSemanticFlags.DefaultMember));
        Assert.IsNotNull(fact.Binding);
    }

    [TestMethod]
    public void ADictionaryAccess_IsOneAndLateBoundOnAnObject()
    {
        var fact = LastValue([], ["Dim x", "Dim o As Object", "x = o!Name"]);

        Assert.IsTrue(fact.Flags.HasFlag(ValueExpressionSemanticFlags.DictionaryAccess));
        Assert.IsTrue(fact.Flags.HasFlag(ValueExpressionSemanticFlags.LateBound));
    }

    [TestMethod]
    [DataRow("Call Work", true)]
    [DataRow("Work", false)]
    public void TheCalleeOfACallStatement_RecordsWhetherTheCallKeywordWasWritten(string statement, bool explicitCall)
    {
        var (model, run, _) = Analyze(["Public Sub Work()", "End Sub"], [statement]);

        var call = run.Children.OfType<CallStatementNode>().Single();
        Assert.AreEqual(explicitCall, model.Expressions[call.Callee.Identity].Flags.HasFlag(ValueExpressionSemanticFlags.ExplicitCallKeyword));
    }

    [TestMethod]
    public void AnOperand_HasAFactOfItsOwn()
    {
        var (model, run, _) = Analyze([], ["Dim x", "Dim n As Long", "x = n + 1"]);
        var sum = (RDCore.SDK.Model.AST.Expressions.VBBinaryOperatorExpressionNode)run.Children.OfType<AssignmentStatementNode>().Last().Value;

        Assert.AreEqual(ExpressionClassification.Variable, model.Expressions[sum.Left.Identity].Classification);
        Assert.AreEqual(ExpressionClassification.Value, model.Expressions[sum.Right.Identity].Classification);
        Assert.IsTrue(model.Expressions[sum.Identity].Flags == 0);
    }

    [TestMethod]
    public void AnExpressionThatIsAnError_HasAFactThatSaysSo()
    {
        var (model, run, _) = Analyze([], ["Dim x", "x = Undeclared"]);

        var fact = model.Expressions[run.Children.OfType<AssignmentStatementNode>().Last().Value.Identity];
        Assert.IsNull(fact.DeclaredType);
        Assert.IsNotNull(fact.Error);
    }

    [TestMethod]
    public void WithNoWorkspaceToResolveNamesIn_TheModelHasNoExpressionFacts()
    {
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Main.bas"), "Sub Run()\r\nDim x\r\nx = 5\r\nEnd Sub\r\n");
        var run = parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();

        var model = StatementStaticSemanticsEvaluator.Analyze(new SemanticId(ModuleUri("Main.Run")), new StatementBlock([.. run.Children]));

        Assert.IsEmpty(model.Expressions);
    }
}
