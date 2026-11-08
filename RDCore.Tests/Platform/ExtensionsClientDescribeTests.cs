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
        Assert.IsNull(described.DiagnosticPrefix);
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
