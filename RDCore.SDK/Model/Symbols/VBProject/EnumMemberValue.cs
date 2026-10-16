using RDCore.SDK.Model.AST.Abstract;

namespace RDCore.SDK.Model.Symbols.VBProject;

/// <summary>
/// How the value of a member of a workspace <c>Enum</c> is declared (<strong>MS-VBAL §5.2.3.4</strong>).
/// </summary>
/// <remarks>
/// An <c>&lt;enum-member&gt;</c> that has a <c>&lt;constant-expression&gt;</c> is the value of that expression Let-coerced to
/// <c>Long</c>. One that has none is 0 when it is the first of its <c>&lt;enum-member-list&gt;</c>, and otherwise 1 greater than the
/// member before it. Every member is therefore the nearest expression at or before it plus the number of members between them, or that
/// number alone when no member before it has an expression - which is what this carries, so that a member's value needs nothing but its
/// own symbol to be determined.
/// </remarks>
/// <param name="BaseValue">
/// The <c>&lt;constant-expression&gt;</c> of this member, or of the nearest member before it that has one; <c>null</c> when neither has one.
/// </param>
/// <param name="Offset">
/// How many members after the one whose expression is <paramref name="BaseValue"/> this one is: 0 for a member that has an expression of
/// its own. When <paramref name="BaseValue"/> is <c>null</c>, the member's position in its list, counting from 0, which is its value.
/// </param>
public sealed record class EnumMemberValue(ExpressionNode? BaseValue, int Offset);
