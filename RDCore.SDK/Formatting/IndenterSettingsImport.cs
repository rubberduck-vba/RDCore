using Newtonsoft.Json.Linq;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server.Configuration;
using System.Globalization;

namespace RDCore.SDK.Formatting;

/// <summary>
/// The indenter settings Rubberduck has, as <c>Formatting</c> settings of the platform: the mapping of what a client read of them
/// (<see cref="RubberduckIndenterSettingsResult"/>) to the settings of <see cref="SdkFormattingOptions"/>.
/// </summary>
/// <remarks>
/// <para>
/// The mapping is made here, once, so that every client only reads Rubberduck's settings and says them as they are. A setting is mapped only when the source says it:
/// one it says nothing of is not one the user chose, and keeps its default. A number is kept to the limits of its setting, as Rubberduck keeps it.
/// </para>
/// <para>
/// From Rubberduck's configuration, a child of <c>IndenterSettings</c> is the setting of its name. From the legacy Smart Indenter's registry key
/// (<strong>Rubberduck</strong> <c>IndenterSettings.LoadLegacyFromRegistry</c>), <c>IndentProc</c> is <c>IndentEntireProcedureBody</c>, <c>IndentFirst</c>
/// <c>IndentFirstCommentBlock</c>, <c>IndentDim</c> <c>IndentFirstDeclarationBlock</c>, <c>IndentCmt</c> <c>AlignCommentsWithCode</c>, <c>AlignContinued</c>
/// <c>AlignContinuations</c>, <c>AlignIgnoreOps</c> <c>IgnoreOperatorsInContinuations</c>, <c>IndentCase</c>, <c>DebugCol1</c> all four <c>Force...InColumn1</c> debug
/// settings, <c>CompilerCol1</c> <c>ForceCompilerDirectivesInColumn1</c>, <c>IndentCompiler</c> <c>IndentCompilerDirectives</c>, <c>AlignDim</c> <c>AlignDims</c>,
/// <c>AlignDimCol</c> <c>AlignDimColumn</c>, <c>EOLComments</c> <c>EndOfLineCommentStyle</c> (<c>AlignInCol</c> is <c>AlignInColumn</c>), <c>EOLAlignCol</c>
/// <c>EndOfLineCommentColumnSpaceAlignment</c>; a flag is <c>Y</c> or it is not, and an empty one is not said. The VBA editor's <c>TabWidth</c> is <c>IndentSpaces</c>.
/// </para>
/// </remarks>
/// <param name="Source">Where the settings were found.</param>
/// <param name="Values">The settings, by their keys: <c>Formatting.IndentSpaces</c>, with the value as it is written (<see cref="SettingsSchema.TryParse"/>).</param>
public sealed record class IndenterSettingsImport(RubberduckIndenterSettingsSource Source, IReadOnlyDictionary<string, string> Values)
{
    private const string Section = "Formatting";

    private static readonly SettingsSchema Schema = SettingsSchema.Platform;

    /// <summary>The key of a <c>Formatting</c> setting.</summary>
    /// <param name="name">The name the setting has in Rubberduck and here.</param>
    public static string KeyOf(string name) => $"{Section}.{name}";

    /// <summary>
    /// Maps what a client read of Rubberduck's indenter settings to <c>Formatting</c> settings.
    /// </summary>
    /// <param name="read">What the client read.</param>
    /// <returns>The settings; <see langword="null"/> when the client found none, or none of what it found is a setting.</returns>
    public static IndenterSettingsImport? From(RubberduckIndenterSettingsResult read)
    {
        var values = read.Source switch
        {
            RubberduckIndenterSettingsSource.RubberduckConfig => FromConfig(read.Values),
            RubberduckIndenterSettingsSource.SmartIndenterRegistry => FromSmartIndenter(read.Values, read.TabWidth),
            _ => [],
        };

        return values.Count == 0 ? null : new IndenterSettingsImport(read.Source, values);
    }

    /// <summary>
    /// Sets the settings for the user, as the <c>CONFIG</c> verb does: what a client whose settings are the user's file does with the settings it is asked to apply
    /// (<see cref="RubberduckIndenterSettingsApplyParams"/>).
    /// </summary>
    /// <param name="store">The user's settings.</param>
    /// <param name="settings">The settings, by their keys, with the value as it is written.</param>
    /// <returns>How many were set.</returns>
    public static int ApplyTo(UserSettingsStore store, IReadOnlyDictionary<string, string> settings)
        => settings.Count(setting => store.Set(setting.Key, setting.Value).IsSet);

    private static Dictionary<string, string> FromConfig(JObject read)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, token) in read)
        {
            if (Schema.TryFind(KeyOf(name), out var setting) && TextOf(token) is { } text && Coerced(setting, text) is { } value)
            {
                values[setting.Key] = value;
            }
        }

        return values;
    }

    private static Dictionary<string, string> FromSmartIndenter(JObject read, int? tabWidth)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void Flag(string name, params string[] settings)
        {
            // an empty value keeps the setting as it is; anything but "Y" is no.
            if (read.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out var token) && token is { Type: JTokenType.String } && token.Value<string>() is { Length: > 0 } text)
            {
                foreach (var setting in settings)
                {
                    values[KeyOf(setting)] = text.Trim().Equals("Y", StringComparison.Ordinal) ? bool.TrueString : bool.FalseString;
                }
            }
        }

        void Number(string name, string setting)
        {
            if (read.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out var token) && token is { Type: JTokenType.Integer } && token.Value<long>() is var number and >= 0
                && Schema.TryFind(KeyOf(setting), out var descriptor))
            {
                values[descriptor.Key] = Math.Min(number, descriptor.Maximum!.Value).ToString(CultureInfo.InvariantCulture);
            }
        }

        Flag("IndentProc", nameof(CodeFormattingOptions.IndentEntireProcedureBody));
        Flag("IndentFirst", nameof(CodeFormattingOptions.IndentFirstCommentBlock));
        Flag("IndentDim", nameof(CodeFormattingOptions.IndentFirstDeclarationBlock));
        Flag("IndentCmt", nameof(CodeFormattingOptions.AlignCommentsWithCode));
        Flag("AlignContinued", nameof(CodeFormattingOptions.AlignContinuations));
        Flag("AlignIgnoreOps", nameof(CodeFormattingOptions.IgnoreOperatorsInContinuations));
        Flag("IndentCase", nameof(CodeFormattingOptions.IndentCase));
        Flag("DebugCol1", nameof(CodeFormattingOptions.ForceDebugStatementsInColumn1), nameof(CodeFormattingOptions.ForceDebugPrintInColumn1),
            nameof(CodeFormattingOptions.ForceDebugAssertInColumn1), nameof(CodeFormattingOptions.ForceStopInColumn1));
        Flag("CompilerCol1", nameof(CodeFormattingOptions.ForceCompilerDirectivesInColumn1));
        Flag("IndentCompiler", nameof(CodeFormattingOptions.IndentCompilerDirectives));
        Flag("AlignDim", nameof(CodeFormattingOptions.AlignDims));
        Number("AlignDimCol", nameof(CodeFormattingOptions.AlignDimColumn));
        Number("EOLAlignCol", nameof(CodeFormattingOptions.EndOfLineCommentColumnSpaceAlignment));

        if (read.TryGetValue("EOLComments", StringComparison.OrdinalIgnoreCase, out var style) && style is { Type: JTokenType.String })
        {
            var mapped = style.Value<string>() switch
            {
                "Absolute" => nameof(EndOfLineCommentStyle.Absolute),
                "SameGap" => nameof(EndOfLineCommentStyle.SameGap),
                "StandardGap" => nameof(EndOfLineCommentStyle.StandardGap),
                "AlignInCol" => nameof(EndOfLineCommentStyle.AlignInColumn),
                _ => null,
            };

            if (mapped is not null)
            {
                values[KeyOf(nameof(CodeFormattingOptions.EndOfLineCommentStyle))] = mapped;
            }
        }

        if (tabWidth is { } width && Schema.TryFind(KeyOf(nameof(CodeFormattingOptions.IndentSpaces)), out var indent))
        {
            values[indent.Key] = Math.Clamp(width, (int)indent.Minimum!.Value, (int)indent.Maximum!.Value).ToString(CultureInfo.InvariantCulture);
        }

        return values;
    }

    private static string? TextOf(JToken token) => token.Type is JTokenType.String or JTokenType.Integer or JTokenType.Boolean ? Convert.ToString(((JValue)token).Value, CultureInfo.InvariantCulture) : null;

    // a value as the setting is written: a number kept to its limits as Rubberduck keeps it; nothing for a value the setting cannot be.
    private static string? Coerced(SettingDescriptor setting, string text)
    {
        var trimmed = text.Trim();
        if (setting.Kind == SettingValueKind.Integer && long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            return Math.Clamp(number, setting.Minimum ?? long.MinValue, setting.Maximum ?? long.MaxValue).ToString(CultureInfo.InvariantCulture);
        }

        return SettingsSchema.TryParse(setting, trimmed, out var parsed) ? parsed switch
        {
            bool flag => flag ? bool.TrueString : bool.FalseString,
            _ => parsed.ToString(),
        } : null;
    }
}
