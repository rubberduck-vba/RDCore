using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Workspace.Forms;

namespace RDCore.Runtime.LanguageCore.Static;

public static class FormClasses
{
    // the roots whose class the root's own class is built on, not composed with: VB6's UserControl, PropertyPage and UserDocument. The base class's members
    // are names in scope in the root's code, unqualified or through the base class's name (`UserControl.BackColor`), and not members of the root's class:
    // Me has none of them, early- or late-bound (observed in VB6).
    private static readonly HashSet<string> BaseClasses = new(StringComparer.OrdinalIgnoreCase) { "UserControl", "PropertyPage", "UserDocument" };

    public static string? HandlerPrefixOf(FormDescriptor designer)
        => designer.Kind == FormDesignerKind.Other ? null : designer.Class.Name;

    public static string? BaseClassNameOf(FormDescriptor designer)
        => designer is { Kind: FormDesignerKind.VisualBasic6, Class: { Library: { } library, Name: { } name } }
            && string.Equals(library, "VB", StringComparison.OrdinalIgnoreCase) && BaseClasses.Contains(name)
            ? name
            : null;

    public static IEnumerable<VBClassModuleSymbol> Of(FormDescriptor designer, ISymbolResolver resolver, Uri handle, Uri workspaceRoot)
    {
        if (designer.Kind == FormDesignerKind.Other)
        {
            yield break;
        }

        if (designer.Class is { Library: { } library, Name: { } name }
            && VBProjectSymbol.ResolveQualifiedType(resolver, library, name, handle).Symbol is VBClassModuleSymbol hosted)
        {
            yield return hosted;
        }

        if (designer.Kind == FormDesignerKind.MSForms)
        {
            yield return FormObject.ClassIn(workspaceRoot);
        }
    }

    public static VBClassModuleSymbol? ExtenderOf(FormDescriptor designer, ISymbolResolver resolver, Uri handle)
        => designer.Kind == FormDesignerKind.MSForms ? FormControlExtender.Of(resolver, handle) : null;
}
