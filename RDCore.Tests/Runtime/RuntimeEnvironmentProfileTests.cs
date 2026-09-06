using Microsoft.Extensions.Configuration;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Server.Configuration;
using System.Globalization;

namespace RDCore.Tests.Runtime;

[TestClass]
public sealed class RuntimeEnvironmentProfileTests
{
    [TestMethod]
    public void Default_Is64BitInvariantWindows1252NoDatabase()
    {
        var sut = RuntimeEnvironmentProfile.Default;

        Assert.IsTrue(sut.Is64Bit);
        Assert.AreEqual(0, sut.Lcid);
        Assert.AreEqual(1252, sut.AnsiCodePage);
        Assert.IsFalse(sut.SupportsOptionCompareDatabase);
        Assert.AreEqual(CultureInfo.InvariantCulture, sut.Culture);
    }

    [TestMethod]
    public void From_MapsEveryOption()
    {
        var options = new SdkEnvironmentOptions
        {
            Is64Bit = false,
            Lcid = 1036, // fr-FR
            AnsiCodePage = 1250,
            SupportsOptionCompareDatabase = true,
        };

        IRuntimeEnvironmentProfile sut = RuntimeEnvironmentProfile.From(options);

        Assert.IsFalse(sut.Is64Bit);
        Assert.AreEqual(1036, sut.Lcid);
        Assert.AreEqual(1250, sut.AnsiCodePage);
        Assert.IsTrue(sut.SupportsOptionCompareDatabase);
        Assert.AreEqual("fr-FR", sut.Culture.Name);
    }

    [TestMethod]
    public void BindsFromAppSettingsConfigurationSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Configuration:Environment:Is64Bit"] = "false",
                ["Configuration:Environment:Lcid"] = "1033",
                ["Configuration:Environment:AnsiCodePage"] = "932",
                ["Configuration:Environment:SupportsOptionCompareDatabase"] = "true",
            })
            .Build();

        var options = configuration.GetSection("Configuration").Get<SdkAppOptions>()!.Environment;
        var sut = RuntimeEnvironmentProfile.From(options);

        Assert.IsFalse(sut.Is64Bit);
        Assert.AreEqual(1033, sut.Lcid);
        Assert.AreEqual(932, sut.AnsiCodePage);
        Assert.IsTrue(sut.SupportsOptionCompareDatabase);
    }

    [TestMethod]
    public void MissingSection_YieldsTheDefaults()
    {
        var options = new ConfigurationBuilder().Build()
            .GetSection("Configuration").Get<SdkAppOptions>() ?? new SdkAppOptions();

        Assert.IsTrue(options.Environment.Is64Bit);
        Assert.AreEqual(1252, options.Environment.AnsiCodePage);
    }
}
