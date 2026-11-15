using RDCore.Runtime.Semantics;
using RDCore.Runtime.Semantics.Operators;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.Operators;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Runtime.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Runtime.Execution;

/// <summary>
/// Evaluates a <c>For Each</c> loop's collection expression and assigns each element to the loop's
/// control variable — <strong>MS-VBAL §5.4.2.4</strong>.
/// </summary>
/// <remarks>
/// The control variable is Set-assigned when the collection's own declared element type is an object
/// (an array of <c>Object</c>), Let-assigned otherwise — real Let/Set-coercion, through the same real
/// machinery every other assignment in the interpreter uses, never a hand-rolled write. The
/// location-bearing node passed to either is always the control variable's own expression, never a
/// synthetic stand-in.
/// </remarks>
public sealed class ForEachEvaluator(RuntimeExpressionEvaluator expressionEvaluator, IOperatorRuntimeSemanticsProvider operators, ISetCoercionRuntimeSemantics setCoercion)
{
    /// <summary>
    /// Evaluates the loop's collection expression — once, ahead of any element assignment.
    /// </summary>
    public RuntimeSemanticsEvaluationResult EvaluateCollection(IRuntimeSession session, ExpressionNode expression, RuntimeEvaluationContext context)
        => expressionEvaluator.Evaluate(session, expression, context);

    /// <summary>
    /// How many times the evaluation has assumed something, so far: taken before the enumeration begins, for <see cref="ObserveEnumeration"/>.
    /// </summary>
    public long AssumedSoFar => expressionEvaluator.AssumedSoFar;

    /// <summary>
    /// States the enumeration of an object as the access of its enumeration member, when the evaluation is observed.
    /// </summary>
    /// <param name="session">The session the statement was evaluated in.</param>
    /// <param name="context">The scope of the statement.</param>
    /// <param name="expression">The collection expression.</param>
    /// <param name="collection">What it evaluated to.</param>
    /// <param name="assumedBefore">What <see cref="AssumedSoFar"/> was before the enumeration began.</param>
    /// <param name="outcome">How the enumeration began: its error, if it is the code's, is the fact's.</param>
    public void ObserveEnumeration(
        IRuntimeSession session, RuntimeEvaluationContext context, ExpressionNode expression, VBTypedValue collection, long assumedBefore, RuntimeExecutionOutcome? outcome)
        => expressionEvaluator.ObserveEnumeration(session, context, expression, collection, assumedBefore,
            outcome is { Kind: RuntimeExecutionOutcomeKind.Error, ErrorInfo: { } error } ? error : null);

    /// <summary>
    /// Whether an element of an array is Set-assigned to the control variable of a <c>For Each</c>, rather than Let-assigned.
    /// </summary>
    /// <remarks>
    /// <strong>MS-VBAL §5.4.2.4</strong> Set-assigns when "the declared type of the array is Object", and Let-assigns otherwise - which would ask an
    /// object an array of <c>Variant</c> holds for its default member, and raise 438 for one that has none. MS-VBA Set-assigns an element that is an
    /// object whatever the array is declared as, and so does this: the specification's wording is an oversight. An
    /// array of a class is an array of objects as much as one of <c>Object</c> is.
    /// </remarks>
    /// <param name="itemType">The declared type of the elements.</param>
    /// <param name="element">The element about to be assigned.</param>
    public static bool IsSetAssigned(VBType itemType, VBTypedValue element)
    {
        if (itemType is VBObjectType or VBClassType)
        {
            return true;
        }

        var held = element;
        while (held is VBVariantValue { IsIndeterminate: false, TypedValue: var wrapped })
        {
            held = wrapped;
        }

        return held is VBObjectValue;
    }

    /// <summary>
    /// Assigns <paramref name="element"/> to <paramref name="control"/> — Set-assigns when
    /// <paramref name="useSetAssignment"/> (<see cref="IsSetAssigned"/>), Let-assigns otherwise.
    /// </summary>
    public RuntimeSemanticsEvaluationResult AssignControl(IRuntimeSession session, ITypedSymbol control, ExpressionNode locationNode, bool useSetAssignment, VBTypedValue element)
    {
        // the control variable holds each element in turn, as any variable holds what it is assigned: it lets go of the one before (ObjectReferences).
        var handle = session.Symbols.Resolver.GetValue((Symbol)control);
        var before = ObjectReferences.CanHoldAnObject(control.ResolvedType) ? ObjectReferences.HeldIn(session, handle) : ObjectReferences.Held.None;

        RuntimeSemanticsEvaluationResult result;
        if (useSetAssignment)
        {
            var coercionResult = setCoercion.EvaluateSetCoercion(session, locationNode, element, control.ResolvedType);
            if (!coercionResult.IsSuccess)
            {
                return RuntimeSemanticsEvaluationResult.Error(coercionResult.ErrorInfo!);
            }

            handle.Store(session.Symbols.Resolver, coercionResult.Result!);
            result = RuntimeSemanticsEvaluationResult.Success(coercionResult.Result!);
        }
        else
        {
            var syntheticOperator = new VBBinaryOperatorExpressionNode(OperatorSymbolNames.BinaryAssignmentValueOp, locationNode.Identity, locationNode.Location, locationNode, locationNode);
            result = operators.EvaluateBinaryOperator(session, syntheticOperator, new VBSymbolDescValue((Symbol)control), element);
        }

        if (result.IsSuccess && ObjectReferences.CanHoldAnObject(control.ResolvedType))
        {
            ObjectReferences.Rebind(session, handle, before);
        }

        return result;
    }

    /// <summary>
    /// Empties the control variable of a loop that has no element left to assign: a <c>Variant</c> is <c>Empty</c>, an object variable <c>Nothing</c>.
    /// </summary>
    /// <remarks>
    /// <strong>MS-VBAL §5.4.2.4</strong> says the control variable holds the last element once the loop has finished. MS-VBA empties it instead: when the
    /// collection runs out, when it has no element at all, and when a <c>Next</c> finds no loop running, unless <see cref="KeepsControlWithNoLoopRunning"/>
    /// (<strong>RD-VBAL §5.4.2.4</strong>). A loop left by <c>Exit For</c>, a jump or an error keeps the element it was at.
    /// </remarks>
    /// <param name="session">The session the loop runs in.</param>
    /// <param name="control">The control variable.</param>
    /// <param name="locationNode">The control variable's own expression, which the assignment is located at.</param>
    public RuntimeSemanticsEvaluationResult EmptyControl(IRuntimeSession session, ITypedSymbol control, ExpressionNode locationNode)
        => control.ResolvedType is VBObjectType or VBClassType
            ? AssignControl(session, control, locationNode, useSetAssignment: true, VBObjectValue.Nothing)
            : AssignControl(session, control, locationNode, useSetAssignment: false, VBEmptyValue.Empty);

    /// <summary>
    /// Whether a <c>Next</c> that finds no loop running (error 92) leaves the control variable of its loop as it is: MS-VBA does when the control variable
    /// is an object variable, or when the collection expression is declared as an array, as the static pass types it. Otherwise it empties the control
    /// variable before it raises the error (<strong>RD-VBAL §5.4.2.4</strong>).
    /// </summary>
    /// <param name="session">The session the loop runs in.</param>
    /// <param name="control">The control variable.</param>
    /// <param name="collection">The collection expression.</param>
    /// <param name="context">The scope the loop is written in.</param>
    public bool KeepsControlWithNoLoopRunning(IRuntimeSession session, ITypedSymbol control, ExpressionNode collection, RuntimeEvaluationContext context)
        => control.ResolvedType is VBObjectType or VBClassType || expressionEvaluator.DeclaredTypeOf(session, collection, context) is VBArrayType;

    /// <summary>
    /// Asks <paramref name="collection"/>, an object, for the enumerator its members are enumerated with: invokes its enumeration member (<c>_NewEnum</c>).
    /// </summary>
    /// <remarks>
    /// MS-VBAL §5.4.2.4 leaves the enumeration of an object "implementation-defined". The member returns an enumerator object, which is asked for each member in turn
    /// (<see cref="MoveNext"/>, <see cref="Current"/>); a <c>Variant</c> that holds the object is the object.
    /// </remarks>
    public RuntimeSemanticsEvaluationResult Enumerate(IRuntimeSession session, RuntimeEvaluationContext context, VBObjectValue collection)
        => expressionEvaluator.InvokeEnumerationMember(session, context, collection);

    /// <summary>
    /// Moves <paramref name="enumerator"/> to its next member.
    /// </summary>
    /// <returns>A Boolean: <c>True</c> when there is a member to be read, <c>False</c> when they have run out; or the error that the enumerator raised.</returns>
    public RuntimeSemanticsEvaluationResult MoveNext(IRuntimeSession session, RuntimeEvaluationContext context, VBObjectValue enumerator)
        => expressionEvaluator.InvokeMember(session, context, enumerator, "MoveNext");

    /// <summary>
    /// Reads the member <paramref name="enumerator"/> is at.
    /// </summary>
    public RuntimeSemanticsEvaluationResult Current(IRuntimeSession session, RuntimeEvaluationContext context, VBObjectValue enumerator)
        => expressionEvaluator.InvokeMember(session, context, enumerator, "Current");

    /// <summary>
    /// Assigns a member an enumerator produced to <paramref name="control"/>: Set-assigned when it is an object, Let-assigned otherwise.
    /// </summary>
    /// <remarks>
    /// "The &lt;bound-variable-expression&gt; is either Let-assigned or Set-assigned to the first element in &lt;collection&gt; in an implementation-defined
    /// manner": what a member is is what decides, there being no declared element type to say - an object is the member itself, and anything else is its value.
    /// </remarks>
    public RuntimeSemanticsEvaluationResult AssignEnumerated(IRuntimeSession session, ITypedSymbol control, ExpressionNode locationNode, VBTypedValue member)
    {
        var held = member;
        while (held is VBVariantValue { TypedValue: { } wrapped })
        {
            held = wrapped;
        }

        return held is VBObjectValue
            ? AssignControl(session, control, locationNode, useSetAssignment: true, held)
            : AssignControl(session, control, locationNode, useSetAssignment: false, member);
    }
}
