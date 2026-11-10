using RDCore.Runtime.LanguageCore.Binding;

namespace RDCore.Runtime.Compiled;

// a scalar public variable of an object, read: the one numbered Field of the body's MemberFields, of the object an object variable holds (Receiver), an
// object field of a record holds (ReceiverField), of the object of the With block opened at WithAt, or of the object a call returns (ReceiverCall); of its
// kind (Kind). Where the object holds no such variable of the kind, or is Nothing, the instruction is the interpreter's, from its start, or from where what
// was evaluated of it is known.
internal sealed record class MemberFieldNode(int Field, int? Receiver, ScalarKind Kind) : PayloadNode(Kind)
{
    public int? WithAt { get; init; }

    public FieldPath? ReceiverField { get; init; }

    public ObjectCallNode? ReceiverCall { get; init; }
}

// a String public variable of variable length of an object, read: the one numbered Field of the body's MemberFields, of the object a receiver holds, as
// a MemberFieldNode reads a scalar one.
internal sealed record class MemberTextNode(int Field, int? Receiver) : TextNode
{
    public int? WithAt { get; init; }

    public FieldPath? ReceiverField { get; init; }
}

// Err.Number: the number of the session's error, a Long, read where the session holds it (CompiledActivation.TryReadErrNumber).
internal sealed record class ErrNumberNode() : PayloadNode(ScalarKind.Long);

// Err.Description, or Err.Source: the text of the session's error, read where the session holds it (CompiledActivation.TryReadErrText).
internal sealed record class ErrTextNode(bool Source) : TextNode;

/// <summary>
/// A public variable of an object a compiled body reads: the member access as it is written, which the variable the object's class has for it is found
/// by, and the kind it is read in.
/// </summary>
/// <param name="Site">The member access.</param>
/// <param name="Kind">The kind of the variable, as the class the object is declared as declares it.</param>
internal sealed record class CompiledMemberField(BoundMember Site, ScalarKind Kind);
