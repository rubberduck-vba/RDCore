using RDCore.External.Automation;
using RDCore.Forms.Drawing;
using RDCore.Forms.Model;
using RDCore.Forms.Model.VB;
using System.Globalization;

namespace RDCore.External.Forms.RDForms;

// what a Visual Basic 6.0 form or PictureBox draws with: the methods its graphics statements call (Line, Circle, PSet, Scale, with GraphicsFlags first), Cls,
// Point, CurrentX and CurrentY, on the component's RDForms surface. Coordinates are in the component's scale, which the surface reads them in. A component
// that is not shown has no surface yet: it draws nothing, Point reads -1 there, and CurrentX and CurrentY read 0.
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

            default:
                return false;
        }
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
