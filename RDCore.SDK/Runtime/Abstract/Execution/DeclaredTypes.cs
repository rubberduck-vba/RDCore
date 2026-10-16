using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;

namespace RDCore.SDK.Runtime.Abstract.Execution;

/// <summary>
/// The type an <c>&lt;as-type&gt;</c> clause names (<strong>MS-VBAL §5.2.3.1.4</strong>), as a session's symbols resolve it.
/// </summary>
/// <remarks>
/// One resolution for every construct that names a type: the declarations the host defines, and the statements that name one as they run, a
/// <c>ReDim</c>'s <c>As</c> clause among them. A type named in two places is the same type in both.
/// </remarks>
public static class DeclaredTypes
{
    /// <summary>
    /// Resolves a type name as it is written in an <c>As</c> clause.
    /// </summary>
    /// <param name="resolver">The session's symbols.</param>
    /// <param name="typeName">
    /// The name: an intrinsic type's (<c>Long</c>), a workspace or library type's (<c>Point</c>), or one qualified by its module, project or library
    /// (<c>Shapes.Point</c>, <c>VBA.VbDayOfWeek</c>).
    /// </param>
    /// <param name="scope">The module or procedure the name is written in, which the name is resolved as seen from.</param>
    /// <param name="is64Bit">Whether the environment is 64-bit, which decides what a <c>LongPtr</c> is.</param>
    /// <returns>The type, or <c>null</c> when the name resolves to no type.</returns>
    public static VBType? Resolve(ISymbolResolver resolver, string typeName, Uri scope, bool is64Bit)
    {
        if (IntrinsicVBTypes.TryResolve(typeName, is64Bit, out var intrinsic))
        {
            return intrinsic;
        }

        // a name qualified by a library is that library's, whatever else the references call by it.
        var resolved = typeName.IndexOf('.') is > 0 and var dot
            && VBProjectSymbol.ResolveQualifiedType(resolver, typeName[..dot], typeName[(dot + 1)..], scope) is { IsResolved: true } qualified
                ? qualified
                : resolver.ResolveType(typeName, ScopeKind.Module, scope);

        return resolved.IsResolved ? Of(resolved.Symbol) : null;
    }

    /// <summary>
    /// The type a symbol that an <c>As</c> clause names declares.
    /// </summary>
    /// <remarks>
    /// A class module is its class; an <c>Enum</c> is its enumeration, which a declaration reads as <c>Long</c> (<strong>MS-VBAL §5.2.3.4</strong>);
    /// a user-defined type is its fields, which are symbols of their own in the session once the type is defined.
    /// </remarks>
    /// <param name="symbol">The symbol the name resolved to.</param>
    /// <returns>The type, or <c>null</c> for a symbol that declares none.</returns>
    public static VBType? Of(Symbol? symbol) => symbol switch
    {
        VBClassModuleSymbol classModule => VBClassType.FromClassModule(classModule),
        VBEnumMemberSymbol { ResolvedType: VBEnumType enumType } => enumType,
        VBUserDefinedTypeMemberSymbol udt => new VBUserDefinedType(udt, udt.Members),
        _ => null,
    };
}
