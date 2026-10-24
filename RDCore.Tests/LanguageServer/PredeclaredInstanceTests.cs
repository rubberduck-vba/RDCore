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
using RDCore.Symbols;

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
    [DataRow("Set Widget = New Widget")]
    [DataRow("Set Widget = Nothing")]
    public void TheDefaultInstance_IsAValidTargetOfASetAssignment(string statement)
        // MS-VBAL 5.2.4.1.2 says "it is invalid for this named variable to be the target of a Set assignment"; MS-VBA compiles it and runs it
        // (recorded in Excel, oracle\s18), and RD-VBA does what MS-VBA does (RD-VBAL 5.2.4.1.2).
        => Assert.IsEmpty(Walk([statement], Widget("Attribute VB_PredeclaredId = True\r\n")));

    // the default instance of an extensible class - a document module, ThisWorkbook or Sheet1 - is a property of the host that extends it: MS-VBA reports
    // "Invalid use of property" for a Set of it, whatever is assigned, the instance itself included (recorded in Excel, oracle\s18 D06-D08).
    [TestMethod]
    [DataRow("Set Widget = New Widget")]
    [DataRow("Set Widget = Nothing")]
    [DataRow("Set Widget = Widget")]
    public void TheDefaultInstanceOfAnExtensibleClass_IsNoTargetOfASetAssignment(string statement)
    {
        var errors = Walk([statement], Widget("Attribute VB_PredeclaredId = True\r\nAttribute VB_Extensible = True\r\n"));

        Assert.HasCount(1, errors);
        Assert.AreEqual(VBCompileErrorId.InvalidUseOfProperty, errors[0].VBCompileErrorId);
    }

    [TestMethod]
    public void ASetToALocalNamedLikeAnExtensiblePredeclaredClass_IsNotASetOfTheDefaultInstance()
        => Assert.IsEmpty(Walk(["Dim Widget As Widget", "Set Widget = New Widget"], Widget("Attribute VB_PredeclaredId = True\r\nAttribute VB_Extensible = True\r\n")));

    [TestMethod]
    public void AMemberAssignmentThroughTheDefaultInstance_IsValid()
        // `Widget.Size = 3` assigns a member of the object the default instance holds.
        => Assert.IsEmpty(Walk(["Widget.Size = 3"], Widget("Attribute VB_PredeclaredId = True\r\n")));

    [TestMethod]
    public void ASetToALocalNamedLikeAPredeclaredClass_IsNotASetOfTheDefaultInstance()
        // the local hides the default instance (5.6.10's procedure namespace comes first), so it is the target.
        => Assert.IsEmpty(Walk(["Dim Widget As Widget", "Set Widget = New Widget"], Widget("Attribute VB_PredeclaredId = True\r\n")));

    [TestMethod]
    public void ALocalNamedLikeAPredeclaredClass_HidesItsDefaultInstance()
    {
        var widget = Widget("Attribute VB_PredeclaredId = True\r\n");

        var size = ValueOfLastAssignment(["Dim Widget As Widget", "Dim r", "r = Widget.Size"], widget);

        Assert.IsTrue(size.IsSuccess, size.ErrorInfo?.Verbose);
        Assert.AreEqual(VBLongType.TypeInfo, size.Result);
    }
}
