using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Runtime.Abstract.Execution;
using System.Collections.Immutable;
using RDCore.SDK.Semantics.Plans;
using RDCore.SDK.Semantics.Static;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Semantics.Binding;

/// <summary>
/// What an expression binds to (IR lowering §3.3): its declared type, or the compile error it is, and for an operation the plan of the operation, made for
/// the declared types of its operands.
/// </summary>
/// <param name="Node">The <c>Identity</c> of the expression.</param>
/// <param name="Result">What the static semantics make of the expression.</param>
/// <param name="Plan">
/// The plan of the operation, for an operator expression whose operands have a declared type: final when the declared types decide it, deferred when the value
/// of an operand does. <see langword="null"/> for an expression that is not an operation, and for the parentheses around one (a conversion).
/// </param>
public sealed record class BoundExpression(SyntaxNodeId Node, StaticSemanticsEvaluationResult Result, OperationPlan? Plan)
{
    /// <summary>
    /// The declared type of the expression; <see langword="null"/> when it is a compile error.
    /// </summary>
    public VBType? DeclaredType => Result.Result;

    /// <summary>
    /// The compile error the expression is, if it is one.
    /// </summary>
    public VBCompileErrorInfo? Error => Result.ErrorInfo;
}

/// <summary>
/// What the expressions of a tree bind to, recorded as the static semantics evaluate them: an operand before the expression that has it.
/// </summary>
/// <remarks>
/// The bindings are those of the expressions the static semantics evaluate: an expression after an operand that is a compile error is not evaluated, and is not
/// bound. Like the evaluation it records, a recorder is for one binding at a time: it is not thread-safe.
/// </remarks>
public sealed class ExpressionBindings
{
    private readonly Dictionary<SyntaxNodeId, BoundExpression> _bound = [];
    private readonly Dictionary<SyntaxNodeId, BoundNode> _nodes = [];

    /// <summary>
    /// The layout of the activations of the procedure the expressions are written in, which binds its parameters and variables to their slots;
    /// <see langword="null"/> when the expressions are bound outside a procedure, and no variable is bound to a slot.
    /// </summary>
    public FrameLayout? Frame { get; init; }

    /// <summary>
    /// What each expression bound to, by its <c>Identity</c>.
    /// </summary>
    public IReadOnlyDictionary<SyntaxNodeId, BoundExpression> Bound => _bound;

    /// <summary>
    /// What the expression with this <c>Identity</c> bound to, if it was bound.
    /// </summary>
    public BoundExpression? this[SyntaxNodeId node] => _bound.GetValueOrDefault(node);

    /// <summary>
    /// The bound tree of an expression that was bound (<see cref="BoundNode"/>); <see langword="null"/> for one that was not.
    /// </summary>
    /// <param name="expression">The expression.</param>
    public BoundNode? NodeOf(ExpressionNode expression) => _nodes.GetValueOrDefault(expression.Identity);

    /// <summary>
    /// Records what an expression the static semantics evaluated binds to. An expression that is evaluated again has its binding replaced.
    /// </summary>
    /// <param name="context">The context the expression was evaluated in: the scope its names resolve from.</param>
    /// <param name="expression">The expression.</param>
    /// <param name="result">What the static semantics made of it.</param>
    internal void Record(StaticEvaluationContext context, ExpressionNode expression, StaticSemanticsEvaluationResult result)
    {
        var plan = PlanOf(expression);
        _bound[expression.Identity] = new BoundExpression(expression.Identity, result, plan);
        _nodes[expression.Identity] = NodeFor(context, expression, result, plan);
    }

    // an operand is recorded before the expression that has it, so its node is there to build this one from. An expression that is a compile error is bound
    // to the error and to what it is late-bound, its run-time counterpart.
    private BoundNode NodeFor(StaticEvaluationContext context, ExpressionNode expression, StaticSemanticsEvaluationResult result, OperationPlan? plan)
        => result.ErrorInfo is { } error ? new BoundError(expression, error, ShapeOf(context, expression, result, plan)) : ShapeOf(context, expression, result, plan);

    // the node of an expression, by its shape and what its names resolve to. What resolves to nothing the binder can bind is bound late.
    private BoundNode ShapeOf(StaticEvaluationContext context, ExpressionNode expression, StaticSemanticsEvaluationResult result, OperationPlan? plan)
        => expression switch
        {
            LiteralExpressionNode literal => new BoundLiteral(literal),

            // the name is resolved as the evaluation resolves it, from the same scope: the binding is the resolution, made once. A variable is read; what
            // anything else a name resolves to evaluates to is the symbol's to say.
            SimpleNameExpressionNode name when context.Resolver.ResolveValue(name, ScopeKind.Local, context.Scope.Uri).Symbol is { } symbol
                => IsVariable(symbol)
                    ? new BoundVariable(name, symbol, Frame is { } frame && frame.TryGetSlot(symbol, out var slot) ? slot : null,
                        symbol.GetProperty(SymbolProperties.AutoInstantiated))
                    : new BoundName(name, symbol),

            // Me is a reserved word, never a simple name, but the implicit parameter it is really is named Me: resolved as the evaluation resolves it.
            InstanceExpressionNode instance when context.Resolver.ResolveValue(Tokens.Me, ScopeKind.Local, context.Scope.Uri).Symbol is { } me
                => new BoundInstance(instance, me, Frame is { } frame && frame.TryGetSlot(me, out var slot) ? slot : null),

            // an operation of operands bound late has no plan: their declared types are not known.
            VBBinaryOperatorExpressionNode binary => new BoundBinary(binary, plan, ChildNode(binary.Left), ChildNode(binary.Right)),

            VBUnaryOperatorExpressionNode unary => new BoundUnary(unary, plan, ChildNode(unary.Operand)),

            // a call by the bare name of a procedure, resolved as the evaluation resolves it.
            IndexExpressionNode { Callee: SimpleNameExpressionNode callee } index
                when context.Resolver.ResolveValue(callee, ScopeKind.Local, context.Scope.Uri).Symbol is
                    VBTypeMemberSymbol procedure and (VBProcedureMemberSymbol or VBFunctionMemberSymbol or VBPropertyGetMemberSymbol)
                => new BoundCall(index, procedure, BoundArgumentsOf(procedure, index)),

            // MS-VBAL §5.6.12: a member of a project or a procedural module - `Constants.vbCrLf`, `Module1.Count` - is what the name means unqualified there,
            // and a member of an enumeration named by its type is a constant: neither is a member of a value. Classified and resolved as the evaluation does.
            MemberAccessExpressionNode { Owner: { } owner } access when QualifiedMemberOf(context, owner, access) is { } member
                => new BoundQualifiedName(access, member),

            // a call of a procedure of a project or a procedural module - `VBA.Len(s)` - is a call with the arguments, and no object to evaluate; anything
            // else such a name means is a value the arguments index.
            IndexExpressionNode { Callee: MemberAccessExpressionNode { Owner: { } owner } access } index
                when context.Resolver.NamespaceOf(owner, context.Scope.Uri) is { } qualifier && NamespaceMemberOf(context, qualifier, access) is { } member
                => member is VBTypeMemberSymbol procedure and (VBProcedureMemberSymbol or VBFunctionMemberSymbol or VBPropertyGetMemberSymbol)
                    ? new BoundCall(index, procedure, BoundArgumentsOf(procedure, index))
                    : new BoundIndex(index, new BoundQualifiedName(access, member), ArgumentNodes(index)),

            // a member of an object declared as a class, read or called: resolved in the class, as the evaluation resolves it. A function that takes no
            // argument is not given what follows it in parentheses, which indexes what it returns: that is evaluated late-bound.
            MemberAccessExpressionNode { Owner: { } owner } access
                when OwnerOf(context, owner) is var (ownerNode, declared) && InvocableMember(declared, access.Member.IdentifierName) is { } member
                => new BoundMember(access, access, ownerNode, declared, member, null),

            // the class a TypeOf … Is tests against, by its name - bare, or qualified by its project - resolved as the evaluation resolves it.
            TypeOfIsExpressionNode typeOf
                when NodeOf(typeOf.Operand) is { } operand && QualifiedTypeName(typeOf.TypeExpression) is var (qualifier, typeName)
                    && VBProjectSymbol.ResolveQualifiedType(context.Resolver, qualifier, typeName, context.Scope.Uri).Symbol is VBClassModuleSymbol target
                => new BoundTypeOf(typeOf, operand, target),

            PrecompilerNameExpressionNode precompilerName => new BoundPrecompilerName(precompilerName),

            // the class a New expression creates an object of, as its declaration is now.
            NewExpressionNode newExpression when result.Result is VBClassType { Symbol: var snapshot }
                => new BoundNew(newExpression, VBProjectSymbol.ResolveClass(context.Resolver, snapshot) ?? snapshot),

            IndexExpressionNode { Callee: MemberAccessExpressionNode { Owner: { } owner } access } index
                when OwnerOf(context, owner) is var (ownerNode, declared) && InvocableMember(declared, access.Member.IdentifierName) is { } member
                    && !ReturnsToBeIndexed(member, index.Arguments)
                => new BoundMember(index, access, ownerNode, declared, member, index, BoundArgumentsOf(member, index)),

            // an element of an array, or a default member of an object, a variable holds: the variable is read through its node (a procedure, which the name
            // calls, is a BoundCall above).
            IndexExpressionNode { Callee: SimpleNameExpressionNode callee } index when NodeOf(callee) is BoundVariable variable
                => new BoundIndex(index, variable, ArgumentNodes(index)),

            // what any other value - a call's result, `F()(1)`, an element, `a(1)(2)` - is indexed by: the callee is evaluated as it is, then indexed.
            IndexExpressionNode { Callee: not (SimpleNameExpressionNode or MemberAccessExpressionNode) } index => new BoundIndex(index, ChildNode(index.Callee), ArgumentNodes(index)),

            // a member of any other value - an object declared Object or Variant (late-bound), a field of a record, what an element, a call or another member
            // is: the owner bound, the member found from the value each time. A name of a namespace or of an enumeration is
            // no owner of the kind, whether or not the member is found in it.
            MemberAccessExpressionNode { Owner: { } owner } access when !IsQualifier(context, owner)
                => new BoundMember(access, access, ChildNode(owner), null, null, null),

            IndexExpressionNode { Callee: MemberAccessExpressionNode { Owner: { } owner } access } index
                when context.Resolver.NamespaceOf(owner, context.Scope.Uri) is null
                => new BoundMember(index, access, ChildNode(owner), null, null, index),

            // a member of the target of the enclosing With block (MS-VBAL §5.6.15), declared as a class: the object is the block's, not an expression's.
            MemberAccessExpressionNode { Owner: null } access
                when WithTargetOf(context) is { } declared && InvocableMember(declared, access.Member.IdentifierName) is { } member
                => new BoundMember(access, access, null, declared, member, null),

            IndexExpressionNode { Callee: MemberAccessExpressionNode { Owner: null } access } index
                when WithTargetOf(context) is { } declared && InvocableMember(declared, access.Member.IdentifierName) is { } member
                    && !ReturnsToBeIndexed(member, index.Arguments)
                => new BoundMember(index, access, null, declared, member, index, BoundArgumentsOf(member, index)),

            // a member of the target of a With block declared as anything else: the object is the block's, the member found from it.
            MemberAccessExpressionNode { Owner: null } access => new BoundMember(access, access, null, null, null, null),

            IndexExpressionNode { Callee: MemberAccessExpressionNode { Owner: null } access } index => new BoundMember(index, access, null, null, null, index),

            // the special forms and the dictionary access: their operands are bound, and what those evaluate to is the evaluation's to bound, gather or
            // look up, each time.
            ArrayBoundExpressionNode arrayBound
                => new BoundArrayBound(arrayBound, ChildNode(arrayBound.Array), arrayBound.Dimension is { } dimension ? ChildNode(dimension) : null),

            ArrayExpressionNode array => new BoundArray(array, [.. array.Elements.Select(ChildNode)]),

            DictionaryAccessExpressionNode dictionary => new BoundDictionaryAccess(dictionary, dictionary.Owner is { } owner ? ChildNode(owner) : null),

            // ByVal flags how an argument is passed - as a value, never aliased - and evaluates to its operand.
            ByValArgumentExpressionNode byVal => ChildNode(byVal.Operand),

            _ => new BoundLate(expression),
        };

    // what a qualified name means when its owner is a namespace - the member of the namespace, and nothing else when it has none - or an enumeration, which
    // the evaluation asks in that order.
    private static Symbol? QualifiedMemberOf(StaticEvaluationContext context, ExpressionNode owner, MemberAccessExpressionNode access)
        => context.Resolver.NamespaceOf(owner, context.Scope.Uri) is { } qualifier
            ? NamespaceMemberOf(context, qualifier, access)
            : EnumerationOf(context, owner)?.Members.FirstOrDefault(member
                => string.Equals(member.Name, access.Member.IdentifierName, StringComparison.OrdinalIgnoreCase));

    // the member of a namespace a qualified name refers to, when it refers to one that is no namespace itself.
    private static Symbol? NamespaceMemberOf(StaticEvaluationContext context, Symbol qualifier, MemberAccessExpressionNode access)
        => context.Resolver.ResolveMember(qualifier, access.Member, context.Scope.Uri).Symbol is { } member && !NamespaceExpressions.IsNamespace(member)
            ? member
            : null;

    // the enumeration an expression names - `Mode`, or `Widgets.Mode` - classified by what the name resolves to.
    private static VBEnumType? EnumerationOf(StaticEvaluationContext context, ExpressionNode owner)
        => NameClassificationStaticSemantics.EnumerationOf(context, owner);

    // the name of a class a type expression is - bare, or qualified by its project - as New and TypeOf … Is name it.
    private static (string? Qualifier, string TypeName)? QualifiedTypeName(ExpressionNode typeExpression) => typeExpression switch
    {
        SimpleNameExpressionNode simple => (null, simple.IdentifierName),
        MemberAccessExpressionNode { Owner: SimpleNameExpressionNode owner, Member: { } member } => (owner.IdentifierName, member.IdentifierName),
        _ => null,
    };

    // the node of an operand a structural node evaluates as it is: one that is not bound is bound late, on its own.
    private BoundNode ChildNode(ExpressionNode operand) => NodeOf(operand) ?? new BoundLate(operand);

    // the arguments of an index expression, each the node of its expression; a named or an omitted one has no value of its own to be bound, and is late.
    private ImmutableArray<BoundNode> ArgumentNodes(IndexExpressionNode index) => [.. index.Arguments.Select(ChildNode)];

    // the bound node of the expression an object is reached through, and the class it is declared as, when it is declared as one: the class's declaration as
    // it is now, which a type built before it was complete only has a snapshot of (MemberAccessExpressionStaticSemantics reads its members the same way).
    private (BoundNode Node, VBClassModuleSymbol Declared)? OwnerOf(StaticEvaluationContext context, ExpressionNode owner)
        => NodeOf(owner) is { } node && DeclaredTypeOf(owner) is VBClassType { Symbol: var snapshot }
            ? (node, VBProjectSymbol.ResolveClass(context.Resolver, snapshot) ?? snapshot)
            : null;

    // whether an owner names a namespace or an enumeration, which the evaluation classifies it as before it evaluates any owner as a value.
    private static bool IsQualifier(StaticEvaluationContext context, ExpressionNode owner)
        => context.Resolver.NamespaceOf(owner, context.Scope.Uri) is not null || EnumerationOf(context, owner) is not null;

    // the class the target of the enclosing With block is declared as, when it is declared as one, as its declaration is now.
    private static VBClassModuleSymbol? WithTargetOf(StaticEvaluationContext context)
        => context.EnclosingWithTargetType is VBClassType { Symbol: var snapshot } ? VBProjectSymbol.ResolveClass(context.Resolver, snapshot) ?? snapshot : null;

    // the member of a class a qualified name reads or calls: a Sub, a Function or a Property Get - not a Property Let or Set, which an assignment invokes -
    // or, when there is none of the name, a public variable, which is the storage of the object and is read.
    private static VBTypeMemberSymbol? InvocableMember(VBClassModuleSymbol declared, string name)
        => declared.DefaultInterfaceMembers.FirstOrDefault(candidate =>
            candidate is VBPropertyGetMemberSymbol or VBFunctionMemberSymbol or VBProcedureMemberSymbol and not (VBPropertyLetMemberSymbol or VBPropertySetMemberSymbol)
            && string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? declared.DefaultInterfaceMembers.FirstOrDefault(candidate =>
            candidate.Kind is SymbolKindExt.Field or SymbolKindExt.Variable && string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));

    // a Function or Property Get of an object that declares no parameter but its implicit Me, given arguments: they index what it returns.
    private static bool ReturnsToBeIndexed(VBTypeMemberSymbol member, ImmutableArray<ExpressionNode> arguments)
        => member is VBFunctionMemberSymbol or VBPropertyGetMemberSymbol && !arguments.IsDefaultOrEmpty && arguments is not [MissingArgumentNode]
            && ParametersOf(member).All(parameter => parameter.Name == "Me");

    // how each argument of a call is passed, bound once (IR lowering S1.7): to the parameters of the procedure after its Me, if it has one - a member called
    // on an object is given that object, and one of the class named bare in its own code the current instance (MS-VBAL §5.6.10).
    private ImmutableArray<BoundArgument>? BoundArgumentsOf(VBTypeMemberSymbol procedure, IndexExpressionNode call)
    {
        if (procedure is not (VBProcedureMemberSymbol or VBReturningMemberSymbol))
        {
            return null;
        }

        var parameters = ParametersOf(procedure);
        return ArgumentMapping.Bind(parameters is [{ Name: Tokens.Me }, ..] ? parameters.RemoveAt(0) : parameters, call.Arguments, NodeOf, DeclaredTypeOf);
    }

    private static ImmutableArray<VBParameterSymbol> ParametersOf(VBTypeMemberSymbol member) => member switch
    {
        VBReturningMemberSymbol returning => returning.Parameters,
        VBProcedureMemberSymbol procedure => procedure.Parameters,
        _ => [],
    };

    // a parameter, a variable or a field, which a name reads the value of: no procedure, which it calls, nor a constant or an enumeration member, which have
    // a value and no storage.
    internal static bool IsVariable(Symbol symbol)
        => symbol is ITypedSymbol && symbol.Kind is SymbolKindExt.Variable or SymbolKindExt.Field
            && symbol is not (VBProcedureMemberSymbol or VBFunctionMemberSymbol or VBPropertyGetMemberSymbol or VBLocalConstantSymbol or VBConstantMemberSymbol);

    // the plan of an operation, for the declared types of its operands as the operator is given them (an Enum is a Long, a LongPtr what it is an alias of).
    private OperationPlan? PlanOf(ExpressionNode expression) => expression switch
    {
        VBBinaryOperatorExpressionNode binary when DeclaredTypeOf(binary.Left) is { } left && DeclaredTypeOf(binary.Right) is { } right
            => OperationPlanner.ForDeclared(binary.Token,
                ExpressionStaticSemanticsEvaluator.AsOperand(left), ExpressionStaticSemanticsEvaluator.AsOperand(right)),

        // a pair of parentheses is a conversion, not an operation with a table of effective types.
        VBUnaryOperatorExpressionNode unary when unary.Token is Tokens.NegationOp or Tokens.LogicalNotOp && DeclaredTypeOf(unary.Operand) is { } operand
            => OperationPlanner.ForDeclared(unary.Token, ExpressionStaticSemanticsEvaluator.AsOperand(operand)),

        _ => null,
    };

    private VBType? DeclaredTypeOf(ExpressionNode operand) => this[operand.Identity]?.DeclaredType;
}

/// <summary>
/// What an assignment binds to: its target and its value, and the plan of the let-coercion of the value to the target.
/// </summary>
/// <param name="Expressions">What the expressions of the target and of the value bind to.</param>
/// <param name="Conversion">
/// The plan of the let-coercion of the value to the declared type of the target, for a Let assignment whose target and value have one; <see langword="null"/>
/// for a Set assignment, which is not a let-coercion, and when either is a compile error or the target's type is not known.
/// </param>
public sealed record class BoundAssignment(ExpressionBindings Expressions, ConversionPlan? Conversion);

/// <summary>
/// Binds expressions (IR lowering §3.3): each to its declared type, as the static semantics evaluate it, and each operation to its
/// <see cref="OperationPlan"/>, made for the declared types of its operands - final when they decide it, deferred when a value does.
/// </summary>
/// <remarks>
/// The binder is the static evaluation itself, with a recorder for what it evaluates (<see cref="StaticEvaluationContext.Bindings"/>): a declared type is the
/// one the static semantics give the expression, by construction. The run time evaluates the bound tree of each expression it is given
/// (<see cref="ExpressionBindings.NodeOf"/>): names read through their slots, operations by their plans, calls and members resolved once.
/// </remarks>
public static class ExpressionBinder
{
    /// <summary>
    /// What an expression, and each expression in it, binds to.
    /// </summary>
    /// <param name="context">The context the expression is evaluated in. What the evaluation would record of its facts is not recorded: binding is not
    /// analyzing.</param>
    /// <param name="expression">The expression.</param>
    /// <param name="frame">The layout of the activations of the procedure the expression is written in, which binds its variables to their slots;
    /// <see langword="null"/> to bind none to a slot.</param>
    public static ExpressionBindings Bind(StaticEvaluationContext context, ExpressionNode expression, FrameLayout? frame = null)
    {
        var bindings = new ExpressionBindings { Frame = frame };
        ExpressionStaticSemanticsEvaluator.Evaluate(context with { Facts = null, Bindings = bindings }, expression);
        return bindings;
    }

    /// <summary>
    /// What an assignment binds to: the expressions of its target and of its value, and the plan of the let-coercion of the value to the target.
    /// </summary>
    /// <param name="context">The context the assignment is evaluated in.</param>
    /// <param name="assignment">The assignment.</param>
    public static BoundAssignment BindAssignment(StaticEvaluationContext context, AssignmentStatementNode assignment)
    {
        var bindings = new ExpressionBindings();
        var bindingContext = context with { Facts = null, Bindings = bindings };
        var target = ExpressionStaticSemanticsEvaluator.Evaluate(bindingContext, assignment.Target);
        var value = ExpressionStaticSemanticsEvaluator.Evaluate(bindingContext, assignment.Value);

        var conversion = assignment.Kind is AssignmentKind.ImplicitLet or AssignmentKind.ExplicitLet
            && target is { IsSuccess: true, Result: { } targetType and not VBUnknownType }
            && value is { IsSuccess: true, Result: { } valueType }
                ? ConversionPlanner.ForDeclared(valueType, targetType)
                : null;

        return new BoundAssignment(bindings, conversion);
    }
}
