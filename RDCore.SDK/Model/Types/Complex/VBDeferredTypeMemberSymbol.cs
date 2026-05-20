using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Server.ProtocolExtensions;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.Types;

public record class VBDeferredTypeMemberSymbol : TypedSymbol, IVBDeferrableTypeMember
{
    public VBDeferredTypeMemberSymbol(Uri workspaceRoot, string name, SymbolKindExt kind, Uri parentUri)
        : base(ScopeKind.Module, workspaceRoot, name, kind, Accessibility.Public, parentUri)
    {
    }

    public ImmutableHashSet<VBType> CandidateTypes { get; init; } = [];
    public IVBInferableType WithCandidateType(VBType vbType) => this with { CandidateTypes = [.. CandidateTypes, vbType] };

    public VBType? DeferredVBType { get; init; }
    public VBDeferredTypeMemberSymbol WithDeferredVBType(VBType vbType) => this with { DeferredVBType = vbType, CandidateTypes = [vbType] };
}
