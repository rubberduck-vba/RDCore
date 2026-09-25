using RDCore.SDK.Model.AST.Directives;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols;
using System.Collections.Immutable;

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

    /// <summary>
    /// Whether the module declares <c>Option Explicit</c> (<strong>MS-VBAL §5.2.1.3</strong>).
    /// </summary>
    public static bool HasOptionExplicit(this ModuleNode module)
    {
        foreach (var child in module.Children)
        {
            if (child is ModuleOptionDirectiveNode { ModuleOption: ModuleOptions.OptionExplicit })
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The comparison mode the module declares with an <c>Option Compare</c> directive (<strong>MS-VBAL §5.2.1.1</strong>),
    /// <see cref="OptionCompare.Binary"/> when it declares none.
    /// </summary>
    public static OptionCompare GetOptionCompare(this ModuleNode module)
    {
        foreach (var child in module.Children.OfType<ModuleOptionDirectiveNode>())
        {
            switch (child.ModuleOption)
            {
                case ModuleOptions.OptionCompareText:
                    return OptionCompare.Text;
                case ModuleOptions.OptionCompareDatabase:
                    return OptionCompare.Database;
                case ModuleOptions.OptionCompareBinary:
                    return OptionCompare.Binary;
            }
        }

        return OptionCompare.Binary;
    }

    /// <summary>
    /// Whether a class module is instantiable via <c>New</c>, per its <c>Attribute VB_Creatable</c>
    /// directive. Defaults to <c>true</c> — VBE's own default for a class module that declares no
    /// such attribute — so this is meaningful to call on any module, not just class modules.
    /// </summary>
    public static bool IsCreatable(this ModuleNode module)
    {
        foreach (var attribute in module.Children.OfType<AttributeDirectiveNode>())
        {
            if (attribute.Binding is null && string.Equals(attribute.Name, Tokens.VB_Creatable, StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(attribute.Value.Trim(), "True", StringComparison.OrdinalIgnoreCase);
            }
        }

        return true;
    }

    /// <summary>
    /// Whether a class module has a predeclared default instance, per its
    /// <c>Attribute VB_PredeclaredId</c> directive (<strong>MS-VBAL §5.2.4.1.2</strong>). Defaults to
    /// <c>false</c> — VBE's own default for a class module that declares no such attribute.
    /// </summary>
    public static bool IsPredeclared(this ModuleNode module)
    {
        foreach (var attribute in module.Children.OfType<AttributeDirectiveNode>())
        {
            if (attribute.Binding is null && string.Equals(attribute.Name, Tokens.VB_PredeclaredId, StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(attribute.Value.Trim(), "True", StringComparison.OrdinalIgnoreCase);
            }
        }

        return false;
    }

    /// <summary>
    /// The value of a member's own <c>Attribute &lt;member&gt;.VB_UserMemId</c> directive, or
    /// <c>null</c> when the module declares none for that member. RD-VBA's own use of the value:
    /// <c>WellKnownDispIds.Value</c> (<c>0</c>) denotes the class's default member,
    /// <c>WellKnownDispIds.NewEnum</c> (<c>-4</c>) its enumeration member.
    /// </summary>
    /// <remarks>
    /// Unlike a module-level attribute (<c>VB_Name</c>, <c>VB_Creatable</c>, ...), a member-qualified
    /// attribute statement is written lexically inside the member's own body — the parser attaches it
    /// as a child of that member's own <see cref="MemberDeclarationNode"/>, not the module's — so this
    /// searches the named member's <c>Children</c>, not <see cref="ModuleNode.Children"/> directly.
    /// </remarks>
    public static int? GetMemberUserMemId(this ModuleNode module, string memberName)
    {
        foreach (var member in module.Children.OfType<MemberDeclarationNode>())
        {
            if (!string.Equals(member.Name, memberName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            foreach (var attribute in member.Children.OfType<AttributeDirectiveNode>())
            {
                if (attribute.Binding is not null
                    && string.Equals(attribute.Binding, memberName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(attribute.Name, Tokens.VB_UserMemId, StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(attribute.Value.Trim(), out var value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The interface names named by this module's own <c>Implements</c> directives
    /// (<strong>MS-VBAL §5.2.4.2</strong>), exactly as written — unresolved, in source order. A
    /// project-qualified name (<c>Implements Project.IFoo</c>) yields just <c>IFoo</c>: RDCore only
    /// ever composes one project's modules at a time, so the qualifier can only ever mean this same
    /// project. A half-typed <c>Implements</c> with no name at all is skipped.
    /// </summary>
    public static ImmutableArray<string> GetImplementedInterfaceNames(this ModuleNode module)
    {
        var names = ImmutableArray.CreateBuilder<string>();
        foreach (var directive in module.Children.OfType<ImplementsDirectiveNode>())
        {
            var name = directive.NameExpression switch
            {
                SimpleNameExpressionNode simple => simple.IdentifierName,
                MemberAccessExpressionNode { Member: { } member } => member.IdentifierName,
                _ => null,
            };
            if (name is not null)
            {
                names.Add(name);
            }
        }
        return names.ToImmutable();
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
