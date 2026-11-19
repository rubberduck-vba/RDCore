using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Runtime.Execution.Frames;

/// <summary>
/// The cell of a <c>String</c> variable of variable length: what compiled code and the interpreter read the text it holds from and write the text it is
/// assigned to, which knows the address it is bound at, as a <see cref="ScalarCell"/> does. A <c>ByRef</c> parameter given the cell (a call compiled code
/// makes directly) finds the variable's address by it, so the variable has one address whichever of its names is asked for it first.
/// </summary>
internal sealed record class TextCell : ValueBindingHandle, IAddressedCell
{
    /// <summary>
    /// A cell holding <paramref name="value"/>.
    /// </summary>
    /// <param name="value">The <c>String</c> the variable holds.</param>
    public TextCell(IRuntimeValue value)
        : base(value)
    {
    }

    // a copy is bound nowhere.
    private TextCell(TextCell original)
        : base(original)
    {
    }

    /// <inheritdoc/>
    public MemoryAddress? Address { get; set; }

    /// <summary>
    /// Whether a variable of <paramref name="type"/> is held in a <see cref="TextCell"/>: a <c>String</c> of variable length (a fixed-length one pads or
    /// truncates what it is assigned).
    /// </summary>
    /// <param name="type">The variable's declared type.</param>
    public static bool Holds(VBType? type) => type is VBStringType and not VBFixedStringType;
}

/// <summary>
/// A cell that knows the address it is bound at in the session's memory (<see cref="Memory.SessionStorage"/>), if it is: what a second name for its
/// variable, a <c>ByRef</c> parameter given the cell, finds the variable's address by.
/// </summary>
internal interface IAddressedCell
{
    /// <summary>
    /// The address the cell is bound at; <see langword="null"/> when it is bound nowhere.
    /// </summary>
    MemoryAddress? Address { get; set; }
}
