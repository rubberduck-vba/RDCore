using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract;
using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Semantics.Runtime.Operators.Context;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.SDK.Semantics.Runtime.LetCoercion;

/// <summary>
/// MS-VBAL 5.5.1.2.13 Let-coercion to and from <c>VBObjectValue</c>
/// </summary>
public record class VBObjectLetCoercionRuntimeSemantics(
    ILetCoercionRuntimeSemanticsProvider LetCoercionProvider,
    IVerboseMessageBuilder FormatterService) : LetCoercionRuntimeSemantics<VBObjectType>(FormatterService)
{
    private VBTypedValue? GetObjectSimpleDataValue(
        ISymbolResolver memory, 
        VBObjectValue value) 
    { 
        if (value.TypeInfo is VBClassType classType && classType.DefaultMember is VBTypeMemberSymbol defaultMember)
        {
            VBTypedValue resolvedValue = value;
            if (defaultMember.ResolvedType is VBVariantType variantType)
            {
                // late bound variant - the runtime would know, but we know its subtype and we can use it.
                // however if we unwrap another variant, we could be missing the bigger picture: let the coercion provider handle this.

                // ⚠️ FIXME two consecutive VBVariant/VBVariant->VBVariant/VBVariant frames would be deemed recursive and stop resolution, which is a problem:
                // we're resolving to VBUnknownType (so.. *not* resolving then), and this will inevitably end with an InternalError stack trace.
                if (variantType.Subtype != VBVariantType.TypeInfo)
                {
                }
            }

            if (defaultMember.ResolvedType is VBObjectType)
            {
                // late bound call - the runtime knows what we're looking at.
                // 👉 IVirtualHeap.GetValue yields in O(1) the VBTypedValue of any symbol loaded in the global heap.
            }

            if (defaultMember.ResolvedType is VBClassType)
            {
                // early bound call - everything is statically defined.
            }

        }

        return default;
    }

    /// <summary>
    /// 💥 Creates and returns a new <see cref="RuntimeSemanticsEvaluationResult"/> with a <see cref="VBRuntimeErrorId.ObjectVariableOrWithBlockVariableNotSet"/> error.
    /// </summary>
    /// <param name="expression">The <em>binary arithmetic operator expression</em> whose <c>ResultSymbol</c> the error result will be attached to.</param>
    /// <param name="verbose">A detailed <c>Verbose</c> message about the error.</param>
    protected static LetCoercionResult OnLetCoercionObjectVariableNotSet(BoundExpressionNode expression, string verbose)
        => LetCoercionResult.Error(OnRuntimeError(VBRuntimeErrorId.ObjectVariableOrWithBlockVariableNotSet, expression, verbose));

    /// <summary>
    /// A helper method to get a <c>VBRuntimeErrorInfo</c> error metadata from derived types as needed.
    /// </summary>
    protected static VBRuntimeErrorInfo OnRuntimeError(VBRuntimeErrorId errorId, BoundExpressionNode expression, string verbose)
        => new(errorId, expression.Location, VBRuntimeErrorException.GetErrorString(errorId), verbose);

    public override LetCoercionResult EvaluateLetCoercion<ConversionOperationSemanticContext, ConversionSemanticFlags>(
        ISymbolResolver resolver, 
        VBOperatorExpression<ConversionOperationSemanticContext, ConversionSemanticFlags> expression, 
        LetCoercionStackFrame frame) 
        => frame.SourceValue switch
        {
            // IMPLEMENTATION NOTE: moved before VBObjectValue because the inheritance hierarchy would make this case unreachable otherwise.
            VBNothingValue when frame.SourceValue is VBTypedValue 
                => OnLetCoercionObjectVariableNotSet(expression, Exceptions.LetCoercionRuntimeErrorExceptionObjectVariableNotSet),
            
            VBObjectValue objectValue when frame.SourceValue.TypeInfo is VBClassType 
                =>  GetObjectSimpleDataValue(resolver, objectValue) is VBTypedValue result 
                        ? LetCoercionResult.Success(result, frame)
                        : LetCoercionResult.Error(OnLetCoercionTypeMismatch(expression, frame)),

            not VBObjectValue and not VBNothingValue 
                => LetCoercionResult.Error(OnLetCoercionObjectRequired(expression, frame)),

            _ => LetCoercionResult.NotApplicable(frame)
        };

    protected override ILetCoercionSemanticContextBuilder AnalyzeLetCoercionOperation<TContext, TFlags>(
        ILetCoercionSemanticContextBuilder builder,
        ISymbolResolver resolver,
        VBOperatorExpression<TContext, TFlags> expression,
        LetCoercionStackFrame frame) => builder; // TODO
}
