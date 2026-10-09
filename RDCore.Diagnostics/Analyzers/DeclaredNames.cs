using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// A name that a module declares, and where it is written.
/// </summary>
/// <param name="Name">The name, without its type-declaration character.</param>
/// <param name="Range">Where the name is written.</param>
internal readonly record struct DeclaredName(string Name, SourceRange Range);

/// <summary>
/// The names a module chooses: the ones a rule about names applies to.
/// </summary>
/// <remarks>
/// Not every name is chosen by the module. A procedure of an external library is named by the library; a procedure whose name has an underscore is the implementation of
/// an interface member, the handler of an event or a lifecycle handler (<c>Class_Initialize</c>), and is named by the interface, the event or the language. They are
/// left out. The accessors of a property are one declaration, and its name is reported once.
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
                case VariableDeclarationNode variable:
                    yield return new(variable.Name, variable.NameRange ?? variable.SourceLocation.Range);
                    break;

                case ConstantDeclarationNode constant:
                    yield return new(constant.Name, constant.NameRange ?? constant.Location.Range);
                    break;

                case ParameterDeclarationNode parameter:
                    yield return new(parameter.Name, parameter.NameRange ?? parameter.Location.Range);
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
