using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using RDCore.LanguageServer;
using RDCore.SDK.Client;
using RDCore.SDK.Platform;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Services;
using System.IO.Abstractions;

namespace RDCore.Tests.LanguageServer;

[TestClass]
public class RDCoreServerProxyFactoryTests
{
    private readonly List<IRDCoreServerProcess> _resolvedProcesses = [];

    private RDCoreServerProxyFactory CreateSut()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IOptions<SdkAppOptions>>(Options.Create(new SdkAppOptions()));
        services.AddSingleton(Substitute.For<IFileSystem>());
        services.AddSingleton(Substitute.For<ILanguageServerProtocolTransportLayer>());
        services.AddTransient<IHealthCheckService<RDCoreServerProxy>>(_ => Substitute.For<IHealthCheckService<RDCoreServerProxy>>());
        services.AddTransient<IRDCoreServerProcess>(_ =>
        {
            var process = Substitute.For<IRDCoreServerProcess>();
            _resolvedProcesses.Add(process);
            return process;
        });

        return new RDCoreServerProxyFactory(services.BuildServiceProvider());
    }

    [TestMethod]
    public void Create_ResolvesAFreshServerProcessPerProxy()
    {
        // arrange
        var sut = CreateSut();

        // act
        sut.Create(CoreServerComponent.ParsingServer, new CorePlatformClientCapabilities());
        sut.Create(CoreServerComponent.EnvironmentHost, new CorePlatformClientCapabilities());

        // assert
        Assert.AreEqual(2, _resolvedProcesses.Count);
        Assert.AreNotSame(_resolvedProcesses[0], _resolvedProcesses[1]);
    }

    [TestMethod]
    public void Create_ReturnsAProxyForTheRequestedComponent()
    {
        // arrange
        var sut = CreateSut();

        // act
        var parsing = sut.Create(CoreServerComponent.ParsingServer, new CorePlatformClientCapabilities());
        var host = sut.Create(CoreServerComponent.EnvironmentHost, new CorePlatformClientCapabilities());

        // assert
        Assert.AreEqual(CoreServerComponent.ParsingServer, parsing.PlatformComponent);
        Assert.AreEqual(CoreServerComponent.EnvironmentHost, host.PlatformComponent);
    }
}
