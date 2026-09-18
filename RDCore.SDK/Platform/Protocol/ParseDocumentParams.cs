using MediatR;
using OmniSharp.Extensions.JsonRpc;
using RDCore.SDK.Client;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.Source;

namespace RDCore.SDK.Platform.Protocol;

/// <summary>
/// The <em>parameter</em> object for a <c>ParseDocumentCommand</c>. The <c>[Method]</c> attribute lets
/// the JSON-RPC layer infer the request method when the caller sends this by type.
/// </summary>
/// <remarks>
/// The response is a <see cref="PlatformJsonEnvelope"/> wrapping a <see cref="ModuleParseResult"/> —
/// the AST is polymorphic and the JSON-RPC transport's serializer cannot round-trip it (see
/// <see cref="PlatformJson"/>).
/// </remarks>
[Method(RDCorePlatformProtocol.ParseFullDocument, Direction.ClientToServer)]
public record class ParseDocumentParams : IRequest, IRequest<PlatformJsonEnvelope>
{
    /// <summary>
    /// The <c>Uri</c> identifying the document to parse.
    /// </summary>
    /// <remarks>
    /// This identifies the document (for error locations and module identity) but does not by itself
    /// supply source text — the parser does not read from the filesystem. A document with no backing
    /// file (e.g. an unsaved <c>untitled:</c> buffer) still has a <c>DocumentUri</c>; only <see cref="Fragment"/>
    /// need be a real, saved file's content.
    /// </remarks>
    public Uri? DocumentUri { get; init; } = default;
    /// <summary>
    /// The source code to parse, verbatim, as currently held by the caller (which may differ from
    /// what is saved to disk, or may have no on-disk counterpart at all).
    /// </summary>
    /// <remarks>
    /// A full document is simply a fragment anchored at <see cref="SourcePosition.Zero"/> (<c>L0C0</c>),
    /// the default for <see cref="AnchorOffset"/>. A non-zero anchor identifies a sub-range fragment of
    /// a larger document.
    /// </remarks>
    public string? Fragment { get; init; } = default;
    /// <summary>
    /// The position of <see cref="Fragment"/> within the larger source document.
    /// </summary>
    /// <remarks>
    /// Anchor offset is <c>L0C0</c> (a full-document parse) unless specified otherwise.
    /// </remarks>
    public SourcePosition AnchorOffset { get; init; } = SourcePosition.Zero;
}
