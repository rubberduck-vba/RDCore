using Microsoft.Extensions.Configuration;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Server.Configuration;
using System.IO.Abstractions.TestingHelpers;

namespace RDCore.Tests.Server.Configuration;

/// <summary>
/// What runs a program when nothing says is one default (<see cref="SdkEnvironmentOptions.DefaultExecutionEngine"/>): the setting's, a profile's, and the one
/// the environment host is deployed with, in <c>RDCore.CLI/appsettings.json</c>. The host's sessions run with the engine its configuration says, a user's
/// setting taking the place of the deployed one.
/// </summary>
[TestClass]
public sealed class ExecutionEngineDefaultTests
{
    private static string Deployed()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RDCore.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.IsNotNull(directory, "the repository root, where RDCore.slnx is");
        return Path.Combine(directory.FullName, "RDCore.CLI", "appsettings.json");
    }

    // the profile the environment host's sessions run with, as its configuration binds it (AppHost.ConfigureExternalServices).
    private static IRuntimeEnvironmentProfile ProfileOf(IConfiguration configuration)
    {
        var options = configuration.GetSection(SettingsSchema.Root).Get<SdkAppOptions>() ?? new SdkAppOptions();
        return RuntimeEnvironmentProfile.From(options.Environment, options.Workspace.SupportedLanguage);
    }

    [TestMethod]
    public void TheEngineOfAProfile_ThatNothingSaysTheEngineOf_IsTheDefault()
    {
        Assert.AreEqual(SdkEnvironmentOptions.DefaultExecutionEngine, new SdkEnvironmentOptions().ExecutionEngine);
        Assert.AreEqual(SdkEnvironmentOptions.DefaultExecutionEngine, new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false).ExecutionEngine);
        Assert.AreEqual(SdkEnvironmentOptions.DefaultExecutionEngine, RuntimeEnvironmentProfile.Default.ExecutionEngine);
        Assert.AreEqual(SdkEnvironmentOptions.DefaultExecutionEngine, ProfileOf(new ConfigurationBuilder().Build()).ExecutionEngine);
    }

    // the default changed without the deployed configuration would change nothing a user runs: the host reads its appsettings.json.
    [TestMethod]
    public void TheEngineTheEnvironmentHostIsDeployedWith_IsTheDefault()
    {
        var configuration = new ConfigurationBuilder().AddJsonFile(Deployed(), optional: false, reloadOnChange: false).Build();

        Assert.AreEqual(SdkEnvironmentOptions.DefaultExecutionEngine.ToString(), configuration["Configuration:Environment:ExecutionEngine"]);
        Assert.AreEqual(SdkEnvironmentOptions.DefaultExecutionEngine, ProfileOf(configuration).ExecutionEngine);
    }

    [TestMethod]
    [DataRow("Compiled", VBExecutionEngine.Compiled)]
    [DataRow("interpreter", VBExecutionEngine.Interpreter)]
    public void TheEngineAUserSets_IsTheOneTheEnvironmentHostsSessionsRunWith(string set, VBExecutionEngine expected)
    {
        var fileSystem = new MockFileSystem();
        var file = Path.Combine(Path.GetTempPath(), "rdcore-user-engine", UserSettingsStore.FileName);
        var stored = new UserSettingsStore(fileSystem, file, SettingsSchema.Platform).Set("Environment.ExecutionEngine", set);
        Assert.IsTrue(stored.IsSet, "a user can set it");

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Deployed(), optional: false, reloadOnChange: false)
            .AddJsonStream(fileSystem.File.OpenRead(file))
            .Build();

        Assert.AreEqual(expected, ProfileOf(configuration).ExecutionEngine);
    }
}
