using NSubstitute;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.Operators;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Binding;
using RDCore.SDK.Semantics.Plans;
using RDCore.SDK.Semantics.Static;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.Tests.Semantics.Binding;

/// <summary>
/// The binder (IR lowering §3.3): each expression bound to its declared type, as the static semantics evaluate it, and each operation to the plan of its
/// operation, made for the declared types of its operands.
/// </summary>
[TestClass]
public sealed class ExpressionBinderTests
{
    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly SourceRange R = SourceRange.Empty;
    private int _nodes;

    private SyntaxNodeId NextId() => new(TestUri.TestModuleUri().AbsolutePath, [++_nodes]);

    private SimpleNameExpressionNode NameOf(string identifier) => new(NextId(), TestLocations.TestLocation, identifier);

    private VBBinaryOperatorExpressionNode BinaryOf(string token, ExpressionNode left, ExpressionNode right) => new(token, NextId(), TestLocations.TestLocation, left, right);

    private VBUnaryOperatorExpressionNode UnaryOf(string token, ExpressionNode operand) => new(token, NextId(), TestLocations.TestLocation, [operand]);

    // a module with a field of each of these types, and the context of an expression of the module.
    private static StaticEvaluationContext ContextWith(params (string Name, VBType Type)[] fields)
    {
        var module = new VBStandardModuleSymbol(Root, Root, "Module1");
        var members = fields.Select(field => new VBModuleFieldVariableMemberSymbol(Root, module.Uri, field.Name, ScopeKind.Module, field.Type, R, R, AccessModifier.Implicit)).ToArray();
        var tree = ScopeTreeBuilder.Build([module with { Members = [.. members] }, .. members]);
        return new StaticEvaluationContext(new ScopeTreeSymbolResolver(tree), tree.ScopeFor(module.Uri));
    }

    [TestMethod]
    public void AnOperation_IsBoundToItsDeclaredType_AndToThePlanOfItsOperation()
    {
        var context = ContextWith(("x", VBLongType.TypeInfo), ("d", VBDoubleType.TypeInfo));
        var (x, d) = (NameOf("x"), NameOf("d"));
        var sum = BinaryOf(Tokens.AdditionOp, x, d);

        var bindings = ExpressionBinder.Bind(context, sum);

        var bound = bindings[sum.Identity]!;
        Assert.AreEqual(VBDoubleType.TypeInfo, bound.DeclaredType);
        Assert.AreSame(OperationPlanner.ForValues(Tokens.AdditionOp, VBLongType.TypeInfo, VBDoubleType.TypeInfo), bound.Plan, "a final plan, the one the interpreter makes");

        // an operand is bound too, and is no operation.
        Assert.AreEqual(VBLongType.TypeInfo, bindings[x.Identity]!.DeclaredType);
        Assert.IsNull(bindings[x.Identity]!.Plan);
        Assert.AreEqual(VBDoubleType.TypeInfo, bindings[d.Identity]!.DeclaredType);
    }

    // the bound tree (IR lowering §3.1): each name bound to the variable it resolves to, each operation to its plan and its operands' nodes. A field is no
    // variable of an activation, and has no slot.
    [TestMethod]
    public void AnOperation_OfNames_IsBoundToATreeOfItsOperands()
    {
        var context = ContextWith(("x", VBLongType.TypeInfo), ("d", VBDoubleType.TypeInfo));
        var (x, d) = (NameOf("x"), NameOf("d"));
        var sum = BinaryOf(Tokens.AdditionOp, x, d);

        var node = ExpressionBinder.Bind(context, sum).NodeOf(sum);

        var binary = Assert.IsInstanceOfType<BoundBinary>(node);
        Assert.AreSame(sum, binary.Operation);
        Assert.AreSame(OperationPlanner.ForValues(Tokens.AdditionOp, VBLongType.TypeInfo, VBDoubleType.TypeInfo), binary.Plan);
        var left = Assert.IsInstanceOfType<BoundVariable>(binary.Left);
        Assert.AreEqual("x", left.Variable.Name);
        Assert.IsNull(left.Slot);
        Assert.AreEqual("d", Assert.IsInstanceOfType<BoundVariable>(binary.Right).Variable.Name);
    }

    // IR lowering D2: an activation has a slot for each parameter and each variable of its procedure, in the order they are declared - but none for a Static
    // variable, which outlives it.
    [TestMethod]
    public void TheLayoutOfAProcedure_HasASlotForEachParameterAndVariable_ButNotAStaticOne()
    {
        var procedure = new VBProcedureMemberSymbol(Root, Root, "DoSomething", ScopeKind.Module, SymbolKindExt.Procedure, VBVoidType.TypeInfo, R, R, AccessModifier.Public);
        var parameter = new VBParameterSymbol(Root, procedure.Uri, "value", R, R, ParameterKind.ExplicitByVal, VBLongType.TypeInfo);
        var local = new VBLocalVariableSymbol(Root, procedure.Uri, "total", ScopeKind.Local, R, R, ResolvedType: VBDoubleType.TypeInfo);
        var remembered = new VBLocalVariableSymbol(Root, procedure.Uri, "calls", ScopeKind.Local, R, R, IsStatic: true, ResolvedType: VBLongType.TypeInfo);

        var layout = FrameLayout.Of(procedure with { Parameters = [parameter], Locals = [local, remembered] });

        Assert.AreEqual(2, layout.Count);
        Assert.IsTrue(layout.TryGetSlot(parameter, out var first));
        Assert.AreEqual(0, first.Index);
        Assert.IsTrue(layout.TryGetSlot(local, out var second));
        Assert.AreEqual(1, second.Index);
        Assert.IsFalse(layout.TryGetSlot(remembered, out _));
    }

    [TestMethod]
    public void ALiteral_IsBoundToItself()
    {
        var literal = new LiteralExpressionNode(NextId(), TestLocations.TestLocation, new RDCore.SDK.Model.Values.Intrinsic.VBLongValue(42));

        Assert.IsInstanceOfType<BoundLiteral>(ExpressionBinder.Bind(ContextWith(), literal).NodeOf(literal));
    }

    // a compile error is not bound (IR lowering D3), and nor is the operation it is an operand of: the evaluation of the syntax tree raises what it raises.
    [TestMethod]
    public void ANameThatDoesNotResolve_AndAnOperationOfIt_AreFallbacks()
    {
        var context = ContextWith(("x", VBLongType.TypeInfo));
        var sum = BinaryOf(Tokens.AdditionOp, NameOf("x"), NameOf("nowhere"));

        Assert.IsInstanceOfType<BoundFallback>(ExpressionBinder.Bind(context, sum).NodeOf(sum));
    }

    [TestMethod]
    public void AnOperation_OfAVariant_IsAVariant_AndItsPlanIsDeferred()
    {
        var context = ContextWith(("v", VBVariantType.TypeInfo), ("x", VBLongType.TypeInfo));
        var sum = BinaryOf(Tokens.AdditionOp, NameOf("v"), NameOf("x"));

        var bound = ExpressionBinder.Bind(context, sum)[sum.Identity]!;

        Assert.AreEqual(VBVariantType.TypeInfo, bound.DeclaredType);
        Assert.IsTrue(bound.Plan!.IsDeferred);
    }

    [TestMethod]
    public void AnOperation_OfAnObject_IsLateBound_AndItsPlanIsDeferred()
    {
        var context = ContextWith(("o", VBObjectType.TypeInfo), ("x", VBLongType.TypeInfo));
        var sum = BinaryOf(Tokens.AdditionOp, NameOf("o"), NameOf("x"));

        var bound = ExpressionBinder.Bind(context, sum)[sum.Identity]!;

        Assert.AreEqual(VBUnknownType.TypeInfo, bound.DeclaredType);
        Assert.IsTrue(bound.Plan!.IsDeferred);
    }

    [TestMethod]
    public void AnOperation_OfALateBoundOperation_IsDeferred()
    {
        // (o + x) * x: the inner operation is late-bound, and so is what is made of it.
        var context = ContextWith(("o", VBObjectType.TypeInfo), ("x", VBLongType.TypeInfo));
        var product = BinaryOf(Tokens.MultiplicationOp, BinaryOf(Tokens.AdditionOp, NameOf("o"), NameOf("x")), NameOf("x"));

        var bound = ExpressionBinder.Bind(context, product)[product.Identity]!;

        Assert.AreEqual(VBUnknownType.TypeInfo, bound.DeclaredType);
        Assert.IsTrue(bound.Plan!.IsDeferred);
    }

    [TestMethod]
    public void AnOperation_ThatIsATypeMismatch_IsBoundToTheCompileError_AndToThePlanThatSaysSo()
    {
        var context = ContextWith(("x", VBLongType.TypeInfo), ("a", new VBResizableArrayType(VBLongType.TypeInfo)));
        var sum = BinaryOf(Tokens.AdditionOp, NameOf("x"), NameOf("a"));

        var bound = ExpressionBinder.Bind(context, sum)[sum.Identity]!;

        Assert.IsNotNull(bound.Error);
        Assert.IsTrue(bound.Plan!.IsTypeMismatch);
    }

    [TestMethod]
    public void EachOperation_OfANestedExpression_IsBound()
    {
        // -(x + e), with e an Enum: a Long to the operator; the parentheses are a conversion, not an operation.
        var colour = new VBEnumType(new VBEnumMemberSymbol(Root, TestUri.TestModuleUri(), "Colour", ScopeKind.Module, SymbolKindExt.Enum, VBUnknownType.TypeInfo,
            R, R, AccessModifier.Public), members: []);
        var context = ContextWith(("x", VBIntegerType.TypeInfo), ("e", colour));
        var sum = BinaryOf(Tokens.AdditionOp, NameOf("x"), NameOf("e"));
        var parentheses = UnaryOf(OperatorSymbolNames.UnaryLetCoerceOp, sum);
        var negation = UnaryOf(Tokens.NegationOp, parentheses);

        var bindings = ExpressionBinder.Bind(context, negation);

        Assert.AreEqual(VBLongType.TypeInfo, bindings[sum.Identity]!.Plan!.EffectiveType);
        Assert.IsNull(bindings[parentheses.Identity]!.Plan);
        Assert.AreEqual(VBLongType.TypeInfo, bindings[negation.Identity]!.Plan!.EffectiveType);
        Assert.AreEqual(VBLongType.TypeInfo, bindings[negation.Identity]!.DeclaredType);
    }

    [TestMethod]
    public void TheDeclaredType_OfEachBoundExpression_IsTheOneTheStaticSemanticsGiveIt()
    {
        var context = ContextWith(("b", VBBooleanType.TypeInfo), ("s", VBSingleType.TypeInfo), ("v", VBVariantType.TypeInfo));
        var expression = BinaryOf(Tokens.CompareLessThanOp, BinaryOf(Tokens.PowerOp, NameOf("b"), NameOf("s")), BinaryOf(Tokens.ConcatOp, NameOf("v"), NameOf("s")));

        var bindings = ExpressionBinder.Bind(context, expression);

        Assert.HasCount(7, bindings.Bound);
        foreach (var node in Flatten(expression))
        {
            Assert.AreEqual(ExpressionStaticSemanticsEvaluator.Evaluate(context, node).Result, bindings[node.Identity]!.DeclaredType, node.ToString());
        }
    }

    private static IEnumerable<ExpressionNode> Flatten(ExpressionNode node)
        => node is VBBinaryOperatorExpressionNode binary ? [node, .. Flatten(binary.Left), .. Flatten(binary.Right)] : [node];

    [TestMethod]
    public void Binding_IsNotAnalyzing_AndStatesNoFact()
    {
        var facts = Substitute.For<IExpressionFactSink>();
        var context = ContextWith(("x", VBLongType.TypeInfo)) with { Facts = facts };

        ExpressionBinder.Bind(context, BinaryOf(Tokens.AdditionOp, NameOf("x"), NameOf("x")));

        facts.DidNotReceiveWithAnyArgs().Record(default!);
    }

    [TestMethod]
    public void ALetAssignment_IsBoundToThePlanOfItsLetCoercion()
    {
        var context = ContextWith(("d", VBDoubleType.TypeInfo), ("x", VBLongType.TypeInfo), ("v", VBVariantType.TypeInfo));

        var final = ExpressionBinder.BindAssignment(context, new AssignmentStatementNode(NextId(), TestLocations.TestLocation, AssignmentKind.ImplicitLet, NameOf("d"), NameOf("x")));
        Assert.AreSame(ConversionPlanner.ForValue(VBLongType.TypeInfo, VBDoubleType.TypeInfo), final.Conversion);
        Assert.AreEqual(ConversionRule.Numeric, final.Conversion!.Rule);

        var deferred = ExpressionBinder.BindAssignment(context, new AssignmentStatementNode(NextId(), TestLocations.TestLocation, AssignmentKind.ExplicitLet, NameOf("d"), NameOf("v")));
        Assert.IsTrue(deferred.Conversion!.IsDeferred);

        // the value of the assignment is bound with its operations.
        var sum = BinaryOf(Tokens.AdditionOp, NameOf("x"), NameOf("x"));
        var assigned = ExpressionBinder.BindAssignment(context, new AssignmentStatementNode(NextId(), TestLocations.TestLocation, AssignmentKind.ImplicitLet, NameOf("d"), sum));
        Assert.AreEqual(VBLongType.TypeInfo, assigned.Expressions[sum.Identity]!.Plan!.EffectiveType);
    }

    [TestMethod]
    public void ASetAssignment_IsNoLetCoercion()
    {
        var context = ContextWith(("o", VBObjectType.TypeInfo), ("p", VBObjectType.TypeInfo));

        var bound = ExpressionBinder.BindAssignment(context, new AssignmentStatementNode(NextId(), TestLocations.TestLocation, AssignmentKind.Set, NameOf("o"), NameOf("p")));

        Assert.IsNull(bound.Conversion);
    }

    // a module with a class Counter (a Sub Increment and a Property Get Value) and a field `c` of the type the class had when the field was typed: a
    // snapshot with no member yet, as a class typed before it is complete has.
    private static StaticEvaluationContext ContextWithACounter()
    {
        var counter = new VBClassModuleSymbol(Root, Root, "Counter");
        var increment = new VBProcedureMemberSymbol(Root, counter.Uri, "Increment", ScopeKind.Instance, SymbolKindExt.Procedure, VBVoidType.TypeInfo, R, R, AccessModifier.Public);
        var value = new VBPropertyGetMemberSymbol(Root, counter.Uri, ScopeKind.Instance, "Value", R, R, AccessModifier.Public) { ResolvedType = VBLongType.TypeInfo };
        var complete = counter with { Members = [increment, value], DefaultInterfaceMembers = [increment, value] };

        var module = new VBStandardModuleSymbol(Root, Root, "Module1");
        var field = new VBModuleFieldVariableMemberSymbol(Root, module.Uri, "c", ScopeKind.Module, VBClassType.Of(counter), R, R, AccessModifier.Implicit);
        var tree = ScopeTreeBuilder.Build([module with { Members = [field] }, field, complete, increment, value]);
        return new StaticEvaluationContext(new ScopeTreeSymbolResolver(tree), tree.ScopeFor(module.Uri));
    }

    private MemberAccessExpressionNode MemberOf(string owner, string member) => new(NextId(), TestLocations.TestLocation, NameOf(owner), NameOf(member));

    // IR lowering S0.10: a member of an object declared as a class is bound to the member of the class's declaration as it is now - not of the snapshot the
    // declared type was built with.
    [TestMethod]
    [DataRow("Increment")]
    [DataRow("Value")]
    public void AMemberOfAnObjectDeclaredAsAClass_IsBoundToTheMemberOfTheClass(string name)
    {
        var context = ContextWithACounter();
        var access = MemberOf("c", name);

        var member = Assert.IsInstanceOfType<BoundMember>(ExpressionBinder.Bind(context, access).NodeOf(access));

        Assert.AreEqual(name, member.Member.Name);
        Assert.AreEqual("Counter", member.DeclaredClass.Name);
        Assert.IsInstanceOfType<BoundVariable>(member.Owner);
        Assert.IsNull(member.Arguments, "a member that is read has no argument list");
    }

    [TestMethod]
    public void ANewExpression_IsBoundToTheClassItCreatesAnObjectOf()
    {
        var context = ContextWithACounter();
        var created = new NewExpressionNode(NextId(), TestLocations.TestLocation, NameOf("Counter"));

        var bound = Assert.IsInstanceOfType<BoundNew>(ExpressionBinder.Bind(context, created).NodeOf(created));

        Assert.AreEqual("Counter", bound.Class.Name);
        Assert.HasCount(2, bound.Class.DefaultInterfaceMembers, "the class as it is declared now");
    }

    // MS-VBAL 5.6.15: with no object before the dot, the member is one of the With block's target, which the context types.
    [TestMethod]
    public void AMemberOfTheTargetOfAWithBlock_IsBoundWithNoOwner()
    {
        var context = ContextWithACounter();
        var counter = Assert.IsInstanceOfType<VBClassType>(((ITypedSymbol)context.Resolver.ResolveValue("c", ScopeKind.Local, context.Scope.Uri).Symbol!).ResolvedType);
        var access = new MemberAccessExpressionNode(NextId(), TestLocations.TestLocation, null, NameOf("Increment"));

        var member = Assert.IsInstanceOfType<BoundMember>(ExpressionBinder.Bind(context with { EnclosingWithTargetType = counter }, access).NodeOf(access));

        Assert.IsNull(member.Owner);
        Assert.AreEqual("Increment", member.Member.Name);
    }

    [TestMethod]
    public void AMemberThatIsNoMemberOfTheClass_IsNotBound()
    {
        var context = ContextWithACounter();
        var access = MemberOf("c", "Reset");

        Assert.IsInstanceOfType<BoundFallback>(ExpressionBinder.Bind(context, access).NodeOf(access));
    }
}
