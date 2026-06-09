using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Server.ProtocolExtensions;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.Symbols.VBProject;

public record class VBDeferredTypeMemberSymbol(Uri WorkspaceRoot, Uri ParentUri, string Name, SymbolKindExt Kind) 
    : UnboundTypedSymbol(WorkspaceRoot, ParentUri, Name, ScopeKind.Module, Kind, VBUnknownType.TypeInfo), IVBDeferrableTypeMember
{
    public ImmutableHashSet<VBType> CandidateTypes { get; init; } = [];
    public IVBInferableType WithCandidateType(VBType vbType) => this with { CandidateTypes = [.. CandidateTypes, vbType] };

    public VBType? DeferredVBType { get; init; }
    public VBDeferredTypeMemberSymbol WithDeferredVBType(VBType vbType) => this with { DeferredVBType = vbType, CandidateTypes = [vbType] };
}
