using RDCore.Runtime.Semantics.Abstract;
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
/// MS-VBAL 5.5.1.2.12 Let-coercion to <c>VBVariantType</c>
/// </summary>
public record class VBVariantTypeLetCoercionRuntimeSemantics(
    ILetCoercionRuntimeSemanticsProvider Provider,
    IVerboseMessageBuilder FormatterService) 
    : LetCoercionRuntimeSemantics<VBVariantType>(FormatterService) 
{
    public override LetCoercionResult EvaluateLetCoercion(
        ISymbolResolver resolver, 
        ExpressionNode expression, 
        LetCoercionStackFrame frame) => frame.SourceValue switch
        {
            // "The result is a copy of the source value": an array or a record the Variant is given is its own, and shares no element with the source.
            not VBObjectValue and not VBNothingValue =>
                LetCoercionResult.Success(new VBVariantValue(DataValues.Copy(frame.SourceValue))),

            _ => LetCoercionResult.NotApplicable(frame)
        };

    protected override ILetCoercionSemanticContextBuilder AnalyzeLetCoercionOperation(
        ILetCoercionSemanticContextBuilder builder,
        ISymbolResolver resolver,
        ExpressionNode expression,
        LetCoercionStackFrame frame) => builder.AddFlags(ConversionSemanticFlags.VariantTarget);
}
