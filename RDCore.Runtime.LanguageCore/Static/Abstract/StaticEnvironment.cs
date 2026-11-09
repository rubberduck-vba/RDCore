using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;

namespace RDCore.Runtime.LanguageCore.Static.Abstract;

// MS-VBAL §5.6.16.8, §2.2
public sealed record class StaticEnvironment(bool Is64Bit)
{
    public VBType PointerType => Is64Bit ? VBLongPtrType_x64.TypeInfo : VBLongPtrType_x86.TypeInfo;

    public SDK.Runtime.Libraries.ILibrarySource? Libraries { get; init; }
}
