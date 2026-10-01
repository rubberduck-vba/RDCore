using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Static;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.Tests.Semantics.Static;

/// <summary>
/// The declared type of a member access whose left-hand side names a project or a procedural module rather than a
/// value (<strong>MS-VBAL §5.6.12</strong>): <c>Strings.LenB</c>, <c>VBA.LenB</c>, <c>VBA.Strings.LenB</c>.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.6.12 Member Access Expressions")]
public sealed class NamespaceMemberAccessStaticSemanticsTests
{
    private const string Library = "VBA";

    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly SourceRange R = SourceRange.Empty;

    private static Symbol InLibrary(Symbol symbol) => symbol.With(SymbolProperties.Library, Library);

    private static SimpleNameExpressionNode NameOf(string identifier)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [1]), TestLocations.TestLocation, identifier);

    private static MemberAccessExpressionNode MemberOf(ExpressionNode owner, string memberName)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [2]), TestLocations.TestLocation, owner, NameOf(memberName));

    private static IndexExpressionNode IndexOf(ExpressionNode callee, params ExpressionNode[] arguments)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [7]), TestLocations.TestLocation, callee, [.. arguments]);

    private static LiteralExpressionNode TextOf(string value)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [12]), TestLocations.TestLocation, new RDCore.SDK.Model.Values.Intrinsic.VBStringValue(value));

    // a project of the workspace and the library; Strings (the library's), Util and Caller; and the members under test.
    private sealed class World
    {
        public VBProjectSymbol Enclosing { get; } = new(Root, "Project1");
        public VBProjectSymbol LibraryProject { get; } = (VBProjectSymbol)InLibrary(new VBProjectSymbol(Root, Library));
        public VBStandardModuleSymbol Strings { get; } = (VBStandardModuleSymbol)InLibrary(new VBStandardModuleSymbol(Root, Root, "Strings"));
        public VBStandardModuleSymbol Util { get; } = new(Root, Root, "Util");
        public VBStandardModuleSymbol Caller { get; } = new(Root, Root, "Caller");
        public Symbol LenB { get; }
        public Symbol Counter { get; }
        public Symbol Reset { get; }

        public World()
        {
            LenB = InLibrary(new VBFunctionMemberSymbol(
                Root, Strings.Uri, "LenB", ScopeKind.Module, SymbolKindExt.Function, VBLongType.TypeInfo, R, R, AccessModifier.Public));
            Counter = new VBModuleFieldVariableMemberSymbol(
                Root, Util.Uri, "Counter", ScopeKind.Module, VBStringType.TypeInfo, R, R, AccessModifier.Public);
            Reset = new VBFunctionMemberSymbol(
                Root, Util.Uri, "Reset", ScopeKind.Module, SymbolKindExt.Procedure, VBVoidType.TypeInfo, R, R, AccessModifier.Public);
        }

        public StaticEvaluationContext Context(params Symbol[] extra)
        {
            var tree = ScopeTreeBuilder.Build([Enclosing, LibraryProject, Strings, Util, Caller, LenB, Counter, Reset, .. extra]);
            return new StaticEvaluationContext(new ScopeTreeSymbolResolver(tree), tree.ScopeFor(Caller.Uri));
        }
    }

    private static readonly World W = new();

    private static StaticSemanticsEvaluationResult Evaluate(ExpressionNode expression)
        => ExpressionStaticSemanticsEvaluator.Evaluate(W.Context(), expression);

    [TestMethod]
    public void AModuleQualifiedFunction_HasTheFunctionsDeclaredType()
    {
        var result = Evaluate(MemberOf(NameOf("Strings"), "LenB"));

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Description);
        Assert.AreEqual(VBLongType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void AModuleQualifiedVariable_HasTheVariablesDeclaredType()
    {
        var result = Evaluate(MemberOf(NameOf("Util"), "Counter"));

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Description);
        Assert.AreEqual(VBStringType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void AProjectQualifiedMember_WithNoModuleNamed_HasTheMembersDeclaredType()
    {
        var result = Evaluate(MemberOf(NameOf("VBA"), "LenB"));

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Description);
        Assert.AreEqual(VBLongType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void AProjectAndModuleQualifiedMember_HasTheMembersDeclaredType()
    {
        var result = Evaluate(MemberOf(MemberOf(NameOf("VBA"), "Strings"), "LenB"));

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Description);
        Assert.AreEqual(VBLongType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void ACallThroughAModuleQualifiedFunction_HasTheFunctionsDeclaredType()
    {
        var result = Evaluate(IndexOf(MemberOf(NameOf("Strings"), "LenB"), TextOf("42")));

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Description);
        Assert.AreEqual(VBLongType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void AModuleQualifiedSubroutine_HasNoValue()
    {
        var result = Evaluate(MemberOf(NameOf("Util"), "Reset"));

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Description);
        Assert.AreEqual(VBVoidType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void AMemberTheModuleDoesNotDeclare_IsMethodOrDataMemberNotFound()
    {
        var result = Evaluate(MemberOf(NameOf("Util"), "Nope"));

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(VBCompileErrorId.MethodOrDataMemberNotFound, result.ErrorInfo!.VBCompileErrorId);
    }

    [TestMethod]
    public void AMemberAnotherModuleDeclares_IsNotAMemberOfThisOne()
    {
        var result = Evaluate(MemberOf(NameOf("Util"), "LenB"));

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(VBCompileErrorId.MethodOrDataMemberNotFound, result.ErrorInfo!.VBCompileErrorId);
    }

    [TestMethod]
    public void AMemberTwoModulesOfTheProjectDeclare_IsAnError()
    {
        // `VBA.LenB` when two of the library's modules each declare a LenB: §5.6.12 gives it to neither.
        var other = (VBStandardModuleSymbol)InLibrary(new VBStandardModuleSymbol(Root, Root, "Other"));
        var duplicate = InLibrary(new VBFunctionMemberSymbol(
            Root, other.Uri, "LenB", ScopeKind.Module, SymbolKindExt.Function, VBLongType.TypeInfo, R, R, AccessModifier.Public));

        var result = ExpressionStaticSemanticsEvaluator.Evaluate(W.Context(other, duplicate), MemberOf(NameOf("VBA"), "LenB"));

        Assert.IsTrue(result.IsError);
    }

    [TestMethod]
    public void ALocalOfTheSameNameAsAModule_IsAValueAndItsTypeDecidesTheMember()
    {
        // `Util` here is a Long, which has no members: the name means what is nearest, as it does anywhere else.
        var caller = W.Caller;
        var local = new VBModuleFieldVariableMemberSymbol(Root, caller.Uri, "Util", ScopeKind.Module, VBLongType.TypeInfo, R, R, AccessModifier.Private);

        var context = W.Context(local);

        Assert.IsNull(context.Resolver.NamespaceOf(NameOf("Util"), caller.Uri));
        Assert.AreNotEqual(VBStringType.TypeInfo, ExpressionStaticSemanticsEvaluator.Evaluate(context, MemberOf(NameOf("Util"), "Counter")).Result);
    }

    [TestMethod]
    public void ANamespaceAsTheWholeExpression_IsTypedUnknown()
        // 🚧 TODO see ExpressionStaticSemanticsEvaluator.EvaluateNamespaceMember: no diagnostic rejects it yet.
    {
        var result = Evaluate(MemberOf(NameOf("VBA"), "Strings"));

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Description);
        Assert.AreEqual(VBUnknownType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void NamespaceOf_ClassifiesNamespacesAndNothingElse()
    {
        var context = W.Context();
        var resolver = context.Resolver;
        var scope = W.Caller.Uri;

        Assert.AreEqual(W.Util.Uri, resolver.NamespaceOf(NameOf("Util"), scope)?.Uri);
        Assert.AreEqual(W.LibraryProject.Uri, resolver.NamespaceOf(NameOf("VBA"), scope)?.Uri);
        Assert.AreEqual(W.Strings.Uri, resolver.NamespaceOf(MemberOf(NameOf("VBA"), "Strings"), scope)?.Uri);
        Assert.IsNull(resolver.NamespaceOf(MemberOf(NameOf("Util"), "Counter"), scope));
        Assert.IsNull(resolver.NamespaceOf(NameOf("Nope"), scope));
    }
}
