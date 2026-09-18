using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Semantics.Static;
using RDCore.SDK.Semantics.Static.Abstract;
using System.Collections.Immutable;

namespace RDCore.Tests.Semantics.Static;

/// <summary>
/// Characterization matrix for <see cref="StatementStaticSemanticsEvaluator"/> — proves it recurses
/// into a real, arbitrarily-nested statement tree, threads a <c>With</c> block's target type into its
/// body (and only its body), and collects every error found across the tree rather than
/// short-circuiting on the first one.
/// </summary>
[TestClass]
public sealed class StatementStaticSemanticsEvaluatorTests
{
    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly SourceRange R = SourceRange.Empty;

    private static VBStandardModuleSymbol Module(string name) => new(Root, Root, name);
    private static VBClassModuleSymbol ClassModule(string name) => new(Root, Root, name);

    private static VBModuleFieldVariableMemberSymbol ModuleField(Uri moduleUri, string name, RDCore.SDK.Model.Types.Abstract.VBType type)
        => new(Root, moduleUri, name, ScopeKind.Module, type, R, R, AccessModifier.Implicit);

    private static VBInstanceFieldVariableMemberSymbol InstanceField(Uri moduleUri, string name, RDCore.SDK.Model.Types.Abstract.VBType type)
        => new(Root, moduleUri, name, R, R, type, AccessModifier.Public);

    private static SimpleNameExpressionNode NameOf(string identifier)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [1]), TestLocations.TestLocation, identifier);

    private static MemberAccessExpressionNode WithRelativeMemberOf(string memberName)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [2]), TestLocations.TestLocation, null, NameOf(memberName));

    private static AssignmentStatementNode AssignOf(ExpressionNode target, ExpressionNode value, AssignmentKind kind = AssignmentKind.ImplicitLet)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [3]), TestLocations.TestLocation, kind, target, value);

    private static WithStatementNode WithOf(ExpressionNode targetExpression, params StatementNode[] body)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [4]), TestLocations.TestLocation, targetExpression, new StatementBlock([.. body]));

    private static DoLoopStatementNode DoLoopOf(params StatementNode[] body)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [5]), TestLocations.TestLocation, new StatementBlock([.. body]));

    private static ForStatementNode ForOf(params StatementNode[] body)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [6]), TestLocations.TestLocation, NameOf("i"), NameOf("start"), NameOf("end"), null, new StatementBlock([.. body]));

    private static WhileWendStatementNode WhileWendOf(params StatementNode[] body)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [7]), TestLocations.TestLocation, NameOf("condition"), new StatementBlock([.. body]));

    private static SelectCaseStatementNode SelectCaseOf(ImmutableArray<CaseExpressionStatementNode> caseBlocks, CaseElseClauseStatementNode? caseElse)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [8]), TestLocations.TestLocation, NameOf("control"), caseBlocks, caseElse);

    private static CaseExpressionStatementNode CaseOf(ExpressionNode value, params StatementNode[] body)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [9]), TestLocations.TestLocation,
            [new CaseValueRangeClauseNode(new(TestUri.TestModuleUri().AbsolutePath, [10]), TestLocations.TestLocation, value)], new StatementBlock([.. body]));

    private static CaseElseClauseStatementNode CaseElseOf(params StatementNode[] body)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [11]), TestLocations.TestLocation, new StatementBlock([.. body]));

    private static IfBlockStatementNode IfOf(ExpressionNode condition, StatementNode[] thenBody, ImmutableArray<ElseIfBlockStatementNode> elseIfBlocks, ElseBlockStatementNode? elseBlock)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [12]), TestLocations.TestLocation, condition, new StatementBlock([.. thenBody]), elseIfBlocks, elseBlock);

    private static ElseIfBlockStatementNode ElseIfOf(ExpressionNode condition, params StatementNode[] body)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [13]), TestLocations.TestLocation, condition, new StatementBlock([.. body]));

    private static ElseBlockStatementNode ElseOf(params StatementNode[] body)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [14]), TestLocations.TestLocation, new StatementBlock([.. body]));

    private static StatementBlock Block(params StatementNode[] statements) => new([.. statements]);

    private static StaticEvaluationContext ContextAt(Uri scopeUri, params Symbol[] symbols)
    {
        var tree = ScopeTreeBuilder.Build(symbols);
        return new StaticEvaluationContext(new ScopeTreeSymbolResolver(tree), tree.ScopeFor(scopeUri));
    }

    [TestMethod]
    public void WithStatement_BodyMemberAccess_ResolvesAgainstWithTarget()
    {
        var caller = Module("Caller");
        var owner = ClassModule("Owner");
        var bar = InstanceField(owner.Uri, "Bar", VBLongType.TypeInfo);
        var populatedOwner = owner with { Members = [bar], DefaultInterfaceMembers = [bar] };
        var foo = ModuleField(caller.Uri, "Foo", new VBClassType(populatedOwner, populatedOwner.DefaultInterfaceMembers));
        var context = ContextAt(caller.Uri, caller with { Members = [foo] }, populatedOwner, foo);

        var block = Block(WithOf(NameOf("Foo"), AssignOf(WithRelativeMemberOf("Bar"), NameOf("value"))));
        var errors = StatementStaticSemanticsEvaluator.Evaluate(context, block);

        CollectionAssert.AreEqual(Array.Empty<VBCompileErrorInfo>(), errors);
    }

    [TestMethod]
    public void NestedWithStatements_InnerBodyResolvesAgainstTheInnermostTarget()
    {
        var caller = Module("Caller");
        var inner = ClassModule("Inner");
        var baz = InstanceField(inner.Uri, "Baz", VBLongType.TypeInfo);
        var populatedInner = inner with { Members = [baz], DefaultInterfaceMembers = [baz] };

        var outer = ClassModule("Outer");
        // Outer deliberately has NO "Baz" member - if the inner With's body resolved against Outer
        // instead of Inner, this would fail with MethodOrDataMemberNotFound.
        var populatedOuter = outer with { Members = [], DefaultInterfaceMembers = [] };

        var outerField = ModuleField(caller.Uri, "OuterField", new VBClassType(populatedOuter, populatedOuter.DefaultInterfaceMembers));
        var innerField = ModuleField(caller.Uri, "InnerField", new VBClassType(populatedInner, populatedInner.DefaultInterfaceMembers));
        var context = ContextAt(caller.Uri, caller with { Members = [outerField, innerField] }, populatedInner, populatedOuter, outerField, innerField);

        var block = Block(WithOf(NameOf("OuterField"), WithOf(NameOf("InnerField"), AssignOf(WithRelativeMemberOf("Baz"), NameOf("value")))));
        var errors = StatementStaticSemanticsEvaluator.Evaluate(context, block);

        CollectionAssert.AreEqual(Array.Empty<VBCompileErrorInfo>(), errors);
    }

    [TestMethod]
    public void WithRelativeAccessOutsideAnyWith_IsCollectedAsAnError()
        // nested inside an If block, but no enclosing With anywhere - MS-VBAL 5.6.15 makes this invalid.
    {
        var module = Module("Caller");
        var context = ContextAt(module.Uri, module);

        var block = Block(IfOf(NameOf("condition"), [AssignOf(WithRelativeMemberOf("Bar"), NameOf("value"))], [], null));
        var errors = StatementStaticSemanticsEvaluator.Evaluate(context, block);

        Assert.AreEqual(1, errors.Length);
        Assert.AreEqual(VBCompileErrorId.WithExpressionOutsideWithBlock, errors[0].VBCompileErrorId);
    }

    [TestMethod]
    public void MultipleIndependentErrors_AreAllCollected_NotJustTheFirst()
    {
        var module = Module("Caller") with { Directives = new ModuleDirectives(Explicit: true) };
        var value = ModuleField(module.Uri, "value", VBLongType.TypeInfo);
        var context = ContextAt(module.Uri, module with { Members = [value] }, value);

        var block = Block(
            AssignOf(NameOf("DoesNotExist1"), NameOf("value")),
            AssignOf(NameOf("DoesNotExist2"), NameOf("value")));
        var errors = StatementStaticSemanticsEvaluator.Evaluate(context, block);

        Assert.AreEqual(2, errors.Length);
        Assert.IsTrue(errors.All(error => error.VBCompileErrorId == VBCompileErrorId.VariableNotDefined));
    }

    [TestMethod]
    public void AllNestedBlockKinds_InheritTheEnclosingWithContext()
        // Do/For/While-Wend/Select-Case/Case-Else/If/ElseIf/Else all recurse under the SAME outer With
        // context (none of them, unlike WithStatementNode, change it) - every with-relative access here
        // must resolve against Owner, so a clean tree (zero errors) proves every switch arm is wired.
    {
        var caller = Module("Caller");
        var owner = ClassModule("Owner");
        var bar = InstanceField(owner.Uri, "Bar", VBLongType.TypeInfo);
        var populatedOwner = owner with { Members = [bar], DefaultInterfaceMembers = [bar] };
        var foo = ModuleField(caller.Uri, "Foo", new VBClassType(populatedOwner, populatedOwner.DefaultInterfaceMembers));
        var context = ContextAt(caller.Uri, caller with { Members = [foo] }, populatedOwner, foo);

        var assign = () => AssignOf(WithRelativeMemberOf("Bar"), NameOf("value"));
        var block = Block(WithOf(NameOf("Foo"),
            DoLoopOf(assign()),
            ForOf(assign()),
            WhileWendOf(assign()),
            SelectCaseOf([CaseOf(NameOf("caseValue"), assign())], CaseElseOf(assign())),
            IfOf(NameOf("condition"), [assign()], [ElseIfOf(NameOf("condition2"), assign())], ElseOf(assign()))));

        var errors = StatementStaticSemanticsEvaluator.Evaluate(context, block);

        CollectionAssert.AreEqual(Array.Empty<VBCompileErrorInfo>(), errors);
    }

    [TestMethod]
    public void EmptyBlock_ReturnsNoErrors()
    {
        var module = Module("Caller");
        var context = ContextAt(module.Uri, module);

        var errors = StatementStaticSemanticsEvaluator.Evaluate(context, Block());

        CollectionAssert.AreEqual(Array.Empty<VBCompileErrorInfo>(), errors);
    }

    [TestMethod]
    public void SetAssignment_BothSidesObjectish_Succeeds()
        // MS-VBAL 5.5.2.1's static table only checks "is either side object-ish" - not same-class
        // compatibility (that's a runtime concern, 5.5.2.2.1) - so two unrelated classes are fine here.
    {
        var module = Module("Caller");
        var widget = ModuleField(module.Uri, "widget", new VBClassType(ClassModule("Widget"), []));
        var gadget = ModuleField(module.Uri, "gadget", new VBClassType(ClassModule("Gadget"), []));
        var context = ContextAt(module.Uri, module with { Members = [widget, gadget] }, widget, gadget);

        var block = Block(AssignOf(NameOf("widget"), NameOf("gadget"), AssignmentKind.Set));
        var errors = StatementStaticSemanticsEvaluator.Evaluate(context, block);

        CollectionAssert.AreEqual(Array.Empty<VBCompileErrorInfo>(), errors);
    }

    [TestMethod]
    public void SetAssignment_NonObjectTarget_IsATypeMismatchError()
    {
        var module = Module("Caller");
        var n = ModuleField(module.Uri, "n", VBLongType.TypeInfo);
        var widget = ModuleField(module.Uri, "widget", new VBClassType(ClassModule("Widget"), []));
        var context = ContextAt(module.Uri, module with { Members = [n, widget] }, n, widget);

        var block = Block(AssignOf(NameOf("n"), NameOf("widget"), AssignmentKind.Set));
        var errors = StatementStaticSemanticsEvaluator.Evaluate(context, block);

        Assert.AreEqual(1, errors.Length);
        Assert.AreEqual(VBCompileErrorId.TypeMismatch, errors[0].VBCompileErrorId);
    }

    [TestMethod]
    public void SetAssignment_NonObjectValue_IsATypeMismatchError()
    {
        var module = Module("Caller");
        var widget = ModuleField(module.Uri, "widget", new VBClassType(ClassModule("Widget"), []));
        var n = ModuleField(module.Uri, "n", VBLongType.TypeInfo);
        var context = ContextAt(module.Uri, module with { Members = [widget, n] }, widget, n);

        var block = Block(AssignOf(NameOf("widget"), NameOf("n"), AssignmentKind.Set));
        var errors = StatementStaticSemanticsEvaluator.Evaluate(context, block);

        Assert.AreEqual(1, errors.Length);
        Assert.AreEqual(VBCompileErrorId.TypeMismatch, errors[0].VBCompileErrorId);
    }

    [TestMethod]
    public void LetAssignment_IncompatibleSource_IsATypeMismatchError()
        // proves Let-coercion is really being checked now (not just permissive by default): a
        // non-Variant intrinsic source into a class destination is invalid per MS-VBAL 5.5.1.1.
    {
        var module = Module("Caller");
        var widget = ModuleField(module.Uri, "widget", new VBClassType(ClassModule("Widget"), []));
        var n = ModuleField(module.Uri, "n", VBLongType.TypeInfo);
        var context = ContextAt(module.Uri, module with { Members = [widget, n] }, widget, n);

        var block = Block(AssignOf(NameOf("widget"), NameOf("n"), AssignmentKind.ImplicitLet));
        var errors = StatementStaticSemanticsEvaluator.Evaluate(context, block);

        Assert.AreEqual(1, errors.Length);
        Assert.AreEqual(VBCompileErrorId.TypeMismatch, errors[0].VBCompileErrorId);
    }

    [TestMethod]
    public void LSetAssignment_SkipsCoercionChecking()
        // LSet/RSet (MS-VBAL 5.4.3.6/5.4.3.7) have their own distinct, not-yet-modeled static semantics
        // - neither Let- nor Set-coercion applies, so even a combination that would fail either must be
        // deferred rather than flagged.
    {
        var module = Module("Caller");
        var n = ModuleField(module.Uri, "n", VBLongType.TypeInfo);
        var widget = ModuleField(module.Uri, "widget", new VBClassType(ClassModule("Widget"), []));
        var context = ContextAt(module.Uri, module with { Members = [n, widget] }, n, widget);

        var block = Block(AssignOf(NameOf("n"), NameOf("widget"), AssignmentKind.LSet));
        var errors = StatementStaticSemanticsEvaluator.Evaluate(context, block);

        CollectionAssert.AreEqual(Array.Empty<VBCompileErrorInfo>(), errors);
    }
}
