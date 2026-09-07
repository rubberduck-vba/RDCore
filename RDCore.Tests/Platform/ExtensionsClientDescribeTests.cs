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
}
