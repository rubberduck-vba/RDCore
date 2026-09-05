using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using RDCore.SDK.Client;
using RDCore.SDK.Client.Connection;
using RDCore.SDK.Server;

namespace RDCore.Tests.Client;

[TestClass]
public class ChildConnectionFactoryTests
{
    [TestMethod]
    public void Create_ResolvesAFreshServerProcessPerConnection()
    {
        // arrange
        var resolved = new List<IRDCoreServerProcess>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<ILanguageServerProtocolTransportLayer>());
        services.AddTransient<IRDCoreServerProcess>(_ =>
        {
            var process = Substitute.For<IRDCoreServerProcess>();
            resolved.Add(process);
            return process;
        });
        var sut = new ChildConnectionFactory(services.BuildServiceProvider());

        // act
        var first = sut.Create();
        var second = sut.Create();

        // assert
        Assert.AreEqual(2, resolved.Count);
        Assert.AreNotSame(resolved[0], resolved[1]);
        Assert.AreNotSame(first, second);
    }
}
