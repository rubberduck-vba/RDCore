using RDCore.Runtime.Semantics;
using RDCore.Runtime.Semantics.Operators;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.Operators;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
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
    /// Assigns <paramref name="element"/> to <paramref name="control"/> — Set-assigns when
    /// <paramref name="useSetAssignment"/> (the array's own item type is <see cref="VBObjectType"/>),
    /// Let-assigns otherwise.
    /// </summary>
    public RuntimeSemanticsEvaluationResult AssignControl(IRuntimeSession session, ITypedSymbol control, ExpressionNode locationNode, bool useSetAssignment, VBTypedValue element)
    {
        if (useSetAssignment)
        {
            var coercionResult = setCoercion.EvaluateSetCoercion(session, locationNode, element, control.ResolvedType);
            if (!coercionResult.IsSuccess)
            {
                return RuntimeSemanticsEvaluationResult.Error(coercionResult.ErrorInfo!);
            }

            var handle = session.Symbols.Resolver.GetValue((Symbol)control);
            handle.SetValue(session.Symbols.Resolver, coercionResult.Result!.RuntimeValue);
            return RuntimeSemanticsEvaluationResult.Success(coercionResult.Result!);
        }

        var syntheticOperator = new VBBinaryOperatorExpressionNode(OperatorSymbolNames.BinaryAssignmentValueOp, locationNode.Identity, locationNode.Location, locationNode, locationNode);
        return operators.EvaluateBinaryOperator(session, syntheticOperator, new VBSymbolDescValue((Symbol)control), element);
    }

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
