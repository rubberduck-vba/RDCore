using RDCore.SDK.Model.Symbols;
using System.Globalization;

namespace RDCore.SDK.Runtime;

/// <summary>
/// How the relational operators compare <c>String</c> values where an operation is evaluated or analyzed (<strong>MS-VBAL §5.6.9.5</strong>):
/// the comparison mode of the module, and the regional settings of the environment.
/// </summary>
/// <remarks>
/// This value says which comparison an operation makes. The runtime implements the comparison itself.
/// </remarks>
/// <param name="Mode">
/// The comparison mode of the module (<strong>MS-VBAL §5.2.1.1</strong>): <see cref="OptionCompare.Text"/> compares regardless of case,
/// anything else compares the data of the strings. Never <see cref="OptionCompare.Database"/>: that is resolved to one of the two by the platform.
/// </param>
/// <param name="Culture">
/// The regional settings of the environment, that text is collated by. The invariant culture when none is given.
/// </param>
public readonly record struct StringComparisonRules(OptionCompare Mode = OptionCompare.Binary, CultureInfo? Culture = null)
{
    /// <summary>
    /// The culture text comparisons are collated by.
    /// </summary>
    public CultureInfo EffectiveCulture => Culture ?? CultureInfo.InvariantCulture;

    /// <summary>
    /// Whether strings that differ in case only are equal.
    /// </summary>
    public bool IgnoresCase => Mode == OptionCompare.Text;

    /// <summary>
    /// Whether this is the text comparison of a locale that a <c>Compare</c> argument names by its identifier, rather than the text mode of the
    /// environment: a search then folds case the same way in every locale, and keeps the letters the locale's alphabet has.
    /// </summary>
    public bool IsLocaleComparison { get; init; }
}
