namespace RDCore.Runtime.Compiled;

// the nodes and steps of arrays: an array parameter or variable of the activation is held in the cell its frame holds it in, by the
// number of its slot (Array), and an element is found in the array that cell holds each time it is read or written (ArrayOperations).

// what the elements of an array variable are, as compiled code reads them: a payload of a scalar kind, a Variant, an object; or none of these - a String, a
// record - whose array compiled code reads only the bounds of.
internal enum ElementClass
{
    Scalar,
    Variant,
    Object,
    Other,
}

// an array parameter or variable of the activation, and its elements: of Kind when they are scalars. An array field of a record (Field) is numbered apart
// from the slots, by the complement of its place among the body's array fields (ProcedureAnalysis.FieldArrays), and found through its record each time.
internal sealed record class CompiledArray(int Slot, ElementClass Class, ScalarKind? Kind)
{
    public FieldPath? Field { get; init; }
}

// an element of an array of a scalar kind, at subscripts each let-coerced to a Long (MS-VBAL §5.6.13), read in the cell the array holds it in
// (ArrayOperations.ScalarElement).
internal sealed record class ElementNode(int Array, IReadOnlyList<PayloadNode> Subscripts, ScalarKind Kind) : PayloadNode(Kind);

// LBound or UBound of an array variable (MS-VBAL §3.3.5.2): of the dimension a Long says, the first when it is omitted (ArrayOperations.Bound).
internal sealed record class ArrayBoundNode(int Array, bool Upper, PayloadNode? Dimension) : PayloadNode(ScalarKind.Long);

// an element of an array of Variants, read in the Variant's cell the array holds it in (ArrayOperations.VariantElement).
internal sealed record class VariantElementNode(int Array, IReadOnlyList<PayloadNode> Subscripts) : VariantNode;

// an operand of Is or TypeOf whose reference compiled code reads, where an element of an array of objects is one, and an object field of a record
// (ObjectFieldNode) another.
internal abstract record class ObjectOperand;

// an object variable of the activation, by its slot.
internal sealed record class ObjectSlotOperand(int Slot) : ObjectOperand;

// an element of an array of objects (ArrayOperations.ObjectElement).
internal sealed record class ObjectElementNode(int Array, IReadOnlyList<PayloadNode> Subscripts) : ObjectOperand;

// left Is right, where either is an element of an array of objects; an operand that is none is Nothing (ObjectOperations.Is, IsNothing).
internal sealed record class ElementIsNode(ObjectOperand? Left, ObjectOperand? Right) : PayloadNode(ScalarKind.Boolean);

// TypeOf of an element of an array of objects, or of an object field of a record, against the class the body knows by its number.
internal sealed record class ElementTypeOfNode(ObjectOperand Element, int Class) : PayloadNode(ScalarKind.Boolean);

// a Let of an element of an array of a scalar kind: its cell found first, as the interpreter finds it before it evaluates the value
// (RuntimeExpressionEvaluator.TryAssignUnboxed), then written a payload of its kind.
internal sealed record class ElementLetStep(int Offset, ElementNode Element, PayloadNode Value) : CompiledStep(Offset);

// a Let of an element of an array of Variants, written as a VariantLetStep writes a Variant's cell: not over an object.
internal sealed record class VariantElementLetStep(int Offset, VariantElementNode Element, VariantNode Value) : CompiledStep(Offset);

// a ReDim or an Erase of arrays of a scalar kind of the activation (MS-VBAL §5.4.3.3-4), each named by its slot: the interpreter's statement run as a support
// call (CompiledActivation.RunArrayStatement), its bounds read from the frame, and the cell of each array found again after it.
internal sealed record class ArrayStatementStep(int Offset, IReadOnlyList<int> Arrays) : CompiledStep(Offset);
