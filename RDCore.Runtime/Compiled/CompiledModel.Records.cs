using System.Collections.Immutable;

namespace RDCore.Runtime.Compiled;

// a field of a record of the activation: the slot of the record's holder - the cell its frame holds the record in - and the field's
// position in declaration order at each level, a record's field that is a record itself first.
internal sealed record class FieldPath(int Holder, ImmutableArray<int> Indexes);

// a field of a record of a scalar type, read where the record holds it (RecordOperations).
internal sealed record class FieldNode(FieldPath Field, ScalarKind Kind) : PayloadNode(Kind);

// a Variant field of a record, read where the record holds it.
internal sealed record class VariantFieldNode(FieldPath Field) : VariantNode;

// an object field of a record, as an operand of Is, TypeOf or a Set: the cell the record holds its object in (RecordOperations.ObjectField).
internal sealed record class ObjectFieldNode(FieldPath Field) : ObjectOperand;

// a Set of an object field of a record to the object Source holds, or to Nothing when it is none (CompiledActivation.TrySetField).
internal sealed record class FieldSetStep(int Offset, FieldPath Field, ObjectOperand? Source) : CompiledStep(Offset);

// a Let of a field of a record of a scalar type: the value, let-coerced to its kind, written where the record holds it.
internal sealed record class FieldLetStep(int Offset, FieldPath Field, PayloadNode Value) : CompiledStep(Offset);

// a Let of a Variant field of a record: the value, of its own subtype, written where the record holds it.
internal sealed record class VariantFieldLetStep(int Offset, FieldPath Field, VariantNode Value) : CompiledStep(Offset);
