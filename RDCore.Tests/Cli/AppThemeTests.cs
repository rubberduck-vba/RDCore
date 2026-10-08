using Microsoft.Extensions.Options;
using NSubstitute;
using RDCore.CLI;
using RDCore.CLI.Themes;
using RDCore.SDK.ConsoleIO;
using RDCore.SDK.ConsoleIO.Model;
using Spectre.Console;
using System.IO.Abstractions;

namespace RDCore.Tests.Cli;

[TestClass]
public sealed class AppThemeTests
{
    private static AppTheme BuiltInDefault()
    {
        var document = new AppThemeLoaderService(new FileSystem()).LoadBuiltInDefault();
        return new AppTheme(document);
    }

    [TestMethod]
    public void BuiltInDefault_ResolvesEveryStyleToAParseableToken()
    {
        var theme = BuiltInDefault();

        foreach (var kind in Enum.GetValues<MessageKind>())
        {
            foreach (var part in new[] { MessagePart.Title, MessagePart.Body, MessagePart.Verbose, MessagePart.Timestamp, MessagePart.Metric, MessagePart.StackTrace })
            {
                var token = theme.GetStyle(kind, part);
                _ = Style.Parse(token); // throws if the resolver produced garbage
            }
        }

        // palette reference (#e95653) — not the literal "error" string.
        StringAssert.StartsWith(theme.GetStyle(MessageKind.Error, MessagePart.Body), "#");
    }

    [TestMethod]
    public void UnknownValue_FallsBackToDefaultToken()
    {
        var theme = new AppTheme(new ThemeDocument
        {
            Messages = new() { ["error"] = new() { Body = "not-a-real-colour-name" } },
        });

        Assert.AreEqual(AppTheme.FallbackToken, theme.GetStyle(MessageKind.Error, MessagePart.Body));
    }

    [TestMethod]
    public async Task Service_SetTheme_RejectsUnknownName()
    {
        var options = Options.Create(new AppOptions());
        var service = new AppThemeService(options, new AppThemeLoaderService(new FileSystem()), Substitute.For<IConsoleShellFrame>());
        await service.InitializeAsync(CancellationToken.None);

        Assert.IsTrue(service.SetTheme("dark"));
        Assert.IsFalse(service.SetTheme("does-not-exist"));
        Assert.AreEqual("dark", service.Theme.Name);
    }
}
