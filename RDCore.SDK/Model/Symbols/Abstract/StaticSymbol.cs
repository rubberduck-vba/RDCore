using RDCore.SDK.Extensibility;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Server.ProtocolExtensions;

namespace RDCore.SDK.Model.Symbols.Abstract;

/// <summary>
/// Represents a global, unallocated symbol.
/// </summary>
public record class StaticSymbol(string Name, SymbolKindExt Kind, VBType ResolvedType) 
    : UnboundTypedSymbol(StaticSymbol.GlobalUri, StaticSymbol.GlobalUri, Name, ScopeKind.Unallocated, Kind, ResolvedType)
{
    private static readonly Lazy<Uri> _globalUri = new(() => new(RDCoreUriNamespaces.RDCoreLanguageSpaceGlobalUri));
    /// <summary>
    /// Gets the <c>rd-vba/lang/global</c> namespace <c>Uri</c>.
    /// </summary>
    public static Uri GlobalUri => _globalUri.Value; 
}
