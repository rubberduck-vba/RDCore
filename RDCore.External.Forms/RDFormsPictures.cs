using RDCore.External.Automation;
using RDCore.Forms.Model;

namespace RDCore.External.Forms.RDForms;

// the pictures of the program's that the properties of a form's components hold. RDForms holds a picture as the bytes of its file; the program's picture is
// held too, so that reading the property gives the program its own picture back (`Picture1.Picture Is p`), and the session holds the picture until the property
// lets go of it: another picture, Nothing, or the form's end. Used on the UI thread only.
internal sealed class RDFormsPictures
{
    private const int TypeMismatch = unchecked((int)0x80020005);

    private readonly Dictionary<(ComponentInstance Component, string Property), Held> _held = [];

    // a picture of the program's, and the times it was given to the property: what the session is told when the property lets go of it.
    private sealed record class Held(SessionObjectReference Picture, byte[] Bytes, int Crossings);

    public static bool IsPicture(PropertyDefinition property) => property.ValueType == typeof(byte[]);

    // what a picture property takes for what the program assigns: the bytes of a picture of the program's, and none for Nothing or for an empty picture
    // (LoadPicture()). Anything else is no picture (13).
    public byte[]? Assign(ComponentInstance component, PropertyDefinition property, object? value)
    {
        var key = (component, property.Name);
        switch (value)
        {
            // Nothing, which a Set gives as a wrapper of no object. CA1416: a wrapper of no object asks nothing of COM, whatever the platform.
#pragma warning disable CA1416
            case null or System.Runtime.InteropServices.DispatchWrapper { WrappedObject: null }:
#pragma warning restore CA1416
                Release(key);
                return null;

            case SessionObjectReference { Picture: { } picture } reference:
                var bytes = picture.Bytes.Length == 0 ? null : picture.Bytes;
                if (_held.TryGetValue(key, out var held) && held.Picture.SessionObject == reference.SessionObject)
                {
                    _held[key] = held with { Bytes = bytes ?? [], Crossings = held.Crossings + 1 };
                }
                else
                {
                    Release(key);
                    _held[key] = new Held(reference, bytes ?? [], 1);
                }

                return bytes;

            default:
                throw new AutomationException(TypeMismatch, $"{component}.{property.Name}: only a picture, or Nothing, can be given to it.");
        }
    }

    // the program's own picture, while the property holds what it was given; null when the property was given none, or holds a picture of its designer's.
    public SessionObjectReference? PictureOf(ComponentInstance component, PropertyDefinition property, byte[]? stored)
        => _held.TryGetValue((component, property.Name), out var held) && (ReferenceEquals(held.Bytes, stored) || held.Bytes.Length == 0 && stored is null)
            ? held.Picture
            : null;

    // the form ends: every picture its components hold is let go of.
    public void ReleaseAll()
    {
        foreach (var key in _held.Keys.ToList())
        {
            Release(key);
        }
    }

    private void Release((ComponentInstance, string) key)
    {
        if (_held.Remove(key, out var held))
        {
            held.Picture.Host.Release(held.Picture.SessionObject, held.Crossings);
        }
    }
}
