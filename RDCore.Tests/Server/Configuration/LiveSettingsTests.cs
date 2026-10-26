using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics;
using RDCore.SDK.Server.Configuration;

namespace RDCore.Tests.Server.Configuration;

/// <summary>
/// What a client says the settings are takes effect while a component runs - its live ones, set on the objects the component reads its settings off - and what
/// it does not say is what the component started with; a component hands down what it was said, or, with no client, what its user set.
/// </summary>
[TestClass]
public sealed class LiveSettingsTests
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "rdcore-live-settings-" + Guid.NewGuid().ToString("N"));

    private readonly SdkAppOptions _app = new() { Server = new SdkServerOptions { Verbose = true, TraceLevel = LogLevel.Information, ConnectTimeoutSeconds = 30 } };
    private readonly SdkServerOptions _server = new() { Verbose = true, TraceLevel = LogLevel.Information };
    private readonly DiagnosticsOptions _diagnostics = new();

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private string UserFile => Path.Combine(_folder, UserSettingsStore.FileName);

    private LiveSettings Sut()
    {
        var schema = SettingsSchema.Platform.Combine(DiagnosticsOptions.Schema);
        return new LiveSettings(
            [
                new LiveSettingsTarget(SettingsSchema.Platform, string.Empty, _app),
                new LiveSettingsTarget(SettingsSchema.Platform, "Server", _server),
                new LiveSettingsTarget(DiagnosticsOptions.Schema, DiagnosticsOptions.Section, _diagnostics),
            ],
            schema,
            UserFile);
    }

    [TestMethod]
    public void WhatTheClientSays_IsSetOnTheSettings_AndWhatItStopsSaying_IsWhatTheyStartedWith()
    {
        var sut = Sut();

        sut.Apply(new Dictionary<string, string>
        {
            ["Server:Verbose"] = "False",
            ["Server:TraceLevel"] = "warning",
            ["Diagnostics:MeaningfulNames:AllowedNames:0"] = "i",
            ["Diagnostics:MeaningfulNames:AllowedNames:1"] = "j",
        });

        Assert.IsFalse(_app.Server.Verbose);
        Assert.IsFalse(_server.Verbose, "both objects of the platform's settings");
        Assert.AreEqual(LogLevel.Warning, _server.TraceLevel);
        CollectionAssert.AreEqual(new[] { "i", "j" }, _diagnostics.MeaningfulNames.AllowedNames);

        sut.Apply(new Dictionary<string, string>());
        Assert.IsTrue(_app.Server.Verbose);
        Assert.AreEqual(LogLevel.Information, _server.TraceLevel);
        Assert.IsEmpty(_diagnostics.MeaningfulNames.AllowedNames);
    }

    [TestMethod]
    public void ASettingThatIsNotLive_OrAValueItCannotBe_IsNotSet()
    {
        Sut().Apply(new Dictionary<string, string> { ["Server:ConnectTimeoutSeconds"] = "5", ["Server:Verbose"] = "banana" });

        Assert.AreEqual(30, _app.Server.ConnectTimeoutSeconds, "read when the component starts");
        Assert.IsTrue(_app.Server.Verbose, "no boolean");
    }

    [TestMethod]
    public void WhatTheClientSaid_IsHandedDown_ThoughTheComponentHasNoneOfIt()
    {
        var sut = Sut();
        var changed = 0;
        sut.Changed += (_, _) => changed++;

        sut.Apply(new Dictionary<string, string> { ["Elsewhere:Knob"] = "on", ["Server:Verbose"] = "False" });

        Assert.AreEqual(1, changed);
        Assert.AreEqual("on", sut.Current["Elsewhere:Knob"], "another component's");
    }

    [TestMethod]
    public void WithNoClient_WhatIsHandedDown_IsWhatTheUserSet_ButWhatTakesEffectNextTime()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(UserFile, """{ "Configuration": { "Server": { "Verbose": false, "ConnectTimeoutSeconds": 5 }, "Elsewhere": { "Knob": "on" } } }""");
        var sut = Sut();

        CollectionAssert.AreEquivalent(new[] { "Server:Verbose", "Elsewhere:Knob" }, sut.Current.Keys.ToArray());

        sut.Refresh();
        Assert.IsFalse(_app.Server.Verbose, "what the user set now");
    }
}

/// <summary>
/// The live settings travel as the configuration section <c>rdcore</c>: the keys of the platform's configuration, a list as an array.
/// </summary>
[TestClass]
public sealed class LiveSettingsProtocolTests
{
    private static readonly Dictionary<string, string> Settings = new()
    {
        ["Server:Verbose"] = "False",
        ["Diagnostics:MeaningfulNames:AllowedNames:0"] = "i",
        ["Diagnostics:MeaningfulNames:AllowedNames:1"] = "j",
    };

    private static JToken[] Answer(params string?[] sections)
        => [.. LiveSettingsProtocol.Answer(new ConfigurationParams { Items = new Container<ConfigurationItem>(sections.Select(section => new ConfigurationItem { Section = section })) }, Settings)];

    [TestMethod]
    public void EachItem_IsAnsweredWithTheSectionItNames()
    {
        var answers = Answer("rdcore", "rdcore.server", null, "editor");

        Assert.AreEqual("False", answers[0]["Server"]?["Verbose"]?.ToString());
        Assert.AreEqual(JTokenType.Array, answers[0]["Diagnostics"]?["MeaningfulNames"]?["AllowedNames"]?.Type);
        Assert.AreEqual("False", answers[1]["Verbose"]?.ToString(), "a section of it, without regard to case");
        Assert.AreEqual("False", answers[2]["rdcore"]?["Server"]?["Verbose"]?.ToString(), "no section: all of them");
        Assert.AreEqual(JTokenType.Null, answers[3].Type, "another one's section");
    }

    [TestMethod]
    public void WhatIsAnswered_IsReadBackAsTheKeysItWasMadeOf()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(new JObject { ["rdcore"] = Answer("rdcore")[0] }.ToString())))
            .Build();

        CollectionAssert.AreEquivalent(Settings.ToArray(), LiveSettingsProtocol.Read(configuration).ToArray());
    }
}
