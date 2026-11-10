using RDCore.LanguageServer;
using RDCore.LanguageServer.Services.Platform;
using RDCore.ParseServer;
using RDCore.SDK.Server;

namespace RDCore.Tests.Server;

[TestClass]
public class HealthCheckTargetTests
{
    // a server app watches its owning client process, and the platform's servers are server apps.
    [TestMethod]
    [DataRow(typeof(CoreLanguageServerApp))]
    [DataRow(typeof(RDCoreParserApp))]
    public void APlatformServer_IsAServerApp(Type appType)
        => Assert.IsTrue(typeof(IRDCoreServerApp).IsAssignableFrom(appType));
}
