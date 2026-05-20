using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Server.ProtocolExtensions;

namespace RDCore.SDK.Model.Symbols.Abstract;

/// <summary>
/// Represents a global, unallocated symbol.
/// </summary>
public record class StaticSymbol : TypedSymbol
{
    private static readonly Lazy<Uri> _globalUri = new(() => new("file://a:/rdcore/vblang/global/"));
    /// <summary>
    /// Gets the RDCore <c>vblang/global</c> namespace <c>Uri</c>.
    /// </summary>
    public static Uri GlobalUri => _globalUri.Value;

    /// <summary>
    /// Creates a new <c>StaticSymbol</c> of the specified type.
    /// </summary>
    /// <param name="name"></param>
    /// <param name="kind"></param>
    /// <param name="typeInfo"></param>
    public StaticSymbol(string name, SymbolKindExt kind, VBType typeInfo)
        : base(ScopeKind.Unallocated, GlobalUri, name, kind, Accessibility.Undefined, GlobalUri, default!, default!)
    {
        ResolvedType = typeInfo;
    }
}
