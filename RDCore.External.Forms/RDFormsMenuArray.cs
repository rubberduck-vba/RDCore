using RDCore.External.Automation;
using RDCore.Forms.Menus;
using RDCore.Forms.Model;
using System.Globalization;

namespace RDCore.External.Forms.RDForms;

// the handle of one of a form's menu arrays: one to an array, so that a program that asks for it twice gets the same object.
internal sealed class RDFormsMenuArray(RDFormsForm form, FormMenuArray array)
{
    public RDFormsForm Form { get; } = form;

    public FormMenuArray Array { get; } = array;

    public override string ToString() => $"{Form.Model.Name}.{Array.Name}";
}

// the members of a menu array, as VB6 has them: Count, LBound, UBound, Item (its default member, by index), and its enumerator; and the Load and Unload of an
// element, which the Load and Unload statements call (FormControlArrayMembers). They run on the UI thread. What the array refuses is the error VB6 raises: 340
// for an index no element has.
internal static class RDFormsMenuArrayMembers
{
    private const int UnknownName = unchecked((int)0x80020006);
    private const int BadParameterCount = unchecked((int)0x8002000E);
    private const int VisualBasicError = unchecked((int)0x800A0000);

    // DISPID_VALUE and DISPID_NEWENUM, as a late-bound call names them.
    private const string DefaultMember = "[DISPID=0]";
    private const string EnumerationMember = "[DISPID=-4]";

    public static object? Invoke(RDFormsMenuArray handle, string member, AutomationInvocation invocation, object?[] arguments, CultureInfo culture)
    {
        if (invocation is AutomationInvocation.Let or AutomationInvocation.Set)
        {
            throw new AutomationException(UnknownName, $"{handle}.{member} cannot be assigned.");
        }

        var array = handle.Array;
        try
        {
            switch (member.ToUpperInvariant())
            {
                case "COUNT":
                    Takes(handle, member, arguments, 0);
                    return (short)array.Count;

                case "LBOUND":
                    Takes(handle, member, arguments, 0);
                    return (short)array.LBound;

                case "UBOUND":
                    Takes(handle, member, arguments, 0);
                    return (short)array.UBound;

                case "ITEM" or "_DEFAULT" or DefaultMember:
                    Takes(handle, member, arguments, 1);
                    return handle.Form.HandleOf(array[IndexOf(arguments[0], culture)].Component);

                // the elements loaded when For Each starts, in the order of their indexes.
                case "_NEWENUM" or EnumerationMember:
                    Takes(handle, member, arguments, 0);
                    return new RDFormsControlsEnumerator([.. array.Elements.Select(element => handle.Form.HandleOf(element.Component))]);

                case "[LOAD]" when member == FormControlArrayMembers.Load:
                    Takes(handle, member, arguments, 1);
                    _ = array.Load(IndexOf(arguments[0], culture));
                    return null;

                case "[UNLOAD]" when member == FormControlArrayMembers.Unload:
                    Takes(handle, member, arguments, 1);
                    array.Unload(IndexOf(arguments[0], culture));
                    return null;

                default:
                    throw new AutomationException(UnknownName, $"{handle} has no member named {member}.");
            }
        }
        catch (FormErrorException exception)
        {
            throw new AutomationException(VisualBasicError | exception.Error.Number, exception.Error.Description);
        }
    }

    private static void Takes(RDFormsMenuArray handle, string member, object?[] arguments, int count)
    {
        if (arguments.Length != count || arguments.Any(argument => argument is System.Reflection.Missing))
        {
            throw new AutomationException(BadParameterCount, $"{handle}.{member} takes {count} argument(s).");
        }
    }

    private static int IndexOf(object? index, CultureInfo culture) => (int)RDFormsValues.FromNeutral(index, typeof(int), culture)!;
}
