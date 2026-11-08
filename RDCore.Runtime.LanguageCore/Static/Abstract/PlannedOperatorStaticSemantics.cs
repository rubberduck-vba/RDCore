using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.Runtime.LanguageCore.Plans;
using RDCore.SDK.Semantics;

namespace RDCore.Runtime.LanguageCore.Static.Abstract;

/// <summary>
/// The declared type of an operation, as the plan of the operation has it (<see cref="OperationPlanner"/>): the one table of the effective types of the
/// operators, consulted for declared types here as it is for values at run time.
/// </summary>
/// <remarks>
/// <para>
/// An operand declared <c>Variant</c> makes the operation a <c>Variant</c>: its plan is deferred, and what it yields is decided by the value. An operand
/// declared <c>Object</c> makes it late-bound (<see cref="VBUnknownType"/>): the default member of the object is evaluated at run time, which says what the
/// operand is - an object, as such, is not made into a <c>Variant</c>. Neither may be operated on with an array or a user-defined type, which is a compile
/// error. Any other operation is the plan's: its effective type is the declared type of the operation, and no effective type is the compile error.
/// </para>
/// <para>
/// 👉 The relational operators, <c>Like</c> and <c>Is</c> have static rules of their own: an operand that only a value tells about - a class, which its
/// default member decides - is compared all the same.
/// </para>
/// </remarks>
public abstract record class PlannedOperatorStaticSemantics : StaticSemantics, IStaticSemantics
{
    /// <summary>
    /// The operator: one of the <c>Tokens</c> operators, which is what the plan of the operation is made for.
    /// </summary>
    protected abstract string Operator { get; }

    public sealed override StaticSemanticsEvaluationResult DetermineDeclaredType(StaticEvaluationContext context, ExpressionNode expression, params VBType[] operandDeclaredTypes)
    {
        var isLateBound = operandDeclaredTypes.Any(operand => operand is VBVariantType or VBObjectType);
        if (isLateBound)
        {
            return operandDeclaredTypes.Any(operand => operand is VBArrayType or VBUserDefinedType)
                ? StaticSemanticsEvaluationResult.Error(GetStaticTypeMismatchErrorInfo(expression, operandDeclaredTypes))
                : StaticSemanticsEvaluationResult.Success(operandDeclaredTypes.Any(operand => operand is VBVariantType) ? VBVariantType.TypeInfo : VBUnknownType.TypeInfo);
        }

        var plan = operandDeclaredTypes.Length == 1
            ? OperationPlanner.ForDeclared(Operator, operandDeclaredTypes[(int)InputIndex.UnaryOperand])
            : OperationPlanner.ForDeclared(Operator, operandDeclaredTypes[(int)InputIndex.BinaryLeftOperand], operandDeclaredTypes[(int)InputIndex.BinaryRightOperand]);

        return plan.EffectiveType is { } effectiveType
            ? StaticSemanticsEvaluationResult.Success(effectiveType)
            : StaticSemanticsEvaluationResult.Error(GetStaticTypeMismatchErrorInfo(expression, operandDeclaredTypes));
    }
}
