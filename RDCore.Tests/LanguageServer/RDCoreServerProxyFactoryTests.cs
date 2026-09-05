using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using RDCore.LanguageServer;
using RDCore.SDK.Client;
using RDCore.SDK.Client.Connection;
using RDCore.SDK.Platform;
using RDCore.SDK.Server.Configuration;

namespace RDCore.Tests.LanguageServer;

[TestClass]
public class RDCoreServerProxyFactoryTests
{
    private static RDCoreServerProxyFactory CreateSut()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IOptions<SdkAppOptions>>(Options.Create(new SdkAppOptions()));
        services.AddSingleton(Substitute.For<IChildConnectionFactory>());
        return new RDCoreServerProxyFactory(services.BuildServiceProvider());
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
