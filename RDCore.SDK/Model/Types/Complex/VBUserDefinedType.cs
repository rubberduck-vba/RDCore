using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
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
    public override VBTypedValue DefaultValue => VBVoidValue.Void;
    public override int Size => Members.Sum(member => member.ResolvedType.Size); // FIXME this is wrong, there's actually some padding going on

    public IVBMemberOwnerType WithMembers(IEnumerable<VBTypeMemberSymbol> members) => this with { Members = [.. members] };

    public virtual bool Equals(VBUserDefinedType? other) => other is VBUserDefinedType udt && udt.Symbol.Uri == Symbol.Uri;
    public override int GetHashCode() => Symbol.Uri.GetHashCode();
}

/// <summary>
/// Represents a <c>VBUserDefinedType</c> imported from an external lirary.
/// </summary>
/// <param name="Symbol">The symbol associated with this UDT.</param>
/// <param name="Members">The members (fields) of the UDT.</param>
public record class VBExternalUserDefinedType(Symbol Symbol, ImmutableArray<VBTypeMemberSymbol> Members) : VBUserDefinedType(Symbol, Members) { }