using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Workspace.Forms;

namespace RDCore.Runtime.LanguageCore.Static;

public static class FormControlExtender
{
    public const string ClassName = "Control";

    public static VBClassModuleSymbol? Of(ISymbolResolver resolver, Uri handle)
        => VBProjectSymbol.ResolveQualifiedType(resolver, MSFormsDesignerStorage.Library, ClassName, handle).Symbol as VBClassModuleSymbol;

    public static IEnumerable<VBEventMemberSymbol> EventsOf(VBTypeMemberSymbol variable, VBClassModuleSymbol source, VBClassModuleSymbol? extender)
    {
        var events = source.Events;
        return variable.GetProperty(SymbolProperties.DesignerControl) && extender is not null
            ? events.Concat(extender.Events.Where(extended => source.FindEvent(extended.Name) is null))
            : events;
    }
}
