using RDCore.External.Automation;
using RDCore.Forms.Model;
using RDCore.SDK.Runtime.Stdole;
using System.Globalization;

namespace RDCore.External.Forms.RDForms;

// a font of a form's components as the program has it, a stdole.StdFont: what a component's Font reads, the same object on every read. A change through any
// reference to it changes each component that holds it, and a Set of it to another component's Font shares it, as VB6 does (measured). Used on the UI thread only.
internal sealed class RDFormsFont
{
    private readonly List<(ComponentInstance Component, PropertyDefinition Property)> _holders = [];

    public RDFormsFont(StdFontObject font)
    {
        Font = font;
        font.Changed += (_, _) => Draw();
    }

    public StdFontObject Font { get; }

    public void Hold(ComponentInstance component, PropertyDefinition property)
    {
        _holders.Add((component, property));
        _ = component.TrySet(property, FormFont.FromStdFont(Font));
    }

    // a component the font was made from holds it already: its value is the font's.
    public void HoldAsItIs(ComponentInstance component, PropertyDefinition property) => _holders.Add((component, property));

    public void Release(ComponentInstance component, PropertyDefinition property) => _holders.Remove((component, property));

    // what each component that holds the font draws. Reading the size snaps it, which a later name snaps again from (StdFontObject.Size).
    private void Draw()
    {
        var drawn = FormFont.FromStdFont(Font);
        foreach (var (component, property) in _holders)
        {
            _ = component.TrySet(property, drawn);
        }
    }
}

// the fonts the Font properties of a form's components hand out: one per component and property, made from the value the component holds when it is first read.
internal sealed class RDFormsFonts
{
    private readonly Dictionary<(ComponentInstance Component, string Property), RDFormsFont> _fonts = [];

    public static bool IsFont(PropertyDefinition property) => property.ValueType == typeof(FormFont);

    public RDFormsFont FontOf(ComponentInstance component, PropertyDefinition property)
    {
        var key = (component, property.Name);
        if (!_fonts.TryGetValue(key, out var font))
        {
            // a form's file writes no font a StdFont refuses; one written by hand is refused when it is read, as VB6 refuses it when it is set.
            if (((FormFont)component.GetBoxed(property)!).TryToStdFont(out var made) is not StdFontFailure.None and var failure)
            {
                throw RDFormsFontMembers.Failure(failure);
            }

            font = new RDFormsFont(made!);
            font.HoldAsItIs(component, property);
            _fonts[key] = font;
        }

        return font;
    }

    // Set of a font to a component's Font: the component holds that font, and lets go of its own.
    public void Share(ComponentInstance component, PropertyDefinition property, RDFormsFont font)
    {
        var key = (component, property.Name);
        if (_fonts.TryGetValue(key, out var held))
        {
            if (ReferenceEquals(held, font))
            {
                return;
            }

            held.Release(component, property);
        }

        font.Hold(component, property);
        _fonts[key] = font;
    }
}

// the members of a stdole.StdFont that a form's component hands out: its type library's Font interface, with Name its default member. What a StdFontObject refuses
// raises the error MS-VBA raises for it.
internal static class RDFormsFontMembers
{
    private const int UnknownName = unchecked((int)0x80020006);
    private const int BadParameterCount = unchecked((int)0x8002000E);
    private const int VisualBasicError = unchecked((int)0x800A0000);

    // VB6's own properties of a control's font, each one a member of the control's Font: FontBold is Font.Bold.
    private static readonly Dictionary<string, string> Shorthands = new(StringComparer.OrdinalIgnoreCase)
    {
        ["FontBold"] = "Bold",
        ["FontItalic"] = "Italic",
        ["FontName"] = "Name",
        ["FontSize"] = "Size",
        ["FontStrikethru"] = "Strikethrough",
        ["FontUnderline"] = "Underline",
    };

    public static bool TryShorthand(string member, out string fontMember) => Shorthands.TryGetValue(member, out fontMember!);

    public static object? Invoke(RDFormsFont handle, string member, AutomationInvocation invocation, object?[] arguments, CultureInfo culture)
    {
        var font = handle.Font;
        var name = member is "_Default" or "[DISPID=0]" ? "Name" : member;
        var assigns = invocation is AutomationInvocation.Let or AutomationInvocation.Set;
        if (assigns ? arguments.Length != 1 : arguments.Length != 0)
        {
            throw new AutomationException(BadParameterCount, $"Font.{name} takes {(assigns ? "one value" : "no argument")}.");
        }

        if (!assigns)
        {
            return name.ToUpperInvariant() switch
            {
                "NAME" => font.Name,
                // a decimal of four places or fewer, which the session takes as the Currency the type library declares.
                "SIZE" => font.Size,
                "BOLD" => font.Bold,
                "ITALIC" => font.Italic,
                "UNDERLINE" => font.Underline,
                "STRIKETHROUGH" => font.Strikethrough,
                "WEIGHT" => font.Weight,
                "CHARSET" => font.Charset,
                _ => throw new AutomationException(UnknownName, $"Font has no member named {member}."),
            };
        }

        var value = arguments[0];
        var failure = name.ToUpperInvariant() switch
        {
            "NAME" => font.TrySetName((string)RDFormsValues.FromNeutral(value, typeof(string), culture)!),
            "SIZE" => font.TrySetSize((decimal)RDFormsValues.FromNeutral(value, typeof(decimal), culture)!),
            "WEIGHT" => font.TrySetWeight((short)RDFormsValues.FromNeutral(value, typeof(short), culture)!),
            "CHARSET" => font.TrySetCharset((short)RDFormsValues.FromNeutral(value, typeof(short), culture)!),
            "BOLD" => Assigned(() => font.Bold = Truth(value, culture)),
            "ITALIC" => Assigned(() => font.Italic = Truth(value, culture)),
            "UNDERLINE" => Assigned(() => font.Underline = Truth(value, culture)),
            "STRIKETHROUGH" => Assigned(() => font.Strikethrough = Truth(value, culture)),
            _ => throw new AutomationException(UnknownName, $"Font has no member named {member}."),
        };

        return failure is StdFontFailure.None ? null : throw Failure(failure);
    }

    public static AutomationException Failure(StdFontFailure failure)
    {
        var error = failure is StdFontFailure.Overflow ? FormError.Overflow : FormError.InvalidPropertyValue;
        return new AutomationException(VisualBasicError | error.Number, error.Description);
    }

    private static bool Truth(object? value, CultureInfo culture) => (bool)RDFormsValues.FromNeutral(value, typeof(bool), culture)!;

    private static StdFontFailure Assigned(Action assign)
    {
        assign();
        return StdFontFailure.None;
    }
}
