using RDCore.SDK;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Analysis;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Context.Abstract;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Services.VerboseMessages;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace RDCore.Runtime.Semantics.LetCoercion;

/// <summary>
/// <strong>MS-VBAL 5.5.1.2</strong> Let-coercion (runtime semantics) - Provides the let-coercion runtime semantics needed to evaluate <em>operator expressions</em> requiring let-coercion semantics.
/// </summary>
public interface ILetCoercionRuntimeSemanticsProvider
{
    /// <summary>
    /// Evaluates the let-coerced <see cref="VBTypedValue"/> for the specified <c>sourceValue</c> to the specified <c>destinationDeclaredType</c> in the context of the specified <c>expression</c>.
    /// </summary>
    /// <param name="resolver">A symbol lookup service.</param>
    /// <param name="expression">The <c>ExpressionNode</c> that is being evaluated.</param>
    /// <param name="frame">The current stack frame of the coercion operation.</param>
    /// <returns>A <see cref="LetCoercionResult"/> that encapsulates the outcome of the evaluation.</returns>
    LetCoercionResult EvaluateLetCoercionSemantics(
        ISymbolResolver resolver,
        ExpressionNode expression,
        LetCoercionStackFrame frame);

    /// <summary>
    /// Builds the semantic context of a let-coercion operation.
    /// </summary>
    /// <typeparam name="TContext"></typeparam>
    /// <typeparam name="TFlags"></typeparam>
    /// <param name="resolver">A symbol lookup service.</param>
    /// <param name="expression">The <c>ExpressionNode</c> that is being evaluated.</param>
    /// <remarks>
    /// 🧩 <em>Analyzers</em> (<c>RDCore.Diagnostics</c> and other <em>plug-ins</em>) may perform a more opiniated analysis of the semantic context.
    /// </remarks>
    /// <returns>A <c>LetCoercionAnalysisContext</c> for the context of this let-coercion operation.</returns>
    LetCoercionAnalysisContext Analyze(
        ISymbolResolver resolver,
        ILetCoercionSemanticContextBuilder builder,
        ExpressionNode expression,
        LetCoercionStackFrame frame);
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

    /// <summary>
    /// Resolves the strategy for a destination <see cref="VBType"/> by walking its base-type chain
    /// (a strategy keyed on <c>VBNumericType</c> handles <c>VBIntegerType</c>, <c>VBLongType</c>, …).
    /// </summary>
    private bool TryGetStrategy(VBType destinationType, [MaybeNullWhen(false)] out ILetCoercionRuntimeSemantics strategy)
    {
        for (var type = destinationType.GetType(); type is not null; type = type.BaseType)
        {
            if (_strategies.TryGetValue(type, out var found))
            {
                strategy = found;
                return true;
            }
        }
        strategy = null;
        return false;
    }

    private readonly LetCoercionStackManager _stack = new();

    private VBRuntimeErrorInfo OnLetCoercionTypeMismatch(ExpressionNode expression, LetCoercionStackFrame frame) =>
        VBRuntimeErrorInfo.For(VBRuntimeErrorId.TypeMismatch, expression.Location,
            _formatterService.Format(Exceptions.LetCoercionRuntimeErrorExceptionTypeMismatch_Verbose, expression, [frame]));

    private VBRuntimeErrorInfo OnLetCoercionNotApplicableInternalError(ExpressionNode expression, LetCoercionStackFrame frame) =>
        VBRuntimeErrorInfo.For(VBRuntimeErrorId.InternalError, expression.Location,
            _formatterService.Format(Exceptions.VBRuntimeInternalError_LetCoercionStrategyWasNotApplicable, expression, [frame]));

    private VBRuntimeErrorInfo OnLetCoercionStackCorruptionInternalError(ExpressionNode expression, LetCoercionStackFrame frame) =>
        VBRuntimeErrorInfo.For(VBRuntimeErrorId.InternalError, expression.Location,
            _formatterService.Format(Exceptions.VBRuntimeInternalError_LetCoercionStackCorruption
                .Replace("{$DEPTH}", _stack.Depth.ToString()), expression, [frame]));

    private VBRuntimeErrorInfo OnRecursiveLetCoercionError(ExpressionNode expression, LetCoercionStackFrame frame) =>
        VBRuntimeErrorInfo.For(VBRuntimeErrorId.OutOfStackSpace, expression.Location,
            _formatterService.Format(Exceptions.LetCoercionRuntimeErrorExceptionOutOfStackSpace_Verbose, expression, [frame]));

    public LetCoercionAnalysisContext Analyze(
        ISymbolResolver resolver, 
        ILetCoercionSemanticContextBuilder builder, 
        ExpressionNode expression, 
        LetCoercionStackFrame frame)
    {
        var coercionResult = LetCoercionResult.NotApplicable(frame);
        var context = new LetCoercionAnalysisContext(expression.Identity, coercionResult, 0);

        var operandIndex = frame.OperandIndex;

        if (TryGetStrategy(frame.DestinationTypeDesc.Target, out var strategy))
        {
            // 1. evaluate the strategy that should be applicable for the destination declared type:
            coercionResult = strategy.EvaluateLetCoercion(resolver, expression, frame);

            // 2. add any error to the semantic context so they become unmistakable error diagnostics in analyzers:
            builder.AddOnError(coercionResult.ErrorInfo);

            // 3. add flags about the basic facts of the coercion operation:
            AnalyzeConversionOperation(builder, expression, frame);

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

    private static void AnalyzeConversionOperation(
        ILetCoercionSemanticContextBuilder builder,
        ExpressionNode expression,
        LetCoercionStackFrame frame)
    {
        // the caller's own frame is used directly rather than reading it back off the strategy's
        // LetCoercionResult: a strategy's Success()/Error() call is not required to attach one (most
        // don't, for the ordinary case), and LetCoercionResult.Frame throws on an empty Frames array.
        //
        // whether the coercion is Implicit or Explicit is not known here: it is the operation that asks for it that says.
        builder.AddLetCoercionFlags(ConversionSemanticFlags.LetCoerced | SourceOperandFlagOf(frame.SourceValue), frame.OperandIndex);
        builder.AddLetCoercionFlags(OperandPositionFlags.Of(expression, frame.OperandIndex), frame.OperandIndex);
    }

    // what the source is, whichever destination type it is coerced to: a strategy is chosen by the destination, so a
    // Null, Empty, Error or object source coerced to a Long is never seen by the strategy of its own type.
    private static ConversionSemanticFlags SourceOperandFlagOf(VBTypedValue source) => source switch
    {
        VBNullValue => ConversionSemanticFlags.NullOperand,
        VBEmptyValue => ConversionSemanticFlags.EmptyOperand,
        VBErrorValue => ConversionSemanticFlags.ErrorOperand,
        VBObjectValue => ConversionSemanticFlags.ObjectOperand, // Nothing included
        VBArrayValue { ItemType: VBByteType } => ConversionSemanticFlags.ByteArrayOperand,
        _ => 0
    };

    public LetCoercionResult EvaluateLetCoercionSemantics(
        ISymbolResolver resolver,
        ExpressionNode expression,
        LetCoercionStackFrame frame)
    {
        // a Variant source's own TypeInfo already mirrors its wrapped value's (so dispatch above still
        // picks the right destination strategy), but every strategy casts frame.SourceValue directly to
        // its own concrete value type - unwrap here, once, so that cast sees the real wrapped value
        // instead of the VBVariantValue box around it. VBVariantValue's own ctor allows wrapping another
        // Variant, so this unwraps all the way down rather than assuming a single level of nesting.
        while (frame.SourceValue is VBVariantValue { TypedValue: var wrapped })
        {
            frame = frame with { SourceValue = wrapped };
        }

        // MS-VBAL §5.5.1.2.13's "Any class -> Any type" and "Nothing -> Any type" rules don't key off
        // the destination at all - like the Variant unwrap above, destination-based dispatch alone
        // would only ever reach VBObjectLetCoercionRuntimeSemantics when the destination itself is an
        // object type, never for the (far more common) object-to-Long/String/etc. case.
        var strategyFound = frame.SourceValue is VBObjectValue
            ? TryGetStrategy(VBObjectType.TypeInfo, out var strategy)
            : TryGetStrategy(frame.DestinationTypeDesc.Target, out strategy);

        if (!strategyFound)
        {
            // in-and-out: no need to push the coercion frame for this
            return LetCoercionResult.Error(OnLetCoercionTypeMismatch(expression, frame), [.. _stack.Frames]);
        }

        if (_stack.TryPush(frame))
        {
            var result = strategy.EvaluateLetCoercion(resolver, expression, frame);
            if (!_stack.TryPop(out _) && result.ErrorInfo?.ErrorId != (int)VBRuntimeErrorId.OutOfStackSpace)
            {
                // a nested call detecting recursion clears the whole (shared) stack, including this
                // frame — that's an expected consequence of the recursion guard, not corruption, and
                // is already reported as its own OutOfStackSpace result; propagate it below instead of
                // reporting a second, misleading error here. Any other empty-pop is genuinely unexpected.
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
            _stack.Clear();
            return LetCoercionResult.Error(OnRecursiveLetCoercionError(expression, frame));
        }
    }
}
