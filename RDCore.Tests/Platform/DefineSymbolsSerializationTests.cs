using RDCore.SDK.Platform.Protocol;

namespace RDCore.Tests.Platform;

/// <summary>
/// <see cref="DefineSymbolsParams"/> as the transport carries it: the addresses of the workspace and of its module stay the absolute ones they were, on any
/// platform, for every test that defines a module through <see cref="Wire"/>.
/// </summary>
[TestClass]
public sealed class DefineSymbolsSerializationTests
{
    [TestMethod]
    public void AWorkspaceRoot_IsTheSameAbsoluteUri_AcrossTheWire()
    {
        var root = Wire.FileUri(Path.Combine(Path.GetTempPath(), "rdcore-wire"));
        var module = new UriBuilder(root) { Fragment = "Program" }.Uri;

        var read = Wire.RoundTrip(new DefineSymbolsParams { WorkspaceRoot = root, ModuleUri = module, ModuleName = "Program" });

        Assert.IsTrue(read.WorkspaceRoot!.IsAbsoluteUri, read.WorkspaceRoot.OriginalString);
        Assert.AreEqual(root.AbsoluteUri, read.WorkspaceRoot.AbsoluteUri);
        Assert.AreEqual(module.AbsoluteUri, read.ModuleUri!.AbsoluteUri);
    }
}
