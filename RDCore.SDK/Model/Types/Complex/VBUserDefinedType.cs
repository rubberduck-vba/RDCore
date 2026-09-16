using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
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

    ImmutableArray<VBDeferredTypeMemberSymbol> IVBMemberOwnerType.DeferredMembers { get; init; } = [];

    public IVBMemberOwnerType WithMembers(IEnumerable<VBTypeMemberSymbol> members) => this with { Members = [.. members] };

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