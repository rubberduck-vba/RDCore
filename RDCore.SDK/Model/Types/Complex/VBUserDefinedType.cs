using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using System.Collections.Immutable;

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace RDCore.SDK.Model.Types;

#pragma warning restore IDE0130 // Namespace does not match folder structure

/// <summary>
/// Represents any <em>User-Defined Type</em> (UDT) structure.
/// </summary>
/// <param name="Symbol">The symbol associated with this UDT.</param>
/// <param name="Members">The members (fields) of the UDT.</param>
/// 
public record class VBUserDefinedType(Symbol Symbol, ImmutableArray<VBTypeMemberSymbol> Members) : VBType(typeof(Type), Symbol.Name), 
    IVBMemberOwnerType, IEquatable<VBUserDefinedType>
{
    public override VBTypedValue DefaultValue => new VBUserDefinedTypeValue(this);

    /// <remarks>
    /// A UDT's field cells live on the value itself, so the value is handed back out of its own binding rather
    /// than rebuilt from it — the same way <see cref="VBArrayType.CreateValue"/> hands back its element cells.
    /// Rebuilding would silently return a UDT with default fields however much had been assigned to it.
    /// </remarks>
    public override VBTypedValue CreateValue(Values.Bindings.IBindingHandle handle)
        => handle.Value is VBRuntimeValue<VBRuntimeUserDefinedTypeValue> boxed
            ? boxed.StoredValue.UserDefinedType
            // a binding that is not one of this type's own values is one nothing has stored a UDT in yet: a
            // freshly allocated slot, whose value is a UDT with every field at its declared default.
            : new VBUserDefinedTypeValue(handle, this);

    ImmutableArray<VBDeferredTypeMemberSymbol> IVBMemberOwnerType.DeferredMembers { get; init; } = [];

    public IVBMemberOwnerType WithMembers(IEnumerable<VBTypeMemberSymbol> members) => this with { Members = [.. members] };

    /// <summary>
    /// This type's fields, in declaration order.
    /// </summary>
    /// <remarks>
    /// <see cref="Members"/> is typed for members in general and a <c>Type … End Type</c> declares nothing
    /// but fields, so every caller that wanted them was narrowing the same way. The <em>order</em> is the
    /// part that matters: it is the order <strong>MS-VBAL §5.4.5.11</strong> writes a record in, and the order
    /// <see cref="VBUserDefinedTypeLayout"/> assigns offsets in.
    /// </remarks>
    public IEnumerable<VBUserDefinedTypeFieldSymbol> Fields() => Members.OfType<VBUserDefinedTypeFieldSymbol>();

    /// <summary>
    /// The field named <paramref name="name"/>, or <c>null</c> when this type has no such field.
    /// </summary>
    /// <param name="name">The field name, compared case-insensitively as VBA compares identifiers.</param>
    public VBUserDefinedTypeFieldSymbol? Field(string name)
        => Fields().FirstOrDefault(field => field.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <remarks>
    /// Compares by the declaring symbol's <c>Uri</c> instead of the compiler-generated deep
    /// structural comparison: <see cref="Members"/> can reference field symbols whose own
    /// <c>ResolvedType</c> loops back to this very UDT, which the default record equality has no way
    /// to guard against. Compares <c>Uri.AbsoluteUri</c> as an ordinal string rather than using
    /// <see cref="Uri"/>'s own <c>Equals</c>/<c>GetHashCode</c>: those deliberately ignore
    /// <c>Uri.Fragment</c>, and a symbol's discriminating name/location is encoded entirely in the
    /// fragment here — two distinct UDTs sharing a workspace root would otherwise compare equal.
    /// </remarks>
    public virtual bool Equals(VBUserDefinedType? other)
        => other is VBUserDefinedType udt && string.Equals(udt.Symbol.Uri.AbsoluteUri, Symbol.Uri.AbsoluteUri, StringComparison.Ordinal);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Symbol.Uri.AbsoluteUri);
}

/// <summary>
/// Represents a <c>VBUserDefinedType</c> imported from an external lirary.
/// </summary>
/// <param name="Symbol">The symbol associated with this UDT.</param>
/// <param name="Members">The members (fields) of the UDT.</param>
public record class VBExternalUserDefinedType(Symbol Symbol, ImmutableArray<VBTypeMemberSymbol> Members) : VBUserDefinedType(Symbol, Members) { }