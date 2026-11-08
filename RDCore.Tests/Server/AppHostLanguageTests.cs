using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RDCore.SDK;
using RDCore.SDK.Platform;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Configuration;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Abstractions;

namespace RDCore.Tests.Server;

/// <summary>
/// A component applies the language its configuration sets (<c>Environment.Regional.Language</c>, here as the user set it) as it starts, before the command
/// line is answered and before the application runs, and says once what came of it, at the level it calls for.
/// </summary>
/// <remarks>
/// These change the process's default culture and the folder of the platform's user, which every test that runs at the same time would read: they run on
/// their own.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class AppHostLanguageTests
{
    private string _user = null!;
    private string? _userData;
    private CultureInfo? _defaultUICulture;
    private CultureInfo _uiCulture = CultureInfo.CurrentUICulture;
    private CultureInfo _culture = CultureInfo.CurrentCulture;

    [TestInitialize]
    public void Remember()
    {
        _defaultUICulture = CultureInfo.DefaultThreadCurrentUICulture;
        (_uiCulture, _culture) = (CultureInfo.CurrentUICulture, CultureInfo.CurrentCulture);
        _userData = Environment.GetEnvironmentVariable(PlatformEnvironment.UserDataEnvironmentVariable);
        _user = Path.Combine(Path.GetTempPath(), "RDCore", "ui-language", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_user);
        Environment.SetEnvironmentVariable(PlatformEnvironment.UserDataEnvironmentVariable, _user);

        CultureInfo.DefaultThreadCurrentUICulture = null;
        (CultureInfo.CurrentUICulture, CultureInfo.CurrentCulture) = (CultureInfo.GetCultureInfo("en-US"), CultureInfo.GetCultureInfo("en-US"));
        RecordingApp.Seen.Clear();
        RecordingApp.Logged.Clear();
    }

    [TestCleanup]
    public void Restore()
    {
        Environment.SetEnvironmentVariable(PlatformEnvironment.UserDataEnvironmentVariable, _userData);
        CultureInfo.DefaultThreadCurrentUICulture = _defaultUICulture;
        (CultureInfo.CurrentUICulture, CultureInfo.CurrentCulture) = (_uiCulture, _culture);
        try
        {
            Directory.Delete(_user, recursive: true);
        }
        catch (IOException)
        {
            // a temporary folder.
        }
    }

    private void UserSets(string language)
        => new UserSettingsStore(new FileSystem(), UserSettingsStore.PathIn(PlatformEnvironment.Default), SettingsSchema.Platform).Set("Environment.Regional.Language", language);

    [TestMethod]
    [DataRow("fr", "fr", LogLevel.Information, "🌐 L'interface est en 'fr'", DisplayName = "a language the platform is translated in: said in it")]
    [DataRow("de", "de", LogLevel.Information, "The platform has no translation in 'de'", DisplayName = "one it is not: English, said at Information")]
    [DataRow("not-a-culture-at-all-xx", "en-US", LogLevel.Warning, "'not-a-culture-at-all-xx' is not a culture", DisplayName = "no culture: the machine's language, with a warning")]
    public async Task AComponent_StartsInTheLanguageItsUserSet_AndSaysSoOnce(string language, string started, LogLevel level, string saying)
    {
        UserSets(language);

        Assert.AreEqual(0, await new LanguageTestAppHost().RunAsync([]));

        CollectionAssert.AreEqual(new[] { ("answered", started), ("ran", started) }, RecordingApp.Seen.ToArray(), "the command line is answered in it, then the application runs in it");
        var said = RecordingApp.Logged.Where(entry => entry.Message.Contains("Environment.Regional.Language", StringComparison.Ordinal)).ToArray();
        Assert.HasCount(1, said, string.Join(" | ", RecordingApp.Logged));
        Assert.AreEqual(level, said[0].Level);
        StringAssert.StartsWith(said[0].Message, saying);
        Assert.AreEqual("en-US", CultureInfo.CurrentCulture.Name, "the language is not how numbers and dates are written");
    }

    [TestMethod]
    public async Task AComponentWhoseUserSetsNoLanguage_StartsInTheMachines_AndSaysNothingOfIt()
    {
        Assert.AreEqual(0, await new LanguageTestAppHost().RunAsync([]));

        CollectionAssert.AreEqual(new[] { ("answered", "en-US"), ("ran", "en-US") }, RecordingApp.Seen.ToArray());
        Assert.IsGreaterThan(0, RecordingApp.Logged.Count, "the application logs as it starts");
        Assert.IsFalse(RecordingApp.Logged.Any(entry => entry.Message.Contains("Environment.Regional.Language", StringComparison.Ordinal)), string.Join(" | ", RecordingApp.Logged));
    }

    private sealed class LanguageTestAppHost : AppHost<RecordingApp>
    {
        protected internal override bool TryAnswerCommandLine(string[] args, out int exitCode)
        {
            RecordingApp.Seen.Enqueue(("answered", CultureInfo.CurrentUICulture.Name));
            return base.TryAnswerCommandLine(args, out exitCode);
        }

        protected override void Configure(IConfigurationBuilder configuration, IServiceCollection services, string[] args) { }
    }

    internal sealed class RecordingApp : IRDCoreApp
    {
        public static ConcurrentQueue<(string What, string Culture)> Seen { get; } = [];

        public static ConcurrentQueue<(LogLevel Level, string Message)> Logged { get; } = [];

        public CoreServerComponent PlatformComponent => CoreServerComponent.LanguageServer;

        public Task RunAsync(IServiceProvider provider, string[] args)
        {
            Seen.Enqueue(("ran", CultureInfo.CurrentUICulture.Name));
            return Task.CompletedTask;
        }

        public void LogIfEnabled(LogLevel logLevel, string message) => Logged.Enqueue((logLevel, message));

        public void Dispose() { }
    }
}
