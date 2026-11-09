using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using RDCore.CLI.Host;
using RDCore.CLI.Host.Handlers;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime;
using RDCore.SDK.Workspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <c>rdcore/host/session/status</c> is how the language server answers a client asking about the
/// platform's runtime session: the environment host owns it, so the host reports it.
/// </summary>
[TestClass]
public sealed class HostSessionStatusHandlerTests
{
    private static readonly Uri WorkspaceRoot = new("file:///c:/ws/");

    private static EnvironmentSessionProvider NewProvider()
        => new(new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false),
            new MockFileSystem(),
            NullLogger<EnvironmentSessionProvider>.Instance);

    [TestMethod]
    public async Task BeforeTheSessionIsComposed_ReportsNoSession()
    {
        var sut = new HostSessionStatusHandler(NewProvider());

        var result = await sut.Handle(new HostSessionStatusParams(), CancellationToken.None);

        Assert.IsFalse(result.IsComposed);
        Assert.AreEqual(0, result.ModuleCount);
        Assert.AreEqual(0, result.Memory.ReservedBytes);
    }

    [TestMethod]
    public async Task OnceComposed_ReportsTheProjectAndTheSessionMemory()
    {
        var provider = NewProvider();
        var project = new RDCoreProject
        {
            Name = "Battleship",
            Modules = [new RDCoreModule { RelativeUri = "src/Mod1.bas" }, new RDCoreModule { RelativeUri = "src/Mod2.bas" }],
        };
        var session = provider.Compose(project, WorkspaceRoot);
        var sut = new HostSessionStatusHandler(provider);

        var result = await sut.Handle(new HostSessionStatusParams(), CancellationToken.None);

        Assert.IsTrue(result.IsComposed);
        Assert.AreEqual("Battleship", result.ProjectName);
        Assert.AreEqual(2, result.ModuleCount);
        Assert.AreEqual(session.References.Count, result.ReferenceCount);
        // the numbers are the allocator's own, projected 1:1 - not re-derived here.
        Assert.AreEqual(session.Memory.Info.ReservedSegmentBytes, result.Memory.ReservedBytes);
        Assert.AreEqual(session.Memory.Info.AvailableBytes, result.Memory.AvailableBytes);
        Assert.AreEqual(session.Memory.Info.AllocatedBytes, result.Memory.AllocatedBytes);
        Assert.AreEqual(session.Memory.Info.FreeBytes, result.Memory.FreeBytes);
        Assert.IsGreaterThan(0, result.Memory.ReservedBytes);
    }
}
