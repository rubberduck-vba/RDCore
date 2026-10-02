using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;

namespace RDCore.SDK.Semantics;

/// <summary>
/// What a declaration declares.
/// </summary>
public enum DeclarationKind
{
    /// <summary>
    /// A variable: a local, or a field of a module.
    /// </summary>
    Variable,

    /// <summary>
    /// A parameter of a procedure.
    /// </summary>
    Parameter,

    /// <summary>
    /// A constant.
    /// </summary>
    Constant,

    /// <summary>
    /// A <c>Sub</c> or a <c>Function</c>.
    /// </summary>
    Procedure,

    /// <summary>
    /// A <c>Property</c>, which its <c>Get</c>, <c>Let</c> and <c>Set</c> accessors declare as one.
    /// </summary>
    Property,

    /// <summary>
    /// An <c>Event</c>.
    /// </summary>
    Event,
}

/// <summary>
/// Every reference to a declaration there is, counted by what the reference does.
/// </summary>
/// <remarks>
/// These are the references, all of them: a count is only ever stated for a declaration that nothing outside the code the pass analyzed can refer to
/// (<see cref="DeclarationFact.References"/>). An expression that is passed as an argument is a reference of its own kind, because what it does is not
/// what the expression says: the procedure it is passed to may read it, write to it, or both, when its parameter is <c>ByRef</c>.
/// </remarks>
/// <param name="Reads">How many expressions refer to it for its value, or to call it.</param>
/// <param name="Writes">How many expressions write to it (<see cref="Flags.ValueExpressionSemanticFlags.AssignmentTarget"/>).</param>
/// <param name="PassedAsArguments">How many expressions pass it as an argument of a call (<see cref="Flags.ValueExpressionSemanticFlags.PassedAsArgument"/>).</param>
public readonly record struct DeclarationReferences(int Reads, int Writes, int PassedAsArguments)
{
    /// <summary>
    /// The number of references of any kind.
    /// </summary>
    public int Total => Reads + Writes + PassedAsArguments;
}

/// <summary>
/// What the static pass found out about a declaration.
/// </summary>
/// <remarks>
/// A fact is stated only when it is true. What refers to a declaration that is accessible from outside the module, or that is called by convention or through
/// the object it is a member of rather than by an expression that names it (an event handler, a member that implements an interface), is not all in the code
/// the pass analyzed, and its references are not counted: <see cref="References"/> is then <see langword="null"/>, which says the references are not known
/// and not that there are none.
/// </remarks>
/// <param name="Symbol">The identity of the declared symbol.</param>
/// <param name="Name">The name it is declared with.</param>
/// <param name="Kind">What it declares.</param>
/// <param name="Access">Who can refer to it besides the module's own code.</param>
/// <param name="IsImplicit">Whether it was never declared: it came into being because something referred to it, which is legal and worth reporting.</param>
/// <param name="Location">Where it is declared.</param>
/// <param name="References">
/// Every reference to it, when every reference to it is in the code that was analyzed and was analyzed completely; <see langword="null"/> otherwise.
/// </param>
public sealed record class DeclarationFact(
    SemanticId Symbol,
    string Name,
    DeclarationKind Kind,
    AccessModifier Access,
    bool IsImplicit,
    SourceLocation Location,
    DeclarationReferences? References);
