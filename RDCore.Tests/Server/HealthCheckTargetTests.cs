using RDCore.LanguageServer;
using RDCore.Parsing;
using RDCore.SDK.Client;
using RDCore.SDK.Platform;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Services;

namespace RDCore.Tests.Server;

[TestClass]
public class HealthCheckTargetTests
{
    // a server app watches its owning client process; a client app watches the server process it started.
    [TestMethod]
    [DataRow(typeof(IRDCoreServerApp), true)]
    [DataRow(typeof(IRDCoreClientApp), false)]
    [DataRow(typeof(CoreLanguageServerApp), true)]
    [DataRow(typeof(RDCoreParserApp), true)]
    [DataRow(typeof(RDCoreServerProxy), false)]
    public void IsOwnedByClient(Type appType, bool expected)
        => Assert.AreEqual(expected, HealthCheckTarget.IsOwnedByClient(appType));
}
