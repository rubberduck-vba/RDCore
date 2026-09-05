using RDCore.Parsing;
using RDCore.SDK.Client;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.Tests.Platform;

[TestClass]
public class PlatformHandshakeTests
{
    [TestMethod]
    public void Reflect_ReadsProvidesCorePlatformClientCapabilityAttributes()
    {
        // RDCore.Parsing declares [assembly: ProvidesCorePlatformClientCapability<ParseFullDocument>]
        var provided = ProvidedCorePlatformCapabilities.Reflect(typeof(RDCoreParserApp).Assembly);

        CollectionAssert.Contains(provided.ToList(), nameof(ParseFullDocument));
    }

    [TestMethod]
    public void Reflect_AssemblyWithoutTheAttribute_IsEmpty()
        => Assert.AreEqual(0, ProvidedCorePlatformCapabilities.Reflect(typeof(PlatformHandshakeTests).Assembly).Count);

    [TestMethod]
    public void Provides_IsTrueWhenTheCapabilityTypeNameIsListed()
    {
        var result = new PlatformInitializeResult { Provided = [nameof(ParseFullDocument)] };

        Assert.IsTrue(result.Provides<ParseFullDocument>());
    }

    [TestMethod]
    public void Provides_IsFalseWhenNotListed()
        => Assert.IsFalse(new PlatformInitializeResult().Provides<ParseFullDocument>());
}
