using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Workspace.Forms;

namespace RDCore.SDK.Semantics.Static;

/// <summary>
/// What a form adds to each of its controls: the members and events of the Microsoft Forms <c>Control</c> class, the extender every control of a form is
/// wrapped in.
/// </summary>
/// <remarks>
/// A control of a form is an object of its own class (<c>MSForms.TextBox</c>) <em>and</em> of <c>MSForms.Control</c>: what the form keeps of each of its
/// controls - its position (<c>Left</c>, <c>Top</c>), its place in the tab order (<c>TabIndex</c>), <c>Visible</c>, <c>SetFocus</c> - and the events the form
/// raises for it (<c>Enter</c>, <c>Exit</c>, <c>BeforeUpdate</c>, <c>AfterUpdate</c>). The control's class does not declare them, and an object of that class
/// declared by the code (<c>Dim t As MSForms.TextBox</c>) does not list them; the field of a form's class that the control is does
/// (<see cref="SymbolProperties.DesignerControl"/>). Both are described by the Microsoft Forms type library, which a project that has forms references.
/// </remarks>
public static class FormControlExtender
{
    /// <summary>
    /// The name of the extender class in the Microsoft Forms library.
    /// </summary>
    public const string ClassName = "Control";

    /// <summary>
    /// The extender class, <c>MSForms.Control</c>, as <paramref name="resolver"/> has it; <see langword="null"/> when the project does not reference
    /// Microsoft Forms.
    /// </summary>
    /// <param name="resolver">What finds the classes of the project's libraries.</param>
    /// <param name="handle">Where the name is resolved from.</param>
    public static VBClassModuleSymbol? Of(ISymbolResolver resolver, Uri handle)
        => VBProjectSymbol.ResolveQualifiedType(resolver, MSFormsDesignerStorage.Library, ClassName, handle).Symbol as VBClassModuleSymbol;

    /// <summary>
    /// The events the procedures named for <paramref name="variable"/> handle: those of <paramref name="source"/>, its class, and - when it is a form's
    /// control - those of the extender after them.
    /// </summary>
    /// <param name="variable">A <c>WithEvents</c> variable of a class module.</param>
    /// <param name="source">The class the variable is declared as.</param>
    /// <param name="resolver">What finds the extender class.</param>
    public static IEnumerable<VBEventMemberSymbol> EventsOf(VBTypeMemberSymbol variable, VBClassModuleSymbol source, ISymbolResolver resolver)
    {
        var events = source.Events;
        return variable.GetProperty(SymbolProperties.DesignerControl) && Of(resolver, variable.ParentUri) is { } extender
            ? events.Concat(extender.Events.Where(extended => source.FindEvent(extended.Name) is null))
            : events;
    }
}
