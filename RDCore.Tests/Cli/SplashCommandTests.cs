using System.Globalization;
using RDCore.CLI.App.Commands;
using RDCore.SDK.Platform;

namespace RDCore.Tests.Cli;

/// <summary>
/// <c>rdc splash</c> writes the platform's splash as the SDK says it, line by line, in plain text: what the publish script shows as it ends.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class SplashCommandTests
{
    [TestMethod]
    public async Task TheSplashVerb_WritesEachLineOfThePlatformsSplash_InPlainText()
    {
        var before = CultureInfo.CurrentUICulture;
        var output = new StringWriter { NewLine = "\n" };
        int exitCode;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-CA");
            exitCode = await new SplashCommand(output).ExecuteAsync([], CancellationToken.None);
        }
        finally
        {
            CultureInfo.CurrentUICulture = before;
        }

        Assert.AreEqual(0, exitCode);
        var expected = PlatformSplash.Lines(CultureInfo.GetCultureInfo("fr-CA")).Select(line => line.Text + "\n");
        Assert.AreEqual(string.Concat(expected), output.ToString());
        StringAssert.Contains(output.ToString(), "tous droits réservés", "the notice is in the language of the user interface");
        Assert.IsFalse(output.ToString().Contains('\u001b'), "no style: the text is plain");
    }

    [TestMethod]
    public void TheSplashVerb_IsNamedSplash()
    {
        var command = new SplashCommand();

        Assert.AreEqual("splash", command.Name);
        CollectionAssert.AreEqual(new[] { "s" }, command.Aliases.ToArray());
    }
}
