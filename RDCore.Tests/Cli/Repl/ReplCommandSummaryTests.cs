using System.Globalization;
using System.Resources;
using System.Runtime.CompilerServices;
using RDCore.CLI;
using RDCore.CLI.App.Repl;

namespace RDCore.Tests.Cli.Repl;

/// <summary>
/// The summary of every shell command, as <c>HELP</c> lists it, is a string of the shell's resources, in English and in French: none is written in the code.
/// </summary>
[TestClass]
public sealed class ReplCommandSummaryTests
{
    // every command of the shell: each class of RDCore.CLI that is one.
    public static IEnumerable<object[]> Commands => typeof(IReplCommand).Assembly.GetTypes()
        .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IReplCommand).IsAssignableFrom(type))
        .OrderBy(type => type.Name, StringComparer.Ordinal)
        .Select(type => new object[] { type.FullName! });

    [TestMethod]
    [DynamicData(nameof(Commands))]
    public void TheSummaryOfACommand_IsAResource_InEnglishAndInFrench(string command)
    {
        // a summary is what the command says it is, whatever it was given to do its work: it is read without the services it is made with.
        var summarized = (IReplCommand)RuntimeHelpers.GetUninitializedObject(typeof(IReplCommand).Assembly.GetType(command, throwOnError: true)!);

        foreach (var culture in new[] { CultureInfo.InvariantCulture, CultureInfo.GetCultureInfo("fr") })
        {
            var shown = CultureInfo.CurrentUICulture;
            CultureInfo.CurrentUICulture = culture;
            try
            {
                var strings = Resources.ResourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: false);
                Assert.IsNotNull(strings, culture.Name);
                var summary = summarized.Summary;
                CollectionAssert.Contains(Values(strings), summary, $"{culture.Name}: {summary}");
            }
            finally
            {
                CultureInfo.CurrentUICulture = shown;
            }
        }
    }

    private static List<string> Values(ResourceSet strings) =>
        [.. strings.Cast<System.Collections.DictionaryEntry>().Select(entry => entry.Value).OfType<string>()];
}
