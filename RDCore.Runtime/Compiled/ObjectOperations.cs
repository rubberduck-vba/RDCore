using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;

namespace RDCore.Runtime.Compiled;

/// <summary>
/// What compiled code does of an object parameter or variable: it reads the reference held in the cell its frame holds it in
/// (<see cref="CompiledActivation.ObjectCellOf"/>), and compares two references, as the interpreter compares them. What it doesn't read - a cell of no object
/// reference, one the analysis doesn't know the value of - is the interpreter's: each operation returns <see langword="false"/>, and the interpreter runs the
/// statement.
/// </summary>
public static class ObjectOperations
{
    /// <summary>
    /// The reference the object held in <paramref name="cell"/> is: <c>Nothing</c> is the default one.
    /// </summary>
    internal static bool TryRead(object? cell, out VBRuntimeObjectId instance)
    {
        if (cell is ValueBindingHandle { IsKnown: true, Value: VBRuntimeValue<VBRuntimeObjectId> held })
        {
            instance = held.StoredValue;
            return true;
        }

        instance = default;
        return false;
    }

    /// <summary>
    /// <c>left Is right</c> of the objects held in two cells (MS-VBAL §5.6.9.7): whether both are the same object, or both <c>Nothing</c>, as
    /// <c>BinaryIsRelationalOperatorRuntimeSemantics</c> compares them.
    /// </summary>
    public static bool Is(object? left, object? right, out bool same)
    {
        same = false;
        if (!TryRead(left, out var l) || !TryRead(right, out var r))
        {
            return false;
        }

        same = l.Equals(r);
        return true;
    }

    /// <summary>
    /// <c>cell Is Nothing</c> (MS-VBAL §5.6.9.7): whether the object held in <paramref name="cell"/> is <c>Nothing</c>.
    /// </summary>
    public static bool IsNothing(object? cell, out bool nothing)
    {
        nothing = false;
        if (!TryRead(cell, out var instance))
        {
            return false;
        }

        nothing = instance.Equals(default(VBRuntimeObjectId));
        return true;
    }
}
