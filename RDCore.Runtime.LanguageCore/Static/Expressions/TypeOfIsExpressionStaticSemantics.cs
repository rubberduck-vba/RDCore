using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.Runtime.LanguageCore.Static.Abstract;
using RDCore.SDK.Semantics;
using RDCore.SDK;

namespace RDCore.Runtime.LanguageCore.Static.Expressions;

// MS-VBAL §5.6.7
public sealed record class TypeOfIsExpressionStaticSemantics : IStaticSemantics
{
    private static readonly Lazy<TypeOfIsExpressionStaticSemantics> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);

    public static TypeOfIsExpressionStaticSemantics Instance => _instance.Value;

    // MS-VBAL §5.6.7
    public StaticSemanticsEvaluationResult DetermineDeclaredType(StaticEvaluationContext context, ExpressionNode expression, params VBType[] operandDeclaredTypes)
    {
        if (expression is not TypeOfIsExpressionNode typeOfIs)
        {
            throw new ArgumentException($"Expected a {nameof(TypeOfIsExpressionNode)}.", nameof(expression));
        }

        var operand = operandDeclaredTypes[(int)InputIndex.TypeOfIsOperand];

        // VBUnknownType (not yet resolved/modeled) can't be shown to violate the classification
        // requirement, so it's deferred rather than flagged — same convention as every other rule.
        if (operand is not (VBUserDefinedType or VBClassType or VBObjectType or VBVariantType or VBUnknownType))
        {
            return StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(VBCompileErrorId.TypeMismatch, expression.Location,
                $"'{operand.Name}' is not a UDT, class, Object, or Variant."));
        }

        return TypeError(context, typeOfIs.TypeExpression) is { } error
            ? StaticSemanticsEvaluationResult.Error(error)
            : StaticSemanticsEvaluationResult.Success(VBBooleanType.TypeInfo);
    }

    // the error of a type expression that names no type TypeOf ... Is can test against, or null.
    private static VBCompileErrorInfo? TypeError(StaticEvaluationContext context, ExpressionNode typeExpression)
    {
        var (qualifier, name) = typeExpression switch
        {
            SimpleNameExpressionNode simple => ((string?)null, simple.IdentifierName),
            MemberAccessExpressionNode { Owner: SimpleNameExpressionNode owner, Member: { } member } => (owner.IdentifierName, member.IdentifierName),
            _ => (null, (string?)null),
        };

        if (typeExpression is BuiltInTypeExpressionNode builtInType)
        {
            return VBCompileErrorInfo.For(VBCompileErrorId.SyntaxError, typeExpression.Location,
                string.Format(Exceptions.VBCompileError_TypeOfIntrinsicType_Verbose, builtInType.TypeName));
        }

        if (name is null)
        {
            return VBCompileErrorInfo.For(VBCompileErrorId.SyntaxError, typeExpression.Location, Exceptions.VBCompileError_TypeOfNotATypeName_Verbose);
        }

        // Object is the one intrinsic type an object can be; `Date` is read as a name, and is an intrinsic type all the same.
        if (qualifier is null && IntrinsicVBTypes.IsIntrinsic(name))
        {
            return string.Equals(name, VBTypeNames.VBObject, StringComparison.OrdinalIgnoreCase)
                ? null
                : VBCompileErrorInfo.For(VBCompileErrorId.SyntaxError, typeExpression.Location, string.Format(Exceptions.VBCompileError_TypeOfIntrinsicType_Verbose, name));
        }

        var written = qualifier is null ? name : $"{qualifier}.{name}";
        var (bound, _) = TypeNameClassifier.Classify(qualifier, name, context.Scope.Uri, context.Resolver);
        return bound switch
        {
            TypeNameClass.Type => null,
            TypeNameClass.Unresolved => VBCompileErrorInfo.For(VBCompileErrorId.UserDefinedTypeNotDefined, typeExpression.Location,
                string.Format(Exceptions.VBCompileError_TypeOfTypeNotResolved_Verbose, written)),
            _ => DeclarationStaticSemanticsEvaluator.NoTypeError(bound, written, typeExpression.Location),
        };
    }
}
