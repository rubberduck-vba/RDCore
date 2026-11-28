using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Intrinsic;
using System.Collections.Immutable;

namespace RDCore.Runtime.Compiled;

// a field of a record of the activation: the slot of the record's holder - the cell its frame holds the record in - and the field's
// position in declaration order at each level, a record's field that is a record itself first. Root is the type the holder's record is declared as.
internal sealed record class FieldPath(int Holder, ImmutableArray<int> Indexes, VBUserDefinedType Root)
{
    // the field's place in the holder's record, which a field of a record it nests is read and written at in the same block, with no view made of the
    // records between (VBUserDefinedTypeValue.TryReadBytesField); -1 for a path that names no field of Root.
    public int Place => RecordShape.Of(Root).PlaceOf(Indexes.AsSpan());
}

// a field of a record of a scalar type, read where the record holds it (RecordOperations).
internal sealed record class FieldNode(FieldPath Field, ScalarKind Kind) : PayloadNode(Kind);

// a Variant field of a record, read where the record holds it.
internal sealed record class VariantFieldNode(FieldPath Field) : VariantNode;

// a String field of a record of variable length, read where the record holds it (RecordOperations.ReadText).
internal sealed record class FieldTextNode(FieldPath Field) : TextNode;

// an object field of a record, as an operand of Is, TypeOf or a Set: the cell the record holds its object in (RecordOperations.ObjectField).
internal sealed record class ObjectFieldNode(FieldPath Field) : ObjectOperand;

// a Set of an object field of a record to the object Source holds, or to Nothing when it is none (CompiledActivation.TrySetField).
internal sealed record class FieldSetStep(int Offset, FieldPath Field, ObjectOperand? Source) : CompiledStep(Offset);

// a Set of Target - an object slot, or the function result variable (SetStep.Result) - to the object an object field of a record holds, as a SetStep Sets it
// to a variable's.
internal sealed record class FieldSourceSetStep(int Offset, int Target, FieldPath Source) : CompiledStep(Offset);

// a Let of a field of a record of a scalar type: the value, let-coerced to its kind, written where the record holds it.
internal sealed record class FieldLetStep(int Offset, FieldPath Field, PayloadNode Value) : CompiledStep(Offset);

// a Let of a Variant field of a record: the value, of its own subtype, written where the record holds it.
internal sealed record class VariantFieldLetStep(int Offset, FieldPath Field, VariantNode Value) : CompiledStep(Offset);

// a Let of a String field of a record of variable length: the text, as it is, written where the record holds it (RecordOperations.WriteText).
internal sealed record class FieldTextLetStep(int Offset, FieldPath Field, TextNode Value) : CompiledStep(Offset);
