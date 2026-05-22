using RDCore.SDK.Model.Types.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.Types.Meta;

/// <summary>
/// Describes a <em>returning member</em>; a module member that returns a valid <c>VBTypedValue</c>.
/// </summary>
/// <param name="Name">The name of the returning member</param>
public abstract record class VBReturningMemberDesc(string Name) : VBMemberDesc(Name) { }
