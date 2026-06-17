using RDCore.Runtime.Semantics.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Runtime.Semantics.LetCoercion;

/// <summary>
/// MS-VBAL 5.5.1.2.8 Let-coercion to and from <c>VBUserDefinedType</c>
/// </summary>
public record class VBUserDefinedTypeLetCoercionRuntimeSemantics(
    ILetCoercionRuntimeSemanticsProvider Provider,
    IVerboseMessageBuilder FormatterService) 
    : LetCoercionRuntimeSemantics<VBUserDefinedType>(FormatterService)
{
    public override LetCoercionResult EvaluateLetCoercion<TContext, TFlags>(
        ISymbolResolver resolver, 
        VBOperatorExpression<TContext, TFlags> expression, 
        LetCoercionStackFrame frame) => frame.SourceValue switch
        {
            VBUserDefinedTypeValue sourceUDT when sourceUDT.TypeInfo == frame.DestinationTypeDesc.Target => 
                LetCoercionResult.Success(VBTypedValueFactory.CreateValue(frame.DestinationTypeDesc, sourceUDT.ResolvedSymbol, 
                    sourceUDT.Value.ManagedValue)),

            VBUserDefinedTypeValue when frame.DestinationTypeDesc.Target is not VBVariantType =>
                LetCoercionResult.Error(OnLetCoercionTypeMismatch(expression, frame)),

            VBNumericTypedValue or VBBooleanValue or VBDateValue or VBStringValue or VBArrayValue =>
                LetCoercionResult.Error(OnLetCoercionTypeMismatch(expression, frame)),

            _ => LetCoercionResult.NotApplicable(frame)
        };

    protected override ILetCoercionSemanticContextBuilder AnalyzeLetCoercionOperation<TContext, TFlags>(
        ILetCoercionSemanticContextBuilder builder,
        ISymbolResolver resolver,
        VBOperatorExpression<TContext, TFlags> expression,
        LetCoercionStackFrame frame) => builder.AddFlags(ConversionSemanticFlags.UserDefinedTypeTarget);
}
