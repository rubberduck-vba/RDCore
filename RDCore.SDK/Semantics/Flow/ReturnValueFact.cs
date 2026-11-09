using RDCore.SDK.Model.Source;

namespace RDCore.SDK.Semantics.Flow;

/// <summary>
/// On how many of the code paths of a function or a property getter its return value is assigned.
/// </summary>
public enum ReturnValueAssignment
{
    /// <summary>
    /// Every code path that ends the activation has assigned the return value.
    /// </summary>
    Always,

    /// <summary>
    /// The return value is assigned on some code paths, and there is one that ends the activation without it.
    /// </summary>
    Sometimes,

    /// <summary>
    /// No code path assigns the return value.
    /// </summary>
    Never,
}

/// <summary>
/// What the semantic analysis pass found out about the return value of a function or a property getter: whether the code assigns it
/// (the function result variable, <strong>MS-VBAL §5.3.1</strong>).
/// </summary>
/// <remarks>
/// A function whose return value is not assigned on a code path returns the default value of its declared type on it: <c>0</c>, an empty string, <c>Empty</c> for a
/// <c>Variant</c>, <c>Nothing</c> for an object. Whether that is a mistake is for an analyzer to say. The fact is stated only for a procedure whose code paths the
/// pass could tell exactly (<c>ReturnValueAnalysis</c>), and is <see langword="null"/> on the model of the others.
/// </remarks>
/// <param name="Assignment">On how many code paths the return value is assigned.</param>
/// <param name="Name">The name of the procedure.</param>
/// <param name="Location">Where the name of the procedure is written.</param>
/// <param name="IsEmpty">
/// Whether the body has no statement: such a procedure declares what it is called with and does nothing, which is how a member of an interface is written.
/// </param>
public sealed record class ReturnValueFact(ReturnValueAssignment Assignment, string Name, SourceLocation Location, bool IsEmpty);
