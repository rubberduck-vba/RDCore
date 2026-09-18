using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Semantics.Static;
using RDCore.SDK.Semantics.Static.Abstract;
using System.Collections.Immutable;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// MS-VBAL §5.2.4.1.2: a class module whose <c>VB_PredeclaredId</c> attribute is <c>True</c> has a default
/// instance variable named after the class, so the class name is a value in the default binding context.
/// A class that is not predeclared is not a value there at all. Parse-driven: real source through
/// <c>WorkspaceSymbolResolver</c> and the static evaluators.
/// </summary>
[TestClass]
public sealed class PredeclaredInstanceTests
{
    private static readonly Uri Root = new("file://rdcore-test");

    private static Uri ModuleUri(string name) => new UriBuilder(Root) { Fragment = name }.Uri;

    private static (Uri Uri, ModuleType ModuleType, ModuleParseResult Parse) Class(string name, string body)
        => (ModuleUri(name), ModuleType.ClassModule, new ModuleParser().Parse(new Uri($"file:///c:/ws/{name}.cls"), $"Attribute VB_Name = \"{name}\"\r\n{body}"));

    private static (Uri Uri, ModuleType ModuleType, ModuleParseResult Parse) Widget(string attributes = "")
        => Class("Widget", $"{attributes}Public Size As Long\r\n");

    // Option Explicit is on, so a name that binds to nothing is an error rather than a deferred Unknown.
    private static (StaticEvaluationContext Context, MemberDeclarationNode Run) Run(string[] body, params (Uri Uri, ModuleType ModuleType, ModuleParseResult Parse)[] modules)
    {
        var main = (ModuleUri("Main"), ModuleType.StdModule, new ModuleParser().Parse(
            new Uri("file:///c:/ws/Main.bas"), $"Attribute VB_Name = \"Main\"\r\nOption Explicit\r\nSub Run()\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n"));
        var composition = WorkspaceSymbolResolver.ComposeWithScopes(Root, [.. modules, main], new IntrinsicSymbolResolver());

        Assert.IsTrue(composition.ScopeTree.TryGetScope(ModuleUri("Main.Run"), out var scope));
        var run = main.Item3.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single(member => member.Name == "Run");
        return (new StaticEvaluationContext(composition.Resolver, scope), run);
    }

    private static ImmutableArray<VBCompileErrorInfo> Walk(string[] body, params (Uri Uri, ModuleType ModuleType, ModuleParseResult Parse)[] modules)
    {
        var (context, run) = Run(body, modules);
        return StatementStaticSemanticsEvaluator.Evaluate(context, new StatementBlock([.. run.Children]));
    }

    private static StaticSemanticsEvaluationResult ValueOfLastAssignment(string[] body, params (Uri Uri, ModuleType ModuleType, ModuleParseResult Parse)[] modules)
    {
        var (context, run) = Run(body, modules);
        return ExpressionStaticSemanticsEvaluator.Evaluate(context, run.Children.OfType<AssignmentStatementNode>().Last().Value);
    }

    [TestMethod]
    public void APredeclaredClassName_IsAVariableOfThatClass_SoItsMembersBindThroughIt()
    {
        var widget = Widget("Attribute VB_PredeclaredId = True\r\n");

        var size = ValueOfLastAssignment(["Dim r", "r = Widget.Size"], widget);
        var instance = ValueOfLastAssignment(["Dim r", "Set r = Widget"], widget);

        Assert.IsTrue(size.IsSuccess, size.ErrorInfo?.Verbose);
        Assert.AreEqual(VBLongType.TypeInfo, size.Result);
        Assert.AreEqual("Widget", Assert.IsInstanceOfType<VBClassType>(instance.Result).Name);
        Assert.IsEmpty(Walk(["Dim r", "r = Widget.Size"], widget));
    }

    [TestMethod]
    public void AClassThatIsNotPredeclared_IsNotAValue_UnderOptionExplicit()
        // MS-VBAL 5.6.10: no default-context tier holds a class module, and 5.2.4.1.2 gives a class that is
        // not predeclared no publicly expressible default instance name.
    {
        var errors = Walk(["Dim r", "r = Widget.Size"], Widget());

        Assert.HasCount(1, errors);
        Assert.AreEqual(VBCompileErrorId.VariableNotDefined, errors[0].VBCompileErrorId);
        Assert.AreEqual("Widget", errors[0].Verbose);
    }

    [TestMethod]
    public void AClassWithVB_PredeclaredIdFalse_IsNotPredeclared()
    {
        var errors = Walk(["Dim r", "r = Widget.Size"], Widget("Attribute VB_PredeclaredId = False\r\n"));

        Assert.HasCount(1, errors);
        Assert.AreEqual(VBCompileErrorId.VariableNotDefined, errors[0].VBCompileErrorId);
    }

    [TestMethod]
    public void APredeclaredClass_IsStillTheClass_WhereATypeIsExpected()
    {
        var widget = Widget("Attribute VB_PredeclaredId = True\r\n");

        var created = ValueOfLastAssignment(["Dim w As Widget", "Dim r", "Set r = New Widget"], widget);

        Assert.IsTrue(created.IsSuccess, created.ErrorInfo?.Verbose);
        Assert.AreEqual("Widget", Assert.IsInstanceOfType<VBClassType>(created.Result).Name);
        Assert.IsEmpty(Walk(["Dim w As Widget", "Set w = New Widget"], widget));
    }

    [TestMethod]
    public void TheDefaultInstance_CanBeTheTargetOfASetAssignment()
        // `Set Widget = New Widget`: an assignment to the predeclared variable is accepted (the benchmark's
        // `Set Interface = New Interface` needs it). MS-VBAL 5.2.4.1.2 would call it invalid; it is not enforced.
        => Assert.IsEmpty(Walk(["Set Widget = New Widget"], Widget("Attribute VB_PredeclaredId = True\r\n")));

    [TestMethod]
    public void ALocalNamedLikeAPredeclaredClass_HidesItsDefaultInstance()
    {
        var widget = Widget("Attribute VB_PredeclaredId = True\r\n");

        var size = ValueOfLastAssignment(["Dim Widget As Widget", "Dim r", "r = Widget.Size"], widget);

        Assert.IsTrue(size.IsSuccess, size.ErrorInfo?.Verbose);
        Assert.AreEqual(VBLongType.TypeInfo, size.Result);
    }
}
