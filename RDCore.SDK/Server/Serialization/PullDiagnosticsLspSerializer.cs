using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Serialization;

namespace RDCore.SDK.Server.Serialization;

/// <summary>
/// The LSP serializer, with the <strong>LSP 3.17</strong> pull diagnostic report types made writable.
/// </summary>
/// <remarks>
/// <para>
/// OmniSharp 0.19.9 declares the pull diagnostic report records with a <c>[JsonConverter]</c> whose
/// <c>WriteJson</c> is <c>throw new NotImplementedException()</c>. The types compile, the handler base
/// exists, the request routes and the handler runs; only the write throws. So a server can serve
/// <c>textDocument/diagnostic</c> right up to the wire and no further.
/// </para>
/// <para>
/// The throw is not survivable either. <c>OutputHandler.ProcessOutputStream</c> is one loop over one
/// channel carrying every outbound frame; it catches, logs at <c>Trace</c>, and calls <c>Error</c>, which
/// completes the writer and disposes the handler. One unserializable response therefore ends all output
/// for the life of the process, and the trace line explaining it is routed over <c>window/logMessage</c>,
/// which is the transport that has just been disposed.
/// </para>
/// <para>
/// Fixed upstream in <c>v0.19.10</c>, which was tagged and never published to NuGet, so a version bump is
/// not available.
/// </para>
/// <para>
/// <b>The repair is re-applied on every call rather than installed once, and it has to be.</b>
/// <c>LspSerializer.Reset</c> assigns a brand new <c>LspContractResolver</c> straight onto
/// <c>Settings</c>, bypassing <c>CreateSerializerSettings</c> entirely, and every <c>With*</c> method and
/// <c>SetServerCapabilities</c> calls it while the client capabilities are being read. So a repair made at
/// construction is silently discarded during <c>initialize</c>, which looks exactly like the repair not
/// working. <c>Reset</c> is private and the methods that call it are not virtual, so there is nothing to
/// override; checking at the point of use is what is left.
/// </para>
/// </remarks>
public sealed class PullDiagnosticsLspSerializer(ClientVersion clientVersion)
    : LspSerializer(clientVersion), OmniSharp.Extensions.JsonRpc.ISerializer
{
    public PullDiagnosticsLspSerializer() : this(ClientVersion.Lsp3) { }

    // Re-implementing the interface rather than overriding: SerializerBase.SerializeObject is not virtual,
    // and the output handler holds this as an ISerializer, so the interface map is the seam available.
    string OmniSharp.Extensions.JsonRpc.ISerializer.SerializeObject(object value)
    {
        Repair();
        return SerializeObject(value);
    }

    object OmniSharp.Extensions.JsonRpc.ISerializer.DeserializeObject(string json, Type type)
    {
        Repair();
        return DeserializeObject(json, type);
    }

    T OmniSharp.Extensions.JsonRpc.ISerializer.DeserializeObject<T>(string json)
    {
        Repair();
        return DeserializeObject<T>(json);
    }

    /// <summary>Puts the repair back if anything has replaced it. Cheap, and idempotent.</summary>
    private void Repair()
    {
        if (Settings.ContractResolver is not WithoutReportStub)
        {
            Settings.ContractResolver = new WithoutReportStub(
                Settings.ContractResolver ?? new DefaultContractResolver());
        }

        if (!Settings.Converters.OfType<RelatedDocumentDiagnosticReportConverter>().Any())
        {
            Settings.Converters.Add(new RelatedDocumentDiagnosticReportConverter());
        }
    }

    /// <summary>
    /// Hides the stub converter the report records carry, so a real one becomes reachable.
    /// </summary>
    /// <remarks>
    /// Supplying a converter is not enough on its own: a class-level <c>[JsonConverter]</c> beats anything
    /// in the <c>Converters</c> collection, so the stub keeps winning until the contract stops naming it.
    /// </remarks>
    private sealed class WithoutReportStub(IContractResolver inner) : IContractResolver
    {
        public JsonContract ResolveContract(Type type)
        {
            var contract = inner.ResolveContract(type);
            if (IsReport(type)) contract.Converter = null;
            return contract;
        }

        /// <summary>
        /// Whether this type is one of the report records.
        /// </summary>
        /// <remarks>
        /// Tested against <see cref="DocumentDiagnosticReportPartialResult"/> rather than the
        /// similarly-named <c>DocumentDiagnosticReport</c>. They are two parallel hierarchies, and the one
        /// a pull handler returns descends from the former; a predicate written against the latter matches
        /// nothing and fails silently.
        /// </remarks>
        private static bool IsReport(Type type) =>
            typeof(DocumentDiagnosticReportPartialResult).IsAssignableFrom(type)
            || typeof(DocumentDiagnosticReport).IsAssignableFrom(type);
    }

    /// <summary>
    /// Writes a pull diagnostic report in the shape LSP 3.17 specifies.
    /// </summary>
    /// <remarks>
    /// Write only. Nothing here reads one: a server receives the request and sends the report, and leaving
    /// the read unimplemented keeps this to the one direction that has been exercised.
    /// </remarks>
    private sealed class RelatedDocumentDiagnosticReportConverter : JsonConverter<RelatedDocumentDiagnosticReport>
    {
        public override bool CanRead => false;

        public override void WriteJson(
            JsonWriter writer, RelatedDocumentDiagnosticReport? value, JsonSerializer serializer)
        {
            if (value is null)
            {
                writer.WriteNull();
                return;
            }

            writer.WriteStartObject();

            writer.WritePropertyName("kind");
            writer.WriteValue(value.Kind.ToString());

            switch (value)
            {
                case RelatedFullDocumentDiagnosticReport full:
                    if (full.ResultId is not null)
                    {
                        writer.WritePropertyName("resultId");
                        writer.WriteValue(full.ResultId);
                    }

                    writer.WritePropertyName("items");
                    serializer.Serialize(writer, full.Items ?? new Container<Diagnostic>());
                    break;

                case RelatedUnchangedDocumentDiagnosticReport unchanged:
                    // Required rather than optional on this branch: an unchanged report says nothing at
                    // all without the identifier the client is to keep using.
                    writer.WritePropertyName("resultId");
                    writer.WriteValue(unchanged.ResultId);
                    break;
            }

            if (value.RelatedDocuments is { Count: > 0 })
            {
                writer.WritePropertyName("relatedDocuments");
                serializer.Serialize(writer, value.RelatedDocuments);
            }

            writer.WriteEndObject();
        }

        public override RelatedDocumentDiagnosticReport ReadJson(
            JsonReader reader, Type objectType, RelatedDocumentDiagnosticReport? existingValue,
            bool hasExistingValue, JsonSerializer serializer) =>
            throw new NotSupportedException(
                "Reading a pull diagnostic report is not needed on the server side.");
    }
}
