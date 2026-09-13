using RDCore.Parsing;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Values.Intrinsic;
using System.Globalization;

namespace RDCore.Tests.Parser;

/// <summary>
/// Adversarial review #167-205 (pre-existing, "still open" backlog item), escalated: the declaration pass's own
/// date-literal parsing used <see cref="DateTime.TryParse(string, out DateTime)"/> with no
/// <see cref="CultureInfo"/> argument, so it read the AMBIENT thread culture — disagreeing with
/// <c>PrecompilerDirectiveListener</c>'s sibling handler for the exact same <c>#...#</c> token syntax,
/// which already forces <see cref="CultureInfo.InvariantCulture"/> per <strong>MS-VBAL §3.3.3</strong>
/// ("date literals are locale-independent"). The same source resolved to a *different date* depending on
/// the host machine's locale (e.g. <c>#3/4/2020#</c> is March 4 under a month-first culture, April 3
/// under a day-first one) — a silent, undetectable correctness bug, not merely a parse failure.
/// </summary>
[TestClass]
public sealed class DateLiteralTests
{
    private static readonly Uri Uri = TestUri.TestModuleUri();

    private static DateTime? ResolveFirstConstDate(string source)
    {
        var result = new ModuleParser().Parse(Uri, source);
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.IsEmpty ? "" : result.SyntaxErrors[0].Description);
        var literal = result.SyntaxTree!.Children.OfType<ConstantDeclarationNode>().Single()
            .Children.OfType<LiteralExpressionNode>().Single();
        return (literal.StaticValue as VBDateValue)?.Value;
    }

    [TestMethod]
    // an ambiguous numeric date (day/month both <= 12) is exactly where locale-dependent parsing would
    // silently disagree with itself: MS-VBAL's own algorithm (§3.3.3) always picks March 4 here
    // (L=3 is a legal month, M=4 is a legal day of that month), regardless of the host's locale.
    [DataRow("en-US")]
    [DataRow("fr-CA")]
    [DataRow("fr-FR")]
    [DataRow("de-DE")]
    [DataRow("ja-JP")]
    public void AmbiguousNumericDateLiteral_ResolvesTheSameRegardlessOfHostCulture(string cultureName)
    {
        const string source = "Public Const D = #3/4/2020#";
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
        try
        {
            var resolved = ResolveFirstConstDate(source);
            Assert.AreEqual(new DateTime(2020, 3, 4), resolved);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [TestMethod]
    // an English month abbreviation must resolve under any host culture, including ones whose OWN
    // month abbreviations for January differ (e.g. French "janv.") - MS-VBAL mandates English names
    // unconditionally, never the host's own locale-specific month names.
    [DataRow("en-US")]
    [DataRow("fr-FR")]
    [DataRow("de-DE")]
    public void EnglishMonthAbbreviation_ResolvesUnderAnyHostCulture(string cultureName)
    {
        const string source = "Public Const D = #1-Jan-2020#";
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
        try
        {
            var resolved = ResolveFirstConstDate(source);
            Assert.AreEqual(new DateTime(2020, 1, 1), resolved);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
