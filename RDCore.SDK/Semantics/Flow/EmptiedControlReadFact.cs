using RDCore.SDK.Model.Source;

namespace RDCore.SDK.Semantics.Flow;

/// <summary>
/// A read of the control variable of a <c>For Each</c> loop that every code path reaches only through the loop running out, with nothing assigned to the
/// variable since (<strong>MS-VBAL §5.4.2.4</strong>).
/// </summary>
/// <remarks>
/// MS-VBAL says that once the loop has finished, the control variable holds the last element. MS-VBA empties it instead when the collection runs out, or has no
/// element at all, and RD-VBA does the same (<strong>RD-VBAL §5.4.2.4</strong>): what the read finds is <c>Empty</c>, or <c>Nothing</c> for an object variable,
/// where the specification says the last element.
/// </remarks>
/// <param name="Name">The name of the control variable, as the read writes it.</param>
/// <param name="Location">Where the read is written.</param>
/// <param name="IsObject">Whether the variable is declared as an object, which the loop leaves <c>Nothing</c>; <c>Empty</c> otherwise.</param>
public sealed record class EmptiedControlReadFact(string Name, SourceLocation Location, bool IsObject);
