using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Semantics.Static.Expressions;

/// <summary>
/// MS-VBAL 5.6.7 TypeOf...Is Expressions (static semantics). Always classified as a value with a
/// declared type of Boolean; <see cref="TypeOfIsExpressionNode.Operand"/> must have a declared type
/// of a specific UDT, a specific class, Object, or Variant, and <see cref="TypeOfIsExpressionNode.TypeExpression"/>
/// must name a type.
/// </summary>
/// <remarks>
/// What MS-VBA accepts as the type (Excel-verified, oracle\s29): the name of a class, an interface, a user-defined type or an enumeration, bare or
/// qualified, and <c>Object</c>. An intrinsic type other than <c>Object</c>, or anything that is no name - <c>Nothing</c>, <c>Object()</c>,
/// <c>String * 2</c> - is a syntax error. A name that binds no type is the error a declared type of that name would be
/// (<see cref="DeclarationStaticSemanticsEvaluator"/>): a module, a library's module or a project is no type, and any other name, a variable's or a
/// procedure's included, is a user-defined type that is not defined.
/// </remarks>
public sealed record class TypeOfIsExpressionStaticSemantics : IStaticSemantics
{
    private static readonly Lazy<TypeOfIsExpressionStaticSemantics> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);

    /// <summary>
    /// The shared instance — this rule has no state of its own.
    /// </summary>
    public static TypeOfIsExpressionStaticSemantics Instance => _instance.Value;

    /// <summary>
    /// Determines the declared type of a <see cref="TypeOfIsExpressionNode"/> — always
    /// <see cref="VBBooleanType"/>, provided <see cref="TypeOfIsExpressionNode.Operand"/>'s declared
    /// type satisfies MS-VBAL 5.6.7's classification requirement, and <see cref="TypeOfIsExpressionNode.TypeExpression"/> names a type.
    /// </summary>
    /// <param name="context">The compile-time context this expression is evaluated against: the type is resolved from its scope.</param>
    /// <param name="expression">The <see cref="TypeOfIsExpressionNode"/> being evaluated.</param>
    /// <param name="operandDeclaredTypes">
    /// <see cref="TypeOfIsExpressionNode.Operand"/>'s already-determined declared type, at
    /// <see cref="InputIndex.TypeOfIsOperand"/>. <see cref="TypeOfIsExpressionNode.TypeExpression"/>
    /// names a type, not a value, and has no declared type of its own.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="expression"/> is not a <see cref="TypeOfIsExpressionNode"/>.</exception>
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
