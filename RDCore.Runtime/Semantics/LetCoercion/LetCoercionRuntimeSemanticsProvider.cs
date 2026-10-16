using RDCore.SDK;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Runtime.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Analysis;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Facts;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Services.VerboseMessages;
using RDCore.SDK.Semantics.Plans;
using System.Collections.Concurrent;
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
/// <param name="semantics">The let-coercion strategies, keyed by the destination type each is specified for.</param>
/// <param name="formatterService">Builds the verbose half of an error message.</param>
/// <param name="observation">Told of every coercion this provider evaluates, as a <see cref="ConversionFact"/>; <see langword="null"/> when nothing is
/// analyzing the code, which is how code runs.</param>
public class LetCoercionRuntimeSemanticsProvider(
    IEnumerable<ILetCoercionRuntimeSemantics> semantics,
    IVerboseMessageBuilder formatterService,
    AnalysisObservation? observation = null)
    : ILetCoercionRuntimeSemanticsProvider
{
    private readonly IVerboseMessageBuilder _formatterService = formatterService;

    private readonly Dictionary<Type, ILetCoercionRuntimeSemantics> _strategies =
        semantics.ToDictionary(strategy => strategy.LetCoercionSpecification, strategy => strategy);

    // the strategy found for each kind of type, by walking its base-type chain once: a provider lets values be coerced to and from few kinds of type.
    private readonly ConcurrentDictionary<Type, ILetCoercionRuntimeSemantics?> _resolved = new();

    /// <summary>
    /// Resolves the strategy for a destination <see cref="VBType"/> by walking its base-type chain
    /// (a strategy keyed on <c>VBNumericType</c> handles <c>VBIntegerType</c>, <c>VBLongType</c>, …).
    /// </summary>
    private bool TryGetStrategy(VBType destinationType, [MaybeNullWhen(false)] out ILetCoercionRuntimeSemantics strategy)
        => TryGetStrategy(destinationType.GetType(), out strategy);

    /// <summary>
    /// The strategy for the rule of a <see cref="ConversionPlan"/>: the strategy of the source's type when the rule is the source's, and of the
    /// destination's otherwise - found by walking the type's base-type chain, once for each kind of type.
    /// </summary>
    private ILetCoercionRuntimeSemantics? StrategyFor(ConversionPlan plan)
        => _resolved.GetOrAdd((plan.IsBySource ? plan.Source : plan.Destination).GetType(),
            static (type, provider) => provider.TryGetStrategy(type, out var strategy) ? strategy : null, this);

    private bool TryGetStrategy(Type kind, [MaybeNullWhen(false)] out ILetCoercionRuntimeSemantics strategy)
    {
        for (var type = kind; type is not null; type = type.BaseType)
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
        => Analyze(resolver, builder, expression, frame, evaluated: null);

    // the result of the coercion is the evaluation's own when it has been evaluated already, which is what a coercion that is
    // observed has: describing it must not evaluate it twice.
    private LetCoercionAnalysisContext Analyze(
        ISymbolResolver resolver,
        ILetCoercionSemanticContextBuilder builder,
        ExpressionNode expression,
        LetCoercionStackFrame frame,
        LetCoercionResult? evaluated)
    {
        // MS-VBAL §5.2.3.4: what is let-coerced to an enumeration is let-coerced to a Long, and is analyzed as the coercion it
        // is evaluated as (Coerce): no strategy is keyed on an enumeration, and the facts are those of the Long's.
        if (frame.DestinationTypeDesc.Target is VBEnumType)
        {
            frame = frame with { DestinationTypeDesc = new VBTypeDescValue(VBLongType.TypeInfo) };
        }

        var coercionResult = LetCoercionResult.NotApplicable(frame);
        var context = new LetCoercionAnalysisContext(expression.Identity, coercionResult, 0);

        var operandIndex = frame.OperandIndex;

        if (TryGetStrategy(frame.DestinationTypeDesc.Target, out var strategy))
        {
            // 1. evaluate the coercion the way it is evaluated when the code runs:
            coercionResult = evaluated ?? EvaluateUnobserved(resolver, expression, frame);

            // 2. add any error to the semantic context so they become unmistakable error diagnostics in analyzers:
            builder.AddOnError(coercionResult.ErrorInfo?.AsErrorInfo);

            // 3. add flags about the basic facts of the coercion operation:
            AnalyzeConversionOperation(builder, expression, frame, coercionResult);

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
        LetCoercionStackFrame frame,
        LetCoercionResult coercionResult)
    {
        // the caller's own frame is used directly rather than reading it back off the strategy's
        // LetCoercionResult: a strategy's Success()/Error() call is not required to attach one (most
        // don't, for the ordinary case), and LetCoercionResult.Frame throws on an empty Frames array.
        //
        // whether the coercion is Implicit or Explicit is not known here: it is the operation that asks for it that says.
        builder.AddLetCoercionFlags(ConversionSemanticFlags.LetCoerced | SourceOperandFlagOf(frame.SourceValue), frame.OperandIndex);
        builder.AddLetCoercionFlags(OperandPositionFlags.Of(expression, frame.OperandIndex), frame.OperandIndex);

        // MS-VBAL 5.5.1.2.10 keys off the source: a Null the destination cannot hold is the operand whose coercion fails.
        // The Null strategy that raises the error says so in its own analysis, but the strategy asked to analyze a coercion
        // is the destination's, which says nothing about a Null. A Variant that holds the Null is the same source: the
        // coercion unwraps it before it picks a strategy, and so does this.
        if (Unwrapped(frame.SourceValue) is VBNullValue && coercionResult.ErrorInfo is not null)
        {
            builder.AddLetCoercionFlags(ConversionSemanticFlags.Failed, frame.OperandIndex);
        }
    }

    // an array's own TypeInfo is the generic VBArrayType whatever its elements are (VBArrayValue tags every array with it): the type of a Byte() is told by
    // its elements, which is all its plan needs to know of them.
    private static VBType ValueTypeOf(VBTypedValue source)
        => source is VBArrayValue { ItemType: VBByteType } ? VBResizableByteArrayType.TypeInfo : source.TypeInfo;

    // the value a Variant holds, however deeply it is wrapped: what the coercion itself is about (see Coerce).
    private static VBTypedValue Unwrapped(VBTypedValue value)
    {
        while (value is VBVariantValue { TypedValue: { } wrapped })
        {
            value = wrapped;
        }

        return value;
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
        var result = EvaluateUnobserved(resolver, expression, frame);
        if (observation is { IsSuspended: false })
        {
            Observe(observation, resolver, expression, frame, result);
        }

        return result;
    }

    // States the coercion that has just been evaluated as a fact: the types and the site are the code's, the flags are the
    // analysis of the coercion, and the error is the evaluation's own.
    private void Observe(
        AnalysisObservation target,
        ISymbolResolver resolver,
        ExpressionNode expression,
        LetCoercionStackFrame frame,
        LetCoercionResult result)
    {
        var builder = new LetCoercionSemanticContextFlagsBuilder();

        // the coercions a strategy evaluates to describe this one are not the code's.
        using (target.Suspend())
        {
            Analyze(resolver, builder, expression, frame, result);
        }

        // every conversion the runtime evaluates is one the code did not spell out: the semantics of an operation say
        // so (an operand of an operator), and a statement that coerces a value does not say otherwise.
        var flags = builder.Build().Flags;
        if ((flags & (ConversionSemanticFlags.Implicit | ConversionSemanticFlags.Explicit)) == 0)
        {
            flags |= ConversionSemanticFlags.Implicit;
        }

        // a Variant whose subtype is not known is a Variant, and nothing more: the Empty it assumes is no fact.
        target.OnConversion(new ConversionFact(
            expression.Identity,
            expression.Location,
            frame.Site,
            frame.OperandIndex,
            frame.SourceValue is VBVariantValue { IsSubtypeKnown: false } ? VBVariantType.TypeInfo : frame.SourceValue.TypeInfo,
            frame.DestinationTypeDesc.Target,
            flags,
            IsValueKnown: !frame.SourceValue.IsIndeterminate,
            StatedErrors.Of(result.ErrorInfo)));
    }

    private LetCoercionResult EvaluateUnobserved(
        ISymbolResolver resolver,
        ExpressionNode expression,
        LetCoercionStackFrame frame)
    {
        var result = Coerce(resolver, expression, frame);
        if (!frame.SourceValue.IsIndeterminate || result.ErrorInfo?.ErrorId == (int)VBRuntimeErrorId.InternalError)
        {
            return result;
        }

        // a type mismatch the type of the source decides is raised whatever value it holds: it is known to happen, and is kept.
        if (result.ErrorInfo is VBRuntimeErrorInfo { ErrorId: (int)VBRuntimeErrorId.TypeMismatch } mismatch && IsDecidedByType(frame))
        {
            return result with { ErrorInfo = mismatch with { IsDecidedByTypes = true } };
        }

        // an indeterminate source coerces to an indeterminate value; what it raises for the value it assumes is not known to happen.
        return result with
        {
            IsApplicable = true,
            Result = (result.IsSuccess ? result.Result! : frame.DestinationTypeDesc.Target.DefaultValue).AsIndeterminate(),
            ErrorInfo = null,
        };
    }

    // whether the type of a source that is not known is enough to say the coercion is a type mismatch: its plan is one (MS-VBAL 5.5.1.2) - but for a Variant
    // whose value is not known, whose type is not either, and an object, which is let-coerced by what its default member returns.
    private static bool IsDecidedByType(LetCoercionStackFrame frame)
        => frame.SourceValue is not (VBVariantValue or VBObjectValue)
            && ConversionPlanner.ForValue(ValueTypeOf(frame.SourceValue), frame.DestinationTypeDesc.Target).IsTypeMismatch;

    private LetCoercionResult Coerce(
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

        // MS-VBAL §2.2: a LongPtr source is the value of what it is an alias of.
        if (frame.SourceValue is VBLongPtrValue pointer)
        {
            frame = frame with
            {
                SourceValue = pointer.TypeInfo is VBLongPtrType_x64 ? new VBLongLongValue(pointer.Value) : new VBLongValue(unchecked((int)pointer.Value)),
            };
        }

        // the plan says what the value is let-coerced to - an Enum is a Long, a LongPtr what it is an alias of - and whose rule applies: an object, a Null,
        // an Empty and an Error are let-coerced by rules of their own whatever the destination is (MS-VBAL §5.5.1.2.9-11 and 13), anything else by the
        // destination's.
        var plan = ConversionPlanner.ForValue(ValueTypeOf(frame.SourceValue), frame.DestinationTypeDesc.Target);
        if (!plan.Destination.Equals(frame.DestinationTypeDesc.Target))
        {
            frame = frame with { DestinationTypeDesc = new VBTypeDescValue(plan.Destination) };
        }

        // no rule applies to a plan that is a type mismatch, whatever strategy its destination has.
        var strategy = plan.IsTypeMismatch ? null : StrategyFor(plan);
        if (strategy is null)
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
