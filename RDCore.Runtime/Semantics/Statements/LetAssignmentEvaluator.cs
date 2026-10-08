using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.Runtime.Semantics.Operators;
using RDCore.SDK;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.Operators;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Facts;

namespace RDCore.Runtime.Semantics.Statements;

/// <summary>
/// Let-assigns a value that has already been evaluated into a variable expression
/// (<strong>MS-VBAL 5.4.3.8</strong>).
/// </summary>
/// <remarks>
/// An assignment statement is not the only thing that Let-assigns. <c>Line Input #</c> Let-assigns the line
/// it read, <c>Input #</c> Let-assigns each value in its list, and <c>Get</c> Let-assigns the record it read
/// (MS-VBAL 5.4.5.6, .10 and .12, each saying so in those words) - so the target side of an assignment is
/// factored out here rather than restated by each of them, and the function-result-variable rule below is
/// honoured by all of them for free.
/// </remarks>
/// <param name="coercions">The Let-coercion rules the assignment applies to its source value.</param>
/// <param name="operators">The let-assignment operator a variable is assigned through.</param>
/// <param name="expressions">Evaluates the owner of a member-access target, which has to be in hand before
/// the field being assigned can be.</param>
public sealed class LetAssignmentEvaluator(
    ILetCoercionRuntimeSemanticsProvider coercions,
    IOperatorRuntimeSemanticsProvider operators,
    RuntimeExpressionEvaluator expressions)
{

    /// <summary>
    /// Resolves the symbol <paramref name="target"/> names.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Assign(IRuntimeSession, RuntimeEvaluationContext, StatementNode, Symbol, ExpressionNode, ExpressionNode, VBTypedValue)"/>
    /// because <c>Input #</c> (<strong>MS-VBAL §5.4.5.10</strong>) reads a <em>different number of characters</em>
    /// depending on the declared type of the variable it is about to assign — so it has to know the target
    /// before it has a value for it.
    /// </remarks>
    /// <param name="session">The session whose symbols the target is resolved against.</param>
    /// <param name="context">The evaluation context, whose scope the target is resolved in.</param>
    /// <param name="target">The variable expression.</param>
    /// <param name="symbol">The symbol it names.</param>
    /// <returns><c>false</c> when the expression is not a shape this can resolve, or names nothing.</returns>
    public bool TryResolveTarget(
        IRuntimeSession session, RuntimeEvaluationContext context, ExpressionNode target, out Symbol? symbol)
    {
        symbol = target is SimpleNameExpressionNode simpleName
            ? session.Symbols.Resolver.ResolveValue(simpleName.IdentifierName, ScopeKind.Local, context.Scope).Symbol
            : null;

        return symbol is not null;
    }

    /// <summary>
    /// Let-assigns <paramref name="value"/> into <paramref name="target"/>.
    /// </summary>
    /// <param name="session">The session whose symbols the target is resolved against.</param>
    /// <param name="context">The evaluation context, whose scope the target is resolved in.</param>
    /// <param name="statement">The statement doing the assigning, whose identity and location the
    /// synthetic assignment operator takes.</param>
    /// <param name="target">The variable expression being assigned into.</param>
    /// <param name="source">The expression the value came from, for the location of a coercion error. The
    /// <paramref name="target"/> itself when the value came from somewhere that is not an expression.</param>
    /// <param name="value">The value to assign, already evaluated.</param>
    public RuntimeExecutionOutcome Assign(
        IRuntimeSession session,
        RuntimeEvaluationContext context,
        StatementNode statement,
        ExpressionNode target,
        ExpressionNode source,
        VBTypedValue value)
        => target is MemberAccessExpressionNode memberAccess
            ? AssignField(session, context, statement, memberAccess, source, value)
            : IsMemberOfObject(target)
                ? AssignObjectMember(session, context, statement, target, source, value, isSet: false)
                : target is IndexExpressionNode element
                    ? AssignArrayElement(session, context, element.Callee, element.Arguments, source, value, isSet: false)
                    : TryResolveTarget(session, context, target, out var symbol)
                        ? Assign(session, context, statement, symbol!, target, source, value)
                        : RuntimeExecutionOutcome.InternalError;

    // `owner.Property(index) = value`: an index expression whose callee is a member access.
    private static bool IsMemberOfObject(ExpressionNode target) => target is IndexExpressionNode { Callee: MemberAccessExpressionNode };

    /// <summary>
    /// Assigns <paramref name="value"/> to an element of an array: <c>a(1, 2) = value</c>, or <c>owner.Items(1) = value</c> when the
    /// array is what a variable of an object holds (<strong>MS-VBAL §5.4.3.8</strong>, <strong>§5.4.3.9</strong>).
    /// </summary>
    /// <remarks>
    /// The value is Let-coerced to the element type of the array - Set-coerced for a <c>Set</c> assignment, which also lets go of the
    /// object the element held and takes a reference to the one it is given. A subscript outside the bounds of the array is
    /// error 9.
    /// </remarks>
    /// <param name="session">The session the array lives in.</param>
    /// <param name="context">The scope of the assignment.</param>
    /// <param name="arrayExpression">The expression the array is the value of: the callee of the index expression.</param>
    /// <param name="subscripts">The subscript expressions, one for each dimension.</param>
    /// <param name="source">The expression the value came from, for the location of an error.</param>
    /// <param name="value">The value to assign, already evaluated.</param>
    /// <param name="isSet">Whether it is a <c>Set</c> assignment.</param>
    public RuntimeExecutionOutcome AssignArrayElement(
        IRuntimeSession session, RuntimeEvaluationContext context, ExpressionNode arrayExpression,
        System.Collections.Immutable.ImmutableArray<ExpressionNode> subscripts, ExpressionNode source, VBTypedValue value, bool isSet)
    {
        var evaluated = expressions.Evaluate(session, arrayExpression, context);
        if (!evaluated.IsSuccess)
        {
            return evaluated.IsInternalError ? RuntimeExecutionOutcome.InternalError : RuntimeExecutionOutcome.Error(evaluated.ErrorInfo!);
        }

        var held = evaluated.Result;
        while (held is VBVariantValue { TypedValue: { } wrapped })
        {
            held = wrapped;
        }

        if (held is not VBArrayValue array)
        {
            return RuntimeExecutionOutcome.InternalError;
        }

        if (expressions.EvaluateSubscripts(session, context, subscripts, out var indices) is { } failure)
        {
            return failure.IsInternalError ? RuntimeExecutionOutcome.InternalError : RuntimeExecutionOutcome.Error(failure.ErrorInfo!);
        }

        if (array.GetElementHandle(indices) is null)
        {
            return RuntimeExecutionOutcome.Error(VBRuntimeErrorInfo.For(
                VBRuntimeErrorId.SubscriptOutOfRange, arrayExpression.Location, string.Join(", ", indices)));
        }

        if (isSet)
        {
            if (expressions.SetCoercion is not { } setCoercion)
            {
                return RuntimeExecutionOutcome.InternalError;
            }

            var setResult = setCoercion.EvaluateSetCoercion(session, source, value, array.ItemType);
            if (!setResult.IsSuccess)
            {
                return RuntimeExecutionOutcome.Error(setResult.ErrorInfo!);
            }

            // what the element held is its object's identity as of now: the cell is about to be bound to something else.
            var previous = array[indices] is VBObjectValue heldObject ? new VBObjectValue(heldObject.Value) : null;
            var cell = StoreElement(session, array, indices, setResult.Result!.RuntimeValue);
            ObjectReferences.Rebind(session, cell, previous, setResult.Result as VBObjectValue);
            return RuntimeExecutionOutcome.Next;
        }

        var frame = new LetCoercionStackFrame(source.Identity, InputIndex.CoercionSourceValue, value, new VBTypeDescValue(array.ItemType), ConversionSite.Assignment);
        var coerced = coercions.EvaluateLetCoercionSemantics(session.Symbols.Resolver, source, frame);
        if (!coerced.IsApplicable)
        {
            return RuntimeExecutionOutcome.InternalError;
        }

        if (!coerced.IsSuccess)
        {
            return RuntimeExecutionOutcome.Error(coerced.ErrorInfo!);
        }

        StoreElement(session, array, indices, coerced.Result!.RuntimeValue);
        return RuntimeExecutionOutcome.Next;
    }

    // An element is written where it is: its cell is the storage, and the one an object reference is held by, which is what
    // lets the reference be released when the element is assigned again. Only a cell that cannot be written - the inert one
    // an element of a class, user-defined type or Object type starts with - is given a binding that can.
    private static IBindingHandle StoreElement(IRuntimeSession session, VBArrayValue array, int[] indices, IRuntimeValue value)
    {
        var cell = array.GetElementHandle(indices)!;
        if (cell.BindingCapabilities.HasFlag(BindingCapabilities.SetValue))
        {
            cell.SetValue(session.Symbols.Resolver, value);
            return cell;
        }

        array.TrySetElement(new ValueBindingHandle(value), indices);
        return array.GetElementHandle(indices)!;
    }

    /// <summary>
    /// Assigns <paramref name="value"/> to a member of an object (<strong>MS-VBAL §5.4.3.8</strong>, <strong>§5.4.3.9</strong>):
    /// a public variable of its class, which is its storage, or a property, which the assignment invokes - its
    /// <c>Property Let</c>, or its <c>Property Set</c> in a <c>Set</c> assignment.
    /// </summary>
    /// <remarks>
    /// The target is <c>owner.Member</c>, or <c>owner.Member(index, ...)</c> for a property that is indexed. The member is
    /// looked up the way a read of it would be: through the interface the owner is declared as, when it is declared as
    /// one the object's class implements (<strong>§5.3.1.9</strong>). A <c>Set</c> of a variable also lets go of the
    /// object it held and takes a reference to the one it is given, and attaches the handlers of a <c>WithEvents</c>
    /// variable to it, as a <c>Set</c> of a variable of the code's own does.
    /// <para>
    /// An index on a public variable is an element of the array it holds, <c>owner.Items(1) = value</c>
    /// (<see cref="AssignArrayElement"/>).
    /// </para>
    /// </remarks>
    /// <param name="session">The session the object lives in.</param>
    /// <param name="context">The scope of the assignment.</param>
    /// <param name="statement">The statement doing the assigning.</param>
    /// <param name="target">The member access, or the index expression on one.</param>
    /// <param name="source">The expression the value came from, for the location of an error.</param>
    /// <param name="value">The value to assign, already evaluated.</param>
    /// <param name="isSet">Whether it is a <c>Set</c> assignment.</param>
    public RuntimeExecutionOutcome AssignObjectMember(
        IRuntimeSession session, RuntimeEvaluationContext context, StatementNode statement, ExpressionNode target,
        ExpressionNode source, VBTypedValue value, bool isSet)
    {
        var (memberAccess, indexArguments) = target switch
        {
            MemberAccessExpressionNode access => (access, []),
            IndexExpressionNode { Callee: MemberAccessExpressionNode access } index => (access, index.Arguments),
            _ => (null, System.Collections.Immutable.ImmutableArray<ExpressionNode>.Empty),
        };

        if (memberAccess is null)
        {
            return RuntimeExecutionOutcome.InternalError;
        }

        if (!TryEvaluateOwner(session, context, memberAccess, out var owner, out var failure))
        {
            return failure;
        }

        return owner is VBObjectValue objectOwner
            ? AssignObjectMember(session, context, statement, memberAccess, indexArguments, objectOwner, source, value, isSet)
            : RuntimeExecutionOutcome.InternalError;
    }

    private RuntimeExecutionOutcome AssignObjectMember(
        IRuntimeSession session, RuntimeEvaluationContext context, StatementNode statement, MemberAccessExpressionNode memberAccess,
        System.Collections.Immutable.ImmutableArray<ExpressionNode> indexArguments, VBObjectValue owner, ExpressionNode source,
        VBTypedValue value, bool isSet)
    {
        // an object variable that holds no object has no member to assign: error 91.
        if (owner.IsNothing())
        {
            return RuntimeExecutionOutcome.Error(VBRuntimeErrorInfo.For(
                VBRuntimeErrorId.ObjectVariableOrWithBlockVariableNotSet, memberAccess.Location, Exceptions.VBMemberAccess_ObjectVariableNotSet_Verbose));
        }

        if (expressions.ResolveAssignableMember(session, context, memberAccess.Owner, owner, memberAccess.Member.IdentifierName, isSet) is not { } member)
        {
            return RuntimeExecutionOutcome.Error(VBRuntimeErrorInfo.For(
                VBRuntimeErrorId.ObjectDoesntSupportThisPropertyOrMethod, memberAccess.Location, Exceptions.VBMemberAssignment_NotAssignable_Verbose));
        }

        if (member.Accessor is { } accessor)
        {
            var invocation = expressions.InvokeAssignment(session, context, accessor, member.Receiver!, indexArguments, value, source, isSet);
            return invocation.IsSuccess ? RuntimeExecutionOutcome.Next
                : invocation.IsInternalError ? RuntimeExecutionOutcome.InternalError
                : RuntimeExecutionOutcome.Error(invocation.ErrorInfo!);
        }

        if (member.Field is null || member.Instance is null)
        {
            return RuntimeExecutionOutcome.InternalError;
        }

        // a public variable is the storage of the object it is a variable of; an index on it is an element of what it holds.
        if (!indexArguments.IsEmpty)
        {
            return AssignArrayElement(session, context, memberAccess, indexArguments, source, value, isSet);
        }

        var field = member.Field;
        var instance = member.Instance;

        var handle = instance.GetValue(field);
        if (!isSet)
        {
            var frame = new LetCoercionStackFrame(statement.Identity, InputIndex.CoercionSourceValue, value, new VBTypeDescValue(field.ResolvedType), ConversionSite.Assignment);
            var coerced = coercions.EvaluateLetCoercionSemantics(session.Symbols.Resolver, source, frame);
            if (!coerced.IsApplicable)
            {
                return RuntimeExecutionOutcome.InternalError;
            }

            if (!coerced.IsSuccess)
            {
                return RuntimeExecutionOutcome.Error(coerced.ErrorInfo!);
            }

            // an array, a record and a Variant are held as the value that identifies them by where it is, which is what every allocation boxes them
            // in: the value's own runtime value is not one for them, and a whole array assigned to a variable of an object would have none.
            handle.SetValue(session.Symbols.Resolver, SymbolAddressTable.BoxedValue(coerced.Result!));
            return RuntimeExecutionOutcome.Next;
        }

        if (expressions.SetCoercion is not { } setCoercion)
        {
            return RuntimeExecutionOutcome.InternalError;
        }

        var setResult = setCoercion.EvaluateSetCoercion(session, source, value, field.ResolvedType);
        if (!setResult.IsSuccess)
        {
            return RuntimeExecutionOutcome.Error(setResult.ErrorInfo!);
        }

        // a value is a view of its handle, which is about to be written to: what the variable held is its object's identity as of now.
        var previous = field.ResolvedType.CreateValue(handle) is VBObjectValue held ? new VBObjectValue(held.Value) : null;
        var withEvents = field.GetProperty(SymbolProperties.WithEvents);
        if (withEvents)
        {
            EventAttachments.Detach(session, owner.Value, field, previous);
        }

        handle.SetValue(session.Symbols.Resolver, setResult.Result!.RuntimeValue);
        ObjectReferences.Rebind(session, handle, previous, setResult.Result as VBObjectValue);
        if (withEvents)
        {
            EventAttachments.Attach(session, owner.Value, field, setResult.Result as VBObjectValue);
        }

        return RuntimeExecutionOutcome.Next;
    }

    // MS-VBAL §5.4.3.8 with a <member-access-expression> target whose owner is a UDT. A UDT field is not an
    // addressable Symbol the way a variable is - it lives on the value, which is what makes it reachable at
    // all - so the assignment is the coercion plus a write to the cell, rather than the "__let_op" operator
    // every Symbol-targeted assignment goes through. A class instance's field is still the operator's, since
    // it does have real storage; this only takes the UDT case.
    private RuntimeExecutionOutcome AssignField(
        IRuntimeSession session,
        RuntimeEvaluationContext context,
        StatementNode statement,
        MemberAccessExpressionNode memberAccess,
        ExpressionNode source,
        VBTypedValue value)
    {
        if (!TryEvaluateOwner(session, context, memberAccess, out var owner, out var failure))
        {
            return failure;
        }

        if (owner is not VBUserDefinedTypeValue udt)
        {
            // a public variable of an object, or a Property Let of it.
            return owner is VBObjectValue objectOwner
                ? AssignObjectMember(session, context, statement, memberAccess, [], objectOwner, source, value, isSet: false)
                : RuntimeExecutionOutcome.InternalError;
        }

        var name = memberAccess.Member.IdentifierName;
        if (udt.Fields.FirstOrDefault(field => field.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            is not { ResolvedType: { } fieldType } declared)
        {
            return RuntimeExecutionOutcome.InternalError;
        }

        // "the source is Let-coerced to the target's declared type" - a field's declared type is its own, and
        // the coercion is the same one an assignment to a variable of that type would apply.
        var frame = new LetCoercionStackFrame(
            statement.Identity, InputIndex.CoercionSourceValue, value, new VBTypeDescValue(fieldType), ConversionSite.Assignment);
        var coerced = coercions.EvaluateLetCoercionSemantics(session.Symbols.Resolver, source, frame);
        if (!coerced.IsApplicable)
        {
            return RuntimeExecutionOutcome.InternalError;
        }

        if (!coerced.IsSuccess)
        {
            return RuntimeExecutionOutcome.Error(coerced.ErrorInfo!);
        }

        return udt.TrySetField(declared.Name, coerced.Result!)
            ? RuntimeExecutionOutcome.Next
            : RuntimeExecutionOutcome.InternalError;
    }

    /// <summary>
    /// The type a member-access <paramref name="target"/> is declared as: the type of the public variable of the object, or of the field of the user-defined type, that it is.
    /// </summary>
    /// <remarks>
    /// What a statement that makes a value for the target - an array that <c>ReDim</c> gives dimensions to - needs to know of it, because the value it holds before
    /// says only what it is, and an array that has no dimensions yet does not say what it holds. A property has no declared type of its own that
    /// an assignment writes to, and says <see langword="false"/> here.
    /// </remarks>
    /// <param name="session">The session the object lives in.</param>
    /// <param name="context">The scope of the target.</param>
    /// <param name="target">The expression the value is written through.</param>
    /// <param name="declared">The declared type.</param>
    /// <returns><see langword="false"/> when the target is not a member access, or does not name a variable that has a declared type.</returns>
    public bool TryGetDeclaredType(IRuntimeSession session, RuntimeEvaluationContext context, ExpressionNode target, out SDK.Model.Types.Abstract.VBType? declared)
    {
        declared = null;
        if (target is not MemberAccessExpressionNode memberAccess || !TryEvaluateOwner(session, context, memberAccess, out var owner, out _))
        {
            return false;
        }

        var name = memberAccess.Member.IdentifierName;
        declared = owner switch
        {
            VBObjectValue objectOwner when expressions.ResolveAssignableMember(session, context, memberAccess.Owner, objectOwner, name, isSet: false)
                is { Field: ITypedSymbol { ResolvedType: { } fieldType } } => fieldType,
            VBUserDefinedTypeValue udt => udt.Fields.FirstOrDefault(field => field.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.ResolvedType,
            _ => null,
        };

        return declared is not null;
    }

    /// <summary>
    /// The address of the storage a member-access <paramref name="target"/> is: the one a <c>ByRef</c> argument that names it is aliased to.
    /// </summary>
    /// <param name="session">The session the object lives in.</param>
    /// <param name="context">The scope of the target.</param>
    /// <param name="target">The expression the value is written through.</param>
    /// <param name="address">The address.</param>
    /// <returns><see langword="false"/> when the target is not a public variable of an object, which is the one that has an address of its own.</returns>
    public bool TryGetAddress(IRuntimeSession session, RuntimeEvaluationContext context, ExpressionNode target, out MemoryAddress address)
    {
        address = default;
        return target is MemberAccessExpressionNode memberAccess
            && TryEvaluateOwner(session, context, memberAccess, out var owner, out _)
            && owner is VBObjectValue objectOwner
            && expressions.ResolveAssignableMember(session, context, memberAccess.Owner, objectOwner, memberAccess.Member.IdentifierName, isSet: false)
                is { Field: { } field, Instance: { } instance }
            && instance.TryGetAddress(field, out address);
    }

    // the owner of a member-access target, which is an expression in its own right: `a.b.c = 1` assigns a
    // field of whatever `a.b` is, so the owner is evaluated rather than resolved.
    private bool TryEvaluateOwner(
        IRuntimeSession session, RuntimeEvaluationContext context, MemberAccessExpressionNode memberAccess,
        out VBTypedValue? owner, out RuntimeExecutionOutcome failure)
    {
        owner = null;
        failure = RuntimeExecutionOutcome.Next;

        if (memberAccess.Owner is null)
        {
            // the With-relative form, whose owner is the enclosing With block's target.
            owner = context.EnclosingWithTarget;
            return owner is not null;
        }

        var evaluated = expressions.Evaluate(session, memberAccess.Owner, context);
        if (!evaluated.IsSuccess)
        {
            failure = evaluated.IsInternalError
                ? RuntimeExecutionOutcome.InternalError
                : RuntimeExecutionOutcome.Error(evaluated.ErrorInfo!);
            return false;
        }

        owner = evaluated.Result;
        while (owner is VBVariantValue { TypedValue: { } wrapped })
        {
            owner = wrapped;
        }

        return owner is not null;
    }

    /// <inheritdoc cref="Assign(IRuntimeSession, RuntimeEvaluationContext, StatementNode, ExpressionNode, ExpressionNode, VBTypedValue)"/>
    /// <param name="session">The session whose call stack a function result variable is assigned on.</param>
    /// <param name="context">The evaluation context, whose scope decides whether the target is the enclosing
    /// procedure's own result variable.</param>
    /// <param name="statement">The statement doing the assigning.</param>
    /// <param name="symbol">The already-resolved symbol <paramref name="target"/> names.</param>
    /// <param name="target">The variable expression being assigned into.</param>
    /// <param name="source">The expression the value came from, for the location of a coercion error.</param>
    /// <param name="value">The value to assign, already evaluated.</param>
    public RuntimeExecutionOutcome Assign(
        IRuntimeSession session,
        RuntimeEvaluationContext context,
        StatementNode statement,
        Symbol symbol,
        ExpressionNode target,
        ExpressionNode source,
        VBTypedValue value)
    {

        if (symbol is VBFunctionMemberSymbol or VBPropertyGetMemberSymbol
            && symbol.Uri.AbsoluteUri == context.Scope.AbsoluteUri && session.CallStack.Current is { } enclosing)
        {
            // MS-VBAL 5.3.1: "Foo = value" inside Foo's own body Let-assigns its function result
            // variable, not the general symbol table - the read-side mirror of this check is
            // RuntimeExpressionEvaluator.EvaluateSimpleName's own self-reference check. The function
            // result variable isn't a real addressable Symbol, so this can't go through the same
            // "__let_op" operator every other target does (it needs a real IBindingHandle) - Let-coerce
            // directly instead, the same lower-level call ByVal/ByRef-fallback parameter passing already
            // makes for the identical reason.
            var returnCoercionFrame = new LetCoercionStackFrame(statement.Identity, InputIndex.CoercionSourceValue,
                value, new VBTypeDescValue(((ITypedSymbol)symbol).ResolvedType), ConversionSite.Return);
            var returnCoercionResult = coercions.EvaluateLetCoercionSemantics(session.Symbols.Resolver, source, returnCoercionFrame);
            if (!returnCoercionResult.IsApplicable)
            {
                return RuntimeExecutionOutcome.InternalError;
            }

            if (!returnCoercionResult.IsSuccess)
            {
                return RuntimeExecutionOutcome.Error(returnCoercionResult.ErrorInfo!);
            }

            ((CallStackFrame)enclosing).ReturnValue = returnCoercionResult.Result!;
            return RuntimeExecutionOutcome.Next;
        }

        // MS-VBAL §5.4.3.8: a value let-assigned to a variable of a class or Object is let-assigned to the default property of the object it holds.
        if (symbol is ITypedSymbol { ResolvedType: VBClassType or VBObjectType })
        {
            return AssignDefaultMember(session, context, statement, target, source, value);
        }

        // the reserved synthetic "__let_op" binary operator - the same shape its own test suite
        // exercises it with: a throwaway node carrying this statement's own identity/location, operands
        // passed directly rather than read back off the node's Children.
        var syntheticOperator = new VBBinaryOperatorExpressionNode(
            OperatorSymbolNames.BinaryAssignmentValueOp, statement.Identity, statement.SourceLocation, target, source);
        var result = operators.EvaluateBinaryOperator(session, syntheticOperator, new VBSymbolDescValue(symbol), value);

        return result.IsSuccess ? RuntimeExecutionOutcome.Next
            : result.IsInternalError ? RuntimeExecutionOutcome.InternalError
            : RuntimeExecutionOutcome.Error(result.ErrorInfo!);
    }

    // the object the target holds is assigned the way `target.Default = value` would be: an object of a class with no default member is error 438.
    private RuntimeExecutionOutcome AssignDefaultMember(
        IRuntimeSession session, RuntimeEvaluationContext context, StatementNode statement, ExpressionNode target, ExpressionNode source, VBTypedValue value)
    {
        var evaluated = expressions.Evaluate(session, target, context);
        if (!evaluated.IsSuccess)
        {
            return evaluated.IsInternalError ? RuntimeExecutionOutcome.InternalError : RuntimeExecutionOutcome.Error(evaluated.ErrorInfo!);
        }

        if (evaluated.Result is not VBObjectValue owner)
        {
            return RuntimeExecutionOutcome.InternalError;
        }

        if (owner.IsNothing())
        {
            return RuntimeExecutionOutcome.Error(VBRuntimeErrorInfo.For(
                VBRuntimeErrorId.ObjectVariableOrWithBlockVariableNotSet, target.Location, Exceptions.VBMemberAccess_ObjectVariableNotSet_Verbose));
        }

        if (!session.Symbols.TryGetInstance(owner.Value, out var instance)
            || VBClassType.FromClassModule(instance.ClassModule).DefaultMember is not { } defaultMember)
        {
            return RuntimeExecutionOutcome.Error(VBRuntimeErrorInfo.For(
                VBRuntimeErrorId.ObjectDoesntSupportThisPropertyOrMethod, target.Location, Exceptions.VBMemberAssignment_NotAssignable_Verbose));
        }

        var access = new MemberAccessExpressionNode(target.Identity, target.Location, target,
            new SimpleNameExpressionNode(target.Identity, target.Location, defaultMember.Name));
        return AssignObjectMember(session, context, statement, access, [], owner, source, value, isSet: false);
    }
}
