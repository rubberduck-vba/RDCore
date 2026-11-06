using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol.Serialization;
using RDCore.CLI.App.Configuration;
using RDCore.SDK.Client;
using RDCore.SDK.ConsoleIO.Model;
using RDCore.SDK.Extensibility;
using RDCore.SDK.Formatting;
using RDCore.SDK.Platform;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server.Configuration;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RDCore.Tests.Server.Configuration;

/// <summary>
/// The indenter settings a user had in Rubberduck are read where Rubberduck keeps them - its configuration, then the registry key of the legacy Smart Indenter - and
/// said as they are; the platform maps them to its <c>Formatting</c> settings once, and a client takes them as its own.
/// </summary>
[TestClass]
public sealed class RubberduckIndenterImportTests
{
    private static readonly string AppData = Path.Combine(Path.GetTempPath(), "rdcore-rubberduck-appdata");

    private const string Config = """
        <?xml version="1.0" encoding="utf-8"?>
        <Configuration>
          <UserSettings>
            <GeneralSettings><Language>fr-CA</Language></GeneralSettings>
            <IndenterSettings>
              <IndentEntireProcedureBody>false</IndentEntireProcedureBody>
              <IndentCase>true</IndentCase>
              <AlignDimColumn>150</AlignDimColumn>
              <EndOfLineCommentStyle>SameGap</EndOfLineCommentStyle>
              <EmptyLineHandlingMethod>Remove</EmptyLineHandlingMethod>
              <IndentSpaces>2</IndentSpaces>
              <LinesBetweenProcedures>7</LinesBetweenProcedures>
              <SomethingRubberduckAddedLater>x</SomethingRubberduckAddedLater>
            </IndenterSettings>
          </UserSettings>
        </Configuration>
        """;

    private sealed class FakeRegistry(IReadOnlyDictionary<string, object>? smartIndenter, int? tabWidth = null) : ILegacyIndenterRegistry
    {
        public IReadOnlyDictionary<string, object>? ReadSmartIndenter() => smartIndenter;

        public int? ReadTabWidth() => tabWidth;
    }

    private static MockFileSystem WithConfig(string xml)
        => new(new Dictionary<string, MockFileData> { [RubberduckIndenterSettingsReader.ConfigPathIn(AppData)] = new MockFileData(xml) });

    private static RubberduckIndenterSettingsReader Reader(MockFileSystem files, ILegacyIndenterRegistry? registry = null, bool isWindows = true)
        => new(files, AppData, registry ?? new FakeRegistry(null), isWindows);

    private static IndenterSettingsImport Mapped(RubberduckIndenterSettingsResult read) => IndenterSettingsImport.From(read)!;

    [TestMethod]
    public void RubberducksConfiguration_IsReadAsItIs_NothingMapped()
    {
        var read = Reader(WithConfig(Config)).Read();

        Assert.AreEqual(RubberduckIndenterSettingsSource.RubberduckConfig, read.Source);
        Assert.AreEqual("false", read.Values["IndentEntireProcedureBody"].GetString());
        Assert.AreEqual("150", read.Values["AlignDimColumn"].GetString());
        Assert.IsFalse(read.Values.ContainsKey("GeneralSettings"), "only the indenter's settings");
        Assert.IsFalse(read.Values.ContainsKey("Language"));
    }

    [TestMethod]
    public void TheSettingsOfAConfiguration_AreMappedToFormattingSettings_KeptToTheirLimits()
    {
        var import = Mapped(Reader(WithConfig(Config)).Read());

        Assert.AreEqual(RubberduckIndenterSettingsSource.RubberduckConfig, import.Source);
        Assert.AreEqual("False", import.Values["Formatting.IndentEntireProcedureBody"]);
        Assert.AreEqual("True", import.Values["Formatting.IndentCase"]);
        Assert.AreEqual("100", import.Values["Formatting.AlignDimColumn"], "kept to 100, as Rubberduck keeps it");
        Assert.AreEqual("SameGap", import.Values["Formatting.EndOfLineCommentStyle"]);
        Assert.AreEqual("Remove", import.Values["Formatting.EmptyLineHandlingMethod"]);
        Assert.AreEqual("2", import.Values["Formatting.IndentSpaces"]);
        Assert.AreEqual("2", import.Values["Formatting.LinesBetweenProcedures"], "kept to 2");
        Assert.AreEqual(7, import.Values.Count, "a setting of a later Rubberduck is none of ours, and one the file does not say keeps its default");
    }

    [TestMethod]
    [DataRow("<notxml")]
    [DataRow("<Configuration><UserSettings/></Configuration>")]
    public void AConfiguration_WithNoIndenterSettings_HasNone(string xml)
    {
        Assert.AreEqual(RubberduckIndenterSettingsSource.None, Reader(WithConfig(xml)).Read().Source);
        Assert.IsNull(IndenterSettingsImport.From(RubberduckIndenterSettingsResult.Nothing));
    }

    [TestMethod]
    public void AClientSaysWhatItFinds_AndTheMappingKeepsWhatIsASetting()
    {
        var read = Reader(WithConfig("<Configuration><UserSettings><IndenterSettings><Unknown>1</Unknown></IndenterSettings></UserSettings></Configuration>")).Read();

        Assert.AreEqual("1", read.Values["Unknown"].GetString(), "read as it is");
        Assert.IsNull(IndenterSettingsImport.From(read), "and none of it is a setting");
    }

    [TestMethod]
    public void TheLegacySmartIndenter_IsReadFromTheRegistryAsItIs_WhenRubberduckHasNoConfiguration()
    {
        var registry = new FakeRegistry(new Dictionary<string, object> { ["IndentProc"] = "Y", ["AlignDimCol"] = "40" }, tabWidth: 3);

        var read = Reader(new MockFileSystem(), registry).Read();

        Assert.AreEqual(RubberduckIndenterSettingsSource.SmartIndenterRegistry, read.Source);
        Assert.AreEqual("Y", read.Values["IndentProc"].GetString());
        Assert.AreEqual("40", read.Values["AlignDimCol"].GetString(), "as SaveSetting wrote it: text");
        Assert.AreEqual(3, read.TabWidth);
    }

    [TestMethod]
    public void RubberducksConfiguration_IsPreferred_ToTheRegistry()
        => Assert.AreEqual(RubberduckIndenterSettingsSource.RubberduckConfig,
            Reader(WithConfig(Config), new FakeRegistry(new Dictionary<string, object> { ["IndentProc"] = "Y" })).Read().Source);

    [TestMethod]
    public void OnAnotherPlatformThanWindows_ThereIsNothingToRead()
        => Assert.AreEqual(RubberduckIndenterSettingsSource.None, Reader(WithConfig(Config), new FakeRegistry(new Dictionary<string, object> { ["IndentProc"] = "Y" }), isWindows: false).Read().Source);

    [TestMethod]
    public void TheSmartIndentersSettings_AreMappedAsRubberduckMapsThem()
    {
        var registry = new Dictionary<string, object>
        {
            ["IndentProc"] = "N",
            ["IndentFirst"] = "Y",
            ["IndentDim"] = string.Empty,
            ["DebugCol1"] = "Y",
            ["CompilerCol1"] = "y",
            ["AlignDimCol"] = "150",
            ["EOLAlignCol"] = -1,
            ["EOLComments"] = "AlignInCol",
            ["IndentCase"] = 1,
        };

        var import = Mapped(RubberduckIndenterSettingsReader.FromSmartIndenter(registry, tabWidth: 40));

        Assert.AreEqual("False", import.Values["Formatting.IndentEntireProcedureBody"], "anything but Y is no");
        Assert.AreEqual("True", import.Values["Formatting.IndentFirstCommentBlock"]);
        Assert.IsFalse(import.Values.ContainsKey("Formatting.IndentFirstDeclarationBlock"), "an empty value says nothing");
        foreach (var debug in new[] { "ForceDebugStatementsInColumn1", "ForceDebugPrintInColumn1", "ForceDebugAssertInColumn1", "ForceStopInColumn1" })
        {
            Assert.AreEqual("True", import.Values[$"Formatting.{debug}"], "DebugCol1 says all four");
        }

        Assert.AreEqual("False", import.Values["Formatting.ForceCompilerDirectivesInColumn1"], "only a capital Y is yes");
        Assert.AreEqual("100", import.Values["Formatting.AlignDimColumn"], "kept to 100");
        Assert.IsFalse(import.Values.ContainsKey("Formatting.EndOfLineCommentColumnSpaceAlignment"), "a negative number says nothing");
        Assert.AreEqual("AlignInColumn", import.Values["Formatting.EndOfLineCommentStyle"]);
        Assert.IsFalse(import.Values.ContainsKey("Formatting.IndentCase"), "a flag is text");
        Assert.AreEqual("32", import.Values["Formatting.IndentSpaces"], "the editor's tab width, kept to 32");
    }

    [TestMethod]
    public void EverySettingRubberduckHas_IsMappedByItsName()
    {
        string[] names =
        [
            "IndentEntireProcedureBody", "IndentEnumTypeAsProcedure", "IndentFirstCommentBlock", "IndentFirstDeclarationBlock", "IgnoreEmptyLinesInFirstBlocks",
            "AlignCommentsWithCode", "AlignContinuations", "IgnoreOperatorsInContinuations", "IndentCase", "ForceDebugStatementsInColumn1", "ForceDebugPrintInColumn1",
            "ForceDebugAssertInColumn1", "ForceStopInColumn1", "ForceCompilerDirectivesInColumn1", "IndentCompilerDirectives", "AlignDims", "AlignDimColumn",
            "EndOfLineCommentStyle", "EndOfLineCommentColumnSpaceAlignment", "EmptyLineHandlingMethod", "IndentSpaces", "VerticallySpaceProcedures",
            "LinesBetweenProcedures", "GroupRelatedProperties",
        ];
        var values = new Dictionary<string, JsonElement>();
        foreach (var name in names)
        {
            values[name] = JsonSerializer.SerializeToElement(name switch
            {
                "EndOfLineCommentStyle" => "StandardGap",
                "EmptyLineHandlingMethod" => "Indent",
                "AlignDimColumn" or "EndOfLineCommentColumnSpaceAlignment" or "IndentSpaces" or "LinesBetweenProcedures" => "1",
                _ => "true",
            });
        }

        var import = Mapped(new RubberduckIndenterSettingsResult { Source = RubberduckIndenterSettingsSource.RubberduckConfig, Values = values });

        CollectionAssert.AreEquivalent(names.Select(name => $"Formatting.{name}").ToArray(), import.Values.Keys.ToArray());
    }

    // the wire: the answer is camelCase JSON with the source as its name, as a client with the Protocol package alone writes it and as the shell does.
    [TestMethod]
    public void TheAnswer_TravelsAsJson_WithTheSourceByName()
    {
        var answer = Reader(WithConfig(Config)).Read();

        foreach (var written in new[]
        {
            JsonSerializer.Serialize(answer, RDCorePlatformJson.Options),
            new LspSerializer().SerializeObject(new RubberduckIndenterSettingsReply(answer)),
        })
        {
            var json = JsonNode.Parse(written)!.AsObject();

            Assert.AreEqual("RubberduckConfig", (string?)json["source"], written);
            Assert.AreEqual("2", (string?)json["values"]!["IndentSpaces"], written);
            Assert.IsTrue(json.ContainsKey("tabWidth"), written);
            Assert.IsNull(json["tabWidth"], written);
        }
    }

    // CONFIG IMPORT RUBBERDUCK, with no round trip: the shell reads, maps and stores.
    private static readonly string UserFolder = Path.Combine(Path.GetTempPath(), "rdcore-import-user");

    private (ConfigCommand Command, MockFileSystem Files, PlatformSettings Settings) Shell(RubberduckIndenterSettingsReader? reader)
    {
        var files = new MockFileSystem();
        var environment = Substitute.For<IPlatformEnvironment>();
        environment.UserDataDirectory.Returns(UserFolder);
        var extensions = Substitute.For<IExtensionsProvider>();
        extensions.Discover().Returns([]);
        var settings = new PlatformSettings(files, environment, extensions);
        return (new ConfigCommand(settings, appliesLive: true, reader), files, settings);
    }

    [TestMethod]
    public void ConfigImportRubberduck_SetsTheFormattingSettingsForTheUser()
    {
        var (command, _, settings) = Shell(Reader(WithConfig(Config)));

        var output = command.Run("import rubberduck").Single();

        Assert.AreEqual(MessageKind.Success, output.Kind);
        Assert.AreEqual(IndenterSettingsImportText.Imported(new IndenterSettingsImportResult(RubberduckIndenterSettingsSource.RubberduckConfig, 7, [])), output.Text);
        var set = settings.Store.Read().ToDictionary(value => value.Setting.Key, value => value.Value);
        Assert.AreEqual("2", set["Formatting.IndentSpaces"]);
        Assert.AreEqual("True", set["Formatting.IndentCase"]);
    }

    [TestMethod]
    public void ConfigImportRubberduck_SaysThereIsNothing_WhenThereIsNothing()
    {
        var (command, _, settings) = Shell(Reader(new MockFileSystem()));

        var output = command.Run("IMPORT RUBBERDUCK").Single();

        Assert.AreEqual(MessageKind.Information, output.Kind);
        Assert.AreEqual(IndenterSettingsImportText.Nothing(), output.Text);
        Assert.AreEqual(0, settings.Store.Read().Count);
    }

    [TestMethod]
    [DataRow("import")]
    [DataRow("import excel")]
    public void ConfigImport_OfAnythingButRubberduck_IsAnError(string arguments)
    {
        var (command, _, _) = Shell(Reader(WithConfig(Config)));

        Assert.AreEqual(MessageKind.Error, command.Run(arguments).Single().Kind);
    }

    [TestMethod]
    public void TheConfigVerb_CompletesImportRubberduck()
    {
        var (command, _, _) = Shell(null);

        var forms = command.Describe([]).Forms;

        Assert.IsTrue(forms.Any(form => form.Count == 2 && form[0].Values.Any(value => value.Value == "IMPORT") && form[1].Values.Any(value => value.Value == "RUBBERDUCK")));
    }

    // the flow: the platform asks the client what it found, maps it, and asks it to take it.
    private static Task<RubberduckIndenterSettingsResult> Read(RubberduckIndenterSettingsResult result) => Task.FromResult(result);

    private static RubberduckIndenterSettingsResult Found() => Reader(WithConfig(Config)).Read();

    [TestMethod]
    public async Task TheImport_AsksTheClientToTakeTheMappedSettings_AndSaysWhatItDid()
    {
        RubberduckIndenterSettingsApplyParams? asked = null;

        var outcome = await IndenterSettingsImportFlow.ImportAsync(_ => Read(Found()), (request, _) =>
        {
            asked = request;
            return Task.FromResult(new RubberduckIndenterSettingsApplyResult { Applied = 7 });
        }, CancellationToken.None);

        Assert.AreEqual("2", asked!.Settings["Formatting.IndentSpaces"]);
        Assert.AreEqual(RubberduckIndenterSettingsSource.RubberduckConfig, asked.Source);
        Assert.AreEqual(7, outcome.Applied);
        Assert.AreEqual(7, outcome.Found);
    }

    [TestMethod]
    public async Task AClientThatDeclines_IsToldSo()
    {
        var outcome = await IndenterSettingsImportFlow.ImportAsync(_ => Read(Found()), (_, _) => Task.FromResult(new RubberduckIndenterSettingsApplyResult()), CancellationToken.None);

        Assert.AreEqual(0, outcome.Applied);
        Assert.AreEqual(IndenterSettingsImportText.Declined(new IndenterSettingsImportResult(RubberduckIndenterSettingsSource.None, 0, [])), outcome.Message);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AClientThatTakesSome_ButDoesNotSayWhichItLeftOut_IsToldHowManyWereNot(bool answersNull)
    {
        // an older client answers with the number of settings it took and nothing else, which is a list that is absent, or null.
        var found = Found();
        var outcome = await IndenterSettingsImportFlow.ImportAsync(_ => Read(found), (_, _) =>
            Task.FromResult(new RubberduckIndenterSettingsApplyResult { Applied = 3, Refused = answersNull ? null! : [] }), CancellationToken.None);

        var expected = IndenterSettingsImportText.Imported(new IndenterSettingsImportResult(found.Source, 3, [], outcome.Found - 3));
        Assert.AreEqual(expected, outcome.Message);
    }

    [TestMethod]
    public async Task AClientThatTakesAll_IsNotToldOfAnySettingLeftOut()
    {
        var found = Found();
        var outcome = await IndenterSettingsImportFlow.ImportAsync(_ => Read(found), (request, _) =>
            Task.FromResult(new RubberduckIndenterSettingsApplyResult { Applied = request.Settings.Count }), CancellationToken.None);

        Assert.IsFalse(outcome.Message.Contains("not taken", StringComparison.OrdinalIgnoreCase), outcome.Message);
    }

    [TestMethod]
    public async Task AClientThatFoundNothing_IsNotAskedToApplyAnything()
    {
        var applied = false;

        var outcome = await IndenterSettingsImportFlow.ImportAsync(_ => Read(RubberduckIndenterSettingsResult.Nothing), (_, _) =>
        {
            applied = true;
            return Task.FromResult(new RubberduckIndenterSettingsApplyResult());
        }, CancellationToken.None);

        Assert.IsFalse(applied);
        Assert.AreEqual(IndenterSettingsImportText.Nothing(), outcome.Message);
    }

    private static readonly string Marker = IndenterSettingsOffer.MarkerIn(Path.Combine(Path.GetTempPath(), "rdcore-offer-user"));

    private sealed class Offering(MockFileSystem files)
    {
        public List<string> Questions { get; } = [];

        public int Applications { get; private set; }

        public string? Answer { get; set; } = IndenterSettingsImportText.Import();

        // the client's error, when nobody answered the question: a shell whose input is a script says so.
        public Exception? Unanswered { get; set; }

        // the client's error, when it could not take the settings as its own.
        public Exception? NotApplied { get; set; }

        public Task<IndenterSettingsImportOutcome?> OfferAsync(bool formattingWasSet, RubberduckIndenterSettingsResult found)
            => new IndenterSettingsOffer(files, Marker).OfferAsync(formattingWasSet, _ => Read(found),
                (message, choices, _) =>
                {
                    Questions.Add(message);
                    return Unanswered is { } unanswered ? Task.FromException<string?>(unanswered) : Task.FromResult(Answer);
                },
                (_, _) =>
                {
                    Applications++;
                    return NotApplied is { } notApplied
                        ? Task.FromException<RubberduckIndenterSettingsApplyResult>(notApplied)
                        : Task.FromResult(new RubberduckIndenterSettingsApplyResult { Applied = 7 });
                },
                CancellationToken.None);
    }

    [TestMethod]
    public async Task TheFirstStart_OffersToImport_OnceAndTakesTheSettingsWhenTheUserSaysSo()
    {
        var files = new MockFileSystem();
        var offering = new Offering(files);

        var outcome = await offering.OfferAsync(formattingWasSet: false, Found());

        Assert.AreEqual(7, outcome!.Applied);
        Assert.AreEqual(1, offering.Questions.Count);
        StringAssert.Contains(offering.Questions[0], IndenterSettingsImportText.SourceName(RubberduckIndenterSettingsSource.RubberduckConfig));
        Assert.IsTrue(files.File.Exists(Marker), "remembered");

        Assert.IsNull(await offering.OfferAsync(formattingWasSet: false, Found()), "the next start");
        Assert.AreEqual(1, offering.Questions.Count);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("No thanks")]
    public async Task AUserWhoDoesNotSayYes_IsNotAskedAgain_AndNothingIsApplied(string? answer)
    {
        var files = new MockFileSystem();
        var offering = new Offering(files) { Answer = answer };

        Assert.IsNull(await offering.OfferAsync(formattingWasSet: false, Found()));

        Assert.AreEqual(0, offering.Applications);
        Assert.IsTrue(files.File.Exists(Marker));
    }

    [TestMethod]
    public async Task AQuestionNobodyAnswered_IsNotAnOfferMade_AndIsAskedAtTheNextStart()
    {
        var files = new MockFileSystem();
        var offering = new Offering(files) { Unanswered = new InvalidOperationException("Nobody is at the shell's console to answer the question.") };

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => offering.OfferAsync(formattingWasSet: false, Found()));
        Assert.IsFalse(files.File.Exists(Marker), "nobody answered, so the offer is still to make");

        offering.Unanswered = null;
        Assert.AreEqual(7, (await offering.OfferAsync(formattingWasSet: false, Found()))!.Applied);
        Assert.AreEqual(2, offering.Questions.Count);
        Assert.IsTrue(files.File.Exists(Marker));
    }

    // the offer can't be remembered where it is kept (a folder is in the way, as a file the user can't write would be): what the user chose is done all the same.
    [TestMethod]
    public async Task AnImportTheUserChose_IsApplied_ThoughTheOfferCantBeRemembered()
    {
        var files = new MockFileSystem();
        files.AddDirectory(Marker);
        var offering = new Offering(files);

        var outcome = await offering.OfferAsync(formattingWasSet: false, Found());

        Assert.AreEqual(7, outcome!.Applied);
        Assert.AreEqual(1, offering.Applications);
    }

    // the settings the user chose to import, that the client could not take: the import is not done, and the offer is made again at the next start.
    [TestMethod]
    public async Task AnImportTheClientCouldNotApply_IsOfferedAgainAtTheNextStart()
    {
        var files = new MockFileSystem();
        var offering = new Offering(files) { NotApplied = new InvalidOperationException("The client could not write its settings.") };

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => offering.OfferAsync(formattingWasSet: false, Found()));
        Assert.IsFalse(files.File.Exists(Marker), "the offer is still to make");

        offering.NotApplied = null;
        Assert.AreEqual(7, (await offering.OfferAsync(formattingWasSet: false, Found()))!.Applied);
        Assert.AreEqual(2, offering.Questions.Count);
        Assert.IsTrue(files.File.Exists(Marker));
    }

    [TestMethod]
    public async Task AUserWhoSetFormatting_IsNotOffered_AndNeitherIsOneWhenThereIsNothing()
    {
        var files = new MockFileSystem();
        var offering = new Offering(files);

        Assert.IsNull(await offering.OfferAsync(formattingWasSet: true, Found()));
        Assert.IsNull(await offering.OfferAsync(formattingWasSet: false, RubberduckIndenterSettingsResult.Nothing));

        Assert.AreEqual(0, offering.Questions.Count);
        Assert.IsFalse(files.File.Exists(Marker), "nothing was offered, so the offer is still to make");
    }
}
