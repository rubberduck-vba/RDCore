using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.Runtime.LanguageCore.Static.Abstract;
using RDCore.SDK.Semantics;
using RDCore.Runtime.LanguageCore.Errors;

namespace RDCore.Runtime.LanguageCore.Static.Expressions;

// MS-VBAL §5.6.14
public sealed record class DictionaryAccessExpressionStaticSemantics : IStaticSemantics
{
    private static readonly Lazy<DictionaryAccessExpressionStaticSemantics> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);

    public static DictionaryAccessExpressionStaticSemantics Instance => _instance.Value;

    // MS-VBAL §5.6.15
    public StaticSemanticsEvaluationResult DetermineDeclaredType(StaticEvaluationContext context, ExpressionNode expression, params VBType[] operandDeclaredTypes)
    {
        if (expression is not DictionaryAccessExpressionNode)
        {
            throw new ArgumentException($"Expected a {nameof(DictionaryAccessExpressionNode)}.", nameof(expression));
        }

        var owner = operandDeclaredTypes[(int)InputIndex.DictionaryAccessOwner];
        if (owner is VBVariantType or VBObjectType)
        {
            // syntactically translated into an index expression over Owner with a single String
            // argument; over a Variant/Object l-expression that's an unbound member, declared Variant.
            return StaticSemanticsEvaluationResult.Success(VBVariantType.TypeInfo);
        }

        if (owner is not VBClassType declaredClass)
        {
            // "invalid if the declared type of l-expression is a type other than a specific class,
            // Object or Variant" (MS-VBAL 5.6.14). VBUnknownType (not yet resolved/modeled) can't be
            // shown to violate this, so it's deferred rather than flagged.
            return owner is VBUnknownType
                ? StaticSemanticsEvaluationResult.Success(VBUnknownType.TypeInfo)
                : StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(VBCompileErrorId.TypeMismatch, expression.Location,
                    $"'{owner.Name}' is not a specific class, Object, or Variant."));
        }

        // the default member of the class as the composition has it now (CurrentTypes).
        var classType = CurrentTypes.Of(context.Resolver, declaredClass);
        return classType.DefaultMember is { } defaultMember
            ? StaticSemanticsEvaluationResult.Success(defaultMember.ResolvedType)
            : StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(VBCompileErrorId.MethodOrDataMemberNotFound, expression.Location, classType.Name));
    }
}
