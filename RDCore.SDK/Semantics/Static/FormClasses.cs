using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Workspace.Forms;

namespace RDCore.SDK.Semantics.Static;

/// <summary>
/// What a form's class has besides its controls and its code, by the designer that wrote it: the members and events of the classes it is composed with, the
/// prefix of the handlers of its own events, and the extender its controls are wrapped in.
/// </summary>
/// <remarks>
/// <list type="table">
/// <listheader><term>Designer</term><description>Composed with; handlers; extender</description></listheader>
/// <item><term><see cref="FormDesignerKind.MSForms"/></term><description><c>MSForms.UserForm</c>, then the VBA form object (<see cref="FormObject"/>);
/// <c>UserForm_EventName</c>; <c>MSForms.Control</c> (<see cref="FormControlExtender"/>).</description></item>
/// <item><term><see cref="FormDesignerKind.VisualBasic6"/></term><description>The class its designer block names (<c>VB.Form</c>, <c>VB.MDIForm</c>);
/// <c>Form_EventName</c>, <c>MDIForm_EventName</c>; none: the classes of the Visual Basic intrinsic controls declare their extender properties and events
/// themselves (<c>Left</c>, <c>TabIndex</c>, <c>GotFocus</c>, <c>Validate</c>).</description></item>
/// <item><term><see cref="FormDesignerKind.Other"/></term><description>Nothing.</description></item>
/// </list>
/// A class of a library is one only when the project references that library; one it does not reference gives the form nothing.
/// </remarks>
public static class FormClasses
{
    /// <summary>
    /// The prefix of the handlers of the form's own events: the name of the class its designer block names (<c>UserForm</c>, <c>Form</c>, <c>MDIForm</c>),
    /// whatever the form's own name. <see langword="null"/> for a designer whose class is named by its class identifier only.
    /// </summary>
    /// <param name="designer">What the form's designer declares.</param>
    public static string? HandlerPrefixOf(FormDescriptor designer)
        => designer.Kind == FormDesignerKind.Other ? null : designer.Class.Name;

    /// <summary>
    /// The classes whose members and events the form's class has besides its controls and its code, in order: the class its designer block names, as
    /// <paramref name="resolver"/> has it, and, for a UserForm, the VBA form object after it.
    /// </summary>
    /// <param name="designer">What the form's designer declares.</param>
    /// <param name="resolver">What finds the classes of the project's libraries.</param>
    /// <param name="handle">Where the names are resolved from.</param>
    /// <param name="workspaceRoot">The root of the workspace the symbols are of.</param>
    /// <returns>The classes; none for a designer this does not know.</returns>
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

    /// <summary>
    /// The extender class the form wraps each of its controls in: <c>MSForms.Control</c> for a UserForm whose project references Microsoft Forms
    /// (<see cref="FormControlExtender.Of"/>); <see langword="null"/> otherwise.
    /// </summary>
    /// <param name="designer">What the form's designer declares.</param>
    /// <param name="resolver">What finds the classes of the project's libraries.</param>
    /// <param name="handle">Where the name is resolved from.</param>
    public static VBClassModuleSymbol? ExtenderOf(FormDescriptor designer, ISymbolResolver resolver, Uri handle)
        => designer.Kind == FormDesignerKind.MSForms ? FormControlExtender.Of(resolver, handle) : null;
}
