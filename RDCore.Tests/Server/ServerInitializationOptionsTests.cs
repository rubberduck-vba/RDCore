using Newtonsoft.Json.Linq;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server;

namespace RDCore.Tests.Server;

/// <summary>
/// The <c>initializationOptions</c> of an <c>initialize</c> request: the part of LSP that exists for what a client has to tell a server that
/// no standard capability describes, which is where a client says the language the workspace is written in.
/// </summary>
[TestClass]
public sealed class ServerInitializationOptionsTests
{
    [TestMethod]
    public void TheOptionsAClientSends_AreReadBackFromTheWire()
    {
        // what the server receives is the JSON the client's object was serialized to.
        var onTheWire = JToken.FromObject(new RDCoreInitializationOptions { Language = "basic" });

        Assert.AreEqual("basic", RDCoreServerApp.ReadInitializationOptions(onTheWire)!.Language);
    }

    [TestMethod]
    public void AClientThatSendsNone_SaysNothing()
        => Assert.IsNull(RDCoreServerApp.ReadInitializationOptions(null));

    [TestMethod]
    public void AClientThatSendsOptionsOfItsOwn_SaysNothingOfTheLanguage()
        => Assert.IsNull(RDCoreServerApp.ReadInitializationOptions(JObject.Parse("{ \"somethingElse\": 1 }"))!.Language);

    [TestMethod]
    public void OptionsThatAreNotAnObject_AreIgnoredRatherThanFailingTheInitialize()
        => Assert.IsNull(RDCoreServerApp.ReadInitializationOptions(JValue.CreateString("basic")));

    [TestMethod]
    public void TheLanguageIsReadWhateverTheCaseOfItsName()
        => Assert.AreEqual("vb6", RDCoreServerApp.ReadInitializationOptions(JObject.Parse("{ \"Language\": \"vb6\" }"))!.Language);
}
