namespace RDCore.SDK.Runtime.Abstract.StdLib;

/// <summary>
/// Declares what a standard-library member looks like to VBA source, where its C# signature cannot
/// say so on its own.
/// </summary>
/// <remarks>
/// Everything this attribute carries has a default drawn from the signature, so it is needed only
/// where the signature and the VBA declaration genuinely diverge - a name C# cannot spell, an
/// accessor kind a signature cannot express, or a return type that is a class or an enumeration
/// rather than an intrinsic. A member without it is a <c>Sub</c> or <c>Function</c> of the same name.
/// </remarks>
/// <param name="name">
/// The member's name in VBA source, when it differs from the name of the method declaring it.
/// </param>
[AttributeUsage(AttributeTargets.Method)]
public sealed class StdLibMemberAttribute(string? name = null) : Attribute
{
    /// <summary>
    /// The member's name in VBA source, or <c>null</c> to use the name of the method declaring it.
    /// </summary>
    /// <remarks>
    /// Needed where a VBA name is not a C# identifier, or where two members differ only in ways C#
    /// overload resolution cannot see. Both happen at once in the <c>$</c>-suffixed pairs
    /// (<strong>MS-VBAL §6.1.2.3.1</strong>): <c>Hex</c> and <c>Hex$</c> take the same argument and
    /// differ only in return type, so they are two differently-named methods, one of which says here
    /// what it is really called.
    /// </remarks>
    public string? Name { get; } = name;

    /// <summary>
    /// Which kind of member this is, for the kinds a signature cannot express.
    /// <see cref="StdLibMemberKind.Procedure"/> by default.
    /// </summary>
    public StdLibMemberKind Kind { get; init; } = StdLibMemberKind.Procedure;

    /// <summary>
    /// The type VBA source sees this member return, when that is a standard-library class or
    /// enumeration rather than an intrinsic type.
    /// </summary>
    /// <remarks>
    /// Either an interface carrying <see cref="StdLibClassAttribute"/> - the class whose instance is
    /// returned - or an enumeration carrying <see cref="StdLibEnumAttribute"/>. The signature's own
    /// <see cref="Shared.RuntimeSemanticsEvaluationResult{TValue}"/> still states the value an
    /// implementation produces, which for these is a
    /// <see cref="Model.Values.Intrinsic.VBObjectValue"/> and a
    /// <see cref="Model.Values.Intrinsic.VBLongValue"/> respectively; this states what the declared
    /// type is called. <c>null</c> - the default - takes the declared type from the signature.
    /// </remarks>
    public Type? ReturnType { get; init; }
}
