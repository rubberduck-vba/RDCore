using RDCore.SDK.Model.AST.Directives;

namespace RDCore.SDK.Model.AST.Declarations;

/// <summary>
/// Helpers over a parsed <see cref="ModuleNode"/>.
/// </summary>
public static class ModuleNodeExtensions
{
    /// <summary>
    /// The module name declared by the module-level <c>Attribute VB_Name</c> directive, or
    /// <c>null</c> when the module declares none.
    /// </summary>
    /// <remarks>
    /// This is the authoritative programmatic name of a module; callers that also need the file-name
    /// fallback should route through <c>ModuleName.Resolve</c>. A member-qualified attribute
    /// (<c>Attribute Foo.VB_Description</c>) carries a <see cref="AttributeDirectiveNode.Binding"/>
    /// and is not a module name.
    /// </remarks>
    public static string? GetDeclaredName(this ModuleNode module)
    {
        foreach (var child in module.Children)
        {
            if (child is AttributeDirectiveNode { Binding: null } attribute
                && string.Equals(attribute.Name, Tokens.VB_Name, StringComparison.OrdinalIgnoreCase))
            {
                return Unquote(attribute.Value);
            }
        }

        return null;
    }

    // AttributeDirectiveNode.Value is the raw parse-tree text; a VB_Name value is a string literal.
    private static string? Unquote(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            return trimmed[1..^1].Replace("\"\"", "\"");
        }

        return trimmed.Length == 0 ? null : trimmed;
    }
}
