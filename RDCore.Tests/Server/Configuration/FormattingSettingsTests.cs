using RDCore.SDK.Formatting;
using RDCore.SDK.Server.Configuration;
using System.Globalization;

namespace RDCore.Tests.Server.Configuration;

/// <summary>
/// The code formatting settings: Rubberduck's indenter settings with the names, types, defaults and limits they have there, and RDCore's own, as settings of the
/// platform that a user sets and that take effect while it runs.
/// </summary>
[TestClass]
public sealed class FormattingSettingsTests
{
    private static readonly string[] RubberduckSettings =
    [
        "IndentEntireProcedureBody", "IndentEnumTypeAsProcedure", "IndentFirstCommentBlock", "IndentFirstDeclarationBlock", "IgnoreEmptyLinesInFirstBlocks",
        "AlignCommentsWithCode", "AlignContinuations", "IgnoreOperatorsInContinuations", "IndentCase", "ForceDebugStatementsInColumn1", "ForceDebugPrintInColumn1",
        "ForceDebugAssertInColumn1", "ForceStopInColumn1", "ForceCompilerDirectivesInColumn1", "IndentCompilerDirectives", "AlignDims", "AlignDimColumn",
        "EndOfLineCommentStyle", "EndOfLineCommentColumnSpaceAlignment", "EmptyLineHandlingMethod", "IndentSpaces", "VerticallySpaceProcedures",
        "LinesBetweenProcedures", "GroupRelatedProperties",
    ];

    private static readonly string[] OwnSettings = ["FormatOnType", "CompleteBlocks", "Snippets", "SnippetDimPlacement", "PrettyList"];

    private static SettingDescriptor Setting(string name)
    {
        Assert.IsTrue(SettingsSchema.Platform.TryFind($"Formatting.{name}", out var setting), name);
        return setting;
    }

    [TestMethod]
    public void TheSchema_HasAllTwentyFourOfRubberducksSettings_AndRDCoresFive_AndNothingElse()
    {
        var keys = SettingsSchema.Platform.Settings.Where(setting => setting.Key.StartsWith("Formatting.", StringComparison.Ordinal)).Select(setting => setting.Key["Formatting.".Length..]).ToArray();

        CollectionAssert.AreEquivalent(RubberduckSettings.Concat(OwnSettings).ToArray(), keys);
        Assert.AreEqual(24, RubberduckSettings.Length);
    }

    [TestMethod]
    [DataRow("IndentSpaces", 0L, 32L)]
    [DataRow("AlignDimColumn", 0L, 100L)]
    [DataRow("EndOfLineCommentColumnSpaceAlignment", 0L, 100L)]
    [DataRow("LinesBetweenProcedures", 0L, 2L)]
    public void ANumber_HasTheLimitsItHasInRubberduck(string name, long least, long greatest)
    {
        var setting = Setting(name);

        Assert.AreEqual(SettingValueKind.Integer, setting.Kind);
        Assert.AreEqual(least, setting.Minimum);
        Assert.AreEqual(greatest, setting.Maximum);
        Assert.IsFalse(SettingsSchema.TryParse(setting, (greatest + 1).ToString(CultureInfo.InvariantCulture), out _));
    }

    [TestMethod]
    [DataRow("EndOfLineCommentStyle", "Absolute,SameGap,StandardGap,AlignInColumn")]
    [DataRow("EmptyLineHandlingMethod", "Ignore,Remove,Indent")]
    [DataRow("SnippetDimPlacement", "AboveBlock,ProcedureTop")]
    public void AChoice_IsOneOfTheNamesRubberduckHasForIt(string name, string choices)
    {
        var setting = Setting(name);

        Assert.AreEqual(SettingValueKind.Choice, setting.Kind);
        CollectionAssert.AreEqual(choices.Split(','), setting.Choices.ToArray());
    }

    [TestMethod]
    public void TheSettings_AreLive_AndNoneIsTrustSensitive()
    {
        foreach (var setting in SettingsSchema.Platform.Settings.Where(setting => setting.Key.StartsWith("Formatting.", StringComparison.Ordinal)))
        {
            Assert.AreEqual(SettingReach.Live, setting.Reach, setting.Key);
            Assert.IsFalse(setting.IsTrustSensitive, setting.Key);
        }
    }

    [TestMethod]
    [DataRow("en")]
    [DataRow("fr")]
    public void EverySetting_IsDescribed_InBothLanguages(string language)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            foreach (var name in RubberduckSettings.Concat(OwnSettings))
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(Setting(name).Description), $"{name} in {language}");
            }
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [TestMethod]
    public void TheDefaults_AreRubberducks_AndRDCoresOwn()
    {
        var options = new SdkFormattingOptions().ToOptions();

        Assert.AreEqual(CodeFormattingOptions.Default, options);
        Assert.IsTrue(options.IndentEntireProcedureBody);
        Assert.IsFalse(options.IndentEnumTypeAsProcedure);
        Assert.IsTrue(options.IndentFirstCommentBlock);
        Assert.IsTrue(options.IndentFirstDeclarationBlock);
        Assert.IsTrue(options.AlignCommentsWithCode);
        Assert.IsTrue(options.AlignContinuations);
        Assert.IsTrue(options.IgnoreOperatorsInContinuations);
        Assert.IsFalse(options.IndentCase);
        Assert.IsTrue(options.IndentCompilerDirectives);
        Assert.IsFalse(options.AlignDims);
        Assert.AreEqual(15, options.AlignDimColumn);
        Assert.AreEqual(EndOfLineCommentStyle.AlignInColumn, options.EndOfLineCommentStyle);
        Assert.AreEqual(50, options.EndOfLineCommentColumnSpaceAlignment);
        Assert.AreEqual(EmptyLineHandlingMethod.Ignore, options.EmptyLineHandlingMethod);
        Assert.AreEqual(4, options.IndentSpaces);
        Assert.IsTrue(options.VerticallySpaceProcedures);
        Assert.AreEqual(1, options.LinesBetweenProcedures);
        Assert.IsFalse(options.GroupRelatedProperties);
        Assert.IsTrue(options.FormatOnType);
        Assert.IsTrue(options.CompleteBlocks);
        Assert.IsTrue(options.Snippets);
        Assert.AreEqual(SnippetDimPlacement.AboveBlock, options.SnippetDimPlacement);
        Assert.IsTrue(options.PrettyList);
    }

    [TestMethod]
    public void ANumberOutOfItsLimits_IsKeptToThem_WhenOptionsAreMadeOfTheSettings()
    {
        var options = new SdkFormattingOptions { IndentSpaces = 99, AlignDimColumn = -3, EndOfLineCommentColumnSpaceAlignment = 101, LinesBetweenProcedures = 7 }.ToOptions();

        Assert.AreEqual(32, options.IndentSpaces);
        Assert.AreEqual(0, options.AlignDimColumn);
        Assert.AreEqual(100, options.EndOfLineCommentColumnSpaceAlignment);
        Assert.AreEqual(2, options.LinesBetweenProcedures);
    }

    [TestMethod]
    public void TheDebugStatementsSetting_SaysItOfThemAll()
    {
        var all = new CodeFormattingOptions { ForceDebugStatementsInColumn1 = true };
        var print = new CodeFormattingOptions { ForceDebugPrintInColumn1 = true };

        Assert.IsTrue(all.ForcesDebugPrintInColumn1 && all.ForcesDebugAssertInColumn1 && all.ForcesStopInColumn1);
        Assert.IsTrue(print.ForcesDebugPrintInColumn1);
        Assert.IsFalse(print.ForcesDebugAssertInColumn1 || print.ForcesStopInColumn1);
    }

    [TestMethod]
    public void ALiveSetting_SetsTheFormattingOptions_InPlace()
    {
        var options = new SdkAppOptions();
        var schema = SettingsSchema.Platform;
        var live = new LiveSettings([new LiveSettingsTarget(schema, string.Empty, options)], schema, Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json"));

        live.Apply(new Dictionary<string, string> { ["Formatting:IndentSpaces"] = "2", ["Formatting:EndOfLineCommentStyle"] = "SameGap", ["Formatting:PrettyList"] = "False" });

        Assert.AreEqual(2, options.Formatting.IndentSpaces);
        Assert.AreEqual(EndOfLineCommentStyle.SameGap, options.Formatting.EndOfLineCommentStyle);
        Assert.IsFalse(options.Formatting.PrettyList);

        live.Apply(new Dictionary<string, string>());

        Assert.AreEqual(4, options.Formatting.IndentSpaces, "a setting the client says nothing of takes its starting value back");
    }
}
