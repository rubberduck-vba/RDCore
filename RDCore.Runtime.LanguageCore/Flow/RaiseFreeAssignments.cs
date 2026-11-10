using RDCore.Runtime.LanguageCore.Instructions;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Semantics;

using ExpressionClassification = RDCore.SDK.Semantics.ExpressionClassification;

namespace RDCore.Runtime.LanguageCore.Flow;

/// <summary>
/// The assignments of a procedure that cannot raise an error: what <see cref="ControlFlowGraph.CanRaiseError"/> says of an instruction by its kind is told
/// exactly of these.
/// </summary>
/// <remarks>
/// <para>
/// An assignment of a literal to a variable of an intrinsic type, where the literal is let-coerced to the type without an error (<c>F = 1</c>, <c>s = "x"</c>,
/// <c>b = True</c>), runs no code and overflows nothing: under <c>On Error Resume Next</c> it always completes. Nothing else is told of: an expression that is not a
/// literal, a <c>Set</c>, a target that is a property or an element, and a literal the type may not hold (<c>b = 300</c> to a <c>Byte</c>, <c>n = "1"</c> to a
/// number) can raise, as far as is known.
/// </para>
/// <para>
/// The conversions are those of <strong>MS-VBAL §5.5.1.2</strong>: a number to an integral type is rounded to the nearest even integer first (Banker's rounding,
/// §5.5.1.2.1.1), and overflows outside the type's range; <c>True</c> is <c>-1</c>; any number or Boolean converts to <c>Boolean</c> and to <c>String</c>, and
/// anything to a <c>Variant</c>.
/// </para>
/// </remarks>
internal static class RaiseFreeAssignments
{
    public static IReadOnlySet<int> Of(InstructionList instructions, ProcedureSemanticModel model)
        => instructions.Items
            .Where(instruction => instruction is { Kind: InstructionKind.Simple, Node: AssignmentStatementNode { Kind: AssignmentKind.ImplicitLet or AssignmentKind.ExplicitLet } assignment }
                                  && CannotRaise(assignment, model))
            .Select(instruction => instruction.Offset)
            .ToHashSet();

    private static bool CannotRaise(AssignmentStatementNode assignment, ProcedureSemanticModel model)
        => assignment.Target is SimpleNameExpressionNode target
           && model.Expressions.TryGetValue(target.Identity, out var fact)
           && fact is { Error: null, DeclaredType: { } type }
           && (fact.Classification is ExpressionClassification.Variable || fact.Binding == model.Procedure)
           && ValueOf(assignment.Value) is { } literal
           && Converts(literal, type);

    // a literal, or a negated numeric one.
    private static (double? Number, bool IsText)? ValueOf(ExpressionNode value) => value switch
    {
        LiteralExpressionNode { StaticValue: VBBooleanValue boolean } => (boolean.Value.StoredValue != 0 ? -1 : 0, false),
        LiteralExpressionNode { StaticValue: VBNumericTypedValue number } => (number.AsDouble, false),
        LiteralExpressionNode { StaticValue: VBStringValue } => (null, true),
        VBUnaryOperatorExpressionNode { Token: Tokens.NegationOp, Operand: LiteralExpressionNode { StaticValue: VBNumericTypedValue negated } } => (-negated.AsDouble, false),
        _ => null,
    };

    private static bool Converts((double? Number, bool IsText) literal, VBType type)
    {
        if (type is VBVariantType)
        {
            return true;
        }

        if (literal.IsText)
        {
            return type is VBStringType;
        }

        if (literal.Number is not { } number || !double.IsFinite(number))
        {
            return false;
        }

        var rounded = Math.Round(number, MidpointRounding.ToEven);
        return type switch
        {
            VBStringType or VBBooleanType or VBDoubleType => true,
            VBByteType => rounded is >= byte.MinValue and <= byte.MaxValue,
            VBIntegerType => rounded is >= short.MinValue and <= short.MaxValue,
            VBLongType => rounded is >= int.MinValue and <= int.MaxValue,
            // well inside the ranges a double tells exactly: what is near their ends is told of no more.
            VBLongLongType => Math.Abs(rounded) < 9e18,
            VBCurrencyType => Math.Abs(number) < 9e14,
            VBSingleType => Math.Abs(number) <= float.MaxValue,
            _ => false,
        };
    }
}
