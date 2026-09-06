using RDCore.Runtime.Semantics.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
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
/// MS-VBAL 5.5.1.2.3 Let-coercion to and from <c>VBDateType</c>
/// </summary>
public record class VBDateLetCoercionRuntimeSemantics(
    ILetCoercionRuntimeSemanticsProvider Provider,
    IVerboseMessageBuilder FormatterService) 
    : LetCoercionRuntimeSemantics<VBDateType>(FormatterService)
{
    public override LetCoercionResult EvaluateLetCoercion(
        ISymbolResolver resolver, 
        VBOperatorExpression expression, 
        LetCoercionStackFrame frame) => 
        frame.SourceValue switch
        {
            VBDateValue sourceDateValue when frame.DestinationTypeDesc.Target is VBDateType 
                // result is a copy of the source date (no implicit DateSerial semantic flag should be issued here)
                => LetCoercionResult.Success(
                    frame.DestinationTypeDesc.Target.CreateValue(new ValueBindingHandle(sourceDateValue.RuntimeValue))),

            VBDateValue sourceDateValue when frame.DestinationTypeDesc.Target is VBNumericType or VBBooleanType 
                // result is the standard Double representation (DateSerial), let-coerced to the destination type
                => LetCoercionResult.Success(frame.DestinationTypeDesc.Target.CreateValue(new ValueBindingHandle(
                    ((VBNumericTypedValue)Provider.EvaluateLetCoercionSemantics(resolver, expression,
                        frame with {
                            // we must first create the VBDoubleValue for the managed SerialValue:
                            SourceValue = new VBDoubleValue((double)sourceDateValue.RuntimeValue.BoxedValue)
                        }).Result!).RuntimeValue))),

            VBNumericTypedValue or VBBooleanValue when frame.DestinationTypeDesc.Target is VBDateType
                // result is the source value let-coerced to Double, then the Double is interpreted as a standard SerialValue.
                => LetCoercionResult.Success(frame.DestinationTypeDesc.Target.CreateValue(new ValueBindingHandle(
                    ((VBDoubleValue)Provider.EvaluateLetCoercionSemantics(resolver, expression,
                        // we must first create the VBDoubleValue for the managed SerialValue:
                        frame with {
                            DestinationTypeDesc = new VBTypeDescValue(VBDoubleType.TypeInfo)
                        }).Result!).RuntimeValue))),

            _ => LetCoercionResult.NotApplicable(frame)
        };

    protected override ILetCoercionSemanticContextBuilder AnalyzeLetCoercionOperation(
        ILetCoercionSemanticContextBuilder builder, 
        ISymbolResolver resolver, 
        VBOperatorExpression expression, 
        LetCoercionStackFrame frame)
    {
        var destinationType = frame.DestinationTypeDesc.Target;
        builder.AddLetCoercionFlags(ConversionSemanticFlags.CTypeAvailable | frame.SourceValue switch
        {
            VBDateValue when destinationType is VBNumericType or VBBooleanType && destinationType.DefaultValue.Size < VBDoubleType.TypeInfo.DefaultValue.Size
                => ConversionSemanticFlags.DateSerial | ConversionSemanticFlags.Narrowing,
            VBDateValue when destinationType is VBNumericType or VBBooleanType
                => ConversionSemanticFlags.DateSerial,

            VBNumericTypedValue or VBBooleanValue when destinationType is VBDateType && frame.SourceValue.Size < VBDoubleType.TypeInfo.DefaultValue.Size
                => ConversionSemanticFlags.Numeric | ConversionSemanticFlags.Widening,
            VBNumericTypedValue or VBBooleanValue when destinationType is VBDateType
                => ConversionSemanticFlags.Numeric,

            _ => 0
        }, frame.OperandIndex);

        return builder;
    }
}
