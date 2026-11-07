using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Runtime;

namespace RDCore.SDK.Runtime.Abstract.Execution;

/// <summary>
/// The addresses a session gives its objects (<c>ObjPtr</c>, <strong>RD-VBAL §6.1.2.13</strong>), read both ways: the address an object is laid out as in the
/// bytes of a record, and the object an address read from those bytes is. A copy of a record as bytes (<c>LSet</c>, <strong>MS-VBAL §5.4.3.6</strong>) reads
/// them this way when an object field and a field of another type overlap.
/// </summary>
/// <remarks>
/// A session's storage gives its objects their addresses, so <see cref="ISessionStorage"/> extends this interface.
/// <para>
/// ⚖️<strong>RDCore</strong> provides an implementation of this interface <strong>licensed under GPLv3</strong>.
/// </para>
/// </remarks>
public interface IObjectPointers
{
    /// <summary>
    /// The host's pointer width in bytes: 8 in a 64-bit environment, 4 in a 32-bit one. The layout of a record depends on it.
    /// </summary>
    int PointerWidth { get; }

    /// <summary>
    /// The address of the interface of <paramref name="instance"/> that <paramref name="declared"/> selects: what <c>ObjPtr</c> returns of a variable of that
    /// type that holds it, and <c>0</c> for <c>Nothing</c>.
    /// </summary>
    /// <remarks>
    /// The first time an interface of an object is asked for, its address is allocated in the session's memory. It is the same address for as long as the
    /// object lives.
    /// </remarks>
    /// <param name="instance">The object.</param>
    /// <param name="declared">The declared type of what holds it: a class, <c>Object</c>, or <see langword="null"/> for the class's own interface.</param>
    /// <param name="pointer">The address.</param>
    /// <returns><see langword="false"/> when there is no room in the session's memory for the interface.</returns>
    bool TryGetPointer(VBRuntimeObjectId instance, VBType? declared, out long pointer);

    /// <summary>
    /// The object whose interface is at <paramref name="pointer"/>, an address <c>ObjPtr</c> returned: <c>Nothing</c> for <c>0</c>.
    /// </summary>
    /// <remarks>
    /// The address of an object that was destroyed resolves to no object, and the session may give it to an interface of another object.
    /// </remarks>
    /// <param name="pointer">The address.</param>
    /// <param name="instance">The object.</param>
    /// <returns><see langword="false"/> when no object of the session has an interface at <paramref name="pointer"/>.</returns>
    bool TryGetObject(long pointer, out VBRuntimeObjectId instance);
}
