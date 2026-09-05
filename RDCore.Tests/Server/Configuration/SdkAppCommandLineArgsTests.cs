using Microsoft.Extensions.Logging;
using RDCore.SDK.Server.Configuration;

namespace RDCore.Tests.Server.Configuration;

[TestClass]
public class SdkAppCommandLineArgsTests
{
    [TestMethod]
    public void ToConfigurationOverrides_NoArgs_YieldsNothing()
    {
        // arrange
        var sut = new SdkAppCommandLineArgs();

        // act
        var result = sut.ToConfigurationOverrides().ToList();

        // assert
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void ToConfigurationOverrides_MapsEachSuppliedOptionToItsConfigurationKey()
    {
        // arrange
        var sut = new SdkAppCommandLineArgs
        {
            ClientProcessId = 1234,
            PipeName = "RDCore.Test.Pipe",
            WorkspaceUri = "file:///workspace",
            DefaultLocation = "/roots",
            Verbose = true,
            TraceLevel = LogLevel.Warning,
            ConnectTimeoutSeconds = 42,
            HealthCheckIntervalSeconds = 7,
            ShutdownTimeoutSeconds = 3,
            UnsafeDevMode = true,
            Type = ServerTransportLayerMode.NamedPipe,
        };

        // act
        var map = sut.ToConfigurationOverrides().ToDictionary(pair => pair.Key, pair => pair.Value);

        // assert
        Assert.AreEqual("1234", map["Configuration:Server:ClientProcessId"]);
        Assert.AreEqual("RDCore.Test.Pipe", map["Configuration:Platform:Transport:PipeConfig:PipeName"]);
        Assert.AreEqual("file:///workspace", map["Configuration:Workspace:WorkspaceUri"]);
        Assert.AreEqual("/roots", map["Configuration:Workspace:DefaultLocation"]);
        Assert.AreEqual("True", map["Configuration:Server:Verbose"]);
        Assert.AreEqual("Warning", map["Configuration:Server:TraceLevel"]);
        Assert.AreEqual("42", map["Configuration:Server:ConnectTimeoutSeconds"]);
        Assert.AreEqual("7", map["Configuration:Server:HealthCheckIntervalSeconds"]);
        Assert.AreEqual("3", map["Configuration:Server:ShutdownTimeoutSeconds"]);
        Assert.AreEqual("True", map["Configuration:Server:UnsafeDevMode"]);
        Assert.AreEqual("NamedPipe", map["Configuration:Platform:Transport:Type"]);
    }

    [TestMethod]
    public void ToConfigurationOverrides_OmitsOptionsThatWereNotSupplied()
    {
        // arrange
        var sut = new SdkAppCommandLineArgs { PipeName = "p", WorkspaceUri = "w" };

        // act
        var keys = sut.ToConfigurationOverrides().Select(pair => pair.Key).ToList();

        // assert
        CollectionAssert.AreEquivalent(
            new[]
            {
                "Configuration:Platform:Transport:PipeConfig:PipeName",
                "Configuration:Workspace:WorkspaceUri",
            },
            keys);
    }

    [TestMethod]
    public void ToConfigurationOverrides_ClientProcessIdZero_IsStillEmittedWhenSupplied()
    {
        // arrange: 0 is a real supplied value (bool? / int? distinguishes "not supplied")
        var sut = new SdkAppCommandLineArgs { ClientProcessId = 0 };

        // act
        var map = sut.ToConfigurationOverrides().ToDictionary(pair => pair.Key, pair => pair.Value);

        // assert
        Assert.AreEqual("0", map["Configuration:Server:ClientProcessId"]);
    }
}
