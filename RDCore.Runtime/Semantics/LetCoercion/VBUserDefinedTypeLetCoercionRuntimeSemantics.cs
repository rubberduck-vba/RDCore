using RDCore.Runtime.Semantics.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Model.AST.Abstract;
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
    public override LetCoercionResult EvaluateLetCoercion(
        ISymbolResolver resolver, 
        ExpressionNode expression, 
        LetCoercionStackFrame frame) => frame.SourceValue switch
        {
            // 5.5.1.2.8: to the same UDT, "the result is a copy of the source value" - fields and all, in cells of its own, so that a field
            // assigned through the copy is not assigned in the source.
            VBUserDefinedTypeValue sourceUDT when sourceUDT.TypeInfo == frame.DestinationTypeDesc.Target =>
                LetCoercionResult.Success(sourceUDT.DataCopy()),

            VBUserDefinedTypeValue when frame.DestinationTypeDesc.Target is not VBVariantType =>
                LetCoercionResult.Error(OnLetCoercionTypeMismatch(expression, frame)),

            VBNumericTypedValue or VBBooleanValue or VBDateValue or VBStringValue or VBArrayValue =>
                LetCoercionResult.Error(OnLetCoercionTypeMismatch(expression, frame)),

            _ => LetCoercionResult.NotApplicable(frame)
        };

    protected override ILetCoercionSemanticContextBuilder AnalyzeLetCoercionOperation(
        ILetCoercionSemanticContextBuilder builder,
        ISymbolResolver resolver,
        ExpressionNode expression,
        LetCoercionStackFrame frame) => builder.AddFlags(ConversionSemanticFlags.UserDefinedTypeTarget);
}
