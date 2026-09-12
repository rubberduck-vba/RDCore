using RDCore.Runtime.Semantics.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;
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
        VBOperatorExpression expression, 
        LetCoercionStackFrame frame) => frame.SourceValue switch
        {
            // 5.5.1.2.8 — coercion to the same UDT type. The value is a location; deep field copy is
            // the execution engine's job, so this shares the source reference. VBType.CreateValue
            // (IBindingHandle) isn't overridden by any type in the codebase yet, so it's bypassed here
            // rather than relied on.
            VBUserDefinedTypeValue sourceUDT when sourceUDT.TypeInfo == frame.DestinationTypeDesc.Target =>
                LetCoercionResult.Success(new VBUserDefinedTypeValue(sourceUDT.Handle, (VBUserDefinedType)frame.DestinationTypeDesc.Target)),

            VBUserDefinedTypeValue when frame.DestinationTypeDesc.Target is not VBVariantType =>
                LetCoercionResult.Error(OnLetCoercionTypeMismatch(expression, frame)),

            VBNumericTypedValue or VBBooleanValue or VBDateValue or VBStringValue or VBArrayValue =>
                LetCoercionResult.Error(OnLetCoercionTypeMismatch(expression, frame)),

            _ => LetCoercionResult.NotApplicable(frame)
        };

    protected override ILetCoercionSemanticContextBuilder AnalyzeLetCoercionOperation(
        ILetCoercionSemanticContextBuilder builder,
        ISymbolResolver resolver,
        VBOperatorExpression expression,
        LetCoercionStackFrame frame) => builder.AddFlags(ConversionSemanticFlags.UserDefinedTypeTarget);
}
