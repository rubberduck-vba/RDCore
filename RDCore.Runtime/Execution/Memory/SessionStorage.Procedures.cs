using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Runtime.Execution.Memory;

// the procedures of the session's memory: what AddressOf is the address of (MS-VBAL §5.6.16.8).
internal sealed partial class SessionStorage
{
    // the stubs of the procedures, by the procedure and by the address each is at.
    private readonly Dictionary<string, MemoryAddress> _stubOf = [];
    private readonly Dictionary<MemoryAddress, Symbol> _stubs = [];

    // how many bytes a stub occupies: in MS-VBA the address is that of code that calls the procedure, which the session has none of to lay out.
    private int StubSize => 2 * pointerSize;

    /// <summary>
    /// The address of <paramref name="procedure"/>, a procedure of a standard module: what <c>AddressOf</c> is (<strong>MS-VBAL §5.6.16.8</strong>). It is made
    /// the first time it is asked for, and is the same for as long as the session runs - bare or qualified by its module, it is the procedure's, and another
    /// procedure has another (<strong>MS-VBA</strong>, recorded in Excel: oracle\s17 A01, A06).
    /// </summary>
    /// <remarks>
    /// The address is the session's, as every address <c>VarPtr</c>, <c>StrPtr</c> and <c>ObjPtr</c> give is: what is at it reads as zeros, and nothing writes
    /// it. 🚧 Native code given it calls nothing that runs the procedure yet (*Ptr plan phase B, callbacks).
    /// </remarks>
    /// <returns><see langword="false"/> when there is no room for it.</returns>
    public bool TryGetProcedureAddress(Symbol procedure, out MemoryAddress address)
    {
        var key = procedure.Uri.AbsoluteUri;
        if (_stubOf.TryGetValue(key, out address))
        {
            return true;
        }

        if (!allocator.TryAllocate(StubSize, out address))
        {
            return false;
        }

        _stubOf[key] = address;
        _stubs[address] = procedure;
        Generation++;
        return true;
    }

    /// <summary>
    /// The procedure <paramref name="address"/> is the address of (<see cref="TryGetProcedureAddress"/>), if it is one: what a callback given it calls.
    /// </summary>
    public bool TryGetProcedureAt(MemoryAddress address, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Symbol? procedure)
        => _stubs.TryGetValue(address, out procedure);
}
