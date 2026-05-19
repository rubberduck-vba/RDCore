using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Intrinsic;
using RDCore.SDK.Model.Values.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.Types.Complex;

public record class VBLibraryType : VBType
{
    public VBLibraryType(string name, Uri uri, IEnumerable<IVBMemberOwnerType> modules, bool isUserDefined = false)
        : base(typeof(object), name, isUserDefined, isHidden: false)
    {
        Uri = uri;
        Modules = [.. modules];
    }

    public Uri Uri { get; init; }
    public ImmutableArray<IVBMemberOwnerType> Modules { get; init; } = [];

    public override VBTypedValue DefaultValue => VBLongPtrType.TypeInfo.DefaultValue;

    public VBLibraryType WithModules(IEnumerable<IVBMemberOwnerType> modules) => this with { Modules = [.. modules] };
}