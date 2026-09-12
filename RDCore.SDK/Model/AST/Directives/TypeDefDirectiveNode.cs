using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.AST.Directives;

/// <summary>
/// A <c>BoundNode</c> representing a <c>Def&lt;Type&gt;</c> module directive.
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="Location">The <c>Location</c> of the directive.</param>
/// <param name="Token">The <c>DefType</c> token mapping to a specific <c>VBType</c> (per the semantics defined in MS-VBAL 5.2.2 Implicit Definition Directives).</param>
/// <param name="Mappings">The prefixing scheme defined by this directive.</param>
public record class TypeDefDirectiveNode(SyntaxNodeId Identity, SourceLocation Location, string Token, ImmutableArray<DefTypePrefixMapping> Mappings)
    : DirectiveNode(Identity, Location, [])
{
    public VBType GetVBType(bool is64bit) => Token switch
    {
        Tokens.DefBool => VBBooleanType.TypeInfo,
        Tokens.DefByte => VBByteType.TypeInfo,
        Tokens.DefCur => VBCurrencyType.TypeInfo,
        Tokens.DefDate => VBDateType.TypeInfo,
        Tokens.DefDbl => VBDoubleType.TypeInfo,
        Tokens.DefInt => VBIntegerType.TypeInfo,
        Tokens.DefLng => VBLongType.TypeInfo,
        Tokens.DefLngLng => VBLongLongType.TypeInfo,
        Tokens.DefLngPtr => is64bit ? VBLongPtrType_x64.TypeInfo : VBLongPtrType_x86.TypeInfo,
        Tokens.DefObj => VBObjectType.TypeInfo,
        Tokens.DefSng => VBSingleType.TypeInfo,
        Tokens.DefStr => VBStringType.TypeInfo,
        Tokens.DefVar => VBVariantType.TypeInfo,

        _ => VBUnknownType.TypeInfo // illegal
    };
}
