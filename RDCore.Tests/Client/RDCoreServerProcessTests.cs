using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using RDCore.SDK.Client;
using RDCore.SDK.Platform;
using RDCore.SDK.Server.Configuration;
using System.IO.Abstractions;

namespace RDCore.Tests.Client;

[TestClass]
public class RDCoreServerProcessTests
{
    private static RDCoreServerProcess CreateSut() => new(
        Substitute.For<IFileSystem>(),
        Substitute.For<IPlatformEnvironment>(),
        Options.Create(new SdkAppOptions()),
        Substitute.For<ILogger<RDCoreServerProcess>>());

    [TestMethod]
    public void HasExited_BeforeStart_IsTrue()
        => Assert.IsTrue(CreateSut().HasExited);

    [TestMethod]
    public void ProcessId_BeforeStart_IsZero()
        => Assert.AreEqual(0, CreateSut().ProcessId);

    [TestMethod]
    public async Task WaitForExitAsync_BeforeStart_IsAlreadyCompleted()
    {
        // act
        var task = CreateSut().WaitForExitAsync();

        // assert
        Assert.IsTrue(task.IsCompleted);
        await task;
    }

    [TestMethod]
    public void Shutdown_BeforeStart_DoesNotThrow()
        => CreateSut().Shutdown();

    [TestMethod]
    public void Dispose_IsIdempotent()
    {
        var sut = CreateSut();

        sut.Dispose();
        sut.Dispose(); // a second dispose (host container + explicit) must not throw
    }
}
