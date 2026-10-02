namespace RDCore.SDK.Model.Types;

/// <summary>
/// The type a declaration names, when the name did not resolve to one: an <see cref="VBUnknownType"/> that remembers the name.
/// </summary>
/// <remarks>
/// An unknown type is a type that is not known <em>yet</em> - presumably a valid one - and says nothing of why. A name that does not resolve once everything the
/// declaration can see is defined is an illegal one, and an error needs the name to say so. It is an <see cref="VBUnknownType"/> in every other respect, so whatever
/// is written to tolerate the one tolerates the other.
/// </remarks>
/// <param name="DeclaredName">The name as it was written, qualified by the project it was written with, if any.</param>
public sealed record class VBUnresolvedType(string DeclaredName) : VBUnknownType;
