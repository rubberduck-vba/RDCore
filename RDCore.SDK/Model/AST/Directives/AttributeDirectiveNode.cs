using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Source;

namespace RDCore.SDK.Model.AST.Directives;

/// <summary>
/// A <c>BoundNode</c> representing a <c>VB_Attribute</c> directive (module- or member-level).
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="Location">The <c>Location</c> of the directive.</param>
/// <param name="Name">The unqualified name of the attribute (e.g. <c>VB_Description</c>, <c>VB_UserMemId</c>).</param>
/// <param name="Value">The raw source text of the attribute value(s) — e.g. <c>"…"</c>, <c>0</c>, <c>True</c>. Comma-separated values are joined with <c>", "</c>.</param>
/// <param name="Binding">The member-name qualifier for a member-level attribute (<c>Foo</c> in <c>Attribute Foo.VB_Description</c>); <c>null</c> for a module-level attribute.</param>
public record class AttributeDirectiveNode(SyntaxNodeId Identity, SourceLocation Location, string Name, string Value, string? Binding = null)
    : DirectiveNode(Identity, Location, []);
