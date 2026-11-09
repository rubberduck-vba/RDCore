using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Workspace.Forms;

namespace RDCore.Runtime.LanguageCore.Static;

public static class FormClasses
{
    public static string? HandlerPrefixOf(FormDescriptor designer)
        => designer.Kind == FormDesignerKind.Other ? null : designer.Class.Name;

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
