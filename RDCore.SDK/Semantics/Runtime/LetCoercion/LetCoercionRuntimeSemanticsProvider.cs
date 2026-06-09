using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract;
using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Semantics.Runtime.Operators;
using RDCore.SDK.Services.VerboseMessages;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace RDCore.SDK.Semantics.Runtime.LetCoercion;

/// <summary>
/// <strong>MS-VBAL 5.5.1.2</strong> Let-coercion (runtime semantics) - Provides the let-coercion runtime semantics needed to evaluate <em>operator expressions</em> requiring let-coercion semantics.
/// </summary>
public interface ILetCoercionRuntimeSemanticsProvider
{
    /// <summary>
    /// Evaluates the let-coerced <see cref="VBTypedValue"/> for the specified <c>sourceValue</c> to the specified <c>destinationDeclaredType</c> in the context of the specified <c>expression</c>.
    /// </summary>
    /// <param name="resolver">A symbol lookup service.</param>
    /// <param name="expression">The <c>BoundExpression</c> that is being evaluated.</param>
    /// <param name="frame">The current stack frame of the coercion operation.</param>
    /// <returns>A <see cref="LetCoercionResult"/> that encapsulates the outcome of the evaluation.</returns>
    LetCoercionResult EvaluateLetCoercionSemantics<TContext, TFlags>(
        ISymbolResolver resolver, 
        BoundNode<TContext, TFlags> expression, 
        LetCoercionStackFrame frame)
    where TContext : SemanticContext<TFlags>, new()
    where TFlags : struct, Enum;

    /// <summary>
    /// Builds the semantic context of a let-coercion operation.
    /// </summary>
    /// <typeparam name="TContext"></typeparam>
    /// <typeparam name="TFlags"></typeparam>
    /// <param name="resolver">A symbol lookup service.</param>
    /// <param name="expression">The <c>BoundExpression</c> that is being evaluated.</param>
    /// <remarks>
    /// 🧩 <em>Analyzers</em> (<c>RDCore.Diagnostics</c> and other <em>plug-ins</em>) may perform a more opiniated analysis of the semantic context.
    /// </remarks>
    /// <returns>A <c>LetCoercionAnalysisContext</c> for the context of this let-coercion operation.</returns>
    LetCoercionAnalysisContext Analyze<TContext, TFlags>(
        ISymbolResolver resolver, 
        ILetCoercionSemanticContextBuilder builder, 
        VBOperatorExpression<TContext, TFlags> expression, 
        LetCoercionStackFrame frame)
    where TContext : SemanticContext<TFlags>, new()
    where TFlags : struct, Enum;
}

public abstract class StackManager<TFrame> where TFrame : IEquatable<TFrame>
{
    private readonly HashSet<TFrame> _frameHash = [];
    private readonly Stack<TFrame> _frameStack = [];

    protected virtual void Clear()
    {
        _frameHash.Clear();
        _frameStack.Clear();
    }

    protected virtual bool TryPushFrame(TFrame frame)
    {
        if (_frameHash.Contains(frame))
        {
            return false;
        }

        _frameHash.Add(frame);
        _frameStack.Push(frame);
        Debug.Assert(_frameHash.Count == _frameStack.Count);

        return true;
    }

    protected virtual bool TryPopFrame(out TFrame frame)
    {
        frame = default!;
        if (_frameHash.Count == 0)
        {
            return false;
        }

        if (_frameStack.TryPop(out TFrame stackFrame))
        {
            Debug.Assert(_frameHash.Contains(stackFrame));
            _frameHash.Remove(stackFrame);

            frame = stackFrame;
            Debug.Assert(_frameHash.Count == _frameStack.Count);

            return true;
        }

        return false;
    }
}

/// <summary>
/// <strong>MS-VBAL 5.5.1.2</strong> Let-coercion (runtime semantics) - Provides the let-coercion runtime semantics needed to evaluate <em>operator expressions</em> requiring let-coercion semantics.
/// </summary>
/// <remarks>
/// 👉 Each instance only instantiates the let-coercion strategies it needs for its context.
/// </remarks>
public class LetCoercionRuntimeSemanticsProvider(
    IEnumerable<ILetCoercionRuntimeSemantics> semantics, 
    IVerboseMessageBuilder formatterService) 
    : ILetCoercionRuntimeSemanticsProvider
{
    private readonly IVerboseMessageBuilder _formatterService = formatterService;

    private readonly Dictionary<Type, ILetCoercionRuntimeSemantics> _strategies = 
        semantics.ToDictionary(strategy => strategy.LetCoercionSpecification, strategy => strategy);

    private readonly HashSet<LetCoercionStackFrame> _frameHash = [];
    private readonly Stack<LetCoercionStackFrame> _frameStack = [];

    private void ClearCoercionStack()
    {
        _frameStack.Clear();
        _frameHash.Clear();
    }

    private bool TryPushCoercionFrame(LetCoercionStackFrame frame)
    {
        if (_frameHash.Contains(frame))
        {
            return false;
        }

        _frameHash.Add(frame);
        _frameStack.Push(frame);
        Debug.Assert(_frameHash.Count == _frameStack.Count);

        return true;
    }

    private bool TryPopCoercionFrame([MaybeNullWhen(false)][NotNullWhen(true)] out LetCoercionStackFrame? frame)
    {
        frame = null;
        if (_frameHash.Count == 0)
        {
            return false;
        }

        if (_frameStack.TryPop(out LetCoercionStackFrame stackFrame))
        {
            Debug.Assert(_frameHash.Contains(stackFrame));
            _frameHash.Remove(stackFrame);

            frame = stackFrame;
            Debug.Assert(_frameHash.Count == _frameStack.Count);

            return true;
        }

        return false;
    }


    private VBRuntimeErrorInfo OnLetCoercionTypeMismatch(BoundExpressionNode expression, LetCoercionStackFrame frame) =>
        new(VBRuntimeErrorId.TypeMismatch, expression.Location,
            VBRuntimeErrorException.GetErrorString(VBRuntimeErrorId.TypeMismatch),
            _formatterService.Format(Exceptions.LetCoercionRuntimeErrorExceptionTypeMismatch_Verbose, expression, [frame]));

    private VBRuntimeErrorInfo OnLetCoercionNotApplicableInternalError(BoundExpressionNode expression, LetCoercionStackFrame frame) =>
        new(VBRuntimeErrorId.InternalError, expression.Location,
            VBRuntimeErrorException.GetErrorString(VBRuntimeErrorId.InternalError),
            _formatterService.Format(Exceptions.VBRuntimeInternalError_LetCoercionStrategyWasNotApplicable, expression, [frame]));

    private VBRuntimeErrorInfo OnLetCoercionStackCorruptionInternalError(BoundExpressionNode expression, LetCoercionStackFrame frame) =>
        new(VBRuntimeErrorId.InternalError, expression.Location,
            VBRuntimeErrorException.GetErrorString(VBRuntimeErrorId.InternalError),
            _formatterService.Format(Exceptions.VBRuntimeInternalError_LetCoercionStackCorruption
                .Replace("${FRAMES}", _frameStack.Count.ToString())
                .Replace("${HASH}", _frameHash.Count.ToString()), expression, [frame]));

    private VBRuntimeErrorInfo OnRecursiveLetCoercionError(BoundExpressionNode expression, LetCoercionStackFrame frame) =>
        new(VBRuntimeErrorId.OutOfStackSpace, expression.Location,
            VBRuntimeErrorException.GetErrorString(VBRuntimeErrorId.OutOfStackSpace),
            _formatterService.Format(Exceptions.LetCoercionRuntimeErrorExceptionOutOfStackSpace_Verbose, expression, [frame]));

    public LetCoercionAnalysisContext Analyze<TContext, TFlags>(
        ISymbolResolver resolver, 
        ILetCoercionSemanticContextBuilder builder, 
        VBOperatorExpression<TContext, TFlags> expression, 
        LetCoercionStackFrame frame)
    where TContext : SemanticContext<TFlags>, new()
    where TFlags : struct, Enum
    {
        var coercionResult = LetCoercionResult.NotApplicable(frame);
        var context = new LetCoercionAnalysisContext(expression.SemanticId, coercionResult, 0);

        var operandIndex = frame.InputIndex;

        if (_strategies.TryGetValue(frame.DestinationTypeDesc.GetType(), out var coercionStrategy) && coercionStrategy is ILetCoercionRuntimeSemantics strategy)
        {
            // 1. evaluate the strategy that should be applicable for the destination declared type:
            coercionResult = strategy.EvaluateLetCoercion(resolver, expression, frame);

            // 2. add any error to the semantic context so they become unmistakable error diagnostics in analyzers:
            builder.AddOnError(coercionResult.ErrorInfo);

            // 3. add flags about the basic facts of the coercion operation:
            AnalyzeConversionOperation(builder, expression, coercionResult);

            // 4. let the applicable **sealed** strategy implementation have a say:
            context = context.Merge(strategy.Analyze(builder, resolver, expression, frame, coercionResult));
        }
        else
        {
            // if no strategy is found/applicable, there was a type mismatch error.
            builder.AddLetCoercionFlags(ConversionSemanticFlags.Failed, operandIndex);
        }

        return context;
    }

    private static void AnalyzeConversionOperation<TContext, TFlags>(
        ILetCoercionSemanticContextBuilder builder, 
        VBOperatorExpression<TContext, TFlags> expression, 
        LetCoercionResult result)
    where TContext : SemanticContext<TFlags>, new()
    where TFlags : struct, Enum
    {
        builder.AddLetCoercionFlags(ConversionSemanticFlags.Implicit | ConversionSemanticFlags.LetCoerced, result.Frame.InputIndex);
        EncodeApplicableOperandFlag(builder, expression, result.Frame);
    }

    private static void EncodeApplicableOperandFlag<TContext, TFlags>(
        ILetCoercionSemanticContextBuilder builder, 
        VBOperatorExpression<TContext, TFlags> expression, 
        LetCoercionStackFrame frame)
    where TContext : SemanticContext<TFlags>, new()
    where TFlags : struct, Enum
    {
        builder.AddLetCoercionFlags(expression switch
        {
            VBUnaryOperatorExpression<TContext, TFlags> when frame.InputIndex == InputIndex.UnaryOperand 
                => ConversionSemanticFlags.UnaryOperand,

            VBBinaryOperatorExpression<TContext, TFlags> when frame.InputIndex == InputIndex.BinaryLeftOperand
                => ConversionSemanticFlags.BinaryLeftOperand,

            VBBinaryOperatorExpression<TContext, TFlags> when frame.InputIndex == InputIndex.BinaryRightOperand
                => ConversionSemanticFlags.BinaryRightOperand,

            _ => 0
        }, frame.InputIndex);
    }

    public LetCoercionResult EvaluateLetCoercionSemantics<TContext, TFlags>(
        ISymbolResolver resolver, 
        VBOperatorExpression<TContext, TFlags> expression, 
        LetCoercionStackFrame frame)
    where TContext : SemanticContext<TFlags>, new()
    where TFlags : struct, Enum
    {
        if (!_strategies.TryGetValue(frame.DestinationTypeDesc.GetType(), out var strategy))
        {
            // in-and-out: no need to push the coercion frame for this
            return LetCoercionResult.Error(OnLetCoercionTypeMismatch(expression, frame), [.. _frameStack]);
        }

        var builder = new LetCoercionSemanticContextFlagsBuilder();

        if (TryPushCoercionFrame(frame))
        {
            var result = strategy.EvaluateLetCoercion(resolver, expression, frame);
            if (!TryPopCoercionFrame(out _))
            {
                Debug.Fail("💥Coercion stack pop failed; internal invariant violation.");
                return LetCoercionResult.Error(OnLetCoercionStackCorruptionInternalError(expression, frame));
            }

            return result.IsApplicable ? result
                // this is unexpected. surfacing it as an internal error puts it in the trace logs.
                : LetCoercionResult.Error(OnLetCoercionNotApplicableInternalError(expression, frame));
        }
        else
        {
            // this let-coercion operation is provably recursive, no need to dig any deeper.
            ClearCoercionStack();
            return LetCoercionResult.Error(OnRecursiveLetCoercionError(expression, frame));
        }
    }
}
