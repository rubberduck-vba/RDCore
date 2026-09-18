using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Semantics.Static;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// A declared type names a workspace type, and the name binds through the type binding context
/// (MS-VBAL §5.6.4) - however the local, field or parameter that carries it is itself named - to a type
/// whose members are then read from its declaration, so a class typed after itself resolves at any depth.
/// Parse-driven: real source through <c>WorkspaceSymbolResolver</c> and the static evaluators.
/// </summary>
[TestClass]
public sealed class DeclaredTypeBindingTests
{
    private static readonly Uri Root = new("file://rdcore-test");

    private static Uri ModuleUri(string name) => new UriBuilder(Root) { Fragment = name }.Uri;

    private static (Uri Uri, ModuleType ModuleType, ModuleParseResult Parse) Std(string name, string body)
        => (ModuleUri(name), ModuleType.StdModule, new ModuleParser().Parse(new Uri($"file:///c:/ws/{name}.bas"), $"Attribute VB_Name = \"{name}\"\r\n{body}"));

    private static (Uri Uri, ModuleType ModuleType, ModuleParseResult Parse) Class(string name, string body)
        => (ModuleUri(name), ModuleType.ClassModule, new ModuleParser().Parse(new Uri($"file:///c:/ws/{name}.cls"), $"Attribute VB_Name = \"{name}\"\r\n{body}"));

    // Composes the modules plus `main`, and evaluates the value expression of the last assignment in
    // main's Run procedure from Run's own scope.
    private static StaticSemanticsEvaluationResult EvaluateLastAssignment(
        (Uri Uri, ModuleType ModuleType, ModuleParseResult Parse) main, string? projectName,
        params (Uri Uri, ModuleType ModuleType, ModuleParseResult Parse)[] modules)
    {
        var composition = WorkspaceSymbolResolver.ComposeWithScopes(Root, [.. modules, main], new IntrinsicSymbolResolver(), projectName);

        Assert.IsTrue(composition.ScopeTree.TryGetScope(ModuleUri("Main.Run"), out var scope));
        var run = main.Parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single(member => member.Name == "Run");
        var value = run.Children.OfType<AssignmentStatementNode>().Last().Value;

        return ExpressionStaticSemanticsEvaluator.Evaluate(new StaticEvaluationContext(composition.Resolver, scope), value);
    }

    private static VBType TypeOfLastAssignment(string[] body, params (Uri Uri, ModuleType ModuleType, ModuleParseResult Parse)[] modules)
    {
        var main = Std("Main", $"Sub Run()\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n");
        var result = EvaluateLastAssignment(main, projectName: null, modules);

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Verbose);
        return result.Result!;
    }

    [TestMethod]
    public void AClassTypedAfterItself_ResolvesAnyNumberOfHops()
    {
        var node = Class("Node", "Public Value As Long\r\nPublic Property Get NextNode() As Node\r\nEnd Property\r\n");

        var value = TypeOfLastAssignment(["Dim n As Node", "Dim r", "r = n.NextNode.NextNode.NextNode.Value"], node);
        var link = TypeOfLastAssignment(["Dim n As Node", "Dim r", "Set r = n.NextNode.NextNode"], node);

        Assert.AreEqual(VBLongType.TypeInfo, value);
        Assert.AreEqual("Node", Assert.IsInstanceOfType<VBClassType>(link).Name);
    }

    [TestMethod]
    public void TwoClassesTypedAfterEachOther_ResolveThroughBothWays()
    {
        var alpha = Class("Alpha", "Public Partner As Beta\r\n");
        var beta = Class("Beta", "Public Origin As Alpha\r\nPublic Id As Long\r\n");

        var id = TypeOfLastAssignment(["Dim x As Alpha", "Dim r", "r = x.Partner.Origin.Partner.Id"], alpha, beta);

        Assert.AreEqual(VBLongType.TypeInfo, id);
    }

    [TestMethod]
    public void AUserDefinedTypeNestedThreeDeep_ResolvesToItsInnermostField()
        // each Type's field is typed by the next Type; the snapshot a field's type was built with predates
        // the next Type's own bound fields, so this only resolves by reading each Type from its declaration.
    {
        var types = Std("Types",
            "Public Type Inner\r\n    Amount As Long\r\nEnd Type\r\n" +
            "Public Type Middle\r\n    Part As Inner\r\nEnd Type\r\n" +
            "Public Type Outer\r\n    Part As Middle\r\nEnd Type\r\n");

        var amount = TypeOfLastAssignment(["Dim o As Outer", "Dim r", "r = o.Part.Part.Amount"], types);

        Assert.AreEqual(VBLongType.TypeInfo, amount);
    }

    [TestMethod]
    public void ALocalNamedLikeItsClass_StillBindsTheClass()
        // `Dim Widget As Widget`: the local is a candidate only for the name in the value expression.
    {
        var widget = Class("Widget", "Public Size As Long\r\n");

        var size = TypeOfLastAssignment(["Dim Widget As Widget", "Dim r", "r = Widget.Size"], widget);

        Assert.AreEqual(VBLongType.TypeInfo, size);
    }

    [TestMethod]
    public void ADeclaredTypeIsBound_OnAFieldAParameterAndAReturnType()
    {
        var widget = Class("Widget", "Public Size As Long\r\n");
        var library = Class("Library",
            "Public Field As Widget\r\n" +
            "Public Function Make(ByVal source As Widget) As Widget\r\nEnd Function\r\n");
        var composition = WorkspaceSymbolResolver.ComposeWithScopes(Root, [widget, library], new IntrinsicSymbolResolver());

        var module = Assert.IsInstanceOfType<VBClassModuleSymbol>(
            composition.Resolver.ResolveType("Library", ScopeKind.Global, StaticSymbol.GlobalUri).Symbol);
        var field = module.Members.Single(member => member.Name == "Field");
        var make = Assert.IsInstanceOfType<VBFunctionMemberSymbol>(module.Members.Single(member => member.Name == "Make"));

        Assert.AreEqual("Widget", Assert.IsInstanceOfType<VBClassType>(field.ResolvedType).Name);
        Assert.AreEqual("Widget", Assert.IsInstanceOfType<VBClassType>(make.ResolvedType).Name);
        // a class module's member also carries the implicit Me parameter, so pick the declared one by name.
        Assert.AreEqual("Widget", Assert.IsInstanceOfType<VBClassType>(make.Parameters.Single(parameter => parameter.Name == "source").ResolvedType).Name);
    }

    [TestMethod]
    public void NewQualifiedByTheProjectsName_IgnoresALocalOfThatName()
        // the legacy Rubberduck bug (issue #973): `New MyProject.Class` bound MyProject to the local variable.
    {
        var widget = Class("Widget", "Public Size As Long\r\n");
        var main = Std("Main", "Sub Run()\r\nDim MyProject As Widget\r\nDim r\r\nSet r = New MyProject.Widget\r\nEnd Sub\r\n");

        var result = EvaluateLastAssignment(main, "MyProject", widget);

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Verbose);
        Assert.AreEqual("Widget", Assert.IsInstanceOfType<VBClassType>(result.Result).Name);
    }

    [TestMethod]
    public void NewQualifiedByTheProjectsName_IsNotTheProject_WhenTheModuleDeclaresATypeOfThatName()
        // MS-VBAL 5.6.10: the enclosing module's own types are the first tier of the type binding context.
    {
        var widget = Class("Widget", "Public Size As Long\r\n");
        var main = Std("Main", "Private Type MyProject\r\n    Value As Long\r\nEnd Type\r\nSub Run()\r\nDim r\r\nSet r = New MyProject.Widget\r\nEnd Sub\r\n");

        var result = EvaluateLastAssignment(main, "MyProject", widget);

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(VBCompileErrorId.UserDefinedTypeNotDefined, result.ErrorInfo!.VBCompileErrorId);
    }
}
