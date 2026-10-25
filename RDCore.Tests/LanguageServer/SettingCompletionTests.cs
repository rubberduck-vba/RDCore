using RDCore.SDK.Platform.Commands;
using RDCore.SDK.Server.Configuration;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// The key of a setting is completed a part at a time, as a member is - the sections, then what is in the one before the last <c>.</c> - and its value from what
/// the setting written before it can be.
/// </summary>
[TestClass]
public sealed class SettingCompletionTests
{
    private static readonly IReadOnlyList<SettingDescriptor> Settings = SettingsSchema.Platform.Settings.Where(setting => setting.IsSettable).ToList();

    private static readonly VerbDescriptor Config = new()
    {
        Name = "CONFIG",
        Forms =
        [
            [new VerbArgument { Name = "reset", Kind = VerbArgumentKind.Keyword, Values = [new() { Value = "RESET" }] }, new VerbArgument { Name = "setting", Kind = VerbArgumentKind.Setting, Settings = Settings }],
            [new VerbArgument { Name = "setting", Kind = VerbArgumentKind.Setting, Settings = Settings }, new VerbArgument { Name = "value", Kind = VerbArgumentKind.SettingValue, ValueOf = "setting" }],
        ],
    };

    private static string[] Parts(string typed) => [.. VerbCompletion.SettingPartsAfter(Settings, typed).Select(part => part.Setting is null ? part.Part + "." : part.Part)];

    [TestMethod]
    public void NothingTyped_IsOfferedTheSections()
        => CollectionAssert.AreEqual(new[] { "Server.", "Workspace.", "Platform.", "Environment." }, Parts(""));

    [TestMethod]
    public void ASectionTyped_IsOfferedWhatIsInIt_WithoutRegardToCase()
    {
        CollectionAssert.IsSubsetOf(new[] { "TraceLevel", "Verbose", "MaxRestartAttempts" }, Parts("server."));
        CollectionAssert.AreEqual(Parts("SERVER."), Parts("server.Ve"), "what is typed after the last dot is the client's to filter");
        CollectionAssert.Contains(Parts("environment."), "Regional.");
        CollectionAssert.DoesNotContain(Parts("server."), "ClientProcessId", "deployment only");
    }

    [TestMethod]
    public void AKeyThatIsWritten_SaysWhatItsValueIsOf()
    {
        var site = VerbCompletion.Read(Config, " server.verbose ").Single();

        Assert.AreEqual(VerbArgumentKind.SettingValue, site.Argument.Kind);
        Assert.AreEqual("Server.Verbose", site.Setting?.Key);
    }

    [TestMethod]
    public void RESET_IsNoSetting_AndASectionIsOne()
    {
        CollectionAssert.AreEqual(new[] { VerbArgumentKind.Setting }, VerbCompletion.Read(Config, " RESET ").Select(site => site.Argument.Kind).ToArray());
        var afterSection = VerbCompletion.Read(Config, " Environment.Regional ").Single();
        Assert.AreEqual(VerbArgumentKind.SettingValue, afterSection.Argument.Kind, "a section is written as a key is");
        Assert.IsNull(afterSection.Setting, "and is no setting a value is of");
    }
}
