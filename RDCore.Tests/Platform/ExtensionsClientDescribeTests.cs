using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using RDCore.SDK.Extensibility;
using RDCore.SDK.Platform;
using RDCore.SDK.Server.Configuration;

namespace RDCore.Tests.Platform;

[TestClass]
public sealed class ExtensionsClientDescribeTests
{
    [TestMethod]
    public void Describe_OutsideAnExtensionFolder_ReturnsNull()
    {
        // rooted paths that work on Windows and Linux CI alike.
        var root = Path.Combine(Path.GetTempPath(), "rdcore-describe-guard");
        var currentDirectory = Path.Combine(root, "not-extensions", "MyExt");
        var extensionsRoot = Path.Combine(root, "platform", "Extensions");

        var fs = new MockFileSystem(new Dictionary<string, MockFileData>(), currentDirectory);

        var environment = Substitute.For<IPlatformEnvironment>();
        environment.Resolve("Extensions").Returns(extensionsRoot);

        var sut = new ExtensionsClient(
            Options.Create(new SdkAppOptions()),
            Substitute.For<IExtensionManifestValidationService>(),
            fs,
            environment,
            NullLogger<ExtensionsClient>.Instance);

        // the current directory's parent (…/not-extensions) is not the resolved extensions root.
        Assert.IsNull(sut.Describe("MyExt.exe", "desc"));
    }

    [TestMethod]
    public void Describe_TheDiagnosticsExtension_RecordsItsId_ItsFileHash_AndTheSdkItReferences()
    {
        // the extension's folder is the one its assembly was built to: its .dll is real, its .exe a stand-in whose bytes the hash is of.
        var extensionFolder = Path.GetDirectoryName(typeof(RDCore.Diagnostics.DiagnosticsOptions).Assembly.Location)!;
        var executable = Path.Combine(extensionFolder, "RDCore.Diagnostics.exe");
        byte[] bytes = [1, 2, 3];
        var fs = new MockFileSystem(new Dictionary<string, MockFileData> { [executable] = new(bytes) }, extensionFolder);

        var environment = Substitute.For<IPlatformEnvironment>();
        environment.Resolve("Extensions").Returns(Path.GetDirectoryName(extensionFolder)!);

        var sut = new ExtensionsClient(
            Options.Create(new SdkAppOptions()),
            Substitute.For<IExtensionManifestValidationService>(),
            fs,
            environment,
            NullLogger<ExtensionsClient>.Instance);

        var described = sut.Describe("RDCore.Diagnostics.exe", "desc");

        Assert.IsNotNull(described);
        Assert.AreEqual(Path.GetFileName(extensionFolder), described.Id, "an extension that declares no id is known by its title");
        Assert.AreEqual(Convert.ToBase64String(System.Security.Cryptography.SHA512.HashData(bytes)), described.FileHash);
        Assert.AreEqual(typeof(ExtensionInfo).Assembly.GetName().Version, described.SdkVersion);
        Assert.AreEqual("RDC", described.DiagnosticPrefix, "the core diagnostics are RDC codes");
    }
}

[TestClass]
public sealed class ExtensionsClientDiscoverTests
{
    private static readonly string ExtensionsRoot = Path.Combine(Path.GetTempPath(), "rdcore-discover-prefix", "Extensions");

    private static ExtensionInfo Manifest(string title, string? prefix) => new()
    {
        Name = $"{title}.exe",
        Title = title,
        Version = new Version(1, 0),
        Publisher = "",
        PublisherWebUrl = "",
        Description = "",
        FileHash = "",
        Capabilities = [],
        DiagnosticPrefix = prefix,
    };

    private static readonly string PlatformManifestPath = Path.Combine(Path.GetDirectoryName(ExtensionsRoot)!, PlatformManifest.FileName);

    // coreExtension: what the platform manifest names; null for a platform with no manifest.
    private static (ExtensionsClient Sut, RecordingLogger Logger) Build(string? coreExtension, params ExtensionInfo[] manifests)
    {
        var files = manifests.ToDictionary(
            manifest => Path.Combine(ExtensionsRoot, manifest.Title, "extension.manifest.json"),
            manifest => new MockFileData(System.Text.Json.JsonSerializer.Serialize(manifest)));
        if (coreExtension is not null)
        {
            files[PlatformManifestPath] = new MockFileData($$"""{ "extensionsDirectory": "Extensions", "coreExtension": "{{coreExtension}}" }""");
        }

        var environment = Substitute.For<IPlatformEnvironment>();
        environment.Resolve("Extensions").Returns(ExtensionsRoot);
        environment.ManifestPath.Returns(PlatformManifestPath);

        var logger = new RecordingLogger();
        return (new ExtensionsClient(Options.Create(new SdkAppOptions()), Substitute.For<IExtensionManifestValidationService>(), new MockFileSystem(files), environment, logger), logger);
    }

    private static (ExtensionsClient Sut, RecordingLogger Logger) Build(params ExtensionInfo[] manifests) => Build("Core", manifests);

    private static IEnumerable<string> Warnings(RecordingLogger logger)
        => logger.Entries.Where(entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Warning).Select(entry => entry.Message);

    [TestMethod]
    public void TwoExtensionsOfAPrefix_AreNeitherFound_AndEachIsLogged()
    {
        var (sut, logger) = Build(Manifest("Beta", "XYZ"), Manifest("Alpha", "XYZ"), Manifest("Gamma", null), Manifest("Delta", "QRS"));

        var found = sut.Discover().Select(extension => extension.Title).ToArray();

        CollectionAssert.AreEqual(new[] { "Delta", "Gamma" }, found);
        var warnings = Warnings(logger).ToArray();
        Assert.HasCount(2, warnings);
        Assert.IsTrue(warnings.Any(warning => warning.Contains("'Alpha'") && warning.Contains("'XYZ'") && warning.Contains("'Beta' also declares")), string.Join("\n", warnings));
        Assert.IsTrue(warnings.Any(warning => warning.Contains("'Beta'") && warning.Contains("'XYZ'") && warning.Contains("'Alpha' also declares")), string.Join("\n", warnings));
    }

    [TestMethod]
    public void TheCorePrefix_IsTheCoreExtensions_WhereverTheFoldersSort()
    {
        // Aardvark sorts before Core, and Zebra after it: neither is the extension the platform bundles.
        var (sut, logger) = Build(Manifest("Aardvark", "RDC"), Manifest("Core", "RDC"), Manifest("Zebra", "RDC"));

        var found = sut.Discover().ToArray();

        var core = Assert.ContainsSingle(found);
        Assert.AreEqual("Core", core.Title);
        Assert.IsTrue(core.IsCoreExtension);
        var warnings = Warnings(logger).ToArray();
        Assert.HasCount(2, warnings);
        Assert.IsTrue(warnings.All(warning => warning.Contains("core extension's ('Core')")), string.Join("\n", warnings));
    }

    [TestMethod]
    public void TwoExtensionsOfTheFirstPartyExtensionPrefix_AreBothFound()
    {
        var (sut, logger) = Build(Manifest("Core", "RDC"), Manifest("VendorExtension", "RDX"), Manifest("OtherVendorExtension", "RDX"));

        var found = sut.Discover().Where(extension => !extension.IsCoreExtension).Select(extension => extension.Title).ToArray();

        CollectionAssert.AreEquivalent(new[] { "VendorExtension", "OtherVendorExtension" }, found);
        Assert.IsEmpty(Warnings(logger).ToArray());
    }

    [TestMethod]
    public void APlatformWithNoManifest_BundlesTheDiagnosticsExtension()
    {
        var (sut, _) = Build(coreExtension: null, Manifest("Aardvark", "RDC"), Manifest("RDCore.Diagnostics", "RDC"));

        var core = Assert.ContainsSingle(sut.Discover().ToArray());

        Assert.AreEqual("RDCore.Diagnostics", core.Title);
        Assert.IsTrue(core.IsCoreExtension);
    }

    [TestMethod]
    public void AnExtensionTheManifestDoesNotBundle_IsNotTheCoreExtension()
        => Assert.IsFalse(Assert.ContainsSingle(Build(Manifest("Vendor", "XYZ")).Sut.Discover().ToArray()).IsCoreExtension);

    [TestMethod]
    public void AnExtensionOfOneOfTheLanguagesOwnPrefixes_IsNotFound()
    {
        var (sut, _) = Build(Manifest("Compiler", "VBC"), Manifest("Runtime", "VBR"), Manifest("Raised", "VBA"));

        Assert.IsEmpty(sut.Discover().ToArray());
    }

    [TestMethod]
    public void AnExtensionOfAPrefixThatIsNotThreeLetters_IsNotFound()
    {
        var (sut, _) = Build(Manifest("Short", "XY"), Manifest("Lower", "xyz"), Manifest("Digits", "X1Z"));

        Assert.IsEmpty(sut.Discover().ToArray());
    }

    private sealed class RecordingLogger : Microsoft.Extensions.Logging.ILogger<ExtensionsClient>
    {
        public List<(Microsoft.Extensions.Logging.LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}

// the folders are real ones: the location check matches the folder name regardless of case, which the mock file system does not implement.
[TestClass]
public sealed class ExtensionManifestValidationServiceTests
{
    private static readonly byte[] Executable = [1, 2, 3];
    private string _root = string.Empty;

    [TestInitialize]
    public void CreateExtensionsFolder()
    {
        _root = Path.Combine(Path.GetTempPath(), "RDCore", $"validation-{Guid.NewGuid():N}");
        var extension = Directory.CreateDirectory(Path.Combine(_root, "Extensions", "RDCore.Diagnostics"));
        File.WriteAllBytes(Path.Combine(extension.FullName, "RDCore.Diagnostics.exe"), Executable);
    }

    [TestCleanup]
    public void DeleteExtensionsFolder()
        => Directory.Delete(_root, recursive: true);

    private ExtensionManifestValidationService Sut()
    {
        var environment = Substitute.For<IPlatformEnvironment>();
        environment.Resolve("Extensions").Returns(Path.Combine(_root, "Extensions"));

        return new ExtensionManifestValidationService(Options.Create(new SdkAppOptions()), new System.IO.Abstractions.FileSystem(), environment);
    }

    private static ExtensionInfo Manifest(string fileHash) => new()
    {
        Name = "RDCore.Diagnostics.exe",
        Title = "RDCore.Diagnostics",
        Version = new Version(1, 0),
        Publisher = "",
        PublisherWebUrl = "",
        Description = "",
        FileHash = fileHash,
        Capabilities = [],
    };

    [TestMethod]
    public void AManifestWhoseFileHashIsTheExecutables_IsValid()
        => Assert.AreEqual(
            ExtensionValidationFlags.NoFlags,
            Sut().Validate(Manifest(Convert.ToBase64String(System.Security.Cryptography.SHA512.HashData(Executable)))));

    [TestMethod]
    public void AManifestWhoseFileHashIsNotTheExecutables_IsAFileHashMismatch()
        => Assert.AreEqual(ExtensionValidationFlags.FileHashMismatch, Sut().Validate(Manifest("not the hash")));
}
