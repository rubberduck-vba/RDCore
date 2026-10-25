using Microsoft.Extensions.Configuration;
using RDCore.SDK.Server.Configuration;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;

namespace RDCore.Tests.Server.Configuration;

/// <summary>
/// What a user sets for themselves is kept apart from what the platform was deployed with, which is never written to: it is checked against the settings before
/// it is kept, and again before the platform reads it, so that no file of a user's keeps the platform from starting - and resetting it is removing it.
/// </summary>
[TestClass]
public sealed class UserSettingsTests
{
    private static readonly string File = Path.Combine(Path.GetTempPath(), "rdcore-user", UserSettingsStore.FileName);

    private readonly MockFileSystem _fileSystem = new();

    private UserSettingsStore Sut => new(_fileSystem, File, SettingsSchema.Platform);

    [TestMethod]
    public void AValue_IsKeptInTheShapeOfTheConfiguration_AsTheSettingIs()
    {
        Assert.IsTrue(Sut.Set("server.verbose", "FALSE").IsSet);
        Assert.IsTrue(Sut.Set("Server.TraceLevel", "warning").IsSet);
        Assert.IsTrue(Sut.Set("Platform.Extensions.Allowed", "RDCore.Diagnostics, Other").IsSet);
        Assert.IsTrue(Sut.Set("SERVER.VERBOSE", "true").IsSet);

        var configuration = new ConfigurationBuilder().AddJsonStream(_fileSystem.File.OpenRead(File)).Build();
        Assert.AreEqual("True", configuration["Configuration:Server:Verbose"], "set again, in any case: one value");
        Assert.AreEqual("Warning", configuration["Configuration:Server:TraceLevel"]);
        Assert.AreEqual("Other", configuration["Configuration:Platform:Extensions:Allowed:1"]);

        CollectionAssert.AreEqual(
            new[] { "Server.TraceLevel=Warning", "Server.Verbose=True", "Platform.Extensions.Allowed=RDCore.Diagnostics, Other" },
            Sut.Read().Select(set => $"{set.Setting.Key}={set.Value}").ToArray());
    }

    [TestMethod]
    [DataRow("Server.Loudness", "true", UserSettingProblem.UnknownSetting, DisplayName = "no such setting")]
    [DataRow("Platform.Transport.Type", "NamedPipe", UserSettingProblem.NotSettable, DisplayName = "deployment only")]
    [DataRow("Server.ConnectTimeoutSeconds", "-5", UserSettingProblem.InvalidValue, DisplayName = "not a value of it")]
    public void AValueThatIsNotOneOfASettingAUserCanSet_IsNotKept(string key, string value, UserSettingProblem problem)
    {
        Assert.AreEqual(problem, Sut.Set(key, value).Problem);
        Assert.IsFalse(_fileSystem.File.Exists(File));
    }

    [TestMethod]
    public void AFileThatCannotBeRead_IsNotWrittenOver_AndResettingEverythingDeletesIt()
    {
        _fileSystem.AddFile(File, new MockFileData("{ not json"));

        Assert.IsFalse(Sut.IsReadable);
        Assert.AreEqual(UserSettingProblem.Unreadable, Sut.Set("Server.Verbose", "false").Problem);
        Assert.AreEqual("{ not json", _fileSystem.File.ReadAllText(File));

        Sut.Reset();
        Assert.IsFalse(_fileSystem.File.Exists(File));
    }

    [TestMethod]
    public void Resetting_RemovesASetting_OrASection_OrEverything()
    {
        Sut.Set("Server.Verbose", "false");
        Sut.Set("Server.TraceLevel", "Error");
        Sut.Set("Environment.Regional.DecimalSymbol", ",");
        Sut.Set("Environment.Lcid", "3084");

        Assert.AreEqual(1, Sut.Reset("server.verbose"));
        Assert.AreEqual(2, Sut.Reset("Environment"), "a section: every setting in it");
        Assert.AreEqual(0, Sut.Reset("Workspace"), "nothing set there");
        CollectionAssert.AreEqual(new[] { "Server.TraceLevel" }, Sut.Read().Select(set => set.Setting.Key).ToArray());

        Assert.AreEqual(1, Sut.Reset());
        Assert.IsFalse(_fileSystem.File.Exists(File), "nothing set: no file");
    }

    [TestMethod]
    public void TheLastSettingReset_TakesTheFileWithIt()
    {
        Sut.Set("Environment.Regional.DecimalSymbol", ",");

        Assert.AreEqual(1, Sut.Reset("Environment.Regional.DecimalSymbol"));
        Assert.IsFalse(_fileSystem.File.Exists(File));
    }
}

/// <summary>
/// What a user set comes after the deployed <c>appsettings.json</c> and before what a process is told when it starts; what is not a value a user can set is
/// not read, and a file that cannot be read is no setting at all.
/// </summary>
[TestClass]
public sealed class UserSettingsConfigurationTests
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "rdcore-user-settings-" + Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private string Write(string name, string json)
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, name);
        System.IO.File.WriteAllText(path, json);
        return path;
    }

    private IConfigurationRoot Configuration(string userSettings, params (string Key, string Value)[] startedWith)
    {
        Write("appsettings.json", """{ "Configuration": { "Server": { "Verbose": true, "TraceLevel": "Trace", "ConnectTimeoutSeconds": 30 } } }""");
        var builder = new ConfigurationBuilder()
            .SetBasePath(_folder)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddInMemoryCollection(startedWith.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)));

        return builder.AddUserSettings(Write(UserSettingsStore.FileName, userSettings), SettingsSchema.Platform).Build();
    }

    [TestMethod]
    public void WhatAUserSet_TakesThePlaceOfTheDeployedValue_AndWhatAProcessIsStartedWithTakesItsPlace()
    {
        var configuration = Configuration(
            """{ "Configuration": { "Server": { "Verbose": false, "TraceLevel": "Error" } } }""",
            ("Configuration:Server:TraceLevel", "Debug"));

        Assert.AreEqual("False", configuration["Configuration:Server:Verbose"]);
        Assert.AreEqual("Debug", configuration["Configuration:Server:TraceLevel"]);
        Assert.AreEqual("30", configuration["Configuration:Server:ConnectTimeoutSeconds"]);
    }

    [TestMethod]
    public void WhatIsNotAValueAUserCanSet_IsNotRead()
    {
        var configuration = Configuration("""
            {
              // written by hand
              "Configuration": {
                "Server": { "Verbose": "banana", "ConnectTimeoutSeconds": -1, "TraceLevel": "Warning" },
                "Platform": { "Transport": { "Type": "Sockets" }, "Extensions": { "Allowed": [ "A", "B" ] } },
                "Diagnostics": { "Anything": "goes" }
              },
            }
            """);

        Assert.AreEqual("True", configuration["Configuration:Server:Verbose"], "no boolean");
        Assert.AreEqual("30", configuration["Configuration:Server:ConnectTimeoutSeconds"], "out of range");
        Assert.AreEqual("Warning", configuration["Configuration:Server:TraceLevel"]);
        Assert.IsNull(configuration["Configuration:Platform:Transport:Type"], "deployment only");
        Assert.AreEqual("B", configuration["Configuration:Platform:Extensions:Allowed:1"]);
        Assert.AreEqual("goes", configuration["Configuration:Diagnostics:Anything"], "another application's to check");
    }

    [TestMethod]
    public void AFileThatCannotBeRead_IsNoSetting()
        => Assert.AreEqual("True", Configuration("{ not json")["Configuration:Server:Verbose"]);

    [TestMethod]
    public void AFileThatIsNotThere_IsNoSetting()
    {
        var builder = new ConfigurationBuilder().AddUserSettings(Path.Combine(_folder, "nowhere", UserSettingsStore.FileName), SettingsSchema.Platform);

        Assert.IsNull(builder.Build()["Configuration:Server:Verbose"]);
    }
}
