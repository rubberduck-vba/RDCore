using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Directives;
using RDCore.SDK.Model.Source;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// A name that a module declares, and where it is written.
/// </summary>
/// <param name="Name">The name, without its type-declaration character.</param>
/// <param name="Range">Where the name is written.</param>
/// <param name="IsModule">Whether it is the name of the module itself, which a rename does not change.</param>
internal readonly record struct DeclaredName(string Name, SourceRange Range, bool IsModule = false);

/// <summary>
/// The names a module chooses: the ones a rule about names applies to.
/// </summary>
/// <remarks>
/// Not every name is chosen by the module. A procedure of an external library is named by the library; a procedure whose name has an underscore is the implementation of
/// an interface member, the handler of an event or a lifecycle handler (<c>Class_Initialize</c>), and is named by the interface, the event or the language. They are
/// left out. The accessors of a property are one declaration, and its name is reported once. A line label is a name the procedure chooses; a line number is a number,
/// and is left out. The module's own name is one of them, where its <c>Attribute VB_Name</c> says it.
/// </remarks>
internal static class DeclaredNames
{
    /// <summary>
    /// Every name <paramref name="module"/> chooses, in the order they are written.
    /// </summary>
    /// <param name="module">The syntax tree of the module.</param>
    public static IEnumerable<DeclaredName> In(ModuleNode module)
    {
        var properties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in module.Descendants())
        {
            switch (node)
            {
                case AttributeDirectiveNode { Binding: null } attribute when string.Equals(attribute.Name, Tokens.VB_Name, StringComparison.OrdinalIgnoreCase):
                    if (module.GetDeclaredName() is { Length: > 0 } name)
                    {
                        yield return new(name, attribute.Location.Range, IsModule: true);
                    }

                    break;

                case VariableDeclarationNode variable:
                    yield return new(variable.Name, variable.NameRange ?? variable.SourceLocation.Range);
                    break;

                case ConstantDeclarationNode constant:
                    yield return new(constant.Name, constant.NameRange ?? constant.Location.Range);
                    break;

                case ParameterDeclarationNode parameter:
                    yield return new(parameter.Name, parameter.NameRange ?? parameter.Location.Range);
                    break;

                case LineLabelNode label when label is not LineNumberNode:
                    yield return new(label.Name, label.NameRange ?? label.SourceLocation.Range);
                    break;

                case ExternalMemberDeclarationNode:
                    break;

                case MemberDeclarationNode { MemberKind: MemberKind.PropertyGet or MemberKind.PropertyLet or MemberKind.PropertySet } property:
                    if (!property.Name.Contains('_') && properties.Add(property.Name))
                    {
                        yield return new(property.Name, property.NameRange ?? property.SourceLocation.Range);
                    }

                    break;

                case MemberDeclarationNode { MemberKind: MemberKind.Procedure or MemberKind.Function } procedure:
                    if (!procedure.Name.Contains('_'))
                    {
                        yield return new(procedure.Name, procedure.NameRange ?? procedure.SourceLocation.Range);
                    }

                    break;

                case MemberDeclarationNode member:
                    yield return new(member.Name, member.NameRange ?? member.SourceLocation.Range);
                    break;
            }
        }
    }
}
