using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Semantics.Static.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics.Static;

/// <summary>
/// The checks that the operands of a statement are held to, which the statements that have them
/// (<see cref="MidStatementStaticSemantics"/>, <see cref="FileStatementStaticSemantics"/>) state once instead of each of them restating.
/// </summary>
/// <remarks>
/// Each check reports into a list rather than returning on the first error, because the operands of a statement are largely independent of one
/// another: a statement with two wrong operands has two errors. And none of them reports what it cannot tell: an operand whose declared type is
/// <see cref="VBUnknownType"/> - a name nothing resolved, a member the library does not declare - is deferred, not rejected, as
/// <see cref="ExpressionStaticSemanticsEvaluator"/> does.
/// </remarks>
internal sealed class StatementOperandChecks(StaticEvaluationContext context)
{
    private readonly ImmutableArray<VBCompileErrorInfo>.Builder _errors = ImmutableArray.CreateBuilder<VBCompileErrorInfo>();

    /// <summary>Every error the checks found, in the order they were found.</summary>
    public ImmutableArray<VBCompileErrorInfo> Errors => _errors.ToImmutable();

    /// <summary>Reports an error a statement's own rule found.</summary>
    public void Add(VBCompileErrorInfo error) => _errors.Add(error);

    /// <summary>
    /// Evaluates <paramref name="expression"/>, reporting what stops it.
    /// </summary>
    /// <returns>Its declared type, or <see langword="null"/> when it has none: the evaluation failed, or the type is not known.</returns>
    public VBType? TypeOf(ExpressionNode expression)
    {
        var result = ExpressionStaticSemanticsEvaluator.Evaluate(context, expression);
        if (result.IsError)
        {
            _errors.Add(result.ErrorInfo!);
            return null;
        }

        return result.Result is null or VBUnknownType ? null : result.Result;
    }

    /// <summary>
    /// "The declared type of <paramref name="expression"/> MUST be a scalar declared type": every declared type but an array and a UDT
    /// (<strong>MS-VBAL §2.2</strong>).
    /// </summary>
    public void RequireScalar(ExpressionNode expression, VBType? type, string role)
    {
        if (type is VBArrayType or VBUserDefinedType)
        {
            _errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.TypeMismatch, expression.Location,
                $"{role} must have a scalar declared type, and this is {(type is VBArrayType ? "an array" : $"the user-defined type {type.Name}")}."));
        }
    }

    /// <summary>
    /// "The declared type MUST be String or Variant."
    /// </summary>
    public void RequireStringOrVariant(ExpressionNode expression, VBType? type, string role)
    {
        if (type is not null && type is not (VBStringType or VBVariantType))
        {
            _errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.TypeMismatch, expression.Location,
                $"{role} must be declared as a String or a Variant, and this is a {type.Name}."));
        }
    }

    /// <summary>
    /// "The declared type MUST NOT be Object or a specific named class" - nor, for the record statements, a UDT whose definition
    /// recursively includes one.
    /// </summary>
    public void RequireNotAnObject(ExpressionNode expression, VBType? type, string role, bool throughUserDefinedTypes)
    {
        if (type is not null && IsObject(type, throughUserDefinedTypes))
        {
            _errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.TypeMismatch, expression.Location,
                $"{role} must not be declared as an Object or as a class{(throughUserDefinedTypes ? ", nor as a user-defined type that has one in it" : string.Empty)}, and this is a {type.Name}."));
        }
    }

    private static bool IsObject(VBType type, bool throughUserDefinedTypes) => type switch
    {
        VBObjectType or VBClassType => true,
        VBUserDefinedType udt when throughUserDefinedTypes => udt.Fields().Any(field => field.ResolvedType is { } fieldType && IsObject(fieldType, true)),
        _ => false,
    };

    /// <summary>
    /// "The value MUST be Let-coercible to the declared type <paramref name="destination"/>" (<strong>MS-VBAL §5.5.1.1</strong>).
    /// </summary>
    public void RequireLetCoercible(ExpressionNode expression, VBType? type, VBType destination)
    {
        if (type is null)
        {
            return;
        }

        var result = LetCoercionStaticSemantics.Instance.DetermineDeclaredType(context, expression, type, destination);
        if (result.IsError)
        {
            _errors.Add(result.ErrorInfo!);
        }
    }

    /// <summary>
    /// "The expression MUST be classified as a variable" (<strong>MS-VBAL §5.6.1</strong>).
    /// </summary>
    /// <remarks>
    /// Only what is certainly not a variable is rejected: a literal, an operator's result, and a name that is a constant. A member access, an
    /// index and the rest are not told apart here, since which of them is a variable depends on what they resolve to.
    /// </remarks>
    public void RequireVariable(ExpressionNode expression, string role)
    {
        if (IsCertainlyNotAVariable(expression))
        {
            _errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.VariableRequired, expression.Location,
                $"{role} must be a variable."));
        }
    }

    private bool IsCertainlyNotAVariable(ExpressionNode expression) => expression switch
    {
        LiteralExpressionNode or VBOperatorExpression => true,
        SimpleNameExpressionNode name => context.Resolver.ResolveValue(name.IdentifierName, ScopeKind.Local, context.Scope.Uri).Symbol
            is VBConstantMemberSymbol or VBLocalConstantSymbol or VBEnumConstMemberSymbol,
        _ => false,
    };
}
