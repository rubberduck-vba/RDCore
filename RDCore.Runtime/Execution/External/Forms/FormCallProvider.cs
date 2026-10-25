using RDCore.External;
using RDCore.External.Automation;
using RDCore.External.Forms;
using RDCore.Runtime.Execution.External.Automation;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Workspace.Forms;

namespace RDCore.Runtime.Execution.External.Forms;

/// <summary>
/// Runs the members a form's designer declares on its class (<see cref="FormObject.IsDeclaredBy"/>): its controls, the members of the Microsoft Forms
/// <c>UserForm</c> it is, and those of the VBA form object - with the form host of the platform, which hosts the form in a window of its own.
/// </summary>
/// <remarks>
/// <para>
/// An instance of a form's class is an object of the workspace, whose code is the form's code-behind. The form it is - the Microsoft Forms form, in its window - is an
/// object of the automation server the form host shares, created from what the form's designer declares the first time the program reaches a member of it, and bound to
/// the instance (<see cref="ISessionExternalObjects"/>): when the instance is let go of, so is the form, window and all.
/// </para>
/// <para>
/// What the form host adds to the form is the window, so the members that are the window's are asked of it: <c>Show</c>, <c>Hide</c>, <c>Visible</c>, and the
/// <c>Caption</c>, which is the window's caption and not the Microsoft Forms form's own. Every other member of the <c>UserForm</c> - <c>Controls</c>,
/// <c>InsideWidth</c> - and every control is the form object's, called through the automation server as any server object's member is.
/// </para>
/// <para>
/// 🚧 The form's lifecycle is F4's: <c>Load</c>/<c>Unload</c>, the <c>UserForms</c> collection, <c>Initialize</c>/<c>QueryClose</c>/<c>Terminate</c>; the
/// form object's other members (<c>Left</c>, <c>Move</c>, <c>Tag</c>…) are not hosted yet, and say so.
/// </para>
/// </remarks>
/// <param name="session">The session whose forms these are.</param>
/// <param name="outside">The outside world: its form host, and the automation server the forms are objects of.</param>
/// <param name="automation">What calls a member of an automation server's object: the form's, once the form is bound to its instance.</param>
/// <param name="forms">What the designers of the session's forms declare, by the name of the form's module (<see cref="ProgramImage.Forms"/>).</param>
public sealed class FormCallProvider(IRuntimeSession session, ExternalWorld outside, AutomationCallProvider automation, IReadOnlyDictionary<string, FormDescriptor> forms)
    : IExternalCallProvider
{
    // the members of the VBA form object, which the form host is what has; of them, those it hosts so far.
    private static readonly HashSet<string> FormObjectMembers = new(
        FormObject.Description.Classes.SelectMany(described => described.Members).Select(member => member.Name), StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public bool CanDispatch(ExternalCallRequest request) => FormObject.IsDeclaredBy(request.Member);

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult Dispatch(ExternalCallRequest request, ISymbolResolver resolver)
    {
        var (form, member, kind) = Parse(request.Member.GetProperty(SymbolProperties.ExternalTarget)!);
        if (request.Arguments is not [VBRuntimeValue<VBRuntimeObjectId> { StoredValue: var identity }, ..])
        {
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        try
        {
            if (FormOf(identity, form, request) is not { } hosted)
            {
                return RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(VBRuntimeErrorId.ApplicationDefinedOrObjectDefinedError, request.CallSite,
                    $"{request.Describe()} could not be made: the designer of '{form}' declares no form to host."));
            }

            return (member.ToUpperInvariant(), kind) switch
            {
                ("SHOW", _) => Show(hosted, request, form),
                ("HIDE", _) => Done(() => outside.Forms.Hide(hosted)),
                ("CAPTION", "get") => RuntimeSemanticsEvaluationResult.Success(new VBStringValue(outside.Forms.GetCaption(hosted))),
                ("CAPTION", "let") => Done(() => outside.Forms.SetCaption(hosted, StringOf(request.Arguments[^1]))),
                ("VISIBLE", "get") => RuntimeSemanticsEvaluationResult.Success(new VBBooleanValue(outside.Forms.IsShown(hosted))),
                ("NAME", "get") => RuntimeSemanticsEvaluationResult.Success(new VBStringValue(form)),
                _ when FormObjectMembers.Contains(member) => RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(
                    VBRuntimeErrorId.ObjectDoesntSupportThisPropertyOrMethod, request.CallSite, $"{request.Describe()}: the form host does not host '{member}' yet.")),

                // a control, or a member of the Microsoft Forms UserForm: a member of the form object, which the automation server calls.
                _ => automation.Dispatch(request, resolver),
            };
        }
        catch (AutomationException failure)
        {
            return RuntimeSemanticsEvaluationResult.Error(AutomationErrors.ToError(failure, request.CallSite, request.Describe()));
        }
    }

    // the form an instance of the form's class is: the one it is bound to, or one created for it now from what the designer declares.
    private object? FormOf(VBRuntimeObjectId identity, string form, ExternalCallRequest request)
    {
        if (session.ExternalObjects.TryGet(identity, out var owner, out var bound) && ReferenceEquals(owner, outside.Automation))
        {
            return bound;
        }

        if (!forms.TryGetValue(form, out var designer))
        {
            return null;
        }

        object created;
        using (session.Turn.Yield())
        {
            created = outside.Forms.Create(FormDefinition.Of(form, designer));
        }

        session.ExternalObjects.Bind(identity, outside.Automation, created);
        return created;
    }

    // Show [Modal]: vbModal unless it says vbModeless, and as the designer's ShowModal says when it says nothing. A modal form is waited for, and the program gives its
    // turn up while it does: what the form raises meanwhile is the answer to the call.
    private RuntimeSemanticsEvaluationResult Show(object hosted, ExternalCallRequest request, string form)
    {
        var modal = request.Arguments.Length > 1 && VariantOf(request.Arguments[1]) is { } argument and not VBMissingValue
            ? Convert.ToInt32(argument.RuntimeValue.BoxedValue, System.Globalization.CultureInfo.InvariantCulture) != 0
            : !forms.TryGetValue(form, out var designer) || (designer.ShowModal ?? true);

        using (session.Turn.Yield())
        {
            outside.Forms.Show(hosted, modal);
        }

        return RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void);
    }

    private static RuntimeSemanticsEvaluationResult Done(Action call)
    {
        call();
        return RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void);
    }

    private static VBTypedValue? VariantOf(IRuntimeValue argument)
        => VBVariantType.TypeInfo.CreateValue(new ValueBindingHandle(argument)) is VBVariantValue { TypedValue: var value } ? value : null;

    private static string StringOf(IRuntimeValue argument)
        => VBStringType.TypeInfo.CreateValue(new ValueBindingHandle(argument)) is VBStringValue { Value: var text } ? text : string.Empty;

    // `UserForms.Form.Member/kind`, as the form's designer stamped it.
    private static (string Form, string Member, string Kind) Parse(string target)
    {
        var slash = target.LastIndexOf('/');
        var path = target[(FormObject.LibraryName.Length + 1)..slash];
        var dot = path.IndexOf('.');
        return (path[..dot], path[(dot + 1)..], target[(slash + 1)..]);
    }
}
