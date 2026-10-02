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
/// What the static pass found out about how a declaration is used, by the code of the module that declares it.
/// </summary>
/// <remarks>
/// A fact is a count of what was found, not a verdict: a <c>Public</c> declaration that nothing in its own module refers to may be used by another module, which
/// this model does not see; whether that is worth a diagnostic, and for which declarations, is for an analyzer to say.
/// </remarks>
/// <param name="Symbol">The identity of the declared symbol.</param>
/// <param name="Name">The name it is declared with.</param>
/// <param name="Kind">What it declares.</param>
/// <param name="Access">Who can refer to it besides the module's own code.</param>
/// <param name="IsImplicit">Whether it was never declared: it came into being because something referred to it, which is legal and worth reporting.</param>
/// <param name="Location">Where it is declared.</param>
/// <param name="Reads">How many expressions of the module refer to it for its value, or to call it.</param>
/// <param name="Writes">How many expressions of the module write to it (<see cref="Flags.ValueExpressionSemanticFlags.AssignmentTarget"/>).</param>
public sealed record class DeclarationFact(
    SemanticId Symbol,
    string Name,
    DeclarationKind Kind,
    AccessModifier Access,
    bool IsImplicit,
    SourceLocation Location,
    int Reads,
    int Writes)
{
    /// <summary>
    /// Whether nothing in the module refers to it at all.
    /// </summary>
    public bool IsUnreferenced => Reads == 0 && Writes == 0;

    /// <summary>
    /// Whether it is read, and never written to: a variable that is never assigned holds the default of its type wherever it is read.
    /// </summary>
    public bool IsNeverAssigned => Kind is DeclarationKind.Variable && Reads > 0 && Writes == 0;
}
