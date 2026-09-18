using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Semantics.Static;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.Tests.Semantics.Static;

/// <summary>
/// Characterization matrix for <see cref="ExpressionStaticSemanticsEvaluator"/> — proves it recurses
/// into a real, arbitrarily-nested expression tree end to end (member-access chains, operators),
/// rather than only ever being exercised with hand-fed operand types the way every individual rule's
/// own tests are.
/// </summary>
[TestClass]
public sealed class ExpressionStaticSemanticsEvaluatorTests
{
    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly SourceRange R = SourceRange.Empty;

    private static VBStandardModuleSymbol Module(string name) => new(Root, Root, name);
    private static VBClassModuleSymbol ClassModule(string name) => new(Root, Root, name);

    private static VBModuleFieldVariableMemberSymbol ModuleField(Uri moduleUri, string name, VBType type)
        => new(Root, moduleUri, name, ScopeKind.Module, type, R, R, AccessModifier.Implicit);

    private static VBInstanceFieldVariableMemberSymbol InstanceField(Uri moduleUri, string name, VBType type)
        => new(Root, moduleUri, name, R, R, type, AccessModifier.Public);

    private static SimpleNameExpressionNode NameOf(string identifier)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [1]), TestLocations.TestLocation, identifier);

    private static MemberAccessExpressionNode MemberOf(ExpressionNode owner, string memberName)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [2]), TestLocations.TestLocation, owner, NameOf(memberName));

    private static MemberAccessExpressionNode WithRelativeMemberOf(string memberName)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [3]), TestLocations.TestLocation, null, NameOf(memberName));

    private static VBBinaryOperatorExpressionNode BinaryOf(string token, ExpressionNode left, ExpressionNode right)
        => new(token, new(TestUri.TestModuleUri().AbsolutePath, [4]), TestLocations.TestLocation, left, right);

    private static NewExpressionNode NewOf(ExpressionNode typeExpression)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [5]), TestLocations.TestLocation, typeExpression);

    private static TypeOfIsExpressionNode TypeOfIsOf(ExpressionNode operand, ExpressionNode typeExpression)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [6]), TestLocations.TestLocation, operand, typeExpression);

    private static IndexExpressionNode IndexOf(ExpressionNode callee, params ExpressionNode[] arguments)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [7]), TestLocations.TestLocation, callee, [.. arguments]);

    private static DictionaryAccessExpressionNode DictionaryAccessOf(ExpressionNode owner, string memberName)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [8]), TestLocations.TestLocation, owner, NameOf(memberName));

    private static DictionaryAccessExpressionNode WithRelativeDictionaryAccessOf(string memberName)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [9]), TestLocations.TestLocation, null, NameOf(memberName));

    private static AddressOfExpressionNode AddressOfOf(ExpressionNode target)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [10]), TestLocations.TestLocation, target);

    private static StaticEvaluationContext ContextAt(Uri scopeUri, params Symbol[] symbols)
    {
        var tree = ScopeTreeBuilder.Build(symbols);
        return new StaticEvaluationContext(new ScopeTreeSymbolResolver(tree), tree.ScopeFor(scopeUri));
    }

    [TestMethod]
    public void SingleLevelMemberAccess_ResolvesFromRealSymbols_NotHandFedOperands()
    {
        var caller = Module("Caller");
        var owner = ClassModule("Owner");
        var member = InstanceField(owner.Uri, "Bar", VBLongType.TypeInfo);
        var populatedOwner = owner with { Members = [member], DefaultInterfaceMembers = [member] };
        var foo = ModuleField(caller.Uri, "Foo", new VBClassType(populatedOwner, populatedOwner.DefaultInterfaceMembers));
        var context = ContextAt(caller.Uri, caller with { Members = [foo] }, populatedOwner, foo);

        var result = ExpressionStaticSemanticsEvaluator.Evaluate(context, MemberOf(NameOf("Foo"), "Bar"));

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Description);
        Assert.AreEqual(VBLongType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void NestedMemberAccessChain_ResolvesEndToEnd()
        // Foo.Bar.Baz: Foo's declared type is Outer, Outer.Bar's declared type is Inner, Inner.Baz is
        // Long. Each level's owner must itself be evaluated recursively - the key new capability.
    {
        var caller = Module("Caller");
        var inner = ClassModule("Inner");
        var baz = InstanceField(inner.Uri, "Baz", VBLongType.TypeInfo);
        var populatedInner = inner with { Members = [baz], DefaultInterfaceMembers = [baz] };

        var outer = ClassModule("Outer");
        var bar = InstanceField(outer.Uri, "Bar", new VBClassType(populatedInner, populatedInner.DefaultInterfaceMembers));
        var populatedOuter = outer with { Members = [bar], DefaultInterfaceMembers = [bar] };

        var foo = ModuleField(caller.Uri, "Foo", new VBClassType(populatedOuter, populatedOuter.DefaultInterfaceMembers));
        var context = ContextAt(caller.Uri, caller with { Members = [foo] }, populatedInner, populatedOuter, foo);

        var chain = MemberOf(MemberOf(NameOf("Foo"), "Bar"), "Baz");
        var result = ExpressionStaticSemanticsEvaluator.Evaluate(context, chain);

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Description);
        Assert.AreEqual(VBLongType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void UnresolvableOwner_PropagatesTheInnerErrorInsteadOfContinuing()
        // an unresolved name is only a static-semantics error under Option Explicit (otherwise
        // SimpleNameExpressionStaticSemantics itself defers to VBUnknownType) - Explicit here so the
        // owner genuinely fails, proving the evaluator returns THAT error rather than attempting the
        // outer member lookup regardless.
    {
        var caller = Module("Caller") with { Directives = new ModuleDirectives(Explicit: true) };
        var context = ContextAt(caller.Uri, caller);

        var result = ExpressionStaticSemanticsEvaluator.Evaluate(context, MemberOf(NameOf("DoesNotExist"), "Bar"));

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(VBCompileErrorId.VariableNotDefined, result.ErrorInfo!.VBCompileErrorId);
    }

    [TestMethod]
    public void BinaryOperator_EvaluatesBothOperandsThenDispatchesByToken()
    {
        var module = Module("Caller");
        var x = ModuleField(module.Uri, "x", VBLongType.TypeInfo);
        var y = ModuleField(module.Uri, "y", VBLongType.TypeInfo);
        var context = ContextAt(module.Uri, module with { Members = [x, y] }, x, y);

        var result = ExpressionStaticSemanticsEvaluator.Evaluate(context, BinaryOf(Tokens.AdditionOp, NameOf("x"), NameOf("y")));

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Description);
        Assert.AreEqual(VBLongType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void UnmappedOperatorToken_DefersToUnknown()
        // Mod has no static semantics rule yet - the evaluator must defer, not throw or misreport.
    {
        var module = Module("Caller");
        var x = ModuleField(module.Uri, "x", VBLongType.TypeInfo);
        var context = ContextAt(module.Uri, module with { Members = [x] }, x);

        var result = ExpressionStaticSemanticsEvaluator.Evaluate(context, BinaryOf(Tokens.ModuloOp, NameOf("x"), NameOf("x")));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(VBUnknownType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void WithRelativeAccess_NoEnclosingWith_IsAnError()
        // MS-VBAL 5.6.15: "If there is no enclosing With block, the with-expression is invalid." A
        // context with no EnclosingWithTargetType means exactly that - a real compile error, not a defer.
    {
        var module = Module("Caller");
        var context = ContextAt(module.Uri, module);

        var result = ExpressionStaticSemanticsEvaluator.Evaluate(context, WithRelativeMemberOf("Bar"));

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(VBCompileErrorId.WithExpressionOutsideWithBlock, result.ErrorInfo!.VBCompileErrorId);
    }

    [TestMethod]
    public void WithRelativeAccess_EnclosingWithTargetType_ResolvesAgainstIt()
        // proves the substitution end to end: a context carrying EnclosingWithTargetType (as a
        // statement walker would supply for code inside a With block) resolves .Bar against it.
    {
        var owner = ClassModule("Owner");
        var member = InstanceField(owner.Uri, "Bar", VBLongType.TypeInfo);
        var populatedOwner = owner with { Members = [member], DefaultInterfaceMembers = [member] };
        var context = ContextAt(owner.Uri, populatedOwner) with
        {
            EnclosingWithTargetType = new VBClassType(populatedOwner, populatedOwner.DefaultInterfaceMembers),
        };

        var result = ExpressionStaticSemanticsEvaluator.Evaluate(context, WithRelativeMemberOf("Bar"));

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Description);
        Assert.AreEqual(VBLongType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void UnmappedNodeKind_DefersToUnknown()
        // AddressOfExpressionNode is only ever meaningful as an IndexExpressionNode argument - the
        // evaluator has no top-level rule for it and must defer, not throw or misreport.
    {
        var module = Module("Caller");
        var context = ContextAt(module.Uri, module);

        var result = ExpressionStaticSemanticsEvaluator.Evaluate(context, AddressOfOf(NameOf("Foo")));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(VBUnknownType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void TypeOfIs_ClassOperand_ResolvesToBoolean_ThroughRealSymbols()
    {
        var caller = Module("Caller");
        var widget = ClassModule("Widget");
        var obj = ModuleField(caller.Uri, "obj", new VBClassType(widget, []));
        var context = ContextAt(caller.Uri, caller with { Members = [obj] }, widget, obj);

        var result = ExpressionStaticSemanticsEvaluator.Evaluate(context, TypeOfIsOf(NameOf("obj"), NameOf("Widget")));

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Description);
        Assert.AreEqual(VBBooleanType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void IndexExpression_ArrayCallee_ResolvesElementType_ThroughRealSymbols()
    {
        var caller = Module("Caller");
        var data = ModuleField(caller.Uri, "data", new VBFixedSizeArrayType(VBLongType.TypeInfo));
        var context = ContextAt(caller.Uri, caller with { Members = [data] }, data);

        var result = ExpressionStaticSemanticsEvaluator.Evaluate(context, IndexOf(NameOf("data"), NameOf("i")));

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Description);
        Assert.AreEqual(VBLongType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void IndexExpression_ArgumentError_PropagatesInsteadOfContinuing()
        // an unresolved argument under Option Explicit must fail the whole index expression, even
        // though IndexExpressionStaticSemantics itself never needs the argument's own declared type.
    {
        var caller = Module("Caller") with { Directives = new ModuleDirectives(Explicit: true) };
        var data = ModuleField(caller.Uri, "data", new VBFixedSizeArrayType(VBLongType.TypeInfo));
        var context = ContextAt(caller.Uri, caller with { Members = [data] }, data);

        var result = ExpressionStaticSemanticsEvaluator.Evaluate(context, IndexOf(NameOf("data"), NameOf("DoesNotExist")));

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(VBCompileErrorId.VariableNotDefined, result.ErrorInfo!.VBCompileErrorId);
    }

    [TestMethod]
    public void DictionaryAccess_ClassOwnerWithDefaultMember_ResolvesEndToEnd()
    {
        var caller = Module("Caller");
        var dictionaryClass = ClassModule("Dictionary");
        var itemMember = InstanceField(dictionaryClass.Uri, "Item", VBVariantType.TypeInfo);
        var dict = ModuleField(caller.Uri, "dict", new VBClassType(dictionaryClass, []) { DefaultMember = itemMember });
        var context = ContextAt(caller.Uri, caller with { Members = [dict] }, dictionaryClass, dict);

        var result = ExpressionStaticSemanticsEvaluator.Evaluate(context, DictionaryAccessOf(NameOf("dict"), "key"));

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Description);
        Assert.AreEqual(VBVariantType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void WithRelativeDictionaryAccess_NoEnclosingWith_IsAnError()
    {
        var module = Module("Caller");
        var context = ContextAt(module.Uri, module);

        var result = ExpressionStaticSemanticsEvaluator.Evaluate(context, WithRelativeDictionaryAccessOf("key"));

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(VBCompileErrorId.WithExpressionOutsideWithBlock, result.ErrorInfo!.VBCompileErrorId);
    }

    [TestMethod]
    public void WithRelativeDictionaryAccess_EnclosingWithTargetType_ResolvesAgainstIt()
    {
        var dictionaryClass = ClassModule("Dictionary");
        var itemMember = InstanceField(dictionaryClass.Uri, "Item", VBVariantType.TypeInfo);
        var context = ContextAt(dictionaryClass.Uri, dictionaryClass) with
        {
            EnclosingWithTargetType = new VBClassType(dictionaryClass, []) { DefaultMember = itemMember },
        };

        var result = ExpressionStaticSemanticsEvaluator.Evaluate(context, WithRelativeDictionaryAccessOf("key"));

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Description);
        Assert.AreEqual(VBVariantType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void NewExpression_StillResolves_TheNoOperandPathDoesNotRegress()
    {
        var module = Module("Caller");
        var classModule = (VBClassModuleSymbol)ClassModule("Widget").With(SymbolProperties.Creatable, true);
        var context = ContextAt(module.Uri, module, classModule);

        var result = ExpressionStaticSemanticsEvaluator.Evaluate(context, NewOf(NameOf("Widget")));

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Description);
        Assert.AreEqual("Widget", result.Result!.Name);
    }
}
