using RDCore.SDK.Model.Symbols.Unbound;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Server.ProtocolExtensions;

namespace RDCore.SDK.Model.Symbols.Abstract;

/// <summary>
/// Represents a global, unallocated symbol.
/// </summary>
public record class StaticSymbol(string Name, SymbolKindExt Kind, VBType? ResolvedType = default) 
    : UnboundTypedSymbol(StaticSymbol.GlobalUri, StaticSymbol.GlobalUri, Name, ScopeKind.Unallocated, Kind, ResolvedType ?? VBUnknownType.TypeInfo)
{
    // TODO move this somewhere else
    private static readonly Lazy<Uri> _globalUri = new(() => new("file://a:/rdcore/vblang/global/"));
    /// <summary>
    /// Gets the RDCore <c>vblang/global</c> namespace <c>Uri</c>.
    /// </summary>
    public static Uri GlobalUri => _globalUri.Value; 
}
