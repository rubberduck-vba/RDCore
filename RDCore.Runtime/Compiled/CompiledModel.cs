using RDCore.SDK.Model.Source;

namespace RDCore.Runtime.Compiled;

/// <summary>
/// Why a procedure is not compiled: what in it compiled code does not do yet, and stays the interpreter's.
/// </summary>
internal enum IneligibilityKind
{
    /// <summary>A member of a class, which is called on an object (stage 2, S2.5).</summary>
    MemberOfAClass,

    /// <summary>A parameter passed by reference (S2.3).</summary>
    ParameterByReference,

    /// <summary>A parameter, a variable or a result of a type that is no scalar (S2.5, S2.6).</summary>
    NotAScalar,

    /// <summary>A <c>Static</c> variable (S2.6).</summary>
    StaticVariable,

    /// <summary>An instruction compiled code does not do: an error handler, a <c>GoSub</c>, a <c>With</c>, a <c>Select Case</c>, a <c>For Each</c>.</summary>
    Instruction,

    /// <summary>A statement compiled code does not do: a call, a <c>Set</c>, a file statement.</summary>
    Statement,

    /// <summary>An expression compiled code does not do: a call, a member, an element, a <c>String</c>, a <c>Variant</c>, a name bound late.</summary>
    Expression,

    /// <summary>An operator compiled code does not do: <c>^</c>, <c>&amp;</c>, <c>Like</c>, <c>Is</c>.</summary>
    Operator,

    /// <summary>A <c>GoTo</c>, a line that is jumped to (S2.4).</summary>
    GoTo,
}

/// <summary>
/// What keeps a procedure from being compiled, and where.
/// </summary>
/// <param name="Kind">What it is.</param>
/// <param name="Detail">What of it: the name of the parameter, the instruction, the expression.</param>
/// <param name="Location">Where it is written, when it is written somewhere.</param>
internal sealed record class Ineligibility(IneligibilityKind Kind, string Detail, SourceLocation? Location = null);

/// <summary>
/// A procedure compiled, or why it is not.
/// </summary>
/// <param name="Body">The compiled body; <see langword="null"/> when the procedure is not compiled.</param>
/// <param name="Reasons">Why it is not; empty when it is.</param>
/// <param name="Elapsed">How long compiling it took, or finding out it is not compiled.</param>
internal sealed record class CompilationResult(CompiledBody? Body, IReadOnlyList<Ineligibility> Reasons, TimeSpan Elapsed);

// a payload, the value of an expression of a scalar kind: in a long when it is of an integral kind, in a double when it is of a floating-point one.
internal abstract record class PayloadNode(ScalarKind Kind);

// a parameter or a variable of the activation, by the number of its slot.
internal sealed record class SlotNode(int Slot, ScalarKind Kind) : PayloadNode(Kind);

// the function result variable, read.
internal sealed record class ResultNode(ScalarKind Kind) : PayloadNode(Kind);

// a literal, or a constant folded.
internal sealed record class ConstantNode(ScalarKind Kind, long Integral, double Floating) : PayloadNode(Kind);

// PayloadOperations.Convert: an exact conversion.
internal sealed record class ConvertNode(PayloadNode Operand, ScalarKind Kind) : PayloadNode(Kind);

// PayloadOperations.Add, Subtract, Multiply, IntegerDivide, Modulo, of an integral kind.
internal sealed record class IntegralNode(string Token, PayloadNode Left, PayloadNode Right, ScalarKind Kind) : PayloadNode(Kind);

// PayloadOperations.Logical.
internal sealed record class LogicalNode(LogicalOperator Operator, PayloadNode Left, PayloadNode Right, ScalarKind Kind) : PayloadNode(Kind);

// PayloadOperations.Compare, of two payloads of one kind: a Boolean.
internal sealed record class CompareNode(ComparisonOperator Operator, PayloadNode Left, PayloadNode Right) : PayloadNode(ScalarKind.Boolean);

// PayloadOperations.Not.
internal sealed record class NotNode(PayloadNode Operand, ScalarKind Kind) : PayloadNode(Kind);

// PayloadOperations.Negate.
internal sealed record class NegateNode(PayloadNode Operand, ScalarKind Kind) : PayloadNode(Kind);

// an extended intermediate converted to a kind: Extended.ToFloating, Extended.ToIntegral.
internal sealed record class StoreNode(ExtendedNode Value, ScalarKind Kind) : PayloadNode(Kind);

// Extended.Compare, of a floating-point effective type: a Boolean.
internal sealed record class ExtendedCompareNode(ComparisonOperator Operator, ExtendedNode Left, ExtendedNode Right, ScalarKind Effective) : PayloadNode(ScalarKind.Boolean);

// an extended intermediate (MS-VBAL §5.6.9.3).
internal abstract record class ExtendedNode;

// a payload, as an extended value: Extended.OfIntegral, Extended.OfFloating.
internal sealed record class WidenNode(PayloadNode Operand) : ExtendedNode;

// Extended.Add, Subtract, Multiply, Divide.
internal sealed record class ExtendedOperationNode(string Token, ExtendedNode Left, ExtendedNode Right) : ExtendedNode;

// Extended.Negate.
internal sealed record class ExtendedNegateNode(ExtendedNode Operand) : ExtendedNode;

// an instruction of the body, compiled: each is at the offset of the instruction it is.
internal abstract record class CompiledStep(int Offset);

// a Let of a variable (Slot), or of the function result variable (Slot is -1).
internal sealed record class LetStep(int Offset, int Slot, PayloadNode Value) : CompiledStep(Offset);

// a ConditionalBranch: true falls through, false goes to Else. Negate for an Until.
internal sealed record class BranchStep(int Offset, PayloadNode Condition, bool Negate, int Else) : CompiledStep(Offset);

// a LoopBack: true goes back to Target, false falls through.
internal sealed record class LoopBackStep(int Offset, PayloadNode Condition, bool Negate, int Target) : CompiledStep(Offset);

// a Jump or an Exit For/Do.
internal sealed record class JumpStep(int Offset, int Target) : CompiledStep(Offset);

// a For opener: the counter is given the start, and the body is skipped when it is past the end already.
internal sealed record class ForOpenStep(int Offset, int Loop, int Counter, ScalarKind Kind, PayloadNode Start, PayloadNode End, PayloadNode Step, int Past)
    : CompiledStep(Offset);

// a For's Next: the counter is given its value plus the step, and the body run again while it is not past the end.
internal sealed record class ForNextStep(int Offset, int Loop, int Counter, ScalarKind Kind, int Body) : CompiledStep(Offset);

// an Exit Sub, Exit Function, Exit Property.
internal sealed record class ExitStep(int Offset) : CompiledStep(Offset);
