using RDCore.Parsing.Model.Symbols.Abstract;
using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Values;
using RDCore.Parsing.Model.Values.Abstract;
using RDCore.Parsing.Model.Values.Intrinsic;
using RDCore.SDK.Server.ProtocolExtensions;
using System.Collections.Immutable;

namespace RDCore.Parsing.Model.Types.Complex;

internal interface IVBInferableType
{
    ImmutableHashSet<VBType> CandidateTypes { get; init; }
    IVBInferableType WithCandidateType(VBType vbType);
}

internal interface IVBDeferrableType : IVBInferableType
{
    IVBMemberOwnerType? DeferredVBType { get; init; }
    VBDeferredType WithDeferredVBType(IVBMemberOwnerType vbType);
}

internal interface IVBDeferrableTypeMember : IVBInferableType
{
    VBType? DeferredVBType { get; init; }
    VBDeferredTypeMemberSymbol WithDeferredVBType(VBType vbType);
}

internal abstract record class VBDeferredType : VBType, IVBDeferrableType
{
    public VBDeferredType(string name, Uri uri)
        : base(typeof(object), name, isUserDefined: true, isHidden: true)
    {
        Uri = uri;
    }

    public Uri Uri { get; init; }

    public ImmutableHashSet<VBDeferredTypeMemberSymbol> Members { get; init; } = [];
    public VBDeferredType WithMembers(IEnumerable<VBDeferredTypeMemberSymbol> members) => this with { Members = [.. members] };

    public IVBMemberOwnerType? DeferredVBType { get; init; }
    public VBDeferredType WithDeferredVBType(IVBMemberOwnerType vbType) => this with { DeferredVBType = vbType };

    public ImmutableHashSet<VBType> CandidateTypes { get; init; } = [];
    public IVBInferableType WithCandidateType(VBType vbType) => this with { CandidateTypes = [.. CandidateTypes, vbType] };
}

internal record class VBDeferredTypeMemberSymbol : TypedSymbol, IVBDeferrableTypeMember
{
    public VBDeferredTypeMemberSymbol(Uri workspaceRoot, string name, SymbolKindExt kind, Uri parentUri)
        : base(workspaceRoot, name, kind, Accessibility.Public, parentUri, ScopeKind.Module)
    {
    }

    public ImmutableHashSet<VBType> CandidateTypes { get; init; } = [];
    public IVBInferableType WithCandidateType(VBType vbType) => this with { CandidateTypes = [.. CandidateTypes, vbType] };

    public VBType? DeferredVBType { get; init; }
    public VBDeferredTypeMemberSymbol WithDeferredVBType(VBType vbType) => this with { DeferredVBType = vbType, CandidateTypes = [vbType] };
}

internal record class VBDeferredModuleType : VBDeferredType
{
    public VBDeferredModuleType(string name, Uri uri) : base(name, uri) { }

    private static readonly Lazy<VBVoidValue> _defaultValue = new(() => VBVoidValue.Void, LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;
}

internal record class VBDeferredClassModuleType : VBDeferredType
{
    public VBDeferredClassModuleType(string name, Uri uri) : base(name, uri) { }

    private static readonly Lazy<VBObjectValue> _defaultValue = new(() => VBObjectValue.Nothing, LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;
}

