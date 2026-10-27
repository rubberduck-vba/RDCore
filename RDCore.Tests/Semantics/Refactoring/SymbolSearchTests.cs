using RDCore.SDK.Semantics.Refactoring;

namespace RDCore.Tests.Semantics.Refactoring;

/// <summary>
/// A search for a symbol matches the names that have what is asked, in order and regardless of case, the closest matches first.
/// </summary>
[TestClass]
public sealed class SymbolSearchTests
{
    [TestMethod]
    [DataRow("total", "Total", SymbolMatch.Exact)]
    [DataRow("tot", "TotalCount", SymbolMatch.Prefix)]
    [DataRow("gt", "GetTotal", SymbolMatch.WordStarts)]
    [DataRow("mi", "max_items", SymbolMatch.WordStarts)]
    [DataRow("total", "GrandTotal", SymbolMatch.Substring)]
    [DataRow("gdt", "GrandTotal", SymbolMatch.Subsequence)]
    [DataRow("", "Anything", SymbolMatch.Exact)]
    public void AName_MatchesAsClosely_AsItHasWhatIsAsked(string query, string name, SymbolMatch expected)
        => Assert.AreEqual(expected, SymbolSearch.Match(query, name));

    [TestMethod]
    [DataRow("tx", "Total")]
    [DataRow("totals", "Total")]
    [DataRow("lt", "Total")]
    public void AName_ThatDoesNotHaveWhatIsAskedInOrder_DoesNotMatch(string query, string name)
        => Assert.IsNull(SymbolSearch.Match(query, name));

    [TestMethod]
    public void TheClosestMatches_ComeFirst_ThenByName()
    {
        string[] names = ["GrandTotal", "subtotal", "TotalCount", "Total", "GetTotal", "Grid", "AddTotal"];

        var found = SymbolSearch.Search("tot", names, name => name, limit: 10);

        CollectionAssert.AreEqual(new[] { "Total", "TotalCount", "AddTotal", "GetTotal", "GrandTotal", "subtotal" }, found.ToArray());
    }

    [TestMethod]
    public void AnEmptyQuery_AnswersEverySymbol_ByName_UpToTheLimit()
    {
        var found = SymbolSearch.Search("  ", ["Gamma", "alpha", "Beta"], name => name, limit: 2);

        CollectionAssert.AreEqual(new[] { "alpha", "Beta" }, found.ToArray());
    }
}
