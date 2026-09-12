using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Source;

namespace RDCore.SDK.Model.AST.Directives;

/// <summary>
/// A <c>BoundNode</c> representing an <c>Option</c> module directive.
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="Location">The <c>Location</c> of the directive.</param>
/// <param name="ModuleOption">The <c>ModuleOptions</c> value being configured.</param>
public record class ModuleOptionDirectiveNode(SyntaxNodeId Identity, SourceLocation Location, ModuleOptions ModuleOption)
    : DirectiveNode(Identity, Location, []);
