using Newtonsoft.Json.Linq;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server;

namespace RDCore.Tests.Server;

/// <summary>
/// The <c>initializationOptions</c> of an <c>initialize</c> request: the part of LSP that exists for what a client has to tell a server that
/// no standard capability describes, which is where a client says the language the workspace is written in.
/// </summary>
[TestClass]
public sealed class ServerInitializationOptionsTests
{
    [TestMethod]
    public void TheOptionsAClientSends_AreReadBackFromTheWire()
    {
        // what the server receives is the JSON the client's object was serialized to.
        var onTheWire = JToken.FromObject(new RDCoreInitializationOptions { Language = "basic" });

        Assert.AreEqual("basic", RDCoreServerApp.ReadInitializationOptions(onTheWire)!.Language);
    }

    [TestMethod]
    public void AClientThatSendsNone_SaysNothing()
        => Assert.IsNull(RDCoreServerApp.ReadInitializationOptions(null));

    [TestMethod]
    public void AClientThatSendsOptionsOfItsOwn_SaysNothingOfTheLanguage()
        => Assert.IsNull(RDCoreServerApp.ReadInitializationOptions(JObject.Parse("{ \"somethingElse\": 1 }"))!.Language);

    [TestMethod]
    public void OptionsThatAreNotAnObject_AreIgnoredRatherThanFailingTheInitialize()
        => Assert.IsNull(RDCoreServerApp.ReadInitializationOptions(JValue.CreateString("basic")));

    // a server loads the workspace while it answers initialize, before the platform handshake says whether the client runs unattended: the client says it
    // here too, and a server asks nothing of a client that says so. A client that does not say is asked.
    [TestMethod]
    [DataRow("{ \"Unattended\": true }", true)]
    [DataRow("{ \"unattended\": true }", true, DisplayName = "whatever the case of its name")]
    [DataRow("{ \"Unattended\": false }", false)]
    [DataRow("{ \"Language\": \"vb6\" }", false, DisplayName = "a client that does not say")]
    [DataRow(null, false, DisplayName = "a client that sends no options")]
    public void WhetherTheClientRunsUnattended_IsReadFromTheOptions(string? options, bool unattended)
    {
        // a client that sends no options leaves them unset.
        var request = options is null
            ? new OmniSharp.Extensions.LanguageServer.Protocol.Models.InitializeParams()
            : new OmniSharp.Extensions.LanguageServer.Protocol.Models.InitializeParams { InitializationOptions = JObject.Parse(options) };

        Assert.AreEqual(unattended, RDCoreServerApp.ClientRunsUnattended(request));
    }

    // a platform's client says it runs unattended in its initializationOptions exactly when it says so in the handshake: rdc.exe --unattended does.
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void APlatformsClient_SaysItRunsUnattended_AsItSaysInTheHandshake(bool unattended)
    {
        var expected = new RDCore.SDK.Client.CorePlatformClientCapabilities
        {
            LanguageServer = new RDCore.SDK.Client.LanguageServerCapabilities { Unattended = new RDCore.SDK.Client.Unattended(unattended) },
        };

        var options = RDCore.SDK.Client.RDCoreClientApp.InitializationOptionsOf("vba", System.Globalization.CultureInfo.InvariantCulture, expected);

        Assert.AreEqual(unattended, options.Unattended);
        Assert.IsFalse(RDCore.SDK.Client.RDCoreClientApp.InitializationOptionsOf("vba", System.Globalization.CultureInfo.InvariantCulture, new()).Unattended, "a client that expects nothing says nothing");
    }

    // a client says it answers what the server asks before initialize is answered only when it does, and one that runs unattended answers nothing.
    [TestMethod]
    [DataRow(true, false, true)]
    [DataRow(false, false, false)]
    [DataRow(true, true, false)]
    [DataRow(false, true, false)]
    public void APlatformsClient_SaysItAnswersDuringInitialize_OnlyWhenItDoes_AndNotUnattended(bool answers, bool unattended, bool says)
    {
        var expected = new RDCore.SDK.Client.CorePlatformClientCapabilities
        {
            LanguageServer = new RDCore.SDK.Client.LanguageServerCapabilities { Unattended = new RDCore.SDK.Client.Unattended(unattended) },
        };

        var options = RDCore.SDK.Client.RDCoreClientApp.InitializationOptionsOf("vba", System.Globalization.CultureInfo.InvariantCulture, expected, answers);

        Assert.AreEqual(says, options.AnswersDuringInitialize);
    }

    // and the server asks then only a client that says it answers, and does not say it runs unattended; one that sends no options is not asked.
    [TestMethod]
    [DataRow(true, false, true)]
    [DataRow(false, false, false)]
    [DataRow(true, true, false)]
    public void TheServer_ReadsWhetherTheClientAnswersDuringInitialize_FromTheWire(bool answers, bool unattended, bool asks)
    {
        var request = new OmniSharp.Extensions.LanguageServer.Protocol.Models.InitializeParams
        {
            InitializationOptions = JToken.FromObject(new RDCoreInitializationOptions { AnswersDuringInitialize = answers, Unattended = unattended }),
        };

        Assert.AreEqual(asks, RDCoreServerApp.ClientAnswersDuringInitialize(request));
        Assert.IsFalse(RDCoreServerApp.ClientAnswersDuringInitialize(new OmniSharp.Extensions.LanguageServer.Protocol.Models.InitializeParams()), "a client that says nothing is not asked");
    }

    [TestMethod]
    public void WhetherTheClientRunsUnattended_IsReadBackFromTheWire()
        => Assert.IsTrue(RDCoreServerApp.ReadInitializationOptions(JToken.FromObject(new RDCoreInitializationOptions { Unattended = true }))!.Unattended);

    [TestMethod]
    public void TheLanguageIsReadWhateverTheCaseOfItsName()
        => Assert.AreEqual("vb6", RDCoreServerApp.ReadInitializationOptions(JObject.Parse("{ \"Language\": \"vb6\" }"))!.Language);
}
