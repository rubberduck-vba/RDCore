using Microsoft.Extensions.Configuration;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Server.Configuration;
using System.Globalization;
using System.Text;

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
    public void Default_AnsiEncodingIsWindows1252()
        => Assert.AreEqual(1252, RuntimeEnvironmentProfile.Default.AnsiEncoding.CodePage);

    [TestMethod]
    public void AnsiEncoding_IsThatOfTheCodePage()
        => Assert.AreEqual(1250, new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1250, false).AnsiEncoding.CodePage);

    [TestMethod]
    public void AnsiEncoding_OfACodePageTheProviderDoesNotSupply_IsLatin1()
        // 🚧 the code pages .NET builds in are not the provider's to supply, UTF-8 among them: this pins what that does today.
        => Assert.AreEqual(Encoding.Latin1.CodePage, new RuntimeEnvironmentProfile(Is64Bit: true, 0, 65001, false).AnsiEncoding.CodePage);

    [TestMethod]
    public void From_MapsEveryOption()
    {
        var options = new SdkEnvironmentOptions
        {
            Is64Bit = false,
            Lcid = 1036, // fr-FR
            AnsiCodePage = 1250,
            SupportsOptionCompareDatabase = true,
            ErlLineNumbering = VBErlLineNumbering.LineLabel,
            AllowDllImports = false,
        };

        IRuntimeEnvironmentProfile sut = RuntimeEnvironmentProfile.From(options);

        Assert.IsFalse(sut.Is64Bit);
        Assert.AreEqual(1036, sut.Lcid);
        Assert.AreEqual(1250, sut.AnsiCodePage);
        Assert.IsTrue(sut.SupportsOptionCompareDatabase);
        Assert.AreEqual("fr-FR", sut.Culture.Name);
        Assert.AreEqual(VBErlLineNumbering.LineLabel, sut.ErlLineNumbering);
        Assert.IsFalse(sut.AllowDllImports, "the switch an administrator sets, which nothing else may override");
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

    [TestMethod]
    public void OptionCompareDatabase_IsTextUnlessThePlatformSaysOtherwise()
    {
        Assert.AreEqual(OptionCompare.Text, RuntimeEnvironmentProfile.From(new SdkEnvironmentOptions()).DatabaseCompare);
        Assert.AreEqual(OptionCompare.Text, RuntimeEnvironmentProfile.Default.DatabaseCompare);
    }

    [TestMethod]
    public void OptionCompareDatabase_BindsFromAppSettingsConfigurationSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Configuration:Environment:DatabaseCompare"] = "Binary" })
            .Build();

        var options = configuration.GetSection("Configuration").Get<SdkAppOptions>()!.Environment;

        Assert.AreEqual(OptionCompare.Binary, RuntimeEnvironmentProfile.From(options).DatabaseCompare);
    }
}
