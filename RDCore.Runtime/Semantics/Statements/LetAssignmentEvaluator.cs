using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.Runtime.Semantics.Operators;
using RDCore.SDK;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.Operators;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values;
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
using RDCore.SDK.Semantics.Flags;

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
            ? session.Symbols.Resolver.ResolveValue(simpleName, ScopeKind.Local, context.Scope).Symbol
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
        // an array that is locked is not assigned another (MS-VBA, recorded in Excel): error 10, as a ReDim of it is.
        => IsALockedArray(session, context, target, value)
            ? RuntimeExecutionOutcome.Error(VBRuntimeErrorInfo.For(VBRuntimeErrorId.ThisArrayIsFixedOrTemporarilyLocked, target.Location, $"{target} is locked"))
            // `Module1.Count = 5` is a variable of the module, assigned as its bare name is (MS-VBAL §5.6.12): not a member of a value.
            : target is MemberAccessExpressionNode qualifiedAccess && expressions.ResolveNamespaceQualified(session, context, qualifiedAccess) is { } qualified
            ? Assign(session, context, statement, qualified, target, source, value)
            : target is MemberAccessExpressionNode memberAccess
            ? AssignField(session, context, statement, memberAccess, source, value)
            // `G() = 6`, `Left("ab", 1) = "x"`: a call of a function, given its arguments, assigned through what it returns.
            : IsACallOfAFunction(session, context, target)
            ? AssignThroughFunction(session, context, statement, target, source, value, isSet: false)
            : IsMemberOfObject(target) && !IsElementOfAQualifiedArray(session, context, target)
                ? AssignObjectMember(session, context, statement, target, source, value, isSet: false)
                : target is IndexExpressionNode element
                    ? AssignArrayElement(session, context, statement, element.Callee, element.Arguments, source, value, isSet: false)
                    // a variable is assigned through the slot it is bound to, by the plan of its coercion, when it can be (IR lowering S0.8c).
                    : expressions.TryAssignBound(session, target, value, context) is { } assigned
                        ? assigned.IsInternalError ? RuntimeExecutionOutcome.InternalError : RuntimeExecutionOutcome.Next
                        : TryResolveTarget(session, context, target, out var symbol)
                            ? Assign(session, context, statement, symbol!, target, source, value)
                            : RuntimeExecutionOutcome.InternalError;

    // a variable, named or named through its module, that holds an array some activation locks (ICallStackFrame.LocksArray), assigned an array. Only
    // asked when what is assigned is an array: anything else is a type mismatch, or the coercion's to say.
    private bool IsALockedArray(IRuntimeSession session, RuntimeEvaluationContext context, ExpressionNode target, VBTypedValue value)
    {
        if (Held(value) is not VBArrayValue)
        {
            return false;
        }

        var symbol = target switch
        {
            SimpleNameExpressionNode name => session.Symbols.Resolver.ResolveValue(name, ScopeKind.Local, context.Scope).Symbol,
            MemberAccessExpressionNode access => expressions.ResolveNamespaceQualified(session, context, access),
            _ => null,
        };

        return symbol is ITypedSymbol { ResolvedType: var declared }
            && session.Symbols.Resolver.TryGetAddress(symbol, out _)
            && Held(declared.CreateValue(session.Symbols.Resolver.GetValue(symbol).ForReading())) is VBArrayValue array
            && session.CallStack.Frames.Any(frame => frame.LocksArray(array));
    }

    private static VBTypedValue? Held(VBTypedValue? value)
    {
        while (value is VBVariantValue { TypedValue: var wrapped })
        {
            value = wrapped;
        }

        return value;
    }

    /// <summary>
    /// Whether <paramref name="target"/> is a call of a function, given arguments - by its name, or qualified by its module - outside the function's own body:
    /// what an assignment to it assigns through is what it returns (<see cref="AssignThroughFunction"/>).
    /// </summary>
    public bool IsACallOfAFunction(IRuntimeSession session, RuntimeEvaluationContext context, ExpressionNode target)
    {
        var callee = target is IndexExpressionNode { Callee: var named } ? named : null;
        var symbol = callee switch
        {
            SimpleNameExpressionNode name => session.Symbols.Resolver.ResolveValue(name, ScopeKind.Local, context.Scope).Symbol,
            MemberAccessExpressionNode access => expressions.ResolveNamespaceQualified(session, context, access),
            _ => null,
        };

        return symbol is VBFunctionMemberSymbol function && function.Uri.AbsoluteUri != context.Scope.AbsoluteUri;
    }

    // `owner.Property(index) = value`: an index expression whose callee is a member access.
    private static bool IsMemberOfObject(ExpressionNode target) => target is IndexExpressionNode { Callee: MemberAccessExpressionNode };

    // `Module1.Items(1) = value` is an element of the module's array, which the bare name's element is (MS-VBAL §5.6.12): not a member of a value.
    private bool IsElementOfAQualifiedArray(IRuntimeSession session, RuntimeEvaluationContext context, ExpressionNode target)
        => target is IndexExpressionNode { Callee: MemberAccessExpressionNode callee } && expressions.ResolveNamespaceQualified(session, context, callee) is not null;

    /// <summary>
    /// Assigns <paramref name="value"/> to an element of an array: <c>a(1, 2) = value</c>, or <c>owner.Items(1) = value</c> when the
    /// array is what a variable of an object holds (<strong>MS-VBAL §5.4.3.8</strong>, <strong>§5.4.3.9</strong>).
    /// </summary>
    /// <remarks>
    /// The value is Let-coerced to the element type of the array - Set-coerced for a <c>Set</c> assignment, which also lets go of the
    /// object the element held and takes a reference to the one it is given. A subscript outside the bounds of the array is
    /// error 9.
    /// <para>
    /// What is indexed can be an object instead, whose default member the arguments are given to: <c>c(1) = value</c> assigns <c>c.Item(1)</c>, as a
    /// read of it reads it (<strong>MS-VBAL §5.6.13</strong>).
    /// </para>
    /// </remarks>
    /// <param name="session">The session the array lives in.</param>
    /// <param name="context">The scope of the assignment.</param>
    /// <param name="statement">The statement doing the assigning.</param>
    /// <param name="arrayExpression">The expression the array is the value of: the callee of the index expression.</param>
    /// <param name="subscripts">The subscript expressions, one for each dimension.</param>
    /// <param name="source">The expression the value came from, for the location of an error.</param>
    /// <param name="value">The value to assign, already evaluated.</param>
    /// <param name="isSet">Whether it is a <c>Set</c> assignment.</param>
    public RuntimeExecutionOutcome AssignArrayElement(
        IRuntimeSession session, RuntimeEvaluationContext context, StatementNode statement, ExpressionNode arrayExpression,
        System.Collections.Immutable.ImmutableArray<ExpressionNode> subscripts, ExpressionNode source, VBTypedValue value, bool isSet)
    {
        var evaluated = expressions.EvaluateIndexed(session, arrayExpression, context);
        if (!evaluated.IsSuccess)
        {
            return evaluated.IsInternalError ? RuntimeExecutionOutcome.InternalError : RuntimeExecutionOutcome.Error(evaluated.ErrorInfo!);
        }

        return AssignElementOf(session, context, statement, evaluated.Result, arrayExpression, subscripts, source, value, isSet);
    }

    // the array is in hand - what the callee evaluated to, or the field of a record already evaluated - and is assigned an element in place.
    private RuntimeExecutionOutcome AssignElementOf(
        IRuntimeSession session, RuntimeEvaluationContext context, StatementNode statement, VBTypedValue? held, ExpressionNode arrayExpression,
        System.Collections.Immutable.ImmutableArray<ExpressionNode> subscripts, ExpressionNode source, VBTypedValue value, bool isSet)
    {
        while (held is VBVariantValue { TypedValue: { } wrapped })
        {
            held = wrapped;
        }

        // an object that is indexed is a call of its default member, and so is one that is assigned: `c(1) = 5` is `c.Item(1) = 5`.
        if (held is VBObjectValue indexed)
        {
            return AssignThroughDefaultMember(session, context, statement, arrayExpression, subscripts, indexed, source, value, isSet);
        }

        if (held is not VBArrayValue array)
        {
            return RuntimeExecutionOutcome.InternalError;
        }

        if (expressions.EvaluateSubscripts(session, context, subscripts, out var indices, out var indicesKnown) is { } failure)
        {
            return failure.IsInternalError ? RuntimeExecutionOutcome.InternalError : RuntimeExecutionOutcome.Error(failure.ErrorInfo!);
        }

        // a subscript outside the bounds is error 9 - unless it, or the array, is not known: the error is not known to happen, and there is
        // no element to assign. A fixed-size array's bounds are its declaration's, known whatever it holds.
        var isElement = indicesKnown && array.GetElementHandle(indices) is not null;
        if (indicesKnown && !isElement && (!array.IsIndeterminate || array is VBFixedSizeArrayValue { IsInitialized: true }))
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

            if (!isElement)
            {
                // which element is assigned is not known: each could now hold the object, or what it held.
                if (!indicesKnown)
                {
                    array.ForgetElements();
                }

                return RuntimeExecutionOutcome.Next;
            }

            StoreElement(session, array, indices, setResult.Result!);
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

        if (!isElement)
        {
            // which element is assigned is not known: each could now hold the value, or what it held - the same, if it held the value already.
            if (!indicesKnown)
            {
                array.ForgetElements(coerced.Result!.IsIndeterminate ? null : coerced.Result.RuntimeValue);
            }

            return RuntimeExecutionOutcome.Next;
        }

        StoreElement(session, array, indices, coerced.Result!);
        return RuntimeExecutionOutcome.Next;
    }

    // An element is written where it is: its cell is the storage, and the one the objects it holds are held by, which is what
    // lets them be released when the element is assigned again (ObjectReferences) - whether it is Set to an object, or Let a
    // Variant, an array or a record that holds some. Only a cell that cannot be written - the inert one an element of a class,
    // user-defined type or Object type starts with - is given a binding that can. An array or a record is stored boxed, as a
    // variable stores one.
    private static IBindingHandle StoreElement(IRuntimeSession session, VBArrayValue array, int[] indices, VBTypedValue source)
    {
        var value = source is VBArrayValue or VBUserDefinedTypeValue ? SymbolAddressTable.BoxedValue(source) : source.RuntimeValue;
        var cell = array.GetElementHandle(indices)!;
        var before = ObjectReferences.HeldIn(session, cell);
        if (cell.BindingCapabilities.HasFlag(BindingCapabilities.SetValue))
        {
            cell.Store(session.Symbols.Resolver, value, !source.IsIndeterminate);
            ObjectReferences.Rebind(session, cell, before);
            return cell;
        }

        array.TrySetElement(BindingKnowledge.NewCell(value, !source.IsIndeterminate), indices);
        var stored = array.GetElementHandle(indices)!;
        ObjectReferences.Rebind(session, stored, before);
        return stored;
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

        return owner switch
        {
            VBObjectValue objectOwner => AssignObjectMember(session, context, statement, memberAccess, indexArguments, objectOwner, source, value, isSet),
            VBUserDefinedTypeValue record when isSet && indexArguments.IsEmpty => SetField(session, memberAccess, record, source, value),
            // `r.Values(1) = value`: an element of the array a field of the record holds, which is written where it is.
            VBUserDefinedTypeValue record when !indexArguments.IsEmpty && record[memberAccess.Member.IdentifierName] is { } field
                => AssignElementOf(session, context, statement, field, memberAccess, indexArguments, source, value, isSet),
            _ => RuntimeExecutionOutcome.InternalError,
        };
    }

    // `c(1) = value`, `Set c(1) = value`: the arguments are given to the default member of the object what is indexed is the value of (MS-VBAL §5.6.13), and
    // the assignment is the default member's, as `c.Item(1) = value` is. An object that is Nothing is error 91, one of a class with no default member 438. A
    // late-bound object's default member is whatever answers to DISPID_VALUE.
    private RuntimeExecutionOutcome AssignThroughDefaultMember(
        IRuntimeSession session, RuntimeEvaluationContext context, StatementNode statement, ExpressionNode indexed,
        System.Collections.Immutable.ImmutableArray<ExpressionNode> arguments, VBObjectValue owner, ExpressionNode source, VBTypedValue value, bool isSet)
    {
        if (owner.IsNothing())
        {
            return RuntimeExecutionOutcome.Error(VBRuntimeErrorInfo.For(
                VBRuntimeErrorId.ObjectVariableOrWithBlockVariableNotSet, indexed.Location, Exceptions.VBMemberAccess_ObjectVariableNotSet_Verbose));
        }

        var name = !session.Symbols.TryGetInstance(owner.Value, out var instance) ? null
            : Execution.External.Automation.LateBinding.IsLateBound(instance.ClassModule) ? Execution.External.Automation.LateBinding.DefaultMemberName
            : VBClassType.FromClassModule(instance.ClassModule).DefaultMember?.Name;
        if (name is null)
        {
            return RuntimeExecutionOutcome.Error(VBRuntimeErrorInfo.For(
                VBRuntimeErrorId.ObjectDoesntSupportThisPropertyOrMethod, indexed.Location, Exceptions.VBMemberAssignment_NotAssignable_Verbose));
        }

        return AssignMemberOf(session, context, statement, DefaultMemberAccess(indexed, name), arguments, owner, source, value, isSet, throughDefaultMember: true);
    }

    // MS-VBAL §5.4.3.9 with a <member-access-expression> target whose owner is a UDT: the field is Set-coerced to its declared type and holds the
    // object. Like Let (AssignField) the field lives on the value, and not in storage of its own, so the object the field held is let go of
    // through the field's cell.
    private RuntimeExecutionOutcome SetField(
        IRuntimeSession session, MemberAccessExpressionNode memberAccess, VBUserDefinedTypeValue record, ExpressionNode source, VBTypedValue value)
    {
        var name = memberAccess.Member.IdentifierName;
        if (record.Fields.FirstOrDefault(field => field.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is not { ResolvedType: { } fieldType } declared
            || record.GetFieldHandle(declared.Name) is not { } handle
            || expressions.SetCoercion is not { } setCoercion)
        {
            return RuntimeExecutionOutcome.InternalError;
        }

        var setResult = setCoercion.EvaluateSetCoercion(session, source, value, fieldType);
        if (!setResult.IsSuccess)
        {
            return RuntimeExecutionOutcome.Error(setResult.ErrorInfo!);
        }

        var before = ObjectReferences.HeldIn(session, handle);
        handle.Store(session.Symbols.Resolver, setResult.Result!);
        ObjectReferences.Rebind(session, handle, before);
        return RuntimeExecutionOutcome.Next;
    }

    private RuntimeExecutionOutcome AssignObjectMember(
        IRuntimeSession session, RuntimeEvaluationContext context, StatementNode statement, MemberAccessExpressionNode memberAccess,
        System.Collections.Immutable.ImmutableArray<ExpressionNode> indexArguments, VBObjectValue owner, ExpressionNode source,
        VBTypedValue value, bool isSet)
    {
        var assumedBefore = expressions.AssumedSoFar;
        var outcome = AssignMemberOf(session, context, statement, memberAccess, indexArguments, owner, source, value, isSet);
        return ObservedAccess(session, context, memberAccess, owner, assumedBefore, outcome);
    }

    // the member of an object or of a record that an assignment writes is a member the code accesses, and is stated as one (MemberAccessFact).
    private RuntimeExecutionOutcome ObservedAccess(
        IRuntimeSession session, RuntimeEvaluationContext context, MemberAccessExpressionNode memberAccess, VBTypedValue owner, long assumedBefore,
        RuntimeExecutionOutcome outcome)
    {
        if (expressions.Observation is not null)
        {
            // what the access raises is that there is no object, or no such member on it: the error of the conversion of the value, or of the Property Let that
            // is invoked, is not the access's, and is a fact of its own.
            var result = outcome switch
            {
                { Kind: RuntimeExecutionOutcomeKind.Error, ErrorInfo: { ErrorId: (int)VBRuntimeErrorId.ObjectVariableOrWithBlockVariableNotSet
                    or (int)VBRuntimeErrorId.ObjectDoesntSupportThisPropertyOrMethod } error } => RuntimeSemanticsEvaluationResult.Error(error),
                { Kind: RuntimeExecutionOutcomeKind.InternalError } => RuntimeSemanticsEvaluationResult.InternalError(),
                _ => RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void),
            };

            expressions.ObserveMemberAccess(session, context, memberAccess, Tokens.MemberAccess, memberAccess.Owner, owner, memberAccess.Member.IdentifierName,
                MemberAccessOperationSemanticFlags.Explicit, assumedBefore, result);
        }

        return outcome;
    }

    private RuntimeExecutionOutcome AssignMemberOf(
        IRuntimeSession session, RuntimeEvaluationContext context, StatementNode statement, MemberAccessExpressionNode memberAccess,
        System.Collections.Immutable.ImmutableArray<ExpressionNode> indexArguments, VBObjectValue owner, ExpressionNode source,
        VBTypedValue value, bool isSet, bool throughDefaultMember = false)
    {
        // an object variable that holds no object has no member to assign: error 91.
        if (owner.IsNothing())
        {
            return RuntimeExecutionOutcome.Error(VBRuntimeErrorInfo.For(
                VBRuntimeErrorId.ObjectVariableOrWithBlockVariableNotSet, memberAccess.Location, Exceptions.VBMemberAccess_ObjectVariableNotSet_Verbose));
        }

        if (expressions.ResolveAssignableMember(session, context, memberAccess, owner, isSet) is not { } member)
        {
            if (DispatchedClassOf(session, context, memberAccess, owner) is { } dispatched)
            {
                return AssignThroughDispatch(session, context, statement, memberAccess, indexArguments, owner, dispatched, source, value, isSet, throughDefaultMember);
            }

            // a property the object can read and not assign - a Property Get, and no Property Let or Set - is error 451 for a Let of one that returns no
            // object, and 450 for a Set; no member of the name at all is 438 (MS-VBA, Excel-verified).
            var name = memberAccess.Member.IdentifierName;
            return RuntimeExecutionOutcome.Error(
                isSet && HasPropertyGet(session, owner, name, returningAValueOnly: false)
                    ? VBRuntimeErrorInfo.For(VBRuntimeErrorId.WrongNumberOfArgumentsOrInvalidPropertyAssignment, memberAccess.Location,
                        string.Format(Exceptions.VBMemberAssignment_ReadOnlyProperty_Verbose, name))
                : !isSet && HasPropertyGet(session, owner, name, returningAValueOnly: true)
                    ? VBRuntimeErrorInfo.For(VBRuntimeErrorId.PropertyLetNotDefinedPropertyGetDidNotReturnObject, memberAccess.Location,
                        string.Format(Exceptions.VBMemberAssignment_ReadOnlyProperty_Verbose, name))
                : VBRuntimeErrorInfo.For(VBRuntimeErrorId.ObjectDoesntSupportThisPropertyOrMethod, memberAccess.Location, Exceptions.VBMemberAssignment_NotAssignable_Verbose));
        }

        if (member.Accessor is { } accessor)
        {
            var invocation = expressions.InvokeAssignment(session, context, accessor, member.Receiver!, indexArguments, value, source, isSet,
                memberAccess.Owner ?? context.EnclosingWithTargetExpression);
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
            return AssignArrayElement(session, context, statement, memberAccess, indexArguments, source, value, isSet);
        }

        var field = member.Field;
        var instance = member.Instance;

        var handle = instance.GetValue(field);
        if (!isSet)
        {
            if (!TryLetCoerce(session, statement, source, value, field.ResolvedType, out var coerced, out var coercionFailure))
            {
                return coercionFailure;
            }

            // an array, a record and a Variant are held as the value that identifies them by where it is, which is what every allocation boxes them
            // in: the value's own runtime value is not one for them, and a whole array assigned to a variable of an object would have none.
            var heldBefore = ObjectReferences.HeldIn(session, handle);
            handle.Store(session.Symbols.Resolver, SymbolAddressTable.BoxedValue(coerced), !coerced.IsIndeterminate);
            ObjectReferences.Rebind(session, handle, heldBefore);
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

        var before = ObjectReferences.HeldIn(session, handle);
        handle.Store(session.Symbols.Resolver, setResult.Result!);
        ObjectReferences.Rebind(session, handle, before);
        if (withEvents)
        {
            EventAttachments.Attach(session, owner.Value, field, setResult.Result as VBObjectValue);
        }

        return RuntimeExecutionOutcome.Next;
    }

    // the class of an object of the standard library that an assignment reaches late-bound - through an expression declared Object or Variant - which MS-VBA
    // assigns through the object's IDispatch (AssignThroughDispatch); null for an object of the workspace's, or one reached through its class.
    private static VBClassModuleSymbol? DispatchedClassOf(
        IRuntimeSession session, RuntimeEvaluationContext context, MemberAccessExpressionNode memberAccess, VBObjectValue owner)
        => session.Symbols.TryGetInstance(owner.Value, out var instance)
            && instance.ClassModule.GetProperty(SymbolProperties.Library) == SDK.Runtime.StdLib.StdLibSymbolProvider.LibraryName
            && RuntimeExpressionEvaluator.DeclaredTypeOfOwner(session, context, memberAccess.Owner ?? context.EnclosingWithTargetExpression) is VBObjectType or VBVariantType
                ? instance.ClassModule
                : null;

    /// <summary>
    /// Assigns a member of an object of the standard library that has no <c>Property Let</c> (no <c>Property Set</c>, for a <c>Set</c>), reached late-bound,
    /// as MS-VBA's call of the object's <c>IDispatch</c> does.
    /// </summary>
    /// <remarks>
    /// Written with arguments - <c>c.Item(1) = 5</c>, <c>r.Test("a") = True</c> - the member is invoked with them and the default member of the object it
    /// returns is assigned, as <strong>MS-VBAL §5.4.3.8</strong> has it for a property or function of an object: what is no object is error 451, and an error
    /// the call raises is the assignment's (<c>e.Raise(5) = 1</c> is error 5). With none, the property's object, when it returns one, has its default
    /// member assigned, and any other property is error 450; a method, and a member that takes arguments it is not given, are error 438. A <c>Set</c> is
    /// error 450 for a property, 438 for a method, and 438 for a property with no argument that returns an object. Recorded in Excel (oracle s26: grid-lb,
    /// grid-lb3, grid-lb5, grid-lb6) for <c>Collection</c>, <c>ErrObject</c>, <c>RegExp</c>, <c>MatchCollection</c>, <c>Match</c> and <c>SubMatches</c>, as
    /// for Scripting's <c>Dictionary</c>.
    /// <para>
    /// The default member of an object that a <c>Variant</c> holds, given arguments - <c>v(1) = 5</c> - is not invoked to have what it returns assigned: a
    /// property is error 450 once its arguments are converted (a <c>Null</c> one is 13), and a method, or a member given arguments it does not take, 438.
    /// </para>
    /// </remarks>
    private RuntimeExecutionOutcome AssignThroughDispatch(
        IRuntimeSession session, RuntimeEvaluationContext context, StatementNode statement, MemberAccessExpressionNode memberAccess,
        System.Collections.Immutable.ImmutableArray<ExpressionNode> indexArguments, VBObjectValue owner, VBClassModuleSymbol classModule,
        ExpressionNode source, VBTypedValue value, bool isSet, bool throughDefaultMember)
    {
        var name = memberAccess.Member.IdentifierName;
        var read = classModule.DefaultInterfaceMembers.FirstOrDefault(candidate =>
            candidate is VBPropertyGetMemberSymbol or VBFunctionMemberSymbol or VBProcedureMemberSymbol and not (VBPropertyLetMemberSymbol or VBPropertySetMemberSymbol)
            && string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));

        var isProperty = read is VBPropertyGetMemberSymbol;
        var returnsAnObject = read is VBPropertyGetMemberSymbol { ResolvedType: VBClassType or VBObjectType };
        var parameters = read is null ? [] : RuntimeProcedureInvoker.GetParameters(read).Where(parameter => parameter.Name != Tokens.Me).ToArray();
        var required = parameters.Count(parameter => parameter is not ParamArrayParameterSymbol && !parameter.IsOptional);
        var reachedThrough = memberAccess.Owner ?? context.EnclosingWithTargetExpression;
        var tooMany = !parameters.OfType<ParamArrayParameterSymbol>().Any() && indexArguments.Length > parameters.Length;

        // a property that takes no argument and returns an object, given some, is read with none, and what it returns is what they index, as a read of it
        // is: `m(0).SubMatches(0) = "x"` assigns the default member of the SubMatches.
        if (returnsAnObject && parameters.Length == 0 && !indexArguments.IsEmpty)
        {
            var indexed = expressions.InvokeOn(session, context, owner, read!, [], reachedThrough);
            if (!indexed.IsSuccess)
            {
                return indexed.IsInternalError ? RuntimeExecutionOutcome.InternalError : RuntimeExecutionOutcome.Error(indexed.ErrorInfo!);
            }

            if (Held(indexed.Result) is VBObjectValue indexedObject)
            {
                return AssignThroughDefaultMember(session, context, statement, memberAccess, indexArguments, indexedObject, source, value, isSet);
            }
        }

        // what a variable declared Variant holds is assigned its default member as the variable is, and not as a late-bound call of the member is.
        var throughAVariant = throughDefaultMember && !indexArguments.IsEmpty && reachedThrough is not null
            && expressions.DenotesAVariable(session, context, reachedThrough)
            && RuntimeExpressionEvaluator.DeclaredTypeOfOwner(session, context, reachedThrough) is VBVariantType;

        if (read is null || indexArguments.Length < required || !isProperty && (isSet || indexArguments.IsEmpty || throughAVariant)
            || isSet && indexArguments.IsEmpty && returnsAnObject || throughAVariant && tooMany)
        {
            return RuntimeExecutionOutcome.Error(VBRuntimeErrorInfo.For(
                VBRuntimeErrorId.ObjectDoesntSupportThisPropertyOrMethod, memberAccess.Location, Exceptions.VBMemberAssignment_NotAssignable_Verbose));
        }

        // the property is read for its arguments to be converted, and is not assigned what it returns.
        if (throughAVariant && !isSet
            && expressions.InvokeOn(session, context, owner, read!, indexArguments, reachedThrough) is { IsSuccess: false } converted)
        {
            return converted.IsInternalError ? RuntimeExecutionOutcome.InternalError : RuntimeExecutionOutcome.Error(converted.ErrorInfo!);
        }

        if (isSet || throughAVariant || indexArguments.IsEmpty && !returnsAnObject)
        {
            return RuntimeExecutionOutcome.Error(VBRuntimeErrorInfo.For(VBRuntimeErrorId.WrongNumberOfArgumentsOrInvalidPropertyAssignment, memberAccess.Location,
                string.Format(Exceptions.VBMemberAssignment_ReadOnlyProperty_Verbose, name)));
        }

        var returned = expressions.InvokeOn(session, context, owner, read!, indexArguments, reachedThrough);
        if (!returned.IsSuccess)
        {
            return returned.IsInternalError ? RuntimeExecutionOutcome.InternalError : RuntimeExecutionOutcome.Error(returned.ErrorInfo!);
        }

        var held = Held(returned.Result);
        return held is VBObjectValue result
            ? AssignDefaultMemberOf(session, context, statement, memberAccess, result, source, value, observed: false)
            : RuntimeExecutionOutcome.Error(indexArguments.IsEmpty
                ? VBRuntimeErrorInfo.For(VBRuntimeErrorId.WrongNumberOfArgumentsOrInvalidPropertyAssignment, memberAccess.Location,
                    string.Format(Exceptions.VBMemberAssignment_ReadOnlyProperty_Verbose, name))
                : VBRuntimeErrorInfo.For(VBRuntimeErrorId.PropertyLetNotDefinedPropertyGetDidNotReturnObject, memberAccess.Location,
                    string.Format(Exceptions.VBMemberAssignment_ReadOnlyProperty_Verbose, name)));
    }

    // whether the class of the object has a Property Get of the name - for a Let, one declared as a type that holds no object: one that does is the default
    // member of the object it returns that MS-VBAL §5.4.3.8 assigns, which is not this.
    private static bool HasPropertyGet(IRuntimeSession session, VBObjectValue owner, string name, bool returningAValueOnly)
        => session.Symbols.TryGetInstance(owner.Value, out var instance)
            && instance.ClassModule.DefaultInterfaceMembers.Any(member => member is VBPropertyGetMemberSymbol { ResolvedType: var type }
                && (!returningAValueOnly || type is not (VBClassType or VBObjectType or VBVariantType))
                && string.Equals(member.Name, name, StringComparison.OrdinalIgnoreCase));

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

        // the field of a record is there to be written, whatever is written to it.
        ObservedAccess(session, context, memberAccess, owner, expressions.AssumedSoFar, RuntimeExecutionOutcome.Next);

        // "the source is Let-coerced to the target's declared type" - a field's declared type is its own, and
        // the coercion is the same one an assignment to a variable of that type would apply.
        if (!TryLetCoerce(session, statement, source, value, fieldType, out var coerced, out var coercionFailure))
        {
            return coercionFailure;
        }

        var index = udt.Fields.IndexOf(declared);
        var before = ObjectReferences.HeldInField(udt, index);
        if (!udt.TrySetFieldAt(index, coerced))
        {
            return RuntimeExecutionOutcome.InternalError;
        }

        // the field holds what it was given in a cell of its own now, which is what holds the objects in it.
        ObjectReferences.RebindField(session, udt, index, before);
        return RuntimeExecutionOutcome.Next;
    }

    // a value let-coerced to the declared type of a field it is assigned to: by the plan of the coercion when nothing observes it (IR lowering S0.11), as
    // a variable's is (RuntimeExpressionEvaluator.TryAssignBound), or by its strategy - which is also how a coercion that fails raises its error.
    private bool TryLetCoerce(
        IRuntimeSession session, StatementNode statement, ExpressionNode source, VBTypedValue value, SDK.Model.Types.Abstract.VBType destination,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out VBTypedValue? coerced, out RuntimeExecutionOutcome failure)
    {
        failure = RuntimeExecutionOutcome.Next;
        if (expressions.TryConvertUnobserved(value, destination, out coerced))
        {
            return true;
        }

        var frame = new LetCoercionStackFrame(statement.Identity, InputIndex.CoercionSourceValue, value, new VBTypeDescValue(destination), ConversionSite.Assignment);
        var result = coercions.EvaluateLetCoercionSemantics(session.Symbols.Resolver, source, frame);
        coerced = result.Result;
        failure = result.IsSuccess ? (coerced is null ? RuntimeExecutionOutcome.InternalError : RuntimeExecutionOutcome.Next)
            : result.IsApplicable ? RuntimeExecutionOutcome.Error(result.ErrorInfo!)
            : RuntimeExecutionOutcome.InternalError;
        return result.IsSuccess && coerced is not null;
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
            VBObjectValue objectOwner when expressions.ResolveAssignableMember(session, context, memberAccess, objectOwner, isSet: false)
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
            && expressions.ResolveAssignableMember(session, context, memberAccess, objectOwner, isSet: false)
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
            // makes for the identical reason - by its plan, when it can be (IR lowering S0.9).
            // the function result variable holds what it is assigned as a variable does (ObjectReferences.AssignResult).
            var resultFrame = (CallStackFrame)enclosing;
            if (expressions.TryConvertUnobserved(value, ((ITypedSymbol)symbol).ResolvedType, out var planned))
            {
                resultFrame.ReturnValue = ObjectReferences.AssignResult(session, resultFrame.ResultCell, planned);
                return RuntimeExecutionOutcome.Next;
            }

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

            resultFrame.ReturnValue = ObjectReferences.AssignResult(session, resultFrame.ResultCell, returnCoercionResult.Result!);
            return RuntimeExecutionOutcome.Next;
        }

        // MS-VBAL §5.4.3.8: a function named outside its own body is called, and what it returns is assigned through (AssignThroughFunction).
        if (symbol is VBFunctionMemberSymbol)
        {
            return AssignThroughFunction(session, context, statement, target, source, value, isSet: false);
        }

        // MS-VBAL §5.4.3.8: a property is assigned by invoking its Property Let with the value - a property of a module, the workspace's or the library's
        // (Calendar, Time), as much as one of an object. It has no storage of its own for the value to be written to.
        if (symbol is VBPropertyGetMemberSymbol or VBPropertyLetMemberSymbol or VBPropertySetMemberSymbol)
        {
            return AssignProperty(session, context, statement, (VBTypeMemberSymbol)symbol, target, source, value);
        }

        // MS-VBAL §5.3.1.11: a Variant reference parameter bound to a variable of another type is assigned "as having the declared type of the argument's
        // referenced variable".
        var referenced = session.CallStack.Current is { } frame && frame.TryGetReferencedType(symbol, out var declared) ? declared : null;

        // MS-VBAL §5.4.3.8: a value let-assigned to a variable of a class or Object is let-assigned to the default property of the object it holds.
        if ((referenced ?? (symbol as ITypedSymbol)?.ResolvedType) is VBClassType or VBObjectType)
        {
            return AssignDefaultMember(session, context, statement, target, source, value);
        }

        // the reserved synthetic "__let_op" binary operator - the same shape its own test suite
        // exercises it with: a throwaway node carrying this statement's own identity/location, operands
        // passed directly rather than read back off the node's Children.
        var syntheticOperator = new VBBinaryOperatorExpressionNode(
            OperatorSymbolNames.BinaryAssignmentValueOp, statement.Identity, statement.SourceLocation, target, source);

        // a Variant, a record or an array can hold objects, and what the variable held is let go of when it is assigned something else: `V = 5`
        // destroys the object V was Set to, if it was the last thing to hold it (MS-VBAL §5.3.1.10).
        var holder = ObjectReferences.CanHoldAnObject(referenced ?? (symbol as ITypedSymbol)?.ResolvedType) ? session.Symbols.Resolver.GetValue(symbol) : null;
        var before = holder is null ? ObjectReferences.Held.None : ObjectReferences.HeldIn(session, holder);

        var result = operators.EvaluateBinaryOperator(session, syntheticOperator, new VBSymbolDescValue(symbol, referenced), value);
        if (holder is not null && result.IsSuccess)
        {
            ObjectReferences.Rebind(session, holder, before);
        }

        return result.IsSuccess ? RuntimeExecutionOutcome.Next
            : result.IsInternalError ? RuntimeExecutionOutcome.InternalError
            : RuntimeExecutionOutcome.Error(result.ErrorInfo!);
    }

    // the Property Let of the name, of the module that declares the property: invoked on the object whose code this is when the module is a class, and on
    // nothing when it is a standard module. A property with no Let whose Get returns an object assigns the default member of that object (MS-VBAL §5.4.3.8),
    // and one whose Get does not is error 451.
    private RuntimeExecutionOutcome AssignProperty(
        IRuntimeSession session, RuntimeEvaluationContext context, StatementNode statement, VBTypeMemberSymbol property,
        ExpressionNode target, ExpressionNode source, VBTypedValue value)
    {
        var accessor = property as VBPropertyLetMemberSymbol
            ?? session.Symbols.MembersOf(property.ParentUri).OfType<VBPropertyLetMemberSymbol>()
                .FirstOrDefault(candidate => string.Equals(candidate.Name, property.Name, StringComparison.OrdinalIgnoreCase));

        if (accessor is null)
        {
            // a property of a module is called as a function is, and what it returns that is no object is error 424 (MS-VBA, oracle\s28 W2, B01: `Now = 5`); a
            // class's own, invoked on the object, is error 451.
            if (property is VBPropertyGetMemberSymbol && RuntimeProcedureInvoker.GetParameters(property) is not [{ Name: Tokens.Me }, ..])
            {
                return AssignThroughFunction(session, context, statement, target, source, value, isSet: false);
            }

            return property is VBPropertyGetMemberSymbol { ResolvedType: VBClassType or VBObjectType or VBVariantType }
                ? AssignDefaultMember(session, context, statement, target, source, value)
                : RuntimeExecutionOutcome.Error(VBRuntimeErrorInfo.For(
                    VBRuntimeErrorId.PropertyLetNotDefinedPropertyGetDidNotReturnObject, target.Location, $"'{property.Name}' has no Property Let."));
        }

        IRuntimeValue? receiver = null;
        if (RuntimeProcedureInvoker.GetParameters(accessor) is [{ Name: Tokens.Me }, ..])
        {
            var me = expressions.Evaluate(session, new InstanceExpressionNode(target.Identity, target.Location), context);
            if (!me.IsSuccess)
            {
                return me.IsInternalError ? RuntimeExecutionOutcome.InternalError : RuntimeExecutionOutcome.Error(me.ErrorInfo!);
            }

            receiver = me.Result!.RuntimeValue;
        }

        var invocation = expressions.InvokeAssignment(session, context, accessor, receiver, [], value, source, isSet: false);
        return invocation.IsSuccess ? RuntimeExecutionOutcome.Next
            : invocation.IsInternalError ? RuntimeExecutionOutcome.InternalError
            : RuntimeExecutionOutcome.Error(invocation.ErrorInfo!);
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

        // a Variant reference parameter bound to an object variable reads as a Variant that holds the object.
        var held = evaluated.Result;
        while (held is VBVariantValue { TypedValue: { } wrapped })
        {
            held = wrapped;
        }

        // a variable of a class always holds an object, or Nothing: what does not is the value of a Property Get that has no Let to be assigned with.
        if (held is not VBObjectValue owner)
        {
            return RuntimeExecutionOutcome.Error(VBRuntimeErrorInfo.For(
                VBRuntimeErrorId.PropertyLetNotDefinedPropertyGetDidNotReturnObject, target.Location, "The property has no Property Let, and its Property Get returned no object."));
        }

        return AssignDefaultMemberOf(session, context, statement, target, owner, source, value, observed: true);
    }

    // the object is assigned the way `reachedThrough.Default = value` would be, where reachedThrough is the expression the object is the value of: one that is
    // Nothing is error 91, and an object of a class with no default member is error 438. An object no expression of the code's is the value of - what a member
    // returned - is no access of the code's, and is not stated as one (observed).
    private RuntimeExecutionOutcome AssignDefaultMemberOf(
        IRuntimeSession session, RuntimeEvaluationContext context, StatementNode statement, ExpressionNode reachedThrough, VBObjectValue owner,
        ExpressionNode source, VBTypedValue value, bool observed, bool isSet = false)
    {
        if (owner.IsNothing())
        {
            return RuntimeExecutionOutcome.Error(VBRuntimeErrorInfo.For(
                VBRuntimeErrorId.ObjectVariableOrWithBlockVariableNotSet, reachedThrough.Location, Exceptions.VBMemberAccess_ObjectVariableNotSet_Verbose));
        }

        if (!session.Symbols.TryGetInstance(owner.Value, out var instance)
            || VBClassType.FromClassModule(instance.ClassModule).DefaultMember is not { } defaultMember)
        {
            return RuntimeExecutionOutcome.Error(VBRuntimeErrorInfo.For(
                VBRuntimeErrorId.ObjectDoesntSupportThisPropertyOrMethod, reachedThrough.Location, Exceptions.VBMemberAssignment_NotAssignable_Verbose));
        }

        var access = DefaultMemberAccess(reachedThrough, defaultMember.Name);
        return observed
            ? AssignObjectMember(session, context, statement, access, [], owner, source, value, isSet)
            : AssignMemberOf(session, context, statement, access, [], owner, source, value, isSet);
    }

    /// <summary>
    /// Assigns <paramref name="value"/> through what a function returns, called with no argument: the target of an assignment that names a function outside
    /// its own body (<strong>MS-VBAL §5.4.3.8</strong>, <strong>§5.4.3.9</strong>).
    /// </summary>
    /// <remarks>
    /// What the function returns is assigned through its default member, as an object variable assigned a value is (<c>Err = 5</c> assigns <c>Err.Number</c>).
    /// What returns no object has nothing to assign through: error <c>424</c>, as MS-VBA has it (Excel-verified, oracle\s28: <c>Command = "x"</c>, a
    /// <c>Variant</c> function of the workspace assigned or Set from another procedure). What cannot be called so is a compile error the static pass reports.
    /// </remarks>
    /// <param name="session">The session the function is called in.</param>
    /// <param name="context">The scope of the assignment.</param>
    /// <param name="statement">The assignment.</param>
    /// <param name="target">The target, which names the function.</param>
    /// <param name="source">The expression the value came from, for the location of a coercion error.</param>
    /// <param name="value">The value, already evaluated - and Set-coerced, for a <c>Set</c>.</param>
    /// <param name="isSet">Whether it is a <c>Set</c> assignment.</param>
    public RuntimeExecutionOutcome AssignThroughFunction(
        IRuntimeSession session, RuntimeEvaluationContext context, StatementNode statement, ExpressionNode target, ExpressionNode source, VBTypedValue value, bool isSet)
    {
        var called = expressions.Evaluate(session, target, context);
        if (!called.IsSuccess)
        {
            return called.IsInternalError ? RuntimeExecutionOutcome.InternalError : RuntimeExecutionOutcome.Error(called.ErrorInfo!);
        }

        var returned = called.Result;
        while (returned is VBVariantValue { TypedValue: { } wrapped })
        {
            returned = wrapped;
        }

        return returned is VBObjectValue owner
            ? AssignDefaultMemberOf(session, context, statement, target, owner, source, value, observed: false, isSet)
            : RuntimeExecutionOutcome.Error(VBRuntimeErrorInfo.For(VBRuntimeErrorId.ObjectRequired, target.Location,
                "The function returned no object to assign through."));
    }

    // the access of the default member of the object an expression is the value of, which an assignment to it writes through: one for each expression and
    // name of the member, because the member each access was last assigned through is remembered by the access (ResolveAssignableMember).
    private MemberAccessExpressionNode DefaultMemberAccess(ExpressionNode reachedThrough, string name)
        => _defaultMemberAccesses.TryGetValue(reachedThrough, out var access) && string.Equals(access.Member.IdentifierName, name, StringComparison.OrdinalIgnoreCase)
            ? access
            : _defaultMemberAccesses[reachedThrough] = new MemberAccessExpressionNode(reachedThrough.Identity, reachedThrough.Location, reachedThrough,
                new SimpleNameExpressionNode(reachedThrough.Identity, reachedThrough.Location, name));

    private readonly System.Collections.Concurrent.ConcurrentDictionary<ExpressionNode, MemberAccessExpressionNode> _defaultMemberAccesses =
        new(ReferenceEqualityComparer.Instance);
}
