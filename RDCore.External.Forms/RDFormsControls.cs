using RDCore.External.Automation;
using RDCore.Forms.Model;
using System.Globalization;

namespace RDCore.External.Forms.RDForms;

// the handle of a form's Controls collection: one to a form, so that a program that asks for it twice gets the same object.
internal sealed class RDFormsControls(RDFormsForm form)
{
    public RDFormsForm Form { get; } = form;

    public override string ToString() => $"{Form.Model.Name}.Controls";
}

// the handle of an enumerator of a form's Controls collection: the controls the collection held when the enumerator was made, as handles, in its order.
internal sealed class RDFormsControlsEnumerator(IReadOnlyList<RDFormsObject> controls)
{
    private int _next;

    public bool MoveNext(out object? current)
    {
        if (_next < controls.Count)
        {
            current = controls[_next++];
            return true;
        }

        current = null;
        return false;
    }

    public void Reset() => _next = 0;
}

// the members of a form's Controls collection, as VB6 has them: Count, Item (its default member, by index from 0 or by name), Add, Remove, and its enumerator. They
// run on the UI thread. What the form refuses (a name taken, a control of the file removed) is the error VB6 raises, with its number and its description.
internal static class RDFormsControlsMembers
{
    private const int UnknownName = unchecked((int)0x80020006);
    private const int BadParameterCount = unchecked((int)0x8002000E);
    private const int VisualBasicError = unchecked((int)0x800A0000);
    private const int TypeMismatch = VisualBasicError | 13;

    // DISPID_VALUE and DISPID_NEWENUM, as a late-bound call names them.
    private const string DefaultMember = "[DISPID=0]";
    private const string EnumerationMember = "[DISPID=-4]";

    public static object? Invoke(RDFormsControls collection, string member, AutomationInvocation invocation, object?[] arguments, CultureInfo culture)
    {
        if (invocation is AutomationInvocation.Let or AutomationInvocation.Set)
        {
            throw new AutomationException(UnknownName, $"{collection}.{member} cannot be assigned.");
        }

        var form = collection.Form;
        try
        {
            switch (member.ToUpperInvariant())
            {
                case "COUNT":
                    Takes(collection, member, arguments, 0, 0);
                    return form.Model.Controls.Count;

                case "ITEM" or DefaultMember:
                    Takes(collection, member, arguments, 1, 1);
                    return form.HandleOf(Find(form, arguments[0], culture));

                case "ADD":
                    Takes(collection, member, arguments, 2, 3);
                    var progId = (string)RDFormsValues.FromNeutral(arguments[0], typeof(string), culture)!;
                    var name = (string)RDFormsValues.FromNeutral(arguments[1], typeof(string), culture)!;
                    var container = arguments.Length == 3 && arguments[2] is not System.Reflection.Missing ? ContainerOf(form, arguments[2]) : null;
                    return form.HandleOf(form.Model.AddControl(progId, name, container));

                case "REMOVE":
                    Takes(collection, member, arguments, 1, 1);
                    if (arguments[0] is string removed)
                    {
                        form.Model.RemoveControl(removed);
                    }
                    else
                    {
                        form.Model.RemoveControl(Find(form, arguments[0], culture));
                    }

                    return null;

                case EnumerationMember:
                    Takes(collection, member, arguments, 0, 0);
                    return new RDFormsControlsEnumerator([.. form.Model.Controls.Select(form.HandleOf)]);

                default:
                    throw new AutomationException(UnknownName, $"{collection} has no member named {member}.");
            }
        }
        catch (FormErrorException exception)
        {
            throw new AutomationException(VisualBasicError | exception.Error.Number, exception.Error.Description);
        }
    }

    private static void Takes(RDFormsControls collection, string member, object?[] arguments, int fewest, int most)
    {
        var given = arguments.Length;
        while (given > fewest && arguments[given - 1] is System.Reflection.Missing)
        {
            given--;
        }

        if (given < fewest || arguments.Length > most || arguments.Take(fewest).Any(argument => argument is System.Reflection.Missing))
        {
            throw new AutomationException(BadParameterCount, $"{collection}.{member} takes {(fewest == most ? $"{fewest}" : $"{fewest} to {most}")} argument(s).");
        }
    }

    // a control of the collection: by its name, without regard to case, or by its index from 0; a control object is the one it is, when it is the form's. One the
    // collection does not hold is 730, Control not found.
    private static ComponentInstance Find(RDFormsForm form, object? key, CultureInfo culture)
    {
        var controls = form.Model.Controls;
        switch (key)
        {
            case string name:
                return controls.FirstOrDefault(control => string.Equals(control.Name, name, StringComparison.OrdinalIgnoreCase))
                    ?? throw new FormErrorException(FormError.ControlNotFound(name));

            case RDFormsObject { IsForm: false } control when ReferenceEquals(control.Form, form):
                return control.Component;

            case RDFormsObject:
                throw new AutomationException(TypeMismatch, "Type mismatch");

            default:
                var index = (int)RDFormsValues.FromNeutral(key, typeof(int), culture)!;
                return index >= 0 && index < controls.Count
                    ? controls[index]
                    : throw new FormErrorException(FormError.ControlNotFound(index.ToString(culture)));
        }
    }

    // the container Add is given: a control of the same form. Anything else is not a container for it.
    private static ComponentInstance ContainerOf(RDFormsForm form, object? container)
        => container is RDFormsObject handle && ReferenceEquals(handle.Form, form) && (handle.IsForm || RDFormsAutomationServer.IsPlaced(handle))
            ? handle.Component
            : throw new AutomationException(TypeMismatch, "Type mismatch");
}
