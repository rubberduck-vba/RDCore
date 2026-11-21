using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

// the fix that adds a name to the names a naming rule allows (NameRuleOptions.AllowedNames): the client adds it to the user's list of the setting, and the
// rule accepts it from then on, wherever it is written.
internal static class AllowedNames
{
    public static SettingFix Allowing(string rule, string name) => new()
    {
        Title = AnalyzerMessages.Format(RDCoreDiagnosticsResources.AllowName_Fix, name),
        Key = $"{DiagnosticsOptions.Section}.{rule}.{nameof(NameRuleOptions.AllowedNames)}",
        Value = name,
    };
}
