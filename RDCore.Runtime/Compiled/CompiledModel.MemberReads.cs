using RDCore.Runtime.LanguageCore.Binding;

namespace RDCore.Runtime.Compiled;

// a scalar public variable of an object, read: the one numbered Field of the body's MemberFields, of the object an object variable holds (Receiver), an
// object field of a record holds (ReceiverField), or of the object of the With block opened at WithAt; of its kind (Kind). Where the object holds no such
// variable of the kind, or is Nothing, the instruction is the interpreter's, from its start.
internal sealed record class MemberFieldNode(int Field, int? Receiver, ScalarKind Kind) : PayloadNode(Kind)
{
    public int? WithAt { get; init; }

    public FieldPath? ReceiverField { get; init; }
}

// Err.Number: the number of the session's error, a Long, read where the session holds it (CompiledActivation.TryReadErrNumber).
internal sealed record class ErrNumberNode() : PayloadNode(ScalarKind.Long);

/// <summary>
/// A public variable of an object a compiled body reads: the member access as it is written, which the variable the object's class has for it is found
/// by, and the kind it is read in.
/// </summary>
/// <param name="Site">The member access.</param>
/// <param name="Kind">The kind of the variable, as the class the object is declared as declares it.</param>
internal sealed record class CompiledMemberField(BoundMember Site, ScalarKind Kind);
