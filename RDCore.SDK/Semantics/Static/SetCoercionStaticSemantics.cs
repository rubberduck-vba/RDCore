using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Semantics.Static;

/// <summary>
/// MS-VBAL 5.5.2.1 Set-coercion (static semantics)
/// </summary>
/// <remarks>
/// Considerably thinner than <see cref="LetCoercionStaticSemantics"/>'s table: Set-coercion only ever
/// moves object references around, so the static table is just two rows — is the destination even
/// object-ish (a specific class, <c>Object</c>, or <c>Variant</c>), and if so, is the source too? MS-VBAL
/// 5.5.2.1 deliberately stops there: whether a source class actually implements or derives from the
/// destination's declared class is a <em>runtime</em>-semantics question (5.5.2.2.1, runtime error 13
/// on mismatch), not a static one — <see cref="VBClassType.Supertypes"/> has no role to play here.
/// </remarks>
public record class SetCoercionStaticSemantics : StaticSemantics
{
    private static readonly Lazy<SetCoercionStaticSemantics> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static IStaticSemantics Instance => _instance.Value;

    public override StaticSemanticsEvaluationResult DetermineDeclaredType(StaticEvaluationContext context, ExpressionNode expression, params VBType[] operandDeclaredTypes)
    {
        var source = operandDeclaredTypes[(int)InputIndex.CoercionSourceValue];
        var destinationType = operandDeclaredTypes[(int)InputIndex.CoercionDestinationType];
        return !IsSetCoercionInvalid(source, destinationType)
            ? StaticSemanticsEvaluationResult.Success(destinationType)
            : StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(VBCompileErrorId.TypeMismatch, expression.Location,
                Exceptions.VBCompileError_SetCoercionTypeMismatch_Verbose));
    }

    /// <returns>
    /// <c>true</c> if a Set-coercion operation is <strong>invalid</strong> between the specified source
    /// and destination data types — i.e. either side isn't a specific class, <c>Object</c>, or
    /// <c>Variant</c>. <see cref="VBUnknownType"/> on either side (not yet resolved/modeled) can't be
    /// shown to violate this, so it's deferred rather than flagged — same convention as every other
    /// rule this evaluator dispatches to.
    /// </returns>
    private static bool IsSetCoercionInvalid(VBType source, VBType destination)
        => source is not VBUnknownType && destination is not VBUnknownType
            && (destination is not (VBClassType or VBObjectType or VBVariantType)
                || source is not (VBClassType or VBObjectType or VBVariantType));
}
