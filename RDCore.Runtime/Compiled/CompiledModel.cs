using RDCore.Runtime.LanguageCore.Instructions;
using RDCore.SDK.Model.Source;

namespace RDCore.Runtime.Compiled;

/// <summary>
/// Why a procedure is not compiled: what in it compiled code does not do yet, and stays the interpreter's.
/// </summary>
public enum IneligibilityKind
{
    /// <summary>A member of a class: it is called on an object, which compiled code is not given.</summary>
    MemberOfAClass,

    /// <summary>
    /// A <c>ParamArray</c> parameter: its elements are the caller's variables, passed by reference, which compiled code holds none of. A scalar parameter
    /// passed by reference is compiled (IR lowering S2.3c).
    /// </summary>
    ParameterByReference,

    /// <summary>A parameter, a variable or a result of a type that is no scalar: compiled code holds only scalars, in locals.</summary>
    NotAScalar,

    /// <summary>A <c>Static</c> variable: it outlives the activation, and compiled code keeps its variables in locals that go with it.</summary>
    StaticVariable,

    /// <summary>An instruction compiled code does not do: a <c>With</c>, a <c>Select Case</c>, a <c>For Each</c>.</summary>
    Instruction,

    /// <summary>A statement compiled code does not do: a call, a <c>Set</c>, a file statement. Its detail is the statement's node.</summary>
    Statement,

    /// <summary>A <c>Let</c> of anything but a variable of the procedure or its function result: a member, an element, a field.</summary>
    AssignmentTarget,

    /// <summary>
    /// A <c>For</c> loop that counts with what is not a variable of the procedure, with a <c>Boolean</c>, or with a variable held in a cell - a parameter
    /// passed by reference, a variable the procedure passes by reference.
    /// </summary>
    ForCounter,

    /// <summary>A call: compiled code makes none, and a call is the interpreter's to make.</summary>
    Call,

    /// <summary>
    /// A variable read in an expression that also passes it by reference to a call, or that passes a parameter by reference that may be the same
    /// variable: the interpreter decides when the variable is read. Its detail is the variable.
    /// </summary>
    OperandPassedByReference,

    /// <summary>A member of an object: compiled code holds no object.</summary>
    Member,

    /// <summary>An element of an array, or a call of a default member: compiled code holds no array and no object.</summary>
    Element,

    /// <summary>A name bound late, which is resolved when it is evaluated.</summary>
    LateBound,

    /// <summary>An operand of a <c>Variant</c> or an object: its operation is planned for the value it holds, which compiled code does not know.</summary>
    VariantOperand,

    /// <summary>
    /// A variable that is not the procedure's - a field of the module, a <c>Static</c>, another procedure's: compiled code holds only the procedure's own
    /// variables, in locals.
    /// </summary>
    ForeignVariable,

    /// <summary>A literal that is no scalar - a <c>String</c>, <c>Nothing</c>, <c>Null</c>, <c>Empty</c>: compiled code holds only scalars.</summary>
    Literal,

    /// <summary>An operator compiled code does not do: <c>^</c>, <c>&amp;</c>, <c>Like</c>, <c>Is</c>; or an operation of no scalar type. Its detail is the operator.</summary>
    Operator,

    /// <summary>A <c>GoTo</c>, a line that is jumped to: compiled code jumps only where its loops and branches go.</summary>
    GoTo,

    /// <summary>
    /// Compiled code that could not be written, or that the runtime refuses to run: a defect of the compiler. Its detail is what the emitter or the runtime
    /// said.
    /// </summary>
    Refused,

    /// <summary>
    /// The session holds and evaluates its values boxed (<c>RDCORE_UNBOXED=0</c>): compiled code reads and writes the payloads of the procedure's scalar
    /// variables, and hands its activation to the interpreter in them, which only the unboxed evaluation keeps.
    /// </summary>
    BoxedEvaluation,

    /// <summary>
    /// An object made with <c>New</c> (MS-VBAL §5.6.10.3): the interpreter makes it, and runs its <c>Class_Initialize</c>. Its detail is the class.
    /// </summary>
    New,
}

/// <summary>
/// What keeps a procedure from being compiled, and where.
/// </summary>
/// <param name="Kind">What it is.</param>
/// <param name="Detail">What of it: the name of the parameter, the instruction, the expression.</param>
/// <param name="Location">Where it is written, when it is written somewhere.</param>
public sealed record class Ineligibility(IneligibilityKind Kind, string Detail, SourceLocation? Location = null);

/// <summary>
/// Whether a procedure runs compiled, and what keeps it in the interpreter when it does not: what <c>rdc compilation</c> reports.
/// </summary>
/// <param name="Procedure">The procedure.</param>
/// <param name="IsCompiled">Whether it is compiled.</param>
/// <param name="Reasons">What keeps it in the interpreter; empty when it is compiled.</param>
public sealed record class ProcedureCompilation(SDK.Model.Symbols.Abstract.VBTypeMemberSymbol Procedure, bool IsCompiled, IReadOnlyList<Ineligibility> Reasons)
{
    /// <summary>
    /// Compiles <paramref name="procedure"/> as its first call in <paramref name="image"/> would, and says what came of it.
    /// </summary>
    /// <param name="image">The program image the procedure's body is loaded in.</param>
    /// <param name="session">The session the image is the code of.</param>
    /// <param name="executor">The interpreter of the session's pipeline, whose evaluator binds the body's expressions.</param>
    /// <param name="procedure">The procedure.</param>
    /// <returns>
    /// <see langword="null"/> for a member that is no <c>Sub</c>, <c>Function</c> or <c>Property</c> procedure - an <c>Enum</c>, a <c>Type</c>, a
    /// <c>Const</c> - and for a procedure that has no body in the image.
    /// </returns>
    public static ProcedureCompilation? Of(Execution.ProgramImage image, SDK.Runtime.Abstract.Execution.IRuntimeSession session, Execution.ProcedureExecutor executor,
        SDK.Model.Symbols.Abstract.VBTypeMemberSymbol procedure)
    {
        if (!IsCompilable(procedure) || !image.TryGetValue(procedure.SemanticId, out var body))
        {
            return null;
        }

        var result = image.Compiler.Compile(session, executor.Expressions, procedure, body);
        return new ProcedureCompilation(procedure, result.Body is not null, result.Reasons) { Stepped = result.Stepped };
    }

    // a Sub, a Function or a Property Get: what compiled code runs, which an Enum, a Type or a Const is not.
    internal static bool IsCompilable(SDK.Model.Symbols.Abstract.VBTypeMemberSymbol procedure)
        => procedure is SDK.Model.Symbols.VBProject.VBProcedureMemberSymbol or SDK.Model.Symbols.VBProject.VBFunctionMemberSymbol
            or SDK.Model.Symbols.VBProject.VBPropertyGetMemberSymbol;

    /// <summary>
    /// What the interpreter runs of a compiled procedure, one instruction at a time, and why: each instruction compiled code does not do yet, which it hands
    /// to the interpreter alone, going on where the interpreter leaves the activation (IR lowering S2.5a). Empty for a procedure that is not compiled.
    /// </summary>
    public IReadOnlyList<Ineligibility> Stepped { get; init; } = [];
}

/// <summary>
/// A procedure compiled, or why it is not.
/// </summary>
/// <param name="Body">The compiled body; <see langword="null"/> when the procedure is not compiled.</param>
/// <param name="Reasons">Why it is not; empty when it is.</param>
/// <param name="Elapsed">How long compiling it took, or finding out it is not compiled.</param>
internal sealed record class CompilationResult(CompiledBody? Body, IReadOnlyList<Ineligibility> Reasons, TimeSpan Elapsed)
{
    /// <summary>The instructions of a compiled body the interpreter runs alone, and why (<see cref="ProcedureCompilation.Stepped"/>).</summary>
    public IReadOnlyList<Ineligibility> Stepped { get; init; } = [];
}

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

// PayloadOperations.Negate; of a constant expression, PayloadOperations.NegateConstant.
internal sealed record class NegateNode(PayloadNode Operand, ScalarKind Kind, bool OfAConstantExpression = false) : PayloadNode(Kind);

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

// Extended.Apply: Abs, Int, Fix, CDbl or CSng of an extended intermediate, by the key of the function.
internal sealed record class ExtendedApplyNode(string Function, ExtendedNode Argument) : ExtendedNode;

// the value of an expression of a Variant: a payload whose kind is the subtype it is of, which is known only when it is evaluated
// (VariantPayload).
internal abstract record class VariantNode;

// a Variant parameter or variable of the activation, read in the cell its frame holds it in, by the number of its slot.
internal sealed record class VariantSlotNode(int Slot) : VariantNode;

// a payload of a kind known before, as a Variant holds it: of its own subtype (MS-VBAL §5.5.1.2.12).
internal sealed record class VariantOfNode(PayloadNode Value) : VariantNode;

// an operation of a Variant operand (VariantOperations.Binary): planned for the subtypes of the payloads, as the interpreter plans it.
internal sealed record class VariantBinaryNode(string Token, VariantNode Left, VariantNode Right) : VariantNode;

// a negation or a Not of a Variant operand (VariantOperations.Unary).
internal sealed record class VariantUnaryNode(string Token, VariantNode Operand, bool OfAConstantExpression) : VariantNode;

// the function result variable of a function of the Variant type, in its own body (MS-VBAL §5.3.1): what its frame holds it as
// (CompiledActivation.TryReadResultVariant).
internal sealed record class VariantResultNode : VariantNode;

// a Variant's payload converted to a kind exactly (VariantOperations.Convert).
internal sealed record class UnvariantNode(VariantNode Value, ScalarKind Kind) : PayloadNode(Kind);

// whether a Variant's payload is not zero (VariantOperations.IsTrue): a Boolean, what a condition of it is.
internal sealed record class TruthNode(VariantNode Value) : PayloadNode(ScalarKind.Boolean);

// left Is right, of two object variables of the activation, by the slot each is held in; a slot of none is Nothing (ObjectOperations.Is, IsNothing).
internal sealed record class IsNode(int? Left, int? Right) : PayloadNode(ScalarKind.Boolean);

// TypeOf of an object variable of the activation, against the class the body knows by its number (CompiledActivation.TypeOf).
internal sealed record class TypeOfNode(int Slot, int Class) : PayloadNode(ScalarKind.Boolean);

// an expression of Strings, as RuntimeExpressionEvaluator.TryEvaluateText evaluates it: to the text it is, with no value made of it.
internal abstract record class TextNode;

// a String literal, a String constant, or Strings joined that are all literals: the text the value holds; null for a null string (vbNullString).
internal sealed record class TextConstantNode(string? Text) : TextNode;

// a String variable of the activation, by its slot: the text its cell holds (StringOperations.TryRead).
internal sealed record class TextSlotNode(int Slot) : TextNode;

// two Strings joined, by & or by + of two Strings (MS-VBAL §5.6.9.4, §5.6.5).
internal sealed record class ConcatNode(TextNode Left, TextNode Right) : TextNode;

// a Byte, an Integer, a Long or a LongLong let-coerced to String (MS-VBAL §5.5.1.2.4): its digits, after a minus sign when it is negative, which no
// regional setting changes (StringOperations.OfWhole).
internal sealed record class WholeTextNode(PayloadNode Value) : TextNode;

// Chr$ of a Byte, an Integer, a Long or a LongLong (MS-VBAL §6.1.2.11.1.4): the character the code names, as the library's Chr$ makes it
// (CompiledActivation.TryChr); a code it names none for is the interpreter's, which raises the error.
internal sealed record class ChrTextNode(PayloadNode Code) : TextNode;

// TypeName of an object (MS-VBAL §6.1.2.7.1.12): the object held in the object slot Slot (an object variable of the activation, or Me), or Nothing when
// there is none, given to the library's function numbered Library of the body (CompiledActivation.TryTypeNameOf), which names it as the interpreter's
// call does.
internal sealed record class TypeNameTextNode(int Library, int? Slot) : TextNode;

// a call of a function of the standard library that returns a String, each of whose parameters it gives an argument to is a String (Replace): the texts
// given to the library's function numbered Library of the body (CompiledActivation.TryCallLibraryText), and a parameter given none given what an omitted
// argument is.
internal sealed record class LibraryTextNode(int Library, IReadOnlyList<TextNode> Arguments) : TextNode;

// two Strings compared under the comparison mode of the module (MS-VBAL §5.6.9.5), as RuntimeExpressionEvaluator.TryCompareText compares them.
internal sealed record class TextCompareNode(ComparisonOperator Operator, TextNode Left, TextNode Right) : PayloadNode(ScalarKind.Boolean);

// the argument of a String parameter (CompiledCallee.Parameters, null): the text of an expression of Strings, given to a parameter passed by value, or to
// one passed by reference that is given no variable, of which the callee makes a cell of its own (MS-VBAL §5.3.1.11). Only an argument of a call; its
// Kind is not read.
internal sealed record class TextArgumentNode(TextNode Value) : PayloadNode(default(ScalarKind));

// the argument of a String parameter passed by reference that is a String variable of the activation (MS-VBAL §5.3.1.11): the variable's address, which
// the parameter is a second name for, as the interpreter gives it. Only an argument of a call; its Kind is not read.
internal sealed record class TextReferenceNode(int Slot) : PayloadNode(default(ScalarKind));

// the argument of an object parameter passed by value, or passed by reference and given no variable (CompiledCallee.ObjectParameters): the object held in
// the object slot Slot (an object variable of the activation, an object field it reaches, or Me), or Nothing when there is none; or what the call Call
// returns, made when the argument is evaluated. Only an argument of a call; its Kind is not read.
internal sealed record class ObjectArgumentNode(int? Slot) : PayloadNode(default(ScalarKind))
{
    public ObjectCallNode? Call { get; init; }
}

// the argument of an object parameter passed by reference that is an object variable of the activation of the parameter's very type (MS-VBAL §5.3.1.11):
// the variable's address, which the parameter is a second name for, as the interpreter gives it. Only an argument of a call; its Kind is not read.
internal sealed record class ObjectReferenceNode(int Slot) : PayloadNode(default(ScalarKind));

// an object parameter of a procedure a compiled body calls: the argument's expression, where an error Set-coercing what it gives is raised, and the
// parameter's declared type (Object, a class or an interface), which the object given is Set-coerced to. An optional parameter the call leaves out has no
// argument: it is given the default value of its type, Nothing, as the interpreter gives it (RuntimeExpressionEvaluator.OmittedArgument).
internal sealed record class ObjectParameter(SDK.Model.AST.Abstract.ExpressionNode? Argument, SDK.Model.Types.Abstract.VBType Type);

// an instruction of the body, compiled: each is at the offset of the instruction it is.
internal abstract record class CompiledStep(int Offset);

// a Let of a Variant variable of the activation: the payload written into its cell, of its own subtype - when the cell holds no object,
// which the interpreter's assignment lets go of.
internal sealed record class VariantLetStep(int Offset, int Slot, VariantNode Value) : CompiledStep(Offset);

// a Let of the function result variable of a function of the Variant type: the payload assigned where its frame holds it, as the interpreter assigns it
// (CompiledActivation.TryAssignResultVariant) - unless what it held was an object only it held, which the interpreter's assignment destroys.
internal sealed record class VariantResultLetStep(int Offset, VariantNode Value) : CompiledStep(Offset);

// the Next of a For whose counter is a Variant: stepped as the interpreter steps it on payloads, with the state its stepped For
// statement gave the frame (CompiledActivation.StepVariantFor); a step it declines is the interpreter's. Counter: the counter's Variant slot, whose
// speculated subtype the step is done in when it holds it (VariantSpeculation).
internal sealed record class VariantForNextStep(int Offset, int Opener, int Body, int Counter) : CompiledStep(Offset);

// a Let of a String variable of the activation - not a fixed-length one: the text written into its cell as it is (MS-VBAL §5.4.3.8).
internal sealed record class TextLetStep(int Offset, int Slot, TextNode Value) : CompiledStep(Offset);

// a Let of the function result variable of a function of the String type: the text assigned where its frame holds it, as the interpreter assigns it
// (CompiledActivation.TryAssignResultText).
internal sealed record class TextResultLetStep(int Offset, TextNode Value) : CompiledStep(Offset);

// a Debug.Print of Strings (MS-VBAL §5.4.5.8): the text of each item, all evaluated before any is written, written to the session's output as it is, and
// the line ended when the last item is followed by no separator (CompiledActivation.Print).
internal sealed record class PrintStep(int Offset, IReadOnlyList<TextNode> Items, bool EndsLine) : CompiledStep(Offset);

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

// an On Error statement: GoTo a label, Resume Next, GoTo 0, GoTo -1 (ProcedureExecutor.TryApplyOnError).
internal sealed record class OnErrorStep(int Offset) : CompiledStep(Offset);

// a GoSub: the offset after it pushed onto the activation's GoSub Resumption List, then a jump to Target (MS-VBAL §5.4.2.14).
internal sealed record class GoSubStep(int Offset, int Target) : CompiledStep(Offset);

// a Return: a jump to the offset popped off the GoSub Resumption List; an empty one is the interpreter's, which raises its error (MS-VBAL §5.4.2.15).
internal sealed record class ReturnStep(int Offset) : CompiledStep(Offset);

// an instruction the interpreter runs alone, and the activation goes on compiled wherever it leaves it: a Resume, an Error statement, an On...GoTo and an
// On...GoSub (CompiledActivation.Recover).
internal sealed record class SteppedStep(int Offset) : CompiledStep(Offset);

// a Select Case of a scalar selector (MS-VBAL §5.4.2.10): the selector evaluated once, into the local of the Select (Select, of CompiledBody.Selects).
internal sealed record class SelectStep(int Offset, int Select, PayloadNode Selector) : CompiledStep(Offset);

// a Case header of a Select Case of a scalar selector: its clauses compared in order, the first that matches falling through into its block; none, going to
// Else - the next Case, the Case Else, or past the Select.
internal sealed record class CaseStep(int Offset, IReadOnlyList<CaseClause> Clauses, int Else) : CompiledStep(Offset);

// a range clause of a Case: one comparison of the selector, or the two of a To range (Upper), which are both made.
internal sealed record class CaseClause(CompareNode Lower, CompareNode? Upper = null);

// the selector of a Select Case of a scalar selector, as its SelectStep evaluated it.
internal sealed record class SelectorNode(int Select, ScalarKind Kind) : PayloadNode(Kind);

// the argument of a parameter passed by reference that is a variable of the activation of the parameter's kind (MS-VBAL §5.3.1.11): the cell it is held
// in, which the parameter is a second name for. Only an argument of a call.
internal sealed record class ReferenceNode(int Slot, ScalarKind Kind) : PayloadNode(Kind);

// the argument of a parameter passed by reference that is no variable - a literal, an operation, an expression in parentheses (MS-VBAL §5.3.1.11): the
// value let-coerced to the parameter's kind, of which a cell of its own is made for a call made directly; the invoker is given the value, as the
// interpreter gives it. Only an argument of a call.
internal sealed record class TemporaryNode(PayloadNode Value) : PayloadNode(Value.Kind);

// a call of a procedure of the workspace (Callee, of CompiledBody.Callees), every argument a scalar passed by value, or the cell of one passed by reference
// (ReferenceNode, TemporaryNode): a call statement (Result is CallStep.NoResult), or the whole value a variable (Result its slot) or the function result
// variable (Result is -1) is assigned, of the callee's kind. Nothing of the instruction is evaluated after the call but that assignment, which can't fail:
// an instruction is handed to the interpreter at its start only before the call is made, which it would make again. A call of a member of an object has the
// slot of the object variable it is made on (Receiver), the object field of a record it is made on (ReceiverField), or the offset of the With statement
// whose block's object it is made on (WithAt).
internal sealed record class CallStep(int Offset, int Callee, IReadOnlyList<PayloadNode> Arguments, int Result, int? Receiver = null) : CompiledStep(Offset)
{
    public const int NoResult = -2;

    public int? WithAt { get; init; }

    public FieldPath? ReceiverField { get; init; }

    // the call whose object the member is called on: made first, inside the instruction (ObjectCallNode).
    public ObjectCallNode? ReceiverCall { get; init; }

    public bool IsOfAMember => Receiver is not null || WithAt is not null || ReceiverField is not null || ReceiverCall is not null;
}

// a call of a function of the workspace inside an expression, made as a CallStep makes one, with arguments as a CallStep's: what it returns is of the
// callee's kind (Kind). Once it is made, the instruction is handed to the interpreter in the middle of it, with what was evaluated of it known
// (Execution.Frames.KnownOperands): this call by where it is in its statement (Syntax), which the interpreter does not make again. A call of a member of
// an object is made on an object variable (Receiver), on an object field of a record (ReceiverField), or on the object of a With block (WithAt), as a
// CallStep's is.
internal sealed record class CallNode(int Callee, IReadOnlyList<PayloadNode> Arguments, int? Receiver, ScalarKind Kind, int Syntax) : PayloadNode(Kind)
{
    public int? WithAt { get; init; }

    public FieldPath? ReceiverField { get; init; }

    public ObjectCallNode? ReceiverCall { get; init; }
}

// a variable of the activation, or the function result variable, read in an instruction that calls a function inside an expression (CallNode): what was
// read is what the interpreter is given of it, by where it is in its statement (Syntax), when the instruction is handed to it after a call that may have
// changed the variable since.
internal sealed record class KnownNode(PayloadNode Value, int Syntax) : PayloadNode(Value.Kind);

// a call of a function of the standard library inside an expression (Library, of CompiledBody.Libraries), made as the interpreter makes it: each argument
// held in a Variant of its own subtype, as a Variant parameter passed by value is given it (MS-VBAL §5.3.1.11), and what it returns read as a payload of
// its kind (Kind). Syntax names the call in its statement, as a CallNode's does: once it is made, the interpreter is given what it returned, and does not
// make it again. It is -1 in an instruction that calls nothing else inside an expression, where the call is of a function that keeps nothing from call to
// call, which the interpreter makes again when it is handed the instruction.
internal sealed record class LibraryCallNode(int Library, IReadOnlyList<VariantNode> Arguments, ScalarKind Kind, int Syntax) : PayloadNode(Kind);

// a call of a function of the standard library that returns a Variant and keeps nothing from call to call (IIf), made as a LibraryCallNode is, each of its
// arguments evaluated first, and what it returns the Variant's payload, of its own subtype. The interpreter makes it again when it is handed the
// instruction, given what compiled code read and called for its arguments.
internal sealed record class LibraryVariantNode(int Library, IReadOnlyList<VariantNode> Arguments) : VariantNode;

/// <summary>
/// A function of the standard library a compiled body calls inside an expression (<see cref="LibraryCallNode"/>, <see cref="LibraryVariantNode"/>,
/// <see cref="TypeNameTextNode"/>, <see cref="LibraryTextNode"/>).
/// </summary>
/// <param name="Function">The function, as the call was bound to it.</param>
/// <param name="Parameters">
/// Its parameters: each a <c>Variant</c> passed by value, or a <c>String</c> for a <see cref="LibraryTextNode"/>. One the call gives no argument is given
/// what an omitted one is.
/// </param>
/// <param name="Site">Where the call is written, which the activation is located at while the function runs, as the interpreter's is.</param>
internal sealed record class CompiledLibraryCall(SDK.Model.Symbols.Abstract.VBTypeMemberSymbol Function,
    System.Collections.Immutable.ImmutableArray<SDK.Model.Symbols.VBProject.VBParameterSymbol> Parameters, SDK.Model.Source.SourceLocation Site);

/// <summary>
/// A procedure a compiled body calls: the symbol, and the kinds of its parameters and of what it returns.
/// </summary>
/// <param name="Procedure">The procedure, as the call was bound to it.</param>
/// <param name="Parameters">
/// The kind of each parameter, a scalar; <see langword="null"/> for a <c>String</c>, whose argument is its text or its cell
/// (<see cref="TextArgumentNode"/>, <see cref="TextReferenceNode"/>), and for an object, whose argument is the object or the variable's address
/// (<see cref="ObjectArgumentNode"/>, <see cref="ObjectReferenceNode"/>).
/// </param>
/// <param name="Result">The kind of what it returns; <see langword="null"/> for a <c>Sub</c>, or a call whose result is not read.</param>
/// <param name="ByReference">Whether each parameter is passed by reference: its argument is then a cell (<see cref="ReferenceNode"/>, <see cref="TemporaryNode"/>).</param>
internal sealed record class CompiledCallee(SDK.Model.Symbols.Abstract.VBTypeMemberSymbol Procedure, IReadOnlyList<ScalarKind?> Parameters, ScalarKind? Result,
    IReadOnlyList<bool> ByReference)
{
    /// <summary>
    /// Whether the call is made with the arguments as the interpreter gives them (<c>RuntimeProcedureInvoker.Invoke</c>): any parameter is passed by
    /// reference, or is a <c>String</c> or an object.
    /// </summary>
    public bool TakesReferences { get; } = ByReference.Contains(true) || Parameters.Contains(null);

    /// <summary>
    /// Whether what the call returns - a <c>Variant</c>, or a scalar - is assigned by the statement that makes it, as the interpreter assigns the value of its
    /// expression (<c>StatementRuntimeSemanticsProvider.AssignValue</c>): to a <c>Variant</c>, whatever it holds, once the call returned. <see cref="Result"/>
    /// is then <see langword="null"/>.
    /// </summary>
    public bool AssignsResult { get; init; }

    /// <summary>
    /// Whether what the call returns is held as a temporary of the statement that makes it until the statement is over (<c>ObjectReferences.HoldTemporarily</c>):
    /// a <c>Variant</c> its statement discards, or a result its statement assigns. The statement is begun before the call and ended after it
    /// (<see cref="CompiledActivation.BeginStatement"/>, <see cref="CompiledActivation.EndStatement"/>), as the interpreter begins and ends it.
    /// </summary>
    public bool HoldsWhatItReturns { get; init; }

    /// <summary>
    /// Whether the call is of a function or a <c>Property Get</c> that returns an object, which compiled code calls a member of, or reads a public variable
    /// of (<see cref="ObjectCallNode"/>): the call is made through the invoker, and what it returns held as a temporary of the statement, which is begun before
    /// its first call and ended once it is over. <see cref="Result"/> is then <see langword="null"/>.
    /// </summary>
    public bool ReturnsObject { get; init; }

    /// <summary>
    /// For each parameter, the object parameter it is, whose <see cref="Parameters"/> kind is <see langword="null"/>; <see langword="null"/> for any other.
    /// Empty when the call has no object parameter.
    /// </summary>
    public IReadOnlyList<ObjectParameter?> ObjectParameters { get; init; } = [];

    /// <summary>
    /// The member access a call of a member of an object was bound from, whose <see cref="Procedure"/> is the member of the class the object is declared as;
    /// <see langword="null"/> for a call of a procedure of a standard module. Which member the call invokes is the object's class's to say, each time.
    /// </summary>
    public LanguageCore.Binding.BoundMember? Site { get; init; }

    /// <summary>
    /// Whether the call is of a member of the class whose code runs, named bare (MS-VBAL §5.6.10). The call is of <see cref="Procedure"/> itself, made on
    /// the activation's own <c>Me</c> as a call of a member of an object is made (<see cref="CompiledActivation.CanCallMember"/>,
    /// <see cref="CompiledActivation.CallMember"/>). <see cref="Site"/> is then <see langword="null"/>.
    /// </summary>
    public bool CallsOwnMember { get; init; }

    /// <summary>
    /// Where the call expression is: what the activation that makes the call is located at while the callee runs (<c>CallStackFrame.EnterCall</c>), as the
    /// interpreter locates it.
    /// </summary>
    public SourceLocation? CallLocation { get; init; }

    /// <summary>
    /// The member other than <see cref="Procedure"/> the call last invoked, of an object of another class, found to be called as
    /// <see cref="Procedure"/> is (<see cref="CompiledActivation.CanCallMember"/>): one implementing it, of an object declared as an interface.
    /// </summary>
    public SDK.Model.Symbols.Abstract.VBTypeMemberSymbol? Implementation { get; set; }

    /// <summary>
    /// The compiled member the call last entered directly (<see cref="CompiledActivation.EnterMemberDirectly"/>).
    /// </summary>
    public MemberEntry? Entered { get; set; }
}

/// <summary>
/// A member of a class a compiled call of a member of an object enters directly: its body, compiled, and its typed entry, which the
/// caller's IL calls through <paramref name="Entry"/>, a pointer to it (the member is the object's class's to say, each time), with the arguments as it
/// holds them.
/// </summary>
/// <param name="Member">The member of the class.</param>
/// <param name="List">The member's lowered body, the one the invoker runs.</param>
/// <param name="Body">The member's compiled body.</param>
/// <param name="Entry">A pointer to the compiled body's typed entry.</param>
/// <param name="Context">The context the member's code is evaluated in.</param>
internal sealed record class MemberEntry(SDK.Model.Symbols.Abstract.VBTypeMemberSymbol Member, RDCore.Runtime.LanguageCore.Instructions.InstructionList List, CompiledBody Body,
    nint Entry, Semantics.RuntimeEvaluationContext Context)
{
}

/// <summary>
/// The cells of the fields of an object a body found (<see cref="CompiledBody.ObjectFields"/>), by the number compiled code knows each
/// field by: each found as it is first asked for, and looked for again while it is none (the object holds the field in no cell of its type). The cells keep the
/// object's identity, which no other object is ever given, and hold no reference to it: a body keeps no object alive once nothing else does. A live object keeps the cell
/// of each of its fields: what is assigned to a field, a <c>ReDim</c> of it included, is written in that cell, and a change to its class compiles the body
/// again, with none found.
/// </summary>
/// <param name="Object">The object's identity.</param>
/// <param name="Cells">The cells, by number.</param>
internal sealed record class FieldCells(SDK.Model.Values.Runtime.VBRuntimeObjectId Object, object?[] Cells)
{
}

/// <summary>
/// The cells of the variables of the session a body found (<see cref="CompiledBody.SessionFields"/>), by the number compiled code knows
/// each field by, as <see cref="FieldCells"/> are an object's. The storage is known by a weak reference: a body is shared by every session composed over the
/// same scopes, and keeps none of them alive.
/// </summary>
/// <param name="Storage">The storage they were found in.</param>
/// <param name="Generation">What the storage bound when they were found: a cell is the variable's for as long as that has not changed.</param>
/// <param name="Cells">The cells, by number.</param>
internal sealed record class SessionFieldCells(WeakReference<Execution.Memory.SessionStorage> Storage, int Generation, object?[] Cells)
{
}

/// <summary>
/// Where a field compiled code reaches is held (<see cref="ProcedureAnalysis.Fields"/>): what its cell is found in, and cached by.
/// </summary>
internal enum FieldScope
{
    /// <summary>
    /// On the object the activation is a call on, <c>Me</c>: a variable of its class module, of object extent (MS-VBAL §2.3), or a <c>Static</c> variable of
    /// a member of the class, which each object holds its own of, as in MS-VBA.
    /// </summary>
    Object,

    /// <summary>
    /// In the session, which no activation and no object holds: a variable of a standard module, or a <c>Static</c> variable of a procedure of one, both
    /// of module extent (MS-VBAL §2.3).
    /// </summary>
    Session,
}

/// <summary>
/// A field compiled code reaches: a variable of the activation's object or of the session, held in a cell compiled code finds where the
/// interpreter finds it, by the number it knows the field by.
/// </summary>
/// <param name="Slot">The slot compiled code numbers it by, past the slots of the procedure's layout.</param>
/// <param name="Symbol">The variable.</param>
/// <param name="Scope">Where its cell is held.</param>
internal sealed record class CompiledField(int Slot, SDK.Model.Symbols.Abstract.Symbol Symbol, FieldScope Scope)
{
}

/// <summary>
/// A call compiled code makes of a procedure that is compiled too (IR lowering S2.3b): the callee's typed entry, which the caller's IL calls with the
/// arguments as they are, and what its activation is made of.
/// </summary>
/// <remarks>
/// The callee is compiled for the scopes its caller is, before the caller: the call is made this way while the session's scopes are still those
/// (<see cref="CompiledActivation.EnterDirectly"/>), and through the invoker otherwise. A procedure that calls itself calls its own entry, which is not
/// created yet when the call is written: its body is known once it is (<see cref="Body"/>).
/// </remarks>
/// <param name="Entry">The callee's typed entry: its compiled activation, then each parameter as a <see cref="long"/> or a <see cref="double"/>.</param>
/// <param name="Procedure">The callee.</param>
/// <param name="List">
/// The callee's instructions, which its activation is of: the call is made this way only while they are the callee's body still, and not after its module
/// was loaded again.
/// </param>
/// <remarks>Nothing of a session is held: the body is shared by every session composed over the same scopes, each of which makes the activations itself.</remarks>
internal sealed class DirectCall(System.Reflection.MethodInfo Entry, SDK.Model.Symbols.Abstract.VBTypeMemberSymbol Procedure, RDCore.Runtime.LanguageCore.Instructions.InstructionList List)
{
    public System.Reflection.MethodInfo Entry { get; } = Entry;
    public SDK.Model.Symbols.Abstract.VBTypeMemberSymbol Procedure { get; } = Procedure;
    public RDCore.Runtime.LanguageCore.Instructions.InstructionList List { get; } = List;

    /// <summary>The callee's compiled body; set once it is created, for a procedure that calls itself.</summary>
    public CompiledBody? Body { get; set; }

    /// <summary>The context the callee's code is evaluated in, the same for each of its activations.</summary>
    public Semantics.RuntimeEvaluationContext Context { get; } = new(Procedure.Uri);
}
