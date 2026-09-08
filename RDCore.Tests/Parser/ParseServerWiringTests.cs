using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using RDCore.Parsing;
using RDCore.Parsing.Handlers;
using RDCore.SDK.ConsoleIO;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Server.Configuration;
using System.IO.Abstractions;

namespace RDCore.Tests.Parser;

/// <summary>
/// The parse server builds its handlers from the OmniSharp-internal container, whose own
/// <c>AddOptions</c> would hand them an unconfigured <see cref="SdkServerOptions"/>.
/// <c>RDCoreParserApp.ConfigureServices</c> bridges the configured instance; this pins that the
/// handler and the parser resolve from a container shaped that way, and that the wire-error scrub is
/// wired to the configured mode.
/// </summary>
[TestClass]
public sealed class ParseServerWiringTests
{
    private static ServiceProvider BuildContainer(SourcePathScrubMode scrub)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IFile>());
        // the bridge RDCoreParserApp.ConfigureServices performs: a configured IOptions<SdkServerOptions>.
        services.AddSingleton<IOptions<SdkServerOptions>>(
            Options.Create(new SdkServerOptions { WireErrorDetail = scrub }));
        services.AddSingleton<IModuleParser, ModuleParser>();
        return services.BuildServiceProvider();
    }

    [TestMethod]
    public void HandlerAndParser_ResolveFromTheConfiguredContainer()
    {
        using var provider = BuildContainer(SourcePathScrubMode.Redacted);

        var handler = ActivatorUtilities.CreateInstance<ParseFullDocumentHandler>(provider);
        var parser = provider.GetRequiredService<IModuleParser>();

        Assert.IsNotNull(handler);
        Assert.IsInstanceOfType<ModuleParser>(parser);

        // AddLogging pulls in AddOptions, whose open-generic IOptions<> would hand a default;
        // the explicit closed-type registration (the bridge) must win.
        Assert.AreEqual(
            SourcePathScrubMode.Redacted,
            provider.GetRequiredService<IOptions<SdkServerOptions>>().Value.WireErrorDetail);
    }

    [TestMethod]
    // the funnel: ModuleParseResult.Failed scrubs its verbose per the given mode, so a build path
    // cannot leak regardless of the call site.
    [DataRow(SourcePathScrubMode.RepoRelative, "RDCore.Parsing/AST/Foo.cs:line 9")]
    [DataRow(SourcePathScrubMode.FileName, "Foo.cs:line 9")]
    [DataRow(SourcePathScrubMode.Redacted, "<source>:line 9")]
    public void Failed_ScrubsTheVerbosePerMode(SourcePathScrubMode mode, string expectedFragment)
    {
        const string trace =
            "System.InvalidOperationException: boom\r\n" +
            "   at RDCore.Parsing.AST.Bar.Baz() in C:\\Users\\somebody\\src\\RDCore\\RDCore.Parsing\\AST\\Foo.cs:line 9";

        var result = ModuleParseResult.Failed(new SourceLocation(TestUri.TestModuleUri(), SourceRange.Empty), trace, mode);

        var verbose = result.SyntaxErrors.Single().Verbose;
        StringAssert.Contains(verbose, expectedFragment);
        Assert.IsFalse(verbose.Contains("somebody"), "the build-machine user name must not survive");
        Assert.IsFalse(verbose.Contains("C:\\"), "the drive-letter prefix must not survive");
    }
}
