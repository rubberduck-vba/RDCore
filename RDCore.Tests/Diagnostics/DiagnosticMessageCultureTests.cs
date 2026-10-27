using Microsoft.Extensions.Options;
using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Diagnostics.Model;
using RDCore.Parsing;
using RDCore.SDK.Model.Diagnostics;
using System.Collections;
using System.Globalization;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// The messages of the diagnostics are in the language of the user interface: English is the neutral set, every English culture and the invariant
/// culture included, and French is the <c>fr</c> set, which every French culture reads.
/// </summary>
[TestClass]
public sealed class DiagnosticMessageCultureTests
{
    private const string Source = "Attribute VB_Name = \"Module1\"\r\nSub DoWork()\r\n    Dim x As Long\r\nEnd Sub\r\n";

    private const string English = "en";
    private const string French = "fr";

    // the message of the finding of a diagnostic, analyzed under a culture.
    private static string MessageIn(string culture, IModuleAnalyzer analyzer, RDCoreDiagnosticId id)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            var uri = TestUri.TestModuleUri();
            var findings = analyzer.Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, Source), null)).ToList();
            return findings.First(finding => finding.Id == id).Message;
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    private static UseMeaningfulIdentifierNamesAnalyzer MeaningfulNames()
        => new(Options.Create(new DiagnosticsOptions { MeaningfulNames = new NameRuleOptions(), HungarianNotation = new NameRuleOptions() }));

    [TestMethod]
    [DataRow("en-US", English)]
    [DataRow("en-GB", English)]
    [DataRow("", English, DisplayName = "invariant")]
    [DataRow("fr-CA", French)]
    [DataRow("fr-FR", French)]
    public void AMessage_IsInTheLanguageOfTheUserInterface(string culture, string language)
    {
        var tooShort = MessageIn(culture, MeaningfulNames(), RDCoreDiagnosticId.UseMeaningfulIdentifierNames);
        var implicitPublic = MessageIn(culture, new ImplicitPublicMemberAnalyzer(), RDCoreDiagnosticId.ImplicitPublicMember);

        // RDC11022 is found by the analysis of the host's runtime facts; its message is read the same way, off the culture of the thread.
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        var reDim = RDCoreDiagnosticsResources.ReDimOfAnImplicitVariant_Message;
        CultureInfo.CurrentUICulture = previous;

        if (language == English)
        {
            Assert.AreEqual("'x' is too short to say what it is: a name should have at least 3 characters.", tooShort, "RDC01001");
            Assert.AreEqual("'DoWork' is implicitly Public. State its accessibility.", implicitPublic, "RDC00105");
            StringAssert.StartsWith(reDim, "'{0}' was declared implicitly", "RDC11022");
        }
        else
        {
            Assert.AreEqual("'x' est trop court pour dire ce qu'il est: un nom devrait avoir au moins 3 caractères.", tooShort, "RDC01001");
            Assert.AreEqual("'DoWork' est implicitement Public. Indiquez son accessibilité.", implicitPublic, "RDC00105");
            StringAssert.StartsWith(reDim, "'{0}' a été déclaré implicitement", "RDC11022");
        }
    }

    // a message added in one language only would be read in English by a French user, and by nobody else in French.
    [TestMethod]
    public void EveryMessage_IsInBothLanguages()
    {
        var english = Keys(CultureInfo.InvariantCulture);
        var french = Keys(CultureInfo.GetCultureInfo(French));

        Assert.IsNotEmpty(english);
        CollectionAssert.AreEquivalent(english, french);
        foreach (var key in english)
        {
            Assert.AreNotEqual(RDCoreDiagnosticsResources.ResourceManager.GetString(key, CultureInfo.InvariantCulture),
                RDCoreDiagnosticsResources.ResourceManager.GetString(key, CultureInfo.GetCultureInfo(French)), $"{key} is not translated");
        }

        static string[] Keys(CultureInfo culture)
            => [.. RDCoreDiagnosticsResources.ResourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: false)!
                .Cast<DictionaryEntry>().Select(entry => (string)entry.Key).Order(StringComparer.Ordinal)];
    }
}
