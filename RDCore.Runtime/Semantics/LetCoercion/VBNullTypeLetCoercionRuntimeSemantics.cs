using RDCore.Runtime.Semantics.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Runtime.Semantics.LetCoercion;

/// <summary>
/// MS-VBAL 5.5.1.2.10 Let-coercion to and from <c>VBNullType</c>
/// </summary>
public record class VBNullTypeLetCoercionRuntimeSemantics(
    IVerboseMessageBuilder FormatterService)
    : LetCoercionRuntimeSemantics<VBNullType>(FormatterService)
{
    public override LetCoercionResult EvaluateLetCoercion(
        ISymbolResolver resolver,
        VBOperatorExpression expression,
        LetCoercionStackFrame frame) => frame.SourceValue switch
        {
            // MS-VBAL 5.5.1.2.10: Null -> a resizable array or UDT is Type mismatch (13), not Overflow.
            VBNullValue when frame.DestinationTypeDesc.GetTargetType() is VBUserDefinedType or VBResizableArrayType
                => LetCoercionResult.Error(OnLetCoercionTypeMismatch(expression, frame)),

            VBNullValue when frame.DestinationTypeDesc.GetTargetType() is not VBNullType and not VBFixedSizeArrayType and not VBVariantType
                => LetCoercionResult.Error(OnLetCoercionInvalidUseOfNull(expression, frame)),

            _ => LetCoercionResult.NotApplicable(frame)
        };

    protected override ILetCoercionSemanticContextBuilder AnalyzeLetCoercionOperation(
        ILetCoercionSemanticContextBuilder builder,
        ISymbolResolver resolver,
        VBOperatorExpression expression,
        LetCoercionStackFrame frame) => builder.AddFlags(ConversionSemanticFlags.NullOperand | 
            frame.SourceValue switch
            {
                VBNullValue when frame.DestinationTypeDesc.GetTargetType() is VBUserDefinedType or VBResizableArrayType
                    => ConversionSemanticFlags.Failed,

                VBNullValue when frame.DestinationTypeDesc.GetTargetType() is not VBNullType and not VBFixedSizeArrayType and not VBVariantType
                    => ConversionSemanticFlags.Failed,
                _ => 0
            });
}
