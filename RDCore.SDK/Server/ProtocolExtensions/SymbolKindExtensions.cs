using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace RDCore.SDK.Server.ProtocolExtensions;

/// <summary>
/// Converts the model's extended <c>SymbolKindExt</c> to the LSP protocol <c>SymbolKind</c> at the protocol boundary.
/// </summary>
/// <remarks>
/// Can be used by a LSP client to categorize symbols. The internal model uses an extended set beyond LSP 3.17 that clients may ignore.
/// </remarks>
public static class SymbolKindExtensions
{
    public static SymbolKind ToLsp(this SymbolKindExt kind) => (SymbolKind)kind;
}
