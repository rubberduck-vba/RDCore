namespace RDCore.SDK.Runtime.Abstract.StdLib;

/// <summary>
/// Which kind of VBA member a standard-library declaration declares, where the signature alone does
/// not say.
/// </summary>
/// <remarks>
/// A declaration's <em>return type</em> is stated by its signature - a
/// <see cref="Shared.RuntimeSemanticsEvaluationResult{TValue}"/> names one, a
/// <see cref="Shared.RuntimeSemanticsEvaluationResult"/> names none - so <c>Sub</c> and
/// <c>Function</c> need not be distinguished here. Nothing in a signature distinguishes a
/// <c>Property Get</c> from a <c>Function</c>, or a <c>Property Let</c> from a <c>Sub</c>, which is
/// what this is for.
/// </remarks>
public enum StdLibMemberKind
{
    /// <summary>
    /// A <c>Sub</c> or a <c>Function</c>, according to whether the signature states a return type.
    /// </summary>
    Procedure,

    /// <summary>
    /// A <c>Property Get</c> accessor. Its signature states a return type.
    /// </summary>
    PropertyGet,

    /// <summary>
    /// A <c>Property Let</c> accessor: the value-assignment half of a property, whose last parameter
    /// is the assigned value.
    /// </summary>
    PropertyLet,

    /// <summary>
    /// A <c>Property Set</c> accessor: the reference-assignment half of a property, whose last
    /// parameter is the assigned object reference.
    /// </summary>
    PropertySet,
}
