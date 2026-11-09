using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Client;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Server;
using System.Globalization;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Server;

/// <summary>
/// The setting of the language of the platform's user interface (<c>Environment.Regional.Language</c>): empty, the machine's language; a culture the platform
/// knows, that one; one it has no translation in, English, said at Information; a name that is no culture, the machine's language, with a warning. It is the
/// language of the user interface only: numbers and dates are written and read as before.
/// </summary>
/// <remarks>
/// These change the process's default culture, which every test that runs at the same time would read: they run on their own.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class UserInterfaceLanguageTests
{
    private CultureInfo? _defaultUICulture;
    private CultureInfo? _defaultCulture;
    private CultureInfo _uiCulture = CultureInfo.CurrentUICulture;
    private CultureInfo _culture = CultureInfo.CurrentCulture;

    [TestInitialize]
    public void Remember()
    {
        (_defaultUICulture, _defaultCulture) = (CultureInfo.DefaultThreadCurrentUICulture, CultureInfo.DefaultThreadCurrentCulture);
        (_uiCulture, _culture) = (CultureInfo.CurrentUICulture, CultureInfo.CurrentCulture);
    }

    [TestCleanup]
    public void Restore()
    {
        (CultureInfo.DefaultThreadCurrentUICulture, CultureInfo.DefaultThreadCurrentCulture) = (_defaultUICulture, _defaultCulture);
        (CultureInfo.CurrentUICulture, CultureInfo.CurrentCulture) = (_uiCulture, _culture);
    }

    // a machine of a language: its culture and the language of its user interface, as a process starts on it.
    private static void OnAMachineIn(string culture)
    {
        CultureInfo.DefaultThreadCurrentUICulture = null;
        CultureInfo.DefaultThreadCurrentCulture = null;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
    }

    [TestMethod]
    [DataRow("en-US", null, DisplayName = "absent, on an English machine")]
    [DataRow("en-US", "", DisplayName = "empty, on an English machine")]
    [DataRow("fr-CA", "  ", DisplayName = "blank, on a French machine")]
    public void NoSetting_IsTheMachinesLanguage_AndSaysNothing(string machine, string? setting)
    {
        OnAMachineIn(machine);

        var applied = UserInterfaceLanguage.Apply(setting);

        Assert.AreEqual(UserInterfaceLanguageOutcome.Machine, applied.Outcome);
        Assert.IsFalse(applied.Overrides);
        Assert.IsNull(applied.Message);
        Assert.AreEqual(machine, CultureInfo.CurrentUICulture.Name);
        Assert.IsNull(CultureInfo.DefaultThreadCurrentUICulture, "the process's default is left as it is");
    }

    [TestMethod]
    [DataRow("en-US", "fr", "fr", "🌐 L'interface est en 'fr' (Environment.Regional.Language).", DisplayName = "fr on an English machine, said in French")]
    [DataRow("en-US", " fr-CA ", "fr-CA", "🌐 L'interface est en 'fr-CA' (Environment.Regional.Language).", DisplayName = "fr-CA on an English machine, trimmed")]
    [DataRow("fr-CA", "en", "en", "🌐 The user interface is in 'en' (Environment.Regional.Language).", DisplayName = "en on a French machine, said in English")]
    [DataRow("fr-FR", "en-US", "en-US", "🌐 The user interface is in 'en-US' (Environment.Regional.Language).", DisplayName = "en-US on a French machine")]
    public void ALanguageThePlatformIsTranslatedIn_IsTheLanguageOfTheUserInterface_AndOnlyThat(string machine, string setting, string language, string message)
    {
        OnAMachineIn(machine);

        var applied = UserInterfaceLanguage.Apply(setting);

        Assert.AreEqual(UserInterfaceLanguageOutcome.Applied, applied.Outcome);
        Assert.IsTrue(applied.Overrides);
        Assert.AreEqual(LogLevel.Information, applied.Level);
        Assert.AreEqual(message, applied.Message, "said in the language it sets");
        Assert.AreEqual(language, CultureInfo.CurrentUICulture.Name);
        Assert.AreEqual(language, CultureInfo.DefaultThreadCurrentUICulture?.Name, "every thread that has not set its own");
        Assert.AreEqual(machine, CultureInfo.CurrentCulture.Name, "how numbers and dates are written is not the language's");
        Assert.IsNull(CultureInfo.DefaultThreadCurrentCulture);
    }

    // a culture the platform knows, of a language its messages are not translated in: applied, its messages fall back to the neutral resources (English), and
    // what is said of it is said in the language the user read before, at Information.
    [TestMethod]
    [DataRow("en-US", "de", "The platform has no translation in 'de' (Environment.Regional.Language): its user interface is in English ('en').", DisplayName = "de, said in English")]
    [DataRow("en-US", "de-CH", "The platform has no translation in 'de-CH' (Environment.Regional.Language): its user interface is in English ('en').", DisplayName = "de-CH, said in English")]
    [DataRow("fr-CA", "de", "La plateforme n'a pas de traduction en 'de' (Environment.Regional.Language) : son interface est en anglais ('en').", DisplayName = "de, said in French")]
    public void ALanguageThePlatformIsNotTranslatedIn_IsShownInEnglish_AndSaysSoAtInformation(string machine, string setting, string message)
    {
        OnAMachineIn(machine);

        var applied = UserInterfaceLanguage.Apply(setting);

        Assert.AreEqual(UserInterfaceLanguageOutcome.NotTranslated, applied.Outcome);
        Assert.IsTrue(applied.Overrides);
        Assert.AreEqual(LogLevel.Information, applied.Level);
        Assert.AreEqual(message, applied.Message);
        Assert.AreEqual(setting, CultureInfo.CurrentUICulture.Name);
        Assert.AreEqual(machine, CultureInfo.CurrentCulture.Name);
        StringAssert.StartsWith(RDCore.Runtime.Support.Resources.Host_ChangedWhileWaiting_Resume, "{0} changed while", "the neutral resources: English");
    }

    [TestMethod]
    [DataRow("en-US", "not-a-culture-at-all-xx", "'not-a-culture-at-all-xx' is not a culture the platform knows (Environment.Regional.Language): the user interface stays in the machine's language, 'en-US'.", DisplayName = "on an English machine")]
    [DataRow("fr-CA", "français", "'français' n'est pas une culture que la plateforme connaît (Environment.Regional.Language) : l'interface reste dans la langue de la machine, 'fr-CA'.", DisplayName = "on a French machine")]
    public void ANameThatIsNoCulture_LeavesTheMachinesLanguage_WithAWarning(string machine, string setting, string message)
    {
        OnAMachineIn(machine);

        var applied = UserInterfaceLanguage.Apply(setting);

        Assert.AreEqual(UserInterfaceLanguageOutcome.Unknown, applied.Outcome);
        Assert.IsFalse(applied.Overrides);
        Assert.AreEqual(LogLevel.Warning, applied.Level);
        Assert.AreEqual(message, applied.Message);
        Assert.AreEqual(machine, CultureInfo.CurrentUICulture.Name);
        Assert.IsNull(CultureInfo.DefaultThreadCurrentUICulture);
    }

    [TestMethod]
    [DataRow("en", true)]
    [DataRow("en-GB", true)]
    [DataRow("fr", true)]
    [DataRow("fr-BE", true)]
    [DataRow("de", false)]
    [DataRow("ja-JP", false)]
    public void ALanguage_IsTranslated_InItsOwnCultureOrAParent(string culture, bool translated)
        => Assert.AreEqual(translated, UserInterfaceLanguage.IsTranslated(CultureInfo.GetCultureInfo(culture)));

    [TestMethod]
    public void TheSetting_IsReadFromTheComponentsConfiguration()
    {
        OnAMachineIn("en-US");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Configuration:Environment:Regional:Language"] = "fr-CA" })
            .Build();

        Assert.AreEqual("fr-CA", UserInterfaceLanguage.Apply(configuration).Culture.Name);
        Assert.AreEqual("fr-CA", CultureInfo.CurrentUICulture.Name);
    }

    // ---- every component agrees ----

    // the client applies its setting and says the language in its initialize request; the language server, in a process of its own on an English machine, takes
    // it and tells the components it starts the same; the diagnostics extension, in a third, analyzes in it on whatever thread it is asked.
    [TestMethod]
    public async Task TheClientsSetting_IsTheLanguageOfTheLanguageServer_AndOfTheDiagnosticsItStarts()
    {
        OnAMachineIn("en-US");
        _ = UserInterfaceLanguage.Apply("fr");
        var toTheLanguageServer = RDCoreClientApp.InitializationOptionsOf(null, CultureInfo.CurrentUICulture, new CorePlatformClientCapabilities());

        OnAMachineIn("en-US");
        Assert.IsNotNull(ClientLocale.Apply(ClientLocale.Of(null, toTheLanguageServer)));
        var told = await Task.Run(() => string.Format(CultureInfo.CurrentUICulture, RDCore.LanguageServer.Debugging.DebuggerMessages.NotAModuleOfTheWorkspace, "Progam"));
        var toTheDiagnostics = RDCoreClientApp.InitializationOptionsOf(null, CultureInfo.CurrentUICulture, new CorePlatformClientCapabilities());

        OnAMachineIn("en-US");
        Assert.IsNotNull(ClientLocale.Apply(ClientLocale.Of(null, toTheDiagnostics)));
        var found = await Task.Factory.StartNew(() =>
        {
            var uri = TestUri.TestModuleUri();
            var source = "Attribute VB_Name = \"Module1\"\r\nSub DoWork()\r\nEnd Sub\r\n";
            return new ImplicitPublicMemberAnalyzer().Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, source), null)).Single().Message;
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        Assert.AreEqual("fr", toTheLanguageServer.Locale);
        Assert.AreEqual("'Progam' n'est pas un module de l'espace de travail", told);
        Assert.AreEqual("fr", toTheDiagnostics.Locale);
        Assert.AreEqual("'DoWork' est implicitement Public. Indiquez son accessibilité.", found);
    }

    // ---- formatting is untouched ----

    // on an en-US machine, a program is run under each language: what CStr writes and CDate reads are the platform's regional settings, and MonthName is the
    // session's Lcid, whatever the language; the error raised is the same error.
    [TestMethod]
    [DataRow("", DisplayName = "the machine's language")]
    [DataRow("fr", DisplayName = "fr")]
    [DataRow("de", DisplayName = "de, which falls back to English")]
    public async Task UnderAnyLanguage_NumbersAndDatesAreWrittenAndReadAsBefore(string language)
    {
        OnAMachineIn("en-US");
        _ = UserInterfaceLanguage.Apply(language);

        var printed = await RunAsync([], StandardModule("Program",
            "Option Explicit",
            "Public Sub Main()",
            "    Debug.Print CStr(2.5)",
            "    Debug.Print CStr(CDbl(\"1,000.5\"))",
            "    Debug.Print CStr(CDate(\"1/2/2026 13:05\"))",
            "    Debug.Print MonthName(1)",
            "    On Error Resume Next",
            "    Debug.Print 1 / 0",
            "    Debug.Print Err.Number",
            "End Sub"));

        CollectionAssert.AreEqual(new[] { "2.5", "1000.5", "1/2/2026 1:05:00 PM", "January", "11" }, printed, string.Join(" | ", printed));
        Assert.AreEqual("en-US", CultureInfo.CurrentCulture.Name);
    }

    [TestMethod]
    [Ignore("the descriptions of the run-time errors (VBRuntimeErrorInfo.VBRuntimeErrors) are English only: under fr, Err.Description of error 11 is 'Division by zero', where MS-VBA in French says 'Division par zéro'")]
    public async Task UnderFrench_AnErrorsDescription_IsInFrench()
    {
        OnAMachineIn("en-US");
        _ = UserInterfaceLanguage.Apply("fr");

        var printed = await RunAsync([], StandardModule("Program",
            "Option Explicit",
            "Public Sub Main()",
            "    On Error Resume Next",
            "    Debug.Print 1 / 0",
            "    Debug.Print Err.Description",
            "End Sub"));

        CollectionAssert.AreEqual(new[] { "Division par zéro" }, printed, string.Join(" | ", printed));
    }
}
