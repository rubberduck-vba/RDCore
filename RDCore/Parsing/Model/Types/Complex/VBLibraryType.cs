using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Types.Intrinsic;
using RDCore.Parsing.Model.Values.Abstract;
using System.Collections.Immutable;

namespace RDCore.Parsing.Model.Types.Complex;

internal record class VBLibraryType : VBType
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