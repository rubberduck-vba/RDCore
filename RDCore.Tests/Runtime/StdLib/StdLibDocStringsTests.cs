using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Runtime.StdLib;
using System.Collections;
using System.Globalization;
using System.Resources;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// What a member of the standard library does, and each of its parameters, is a sentence a completion list and a signature help show - in each language of the
/// platform, and only of members and parameters the library has.
/// </summary>
[TestClass]
public sealed class StdLibDocStringsTests
{
    private static readonly ResourceManager DocStrings = new("RDCore.SDK.Runtime.StdLib.StdLibDocStrings", typeof(StdLibSymbolReader).Assembly);

    private static List<Symbol> Symbols(string culture)
    {
        var current = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            return [.. new StdLibSymbolProvider(new Uri("file:///c:/ws/")).ProvideSymbols()];
        }
        finally
        {
            CultureInfo.CurrentUICulture = current;
        }
    }

    private static IEnumerable<VBParameterSymbol> ParametersOf(Symbol symbol) => symbol switch
    {
        VBReturningMemberSymbol member => member.Parameters,
        VBProcedureMemberSymbol member => member.Parameters,
        _ => [],
    };

    private static Symbol Member(List<Symbol> symbols, string module, string name)
    {
        var owner = symbols.Single(symbol => symbol is VBStandardModuleSymbol && symbol.Name == module);
        return symbols.Single(symbol => symbol.ParentUri == owner.Uri && symbol.Name == name);
    }

    [TestMethod]
    public void AMember_AndItsParameters_AreDescribed_AndAStringFunction_AsTheOneItIsTheStringVersionOf()
    {
        var symbols = Symbols("en");
        var left = Member(symbols, "Strings", "Left");

        Assert.AreEqual("The first characters of a string.", left.GetProperty(SymbolProperties.DocString));
        Assert.AreEqual("How many characters.", ParametersOf(left).Single(parameter => parameter.Name == "Length").GetProperty(SymbolProperties.DocString));
        Assert.AreEqual(left.GetProperty(SymbolProperties.DocString), Member(symbols, "Strings", "Left$").GetProperty(SymbolProperties.DocString));
    }

    [TestMethod]
    public void TheDescriptions_AreInTheLanguageOfTheUserInterface()
        => Assert.AreEqual("Les premiers caractères d'une chaîne.", Member(Symbols("fr-CA"), "Strings", "Left").GetProperty(SymbolProperties.DocString));

    [TestMethod]
    public void EveryDescription_IsOfAMemberOrAParameterTheLibraryHas()
    {
        var symbols = Symbols("en");
        var owners = symbols.Where(symbol => symbol is VBStandardModuleSymbol or VBClassModuleSymbol).ToDictionary(symbol => symbol.Uri.AbsoluteUri, symbol => symbol.Name);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in symbols.Where(symbol => owners.ContainsKey(symbol.ParentUri.AbsoluteUri)))
        {
            var key = $"{owners[member.ParentUri.AbsoluteUri]}_{member.Name.TrimEnd('$')}";
            keys.Add(key);
            keys.UnionWith(ParametersOf(member).Select(parameter => $"{key}_{parameter.Name}"));
        }

        var described = DocStrings.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false)!
            .Cast<DictionaryEntry>().Select(entry => (string)entry.Key).ToList();

        Assert.IsNotEmpty(described);
        CollectionAssert.AreEqual(Array.Empty<string>(), described.Where(key => !keys.Contains(key)).Order().ToArray());
    }

    [TestMethod]
    public void EveryDescription_IsTranslated()
    {
        var english = DocStrings.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false)!.Cast<DictionaryEntry>()
            .ToDictionary(entry => (string)entry.Key, entry => (string?)entry.Value);
        var french = DocStrings.GetResourceSet(CultureInfo.GetCultureInfo("fr"), createIfNotExists: true, tryParents: false)!.Cast<DictionaryEntry>()
            .ToDictionary(entry => (string)entry.Key, entry => (string?)entry.Value);

        CollectionAssert.AreEquivalent(english.Keys.ToArray(), french.Keys.ToArray());
        CollectionAssert.AreEqual(Array.Empty<string>(), english.Keys.Where(key => english[key] == french[key]).ToArray());
    }
}
