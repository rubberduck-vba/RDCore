using RDCore.SDK.Model.Values.Bindings;
﻿using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics.Abstract;
using RDCore.SDK;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Runtime.Semantics.LetCoercion;

/// <summary>
/// MS-VBAL 5.5.1.2.13 Let-coercion to and from <c>VBObjectValue</c>
/// </summary>
/// <remarks>
/// The coercion provider dispatches this strategy centrally whenever the SOURCE is an object
/// (<see cref="LetCoercionRuntimeSemanticsProvider.EvaluateLetCoercionSemantics"/>), not just when the
/// destination happens to be one too - MS-VBAL's "Any class -&gt; Any type" and "Nothing -&gt; Any type"
/// rules don't key off the destination at all.
/// </remarks>
public record class VBObjectLetCoercionRuntimeSemantics(
    ILetCoercionRuntimeSemanticsProvider LetCoercionProvider,
    IVerboseMessageBuilder FormatterService)
    : LetCoercionRuntimeSemantics<VBObjectType>(FormatterService)
{
    /// <summary>
    /// The invoker a default-member call runs through. Settable rather than a constructor parameter,
    /// for the same construction-order reason as <c>RuntimeExpressionEvaluator.ProcedureInvoker</c>:
    /// building an invoker needs a <c>ProcedureExecutor</c>, which is built from providers like this
    /// one - so this strategy has to exist first, and this gets assigned once every other collaborator
    /// is composed. <c>null</c> until then; a default-member resolution then reports <c>InternalError</c>
    /// rather than throwing.
    /// </summary>
    public IProcedureInvoker? ProcedureInvoker { get; set; }

    /// <summary>
    /// Binds the default member to whatever runs it. A default member is not always the workspace's own code -
    /// an <c>ErrObject</c> Let-coerced to a <c>Long</c> reads <c>Err.Number</c>, which is the standard
    /// library's. Settable for the same construction-order reason as <see cref="ProcedureInvoker"/>.
    /// </summary>
    public ICallableBindingFactory? Bindings { get; set; }

    /// <summary>
    /// The session a default member is looked up against. Settable for the same construction-order
    /// reason as <see cref="ProcedureInvoker"/> — needed because, per
    /// <c>SetCoercionRuntimeSemantics</c>'s own remarks, a <see cref="VBObjectValue"/>'s own
    /// <c>TypeInfo</c> is always the generic <see cref="VBObjectType"/>: a live object's actual class
    /// is only known by looking up its <see cref="IObjectInstance.ClassModule"/> through
    /// <see cref="IRuntimeSession.TryGetInstance"/>.
    /// </summary>
    public IRuntimeSession? Session { get; set; }

    // MS-VBAL 5.5.1.2.13's own "simple data value" definition: invoke the source's public default
    // Property Get/Function with no arguments (Me alone, at parameter index 0), then let-coerce
    // WHATEVER it returns - object or not - to the frame's real destination by routing back through
    // the provider. That single recursive call handles both outcomes: a non-object result dispatches
    // straight to the destination's own strategy; another object re-enters this same strategy (the
    // provider's central override still applies), and LetCoercionStackManager's existing recursion
    // guard (keyed on NodeId+SourceValue+DestinationTypeDesc) catches a cyclic default-member chain
    // (e.g. an object whose default member returns itself) as OutOfStackSpace.
    private LetCoercionResult GetObjectSimpleDataValue(
        ISymbolResolver resolver,
        ExpressionNode expression,
        LetCoercionStackFrame frame,
        VBObjectValue value)
    {
        if (Session is not { } session || !session.Symbols.TryGetInstance(value.Value, out var instance))
        {
            return LetCoercionResult.Error(VBRuntimeErrorInfo.For(VBRuntimeErrorId.InternalError, expression.Location,
                Exceptions.VBRuntimeInternalError_LetCoercionStrategyWasNotApplicable));
        }

        if (VBClassType.FromClassModule(instance.ClassModule).DefaultMember is not VBReturningMemberSymbol defaultMember)
        {
            return LetCoercionResult.Error(OnLetCoercionObjectDoesntSupportThisPropertyOrMethod(expression, frame));
        }

        // SymbolBuilder.BuildParameters synthesizes an implicit Me at slot 0 of every class-instance
        // member (rdcore-me-implicit-parameter-design); anything beyond that is the member's own
        // declared parameter list. MS-VBAL 5.5.1.2.13 only needs that declared list "compatible with an
        // argument list containing 0 parameters" - every declared parameter is Optional or a ParamArray,
        // not that there are none at all.
        var parameters = RuntimeProcedureInvoker.GetParameters(defaultMember);
        if (parameters.Length == 0 || parameters.Skip(1).Any(parameter => parameter is not ParamArrayParameterSymbol && !parameter.IsOptional))
        {
            return LetCoercionResult.Error(OnLetCoercionObjectDoesntSupportThisPropertyOrMethod(expression, frame));
        }

        if (ProcedureInvoker is not { } invoker)
        {
            return LetCoercionResult.Error(VBRuntimeErrorInfo.For(VBRuntimeErrorId.InternalError, expression.Location,
                Exceptions.VBRuntimeInternalError_LetCoercionStrategyWasNotApplicable));
        }

        // IProcedureInvoker.Invoke itself has no default-argument filling - the call site is expected to
        // supply a full, parameters.Length-sized array (RuntimeExpressionEvaluator.InvokeProcedure's own
        // MapArguments already does exactly this before calling Invoke) - so every omitted argument
        // beyond Me is filled in here: a ParamArray gets a fresh zero-element array (MS-VBAL §5.3.1.11,
        // same shape RuntimeExpressionEvaluator.CollectParamArrayArguments builds for zero elements), an
        // Optional gets its own declared default, or its type's default when it declared none.
        var arguments = new IRuntimeValue[parameters.Length];
        arguments[0] = value.RuntimeValue;
        for (var i = 1; i < parameters.Length; i++)
        {
            arguments[i] = parameters[i] is ParamArrayParameterSymbol
                ? new VBRuntimeValue<VBRuntimeArrayValue>(new VBRuntimeArrayValue(new VBFixedSizeArrayValue([])))
                : (parameters[i].DefaultValue ?? parameters[i].ResolvedType.DefaultValue).RuntimeValue;
        }

        // the arguments already carry Me at index 0, so the binding is asked for with no receiver of its own.
        var invocation = Bindings is { } bindings
            ? bindings.ForMember(defaultMember).Call(resolver, arguments)
            : invoker.Invoke(defaultMember, resolver, arguments);
        return invocation.IsSuccess
            ? LetCoercionProvider.EvaluateLetCoercionSemantics(resolver, expression, frame with { SourceValue = invocation.Result! })
            : LetCoercionResult.Error(invocation.ErrorInfo!);
    }

    /// <summary>
    /// 💥 Creates and returns a new <see cref="RuntimeSemanticsEvaluationResult"/> with a <see cref="VBRuntimeErrorId.ObjectVariableOrWithBlockVariableNotSet"/> error.
    /// </summary>
    /// <param name="expression">The <em>binary arithmetic operator expression</em> whose <c>ResultSymbol</c> the error result will be attached to.</param>
    /// <param name="verbose">A detailed <c>Verbose</c> message about the error.</param>
    protected static LetCoercionResult OnLetCoercionObjectVariableNotSet(ExpressionNode expression, string verbose)
        => LetCoercionResult.Error(VBRuntimeErrorInfo.For(
            VBRuntimeErrorId.ObjectVariableOrWithBlockVariableNotSet, expression.Location, verbose));

    private VBRuntimeErrorInfo OnLetCoercionObjectDoesntSupportThisPropertyOrMethod(ExpressionNode expression, LetCoercionStackFrame frame) =>
        VBRuntimeErrorInfo.For(VBRuntimeErrorId.ObjectDoesntSupportThisPropertyOrMethod, expression.Location,
            FormatterService.Format(Exceptions.LetCoercionRuntimeErrorExceptionObjectDoesntSupportThisPropertyOrMethod_Verbose, expression, [frame]));

    public override LetCoercionResult EvaluateLetCoercion(
        ISymbolResolver resolver,
        ExpressionNode expression,
        LetCoercionStackFrame frame)
        => frame.SourceValue switch
        {
            // IMPLEMENTATION NOTE: moved before VBObjectValue because the inheritance hierarchy would make this case unreachable otherwise.
            VBNothingValue when frame.SourceValue is VBTypedValue
                => OnLetCoercionObjectVariableNotSet(expression, Exceptions.LetCoercionRuntimeErrorExceptionObjectVariableNotSet),

            // a VBObjectValue's own TypeInfo is always the generic VBObjectType (never a VBClassType) -
            // GetObjectSimpleDataValue resolves the real class itself, via Session.
            VBObjectValue objectValue
                => GetObjectSimpleDataValue(resolver, expression, frame, objectValue),

            _ => LetCoercionResult.Error(OnLetCoercionObjectRequired(expression, frame))
        };

    protected override ILetCoercionSemanticContextBuilder AnalyzeLetCoercionOperation(
        ILetCoercionSemanticContextBuilder builder,
        ISymbolResolver resolver,
        ExpressionNode expression,
        LetCoercionStackFrame frame) => builder; // TODO
}
