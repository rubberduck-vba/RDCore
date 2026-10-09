namespace RDCore.Diagnostics;

/// <summary>
/// What the diagnostics extension can be configured with, bound from the <c>Configuration:Diagnostics</c> section of its <c>appsettings.json</c>.
/// </summary>
public sealed record class DiagnosticsOptions
{
    /// <summary>
    /// The settings of the <c>RDC01001</c> diagnostic: a name that does not say what it is.
    /// </summary>
    public NameRuleOptions MeaningfulNames { get; set; } = new();

    /// <summary>
    /// The settings of the <c>RDC01002</c> diagnostic: a name that begins with a prefix that states its type.
    /// </summary>
    public NameRuleOptions HungarianNotation { get; set; } = new();
}

/// <summary>
/// The settings of a diagnostic about the names of declarations.
/// </summary>
public sealed record class NameRuleOptions
{
    /// <summary>
    /// The names the diagnostic accepts although they break its rule: the bad names that are approved, such as <c>i</c> for the counter of a loop. A name is compared
    /// without regard to case, like every identifier of the language.
    /// </summary>
    public string[] AllowedNames { get; set; } = [];
}
