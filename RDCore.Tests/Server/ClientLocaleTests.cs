using Microsoft.Extensions.Options;
using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Server;
using System.Globalization;

namespace RDCore.Tests.Server;

/// <summary>
/// A server speaks the language of the user interface its client says it shows (<c>InitializeParams.locale</c>): on the thread that is told, and on every
/// thread it begins work on later.
/// </summary>
/// <remarks>
/// These change the process's default culture, which every test that runs at the same time would read: they run on their own.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class ClientLocaleTests
{
    private CultureInfo? _defaultUICulture;
    private CultureInfo _uiCulture = CultureInfo.CurrentUICulture;
    private CultureInfo _culture = CultureInfo.CurrentCulture;

    [TestInitialize]
    public void Remember()
    {
        _defaultUICulture = CultureInfo.DefaultThreadCurrentUICulture;
        _uiCulture = CultureInfo.CurrentUICulture;
        _culture = CultureInfo.CurrentCulture;
    }

    [TestCleanup]
    public void Restore()
    {
        CultureInfo.DefaultThreadCurrentUICulture = _defaultUICulture;
        CultureInfo.CurrentUICulture = _uiCulture;
        CultureInfo.CurrentCulture = _culture;
    }

    [TestMethod]
    public void TheClientsLocale_IsTheLanguageOfTheUserInterface_AndOnlyThat()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        var applied = ClientLocale.Apply("fr-CA");

        Assert.AreEqual("fr-CA", applied?.Name);
        Assert.AreEqual("fr-CA", CultureInfo.CurrentUICulture.Name);
        Assert.AreEqual("fr-CA", CultureInfo.DefaultThreadCurrentUICulture?.Name);
        Assert.AreEqual(CultureInfo.InvariantCulture, CultureInfo.CurrentCulture, "how numbers and dates are written is not the client's");
    }

    // a diagnostic is analyzed on whatever thread the request is served on, long after the client said its locale.
    [TestMethod]
    public async Task ADiagnostic_AnalyzedOnAnotherThread_IsInTheClientsLanguage()
    {
        ClientLocale.Apply("fr-FR");

        var message = await Task.Factory.StartNew(() =>
        {
            var uri = TestUri.TestModuleUri();
            var source = "Attribute VB_Name = \"Module1\"\r\nSub DoWork()\r\nEnd Sub\r\n";
            return new ImplicitPublicMemberAnalyzer().Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, source), null)).Single().Message;
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        Assert.AreEqual("'DoWork' est implicitement Public. Indiquez son accessibilité.", message);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("  ")]
    [DataRow("not-a-culture-at-all-xx")]
    public void NoLocale_OrOneThePlatformDoesNotKnow_LeavesTheLanguageAsItIs(string? locale)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");

        Assert.IsNull(ClientLocale.Apply(locale));
        Assert.AreEqual("en-US", CultureInfo.CurrentUICulture.Name);
    }
}
