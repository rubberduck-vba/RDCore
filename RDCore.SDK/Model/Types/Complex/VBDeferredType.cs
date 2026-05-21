using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.Types.Complex;

public interface IVBInferableType
{
    ImmutableHashSet<VBType> CandidateTypes { get; init; }
    IVBInferableType WithCandidateType(VBType vbType);
}

public interface IVBDeferrableType : IVBInferableType
{
    IVBMemberOwnerType? DeferredVBType { get; init; }
    VBDeferredType WithDeferredVBType(IVBMemberOwnerType vbType);
}

public interface IVBDeferrableTypeMember : IVBInferableType
{
    VBType? DeferredVBType { get; init; }
    VBDeferredTypeMemberSymbol WithDeferredVBType(VBType vbType);
}

public abstract record class VBDeferredType : VBType, IVBDeferrableType
{
    public VBDeferredType(string name, Uri uri)
        : base(typeof(object), name, isHidden: true)
    {
        Uri = uri;
    }

    public override int Size => sizeof(int);

    public Uri Uri { get; init; }

    public ImmutableHashSet<VBDeferredTypeMemberSymbol> Members { get; init; } = [];
    public VBDeferredType WithMembers(IEnumerable<VBDeferredTypeMemberSymbol> members) => this with { Members = [.. members] };

    public IVBMemberOwnerType? DeferredVBType { get; init; }
    public VBDeferredType WithDeferredVBType(IVBMemberOwnerType vbType) => this with { DeferredVBType = vbType };

    public ImmutableHashSet<VBType> CandidateTypes { get; init; } = [];
    public IVBInferableType WithCandidateType(VBType vbType) => this with { CandidateTypes = [.. CandidateTypes, vbType] };
}

public record class VBDeferredModuleType : VBDeferredType
{
    public VBDeferredModuleType(string name, Uri uri) : base(name, uri) { }

    private static readonly Lazy<VBVoidValue> _defaultValue = new(() => VBVoidValue.Void, LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;
}

