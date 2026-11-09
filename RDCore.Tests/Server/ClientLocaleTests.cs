using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Server;
using System.Globalization;
using System.IO.Pipelines;
using System.Text;
using OmniSharp.Extensions.LanguageServer.Server;
using OmniSharpLanguageServer = OmniSharp.Extensions.LanguageServer.Server.LanguageServer;
using RDCore.SDK.Extensibility.Diagnostics;

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

#if SDK_SEAM // needs the SDK's internal ClientLocale, which a server app applies from its client's initialize request
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
#endif

    // ---- what a client of the protocol says ----

    // a message as LSP frames it, written as a client that is not OmniSharp's writes it.
    private static async Task SendAsync(PipeWriter writer, JObject message, CancellationToken token)
    {
        var body = Encoding.UTF8.GetBytes(message.ToString(Newtonsoft.Json.Formatting.None));
        _ = await writer.WriteAsync(Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n"), token);
        _ = await writer.WriteAsync(body, token);
    }

    // ---- what a platform's client says ----

#if SDK_SEAM // needs the SDK's internal ClientLocale, which a server app applies from its client's initialize request
    [TestMethod]
    public async Task WhatAPlatformsClientInFrenchSays_IsWhatTheHostAnswersIn_OnAThreadItBeginsLater()
    {
        // the initializationOptions as they travel, read by the server as it reads them; the environment host's refusals are its own resources, read on whatever
        // thread a request is answered on.
        var sent = Newtonsoft.Json.Linq.JToken.FromObject(new RDCore.SDK.Platform.Protocol.RDCoreInitializationOptions { Locale = ClientLocale.TagOf(CultureInfo.GetCultureInfo("fr-CA")) });
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");

        Assert.IsNotNull(ClientLocale.Apply(ClientLocale.Of(null, RDCore.SDK.Server.RDCoreServerApp.ReadInitializationOptions(sent))));
        var answered = await Task.Run(() => RDCore.Runtime.Support.Resources.Host_ChangedWhileWaiting_Resume);

        StringAssert.StartsWith(answered, "{0} a changé pendant l'attente du programme");
    }
#endif
}
