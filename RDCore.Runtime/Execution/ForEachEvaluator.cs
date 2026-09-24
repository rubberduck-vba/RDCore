using RDCore.Runtime.Semantics;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.Runtime.Semantics.Operators;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.Operators;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Runtime.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Services.VerboseMessages;

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
public sealed class ForEachEvaluator(RuntimeExpressionEvaluator expressionEvaluator, ILetCoercionRuntimeSemanticsProvider letCoercionProvider, ISetCoercionRuntimeSemantics setCoercion, IVerboseMessageBuilder formatterService)
{
    private readonly BinaryLetAssignmentOperatorRuntimeSemantics _letAssignment = new(letCoercionProvider, formatterService);

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
        return _letAssignment.Evaluate(session, new(), syntheticOperator, new VBSymbolDescValue((Symbol)control), element);
    }
}
