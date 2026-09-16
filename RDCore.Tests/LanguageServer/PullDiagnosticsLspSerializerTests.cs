using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Server.Serialization;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// The pull diagnostic report types, written to the wire.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="DocumentDiagnosticHandlerTests"/> already builds these reports and asserts their contents,
/// and that is not enough: OmniSharp 0.19.9 declares them with a <c>[JsonConverter]</c> whose
/// <c>WriteJson</c> throws, so a report can be correct in every respect and still be unserializable. The
/// handler was tested; the wire was not, which is how the fault survived the suite.
/// </para>
/// <para>
/// It is worth knowing what the fault costs, because it is not one failed request.
/// <c>OutputHandler.ProcessOutputStream</c> is a single loop over a single channel carrying every
/// outbound frame, and a throw there completes the writer and disposes the handler. One unserializable
/// response ends all output for the life of the process.
/// </para>
/// </remarks>
[TestClass]
public sealed class PullDiagnosticsLspSerializerTests
{
    private static readonly Uri DocUri = new("file:///c:/ws/src/Mod1.bas");

    private static ISerializer Sut() => new PullDiagnosticsLspSerializer();

    private static Diagnostic ASyntaxError() => new()
    {
        Code = new DiagnosticCode("VBC00001"),
        Source = "RDCore",
        Message = "Syntax error",
        Severity = DiagnosticSeverity.Error,
        Range = new Range(new Position(3, 4), new Position(3, 18)),
    };

    [TestMethod]
    public void AFullReport_WritesTheShapeTheProtocolSpecifies()
    {
        var report = new RelatedFullDocumentDiagnosticReport
        {
            ResultId = "v2",
            Items = new Container<Diagnostic>(ASyntaxError()),
        };

        var json = JObject.Parse(Sut().SerializeObject(report));

        Assert.AreEqual("full", (string?)json["kind"]);
        Assert.AreEqual("v2", (string?)json["resultId"]);
        Assert.AreEqual(1, json["items"]?.Count());
        Assert.AreEqual("VBC00001", (string?)json["items"]?[0]?["code"]);
        Assert.AreEqual("RDCore", (string?)json["items"]?[0]?["source"]);
    }

    [TestMethod]
    public void AnUnchangedReport_CarriesTheResultIdTheClientMustKeepUsing()
    {
        // Required rather than optional on this branch: an unchanged report says nothing at all without
        // the identifier, since the identifier is the entire message.
        var report = new RelatedUnchangedDocumentDiagnosticReport { ResultId = "v2" };

        var json = JObject.Parse(Sut().SerializeObject(report));

        Assert.AreEqual("unchanged", (string?)json["kind"]);
        Assert.AreEqual("v2", (string?)json["resultId"]);
        Assert.IsNull(json["items"], "an unchanged report carries no items");
    }

    [TestMethod]
    public void AFullReport_WithNoDiagnostics_StillWritesAnItemsArray()
    {
        // A clean document answers with an empty array rather than omitting the member. Omitting it would
        // leave the client unable to tell "nothing wrong" from "nothing reported".
        var report = new RelatedFullDocumentDiagnosticReport
        {
            ResultId = "v1",
            Items = new Container<Diagnostic>(),
        };

        var json = JObject.Parse(Sut().SerializeObject(report));

        Assert.AreEqual("full", (string?)json["kind"]);
        Assert.IsNotNull(json["items"]);
        Assert.AreEqual(0, json["items"]!.Count());
    }

    [TestMethod]
    public void TheRepairSurvivesTheSettingsResetThatReadingCapabilitiesCauses()
    {
        // THE ONE THAT PINS THE DESIGN, and the reason the repair is re-applied per call rather than
        // installed in the constructor. LspSerializer.Reset assigns a brand new contract resolver straight
        // onto Settings, bypassing CreateSerializerSettings, and it runs while client capabilities are
        // being read during initialize. A repair made once at construction is therefore discarded before
        // the first real request, which presents exactly as the repair not working at all.
        //
        // Without this test, folding Repair() into the constructor passes everything above and ships a
        // server that still dies on the first pull.
        var sut = new PullDiagnosticsLspSerializer();
        var serializer = (ISerializer)sut;

        var before = JObject.Parse(serializer.SerializeObject(
            new RelatedFullDocumentDiagnosticReport { ResultId = "v1", Items = new Container<Diagnostic>() }));
        Assert.AreEqual("full", (string?)before["kind"], "the fixture must serialize before the reset");

        sut.SetClientCapabilities(new ClientCapabilities());

        var after = JObject.Parse(serializer.SerializeObject(
            new RelatedFullDocumentDiagnosticReport { ResultId = "v2", Items = new Container<Diagnostic>(ASyntaxError()) }));

        Assert.AreEqual("full", (string?)after["kind"]);
        Assert.AreEqual("v2", (string?)after["resultId"]);
        Assert.AreEqual(1, after["items"]?.Count());
    }

    [TestMethod]
    public void OrdinaryPayloads_StillRoundTripThroughThisSerializer()
    {
        // The repair replaces the contract resolver for every type, not only the report records, so this
        // guards against it disturbing everything else the server sends.
        var serializer = Sut();
        var original = new PublishDiagnosticsParams
        {
            Uri = DocUri,
            Diagnostics = new Container<Diagnostic>(ASyntaxError()),
        };

        var result = serializer.DeserializeObject<PublishDiagnosticsParams>(
            serializer.SerializeObject(original));

        Assert.AreEqual(original.Uri, result.Uri);
        Assert.AreEqual("Syntax error", result.Diagnostics.Single().Message);
    }
}
