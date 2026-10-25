using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Runtime.Execution.Memory;

/// <summary>
/// An interface of an object in the session's memory: what <c>ObjPtr</c> is the address of, and what the bytes of an object variable are, as in
/// <strong>MS-VBA</strong> (recorded in Excel, oracle\s17 O01-O08).
/// </summary>
/// <remarks>
/// <para>
/// An object has one for each interface it is asked for through: its class's own, which a variable of <c>Object</c> and a <c>Variant</c> give too (O02, O06),
/// and one for each interface it implements, and for <c>IUnknown</c>. Each is made the first time its address is asked for, and is the same for as long as the
/// object lives; the session frees it when the object is destroyed.
/// </para>
/// <para>
/// Its first pointer is the address of the table of its class's interface, which every object of the class shares (O07) and an object of another class does
/// not (O08); what is past it reads as zeros (O05). The table holds nothing a program can call through yet.
/// </para>
/// </remarks>
/// <param name="Start">Where the interface begins: what <c>ObjPtr</c> is.</param>
/// <param name="Object">The object it is an interface of.</param>
/// <param name="VTable">The address of the table of the class's interface.</param>
internal sealed record class ObjectHeader(MemoryAddress Start, VBRuntimeObjectId Object, MemoryAddress VTable)
{
    /// <summary>How many bytes an interface occupies: its table's pointer, and a pointer's worth that reads as zeros.</summary>
    public static int SizeFor(int pointerWidth) => 2 * pointerWidth;

    /// <summary>The interface's bytes: its table's address, then zeros.</summary>
    public byte[] Image(int pointerWidth)
    {
        var image = new byte[SizeFor(pointerWidth)];
        StringHolders.PointerBytes(VTable.Value, pointerWidth).CopyTo(image, 0);
        return image;
    }
}
