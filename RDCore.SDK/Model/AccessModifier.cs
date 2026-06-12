namespace RDCore.SDK.Model;

/// <summary>
/// Defines the possible <em>access modifier</em> values that can be attached to an <c>AccessibleTypedSymbol</c>.
/// </summary>
public enum AccessModifier
{
    /// <summary>
    /// The symbol has implicit (unspecified in source code) accessibility semantics.
    /// </summary>
    Implicit = 0,
    /// <summary>
    /// The symbol has <c>Private</c> accessibility semantics.
    /// </summary>
    Private,
    /// <summary>
    /// The symbol has <c>Friend</c> accessibility semantics.
    /// </summary>
    Friend,
    /// <summary>
    /// The symbol has <c>Public</c> accessibility semantics.
    /// </summary>
    Public,
    /// <summary>
    /// The symbol has <c>Global</c> accessibility semantics.
    /// </summary>
    Global
}
