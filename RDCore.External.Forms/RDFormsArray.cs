using RDCore.External.Automation;
using RDCore.Forms.Menus;
using RDCore.Forms.Model;
using System.Globalization;

namespace RDCore.External.Forms.RDForms;

// the handle of one of a form's control arrays, of menu items or of other controls: one to an array, so that a program that asks for it twice gets the same
// object. Its elements are read from the form each time, those a program loads included.
internal abstract class RDFormsArray(RDFormsForm form)
{
    public RDFormsForm Form { get; } = form;

    // what the form keys the array's event sink by: the form's own array object.
    public abstract object Source { get; }

    public abstract string Name { get; }

    public abstract int Count { get; }

    public abstract int LBound { get; }

    public abstract int UBound { get; }

    // the elements loaded, in the order of their indexes.
    public abstract IEnumerable<ComponentInstance> Elements { get; }

    // the element of an index; 340 for one not loaded.
    public abstract ComponentInstance this[int index] { get; }

    public abstract void Load(int index);

    public abstract void Unload(int index);

    public override string ToString() => $"{Form.Model.Name}.{Name}";
}

// a menu array: VB6 places a loaded element among the others in index order, in their menu.
internal sealed class RDFormsMenuArray(RDFormsForm form, FormMenuArray array) : RDFormsArray(form)
{
    public override object Source => array;

    public override string Name => array.Name;

    public override int Count => array.Count;

    public override int LBound => array.LBound;

    public override int UBound => array.UBound;

    public override IEnumerable<ComponentInstance> Elements => array.Elements.Select(element => element.Component);

    public override ComponentInstance this[int index] => array[index].Component;

    public override void Load(int index) => _ = array.Load(index);

    public override void Unload(int index) => array.Unload(index);
}

// an array of controls other than menus: VB6 loads an element hidden, as a copy of the one of the lowest index.
internal sealed class RDFormsControlArray(RDFormsForm form, FormControlArray array) : RDFormsArray(form)
{
    public override object Source => array;

    public override string Name => array.Name;

    public override int Count => array.Count;

    public override int LBound => array.LBound;

    public override int UBound => array.UBound;

    public override IEnumerable<ComponentInstance> Elements => array.Elements;

    public override ComponentInstance this[int index] => array[index];

    public override void Load(int index) => _ = array.Load(index);

    public override void Unload(int index) => array.Unload(index);
}

// the members of a control array, as VB6 has them: Count, LBound, UBound, Item (its default member, by index), and its enumerator; and the Load and Unload of an
// element, which the Load and Unload statements call (FormControlArrayMembers). Any other member is 438. They run on the UI thread. What the array refuses is the
// error VB6 raises: 340 for an index no element has.
internal static class RDFormsArrayMembers
{
    private const int UnknownName = unchecked((int)0x80020006);
    private const int BadParameterCount = unchecked((int)0x8002000E);
    private const int VisualBasicError = unchecked((int)0x800A0000);

    // DISPID_VALUE and DISPID_NEWENUM, as a late-bound call names them.
    private const string DefaultMember = "[DISPID=0]";
    private const string EnumerationMember = "[DISPID=-4]";

    public static object? Invoke(RDFormsArray array, string member, AutomationInvocation invocation, object?[] arguments, CultureInfo culture)
    {
        if (invocation is AutomationInvocation.Let or AutomationInvocation.Set)
        {
            throw new AutomationException(UnknownName, $"{array}.{member} cannot be assigned.");
        }

        try
        {
            switch (member.ToUpperInvariant())
            {
                case "COUNT":
                    Takes(array, member, arguments, 0);
                    return (short)array.Count;

                case "LBOUND":
                    Takes(array, member, arguments, 0);
                    return (short)array.LBound;

                case "UBOUND":
                    Takes(array, member, arguments, 0);
                    return (short)array.UBound;

                case "ITEM" or "_DEFAULT" or DefaultMember:
                    Takes(array, member, arguments, 1);
                    return array.Form.HandleOf(array[IndexOf(arguments[0], culture)]);

                // the elements loaded when For Each starts, in the order of their indexes.
                case "_NEWENUM" or EnumerationMember:
                    Takes(array, member, arguments, 0);
                    return new RDFormsControlsEnumerator([.. array.Elements.Select(array.Form.HandleOf)]);

                case "[LOAD]" when member == FormControlArrayMembers.Load:
                    Takes(array, member, arguments, 1);
                    array.Load(IndexOf(arguments[0], culture));
                    return null;

                case "[UNLOAD]" when member == FormControlArrayMembers.Unload:
                    Takes(array, member, arguments, 1);
                    array.Unload(IndexOf(arguments[0], culture));
                    return null;

                default:
                    throw new AutomationException(UnknownName, $"{array} has no member named {member}.");
            }
        }
        catch (FormErrorException exception)
        {
            throw new AutomationException(VisualBasicError | exception.Error.Number, exception.Error.Description);
        }
    }

    private static void Takes(RDFormsArray array, string member, object?[] arguments, int count)
    {
        if (arguments.Length != count || arguments.Any(argument => argument is System.Reflection.Missing))
        {
            throw new AutomationException(BadParameterCount, $"{array}.{member} takes {count} argument(s).");
        }
    }

    private static int IndexOf(object? index, CultureInfo culture) => (int)RDFormsValues.FromNeutral(index, typeof(int), culture)!;
}
