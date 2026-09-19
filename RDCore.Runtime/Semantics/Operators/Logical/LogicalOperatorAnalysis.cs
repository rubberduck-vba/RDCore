using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Context.Abstract;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Runtime.Semantics.Operators.Logical;

/// <summary>
/// The analysis every logical operator shares, unary and binary alike (MS-VBAL 5.6.9.8): what the operands make of the
/// operation, and the effective type it is evaluated in.
/// </summary>
internal static class LogicalOperatorAnalysis
{
    public static ISemanticContextContributor<TContext, LogicalOperatorSemanticFlags> Analyze<TContext>(
        ISemanticContextContributor<TContext, LogicalOperatorSemanticFlags> builder,
        DetermineOperatorEffectiveTypeResult effectiveType,
        params VBTypedValue[] operands)
        where TContext : SemanticContext<LogicalOperatorSemanticFlags>, new()
    {
        // integral operands make a bitwise operation; a Boolean operand (or a Null one) does not.
        if (operands.All(operand => operand.TypeInfo is IIntegralNumericType))
        {
            builder.AddFlags(LogicalOperatorSemanticFlags.IsBitwiseSemantics);
        }
        if (operands.Any(operand => operand is VBNullValue))
        {
            builder.AddFlags(LogicalOperatorSemanticFlags.HasNullOperand);
        }

        return builder.AddFlags(effectiveType.Result switch
        {
            VBBooleanType => LogicalOperatorSemanticFlags.BooleanEffectiveType,
            VBByteType => LogicalOperatorSemanticFlags.ByteEffectiveType,
            VBIntegerType => LogicalOperatorSemanticFlags.IntegerEffectiveType,
            VBLongType => LogicalOperatorSemanticFlags.LongEffectiveType,
            VBLongLongType => LogicalOperatorSemanticFlags.LongLongEffectiveType,
            VBNullType => LogicalOperatorSemanticFlags.NullEffectiveType,
            _ => 0
        });
    }
}
