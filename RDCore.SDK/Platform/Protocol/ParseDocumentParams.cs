using MediatR;
using OmniSharp.Extensions.JsonRpc;
using RDCore.SDK.Client;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;

namespace RDCore.SDK.Platform.Protocol;

/// <summary>
/// The <em>parameter</em> object for a <c>ParseDocumentCommand</c>. The <c>[Method]</c> attribute lets
/// the JSON-RPC layer infer the request method when the caller sends this by type.
/// </summary>
[Method(RDCorePlatformProtocol.ParseFullDocument, Direction.ClientToServer)]
public record class ParseDocumentParams : IRequest, IRequest<ModuleParseResult>
{
    /// <summary>
    /// The <c>Uri</c> of the document to parse.
    /// </summary>
    public Uri? DocumentUri { get; init; } = default;
    /// <summary>
    /// The type of module (for the root AST node).
    /// </summary>
    public ModuleType ModuleType { get; init; } = ModuleType.StdModule;
    /// <summary>
    /// The fragment of source code to parse.
    /// </summary>
    /// <remarks>
    /// An <c>AnchorOffset</c> should also be specified.
    /// </remarks>
    public string? Fragment { get; init; } = default;
    /// <summary>
    /// The position of the fragment in the source document.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>This property is ignored if a <c>DocumentUri</c> is specified.</item>
    /// <item>Anchor offset is <c>L0C0</c> unless specified otherwise.</item>
    /// </list>
    /// </remarks>
    public SourcePosition AnchorOffset { get; init; } = SourcePosition.Zero;
}
