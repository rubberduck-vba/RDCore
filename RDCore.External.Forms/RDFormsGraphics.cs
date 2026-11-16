using RDCore.External.Automation;
using RDCore.Forms.Drawing;
using RDCore.Forms.Model;
using RDCore.Forms.Model.VB;
using System.Globalization;

namespace RDCore.External.Forms.RDForms;

// what a Visual Basic 6.0 form or PictureBox draws with: the methods its graphics statements call (Line, Circle, PSet, Scale, with GraphicsFlags first), Cls,
// Point, PaintPicture, CurrentX and CurrentY, on the component's RDForms surface. Coordinates are in the component's scale, which the surface reads them
// in. A component that is not shown has no surface yet: it draws nothing, Point reads -1 there, and CurrentX and CurrentY read 0.
internal static class RDFormsGraphics
{
    private const int BadParameterCount = unchecked((int)0x8002000E);
    private const int VisualBasicError = unchecked((int)0x800A0000);

    // VB error 5, Invalid procedure call or argument: a color that is no color.
    private const int InvalidArgument = VisualBasicError | 5;

    // whether the member is one of a surface's, on a component that has a surface, and what it gave when it is.
    public static bool TryInvoke(ComponentInstance component, string member, AutomationInvocation invocation, object?[] arguments, CultureInfo culture,
        out object? result)
    {
        result = null;
        if (component.Class != VBClasses.Form && component.Class != VBClasses.PictureBox)
        {
            return false;
        }

        var surface = DrawingSurfaces.Of(component);
        var reads = invocation is AutomationInvocation.Get or AutomationInvocation.Method;
        switch (member.ToUpperInvariant())
        {
            case "CURRENTX" or "CURRENTY" when reads:
                Expect(arguments, 0, member);
                result = (float)(surface is null ? 0d : member.Equals("CurrentX", StringComparison.OrdinalIgnoreCase) ? surface.CurrentX : surface.CurrentY);
                return true;

            case "CURRENTX" or "CURRENTY" when invocation is AutomationInvocation.Let:
                Expect(arguments, 1, member);
                var current = RDFormsValues.SingleOf(arguments[0], culture);
                if (surface is not null && member.Equals("CurrentX", StringComparison.OrdinalIgnoreCase))
                {
                    surface.CurrentX = current;
                }
                else if (surface is not null)
                {
                    surface.CurrentY = current;
                }

                return true;

            case "CLS" when reads:
                Expect(arguments, 0, member);
                surface?.Cls();
                return true;

            case "POINT" when reads:
                Expect(arguments, 2, member);
                var (x, y) = (Coordinate(arguments[0], culture), Coordinate(arguments[1], culture));
                result = surface?.Point(x, y) ?? -1;
                return true;

            case "LINE" when reads:
                Expect(arguments, 6, member);
                Draw(surface, arguments, culture, (drawing, flags) => drawing.Line(
                    flags.HasFlag(GraphicsFlags.HasFirstPoint) ? Point(arguments, 1, flags.HasFlag(GraphicsFlags.FirstStep), culture) : null,
                    Point(arguments, 3, flags.HasFlag(GraphicsFlags.SecondStep), culture),
                    Color(arguments[5], flags.HasFlag(GraphicsFlags.HasColor), culture),
                    flags.HasFlag(GraphicsFlags.FilledBox) ? LineBox.FilledBox : flags.HasFlag(GraphicsFlags.Box) ? LineBox.Box : LineBox.None));
                return true;

            case "CIRCLE" when reads:
                Expect(arguments, 8, member);
                Draw(surface, arguments, culture, (drawing, flags) => drawing.Circle(
                    Point(arguments, 1, flags.HasFlag(GraphicsFlags.FirstStep), culture),
                    Coordinate(arguments[3], culture),
                    Color(arguments[4], flags.HasFlag(GraphicsFlags.HasColor), culture),
                    flags.HasFlag(GraphicsFlags.HasStart) ? Coordinate(arguments[5], culture) : null,
                    flags.HasFlag(GraphicsFlags.HasEnd) ? Coordinate(arguments[6], culture) : null,
                    flags.HasFlag(GraphicsFlags.HasAspect) ? Coordinate(arguments[7], culture) : null));
                return true;

            case "PSET" when reads:
                Expect(arguments, 4, member);
                Draw(surface, arguments, culture, (drawing, flags) => drawing.PSet(
                    Point(arguments, 1, flags.HasFlag(GraphicsFlags.FirstStep), culture),
                    Color(arguments[3], flags.HasFlag(GraphicsFlags.HasColor), culture)));
                return true;

            case "SCALE" when reads:
                Draw(surface, arguments, culture, (drawing, flags) =>
                {
                    if (flags.HasFlag(GraphicsFlags.HasFirstPoint))
                    {
                        Expect(arguments, 5, member);
                        drawing.Scale(Coordinate(arguments[1], culture), Coordinate(arguments[2], culture), Coordinate(arguments[3], culture),
                            Coordinate(arguments[4], culture));
                    }
                    else
                    {
                        drawing.Scale();
                    }
                });
                return true;

            case "PAINTPICTURE" when reads:
                PaintPicture(surface, arguments, culture);
                return true;

            case "[PRINT]" when reads && member == FormPrintMembers.Print:
                var (items, endsLine) = PrintItems(arguments, culture);
                if (surface is null)
                {
                    return true;
                }

                try
                {
                    surface.Print(items, endsLine);
                }
                catch (FormErrorException exception)
                {
                    throw new AutomationException(VisualBasicError | exception.Error.Number, exception.Error.Description);
                }

                return true;

            default:
                return false;
        }
    }

    // vbSrcCopy, the opcode PaintPicture draws with when it is given none.
    private const int SourceCopy = 0xCC0020;

    // PaintPicture's picture, x1 and y1, then width1, height1, x2, y2, width2, height2 and the opcode, any of which can be left out. As VB6 does, the
    // numbers are read before the picture: a size of "x" is error 13 even where the picture is Nothing.
    private static void PaintPicture(IDrawingSurface? surface, object?[] arguments, CultureInfo culture)
    {
        if (arguments.Length is < 3 or > 10 || arguments.Take(3).Any(argument => argument is System.Reflection.Missing))
        {
            throw new AutomationException(BadParameterCount, "PaintPicture takes a picture, x1 and y1, then up to seven more.");
        }

        bool Given(int index) => index < arguments.Length && arguments[index] is not System.Reflection.Missing;
        double? Optional(int index) => Given(index) ? Coordinate(arguments[index], culture) : null;
        var (x1, y1) = (Coordinate(arguments[1], culture), Coordinate(arguments[2], culture));
        var (width1, height1, x2, y2, width2, height2) = (Optional(3), Optional(4), Optional(5), Optional(6), Optional(7), Optional(8));
        var opcode = Given(9) ? (int)RDFormsValues.FromNeutral(arguments[9], typeof(int), culture)! : SourceCopy;
        var picture = PictureOf(arguments[0]);
        if (surface is null)
        {
            return;
        }

        try
        {
            surface.PaintPicture(picture, x1, y1, width1, height1, x2, y2, width2, height2, opcode);
        }
        catch (FormErrorException exception)
        {
            throw new AutomationException(VisualBasicError | exception.Error.Number, exception.Error.Description);
        }
    }

    // the program's picture, as its file holds it. An early-bound call has Set-coerced the argument to a StdPicture already; a late-bound one passes it as it
    // is, and VB6 then raises 13 for a value other than an object and 481 for an object other than a picture. Nothing is error 91.
    // CA1416: matching a wrapper asks nothing of COM, whatever the platform.
#pragma warning disable CA1416
    private static byte[] PictureOf(object? value) => value switch
    {
        null or System.Runtime.InteropServices.DispatchWrapper { WrappedObject: null }
            => throw new AutomationException(VisualBasicError | 91, "Object variable or With block variable not set"),
        SessionObjectReference { Picture: { } picture } => picture.Bytes,
        string or bool or byte or short or int or long or float or double or decimal or DateTime or DBNull
            or System.Runtime.InteropServices.ErrorWrapper or System.Runtime.InteropServices.CurrencyWrapper
            => throw new AutomationException(VisualBasicError | 13, "Type mismatch"),
        _ => throw new AutomationException(VisualBasicError | 481, "Invalid picture"),
    };
#pragma warning restore CA1416

    // whether the output ends the line, then a kind and a value for each part (FormPrintMembers).
    private static (IReadOnlyList<PrintItem> Items, bool EndsLine) PrintItems(object?[] arguments, CultureInfo culture)
    {
        if (arguments.Length % 2 != 1)
        {
            throw new AutomationException(BadParameterCount, "Print takes whether the output ends the line, then a kind and a value for each part.");
        }

        var endsLine = (bool)RDFormsValues.FromNeutral(arguments[0], typeof(bool), culture)!;
        var items = new List<PrintItem>(arguments.Length / 2);
        for (var index = 1; index < arguments.Length; index += 2)
        {
            var value = arguments[index + 1];
            items.Add((FormPrintPartKind)(short)RDFormsValues.FromNeutral(arguments[index], typeof(short), culture)! switch
            {
                FormPrintPartKind.Text => new PrintItem.Text(value as string ?? Convert.ToString(value, culture) ?? string.Empty),
                FormPrintPartKind.Spc => new PrintItem.Spc((int)RDFormsValues.FromNeutral(value, typeof(int), culture)!),
                _ => new PrintItem.Tab(value is null ? null : (int)RDFormsValues.FromNeutral(value, typeof(int), culture)!),
            });
        }

        return (items, endsLine);
    }

    // a drawing with its flags, the first argument, read before anything is drawn; what the surface raises is the program's error.
    private static void Draw(IDrawingSurface? surface, object?[] arguments, CultureInfo culture, Action<IDrawingSurface, GraphicsFlags> draw)
    {
        if (arguments.Length == 0)
        {
            throw new AutomationException(BadParameterCount, "A drawing method takes its flags first.");
        }

        var flags = (GraphicsFlags)(short)RDFormsValues.FromNeutral(arguments[0], typeof(short), culture)!;
        if (surface is null)
        {
            return;
        }

        try
        {
            draw(surface, flags);
        }
        catch (FormErrorException exception)
        {
            throw new AutomationException(VisualBasicError | exception.Error.Number, exception.Error.Description);
        }
    }

    private static void Expect(object?[] arguments, int count, string member)
    {
        if (arguments.Length != count)
        {
            throw new AutomationException(BadParameterCount, $"{member} takes {count} argument(s).");
        }
    }

    private static double Coordinate(object? value, CultureInfo culture) => RDFormsValues.SingleOf(value, culture);

    private static GraphicsPoint Point(object?[] arguments, int index, bool relative, CultureInfo culture)
        => new(Coordinate(arguments[index], culture), Coordinate(arguments[index + 1], culture), relative);

    // a color given is an OLE_COLOR, which the program's code gives as a Long; one left out is the component's ForeColor.
    private static FormColor? Color(object? value, bool given, CultureInfo culture)
    {
        if (!given)
        {
            return null;
        }

        var ole = (int)RDFormsValues.FromNeutral(value, typeof(int), culture)!;
        return FormColor.TryFromOle(ole, out var color) ? color : throw new AutomationException(InvalidArgument, "Invalid procedure call or argument");
    }
}
