using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Symbols.Abstract;
using System.Collections.Concurrent;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Runtime;

namespace RDCore.Runtime;

/// <summary>
/// Creates a virtual memory space for an execution context.
/// </summary>
/// <param name="Is64Bit">The bitness of the runtime environment.</param>
/// <remarks>If no bitness is specified, uses the bitness of the host process.</remarks>
public class VirtualHeap(bool? Is64Bit = true) : IVirtualHeap
{
    private static readonly long _offset = 0x1000;
    private readonly int _ptrSize = (Is64Bit ?? Environment.Is64BitProcess) ? VBLongPtrType_x64.BitnessAwarePtrSize : VBLongPtrType_x86.BitnessAwarePtrSize;

    public bool Is64Bit { get; } = Is64Bit ?? Environment.Is64BitProcess;

    //private static readonly long _addressSpace = 0x10FF; // TODO update from MS-VBAL, if specified

    private readonly Stack<ConcurrentDictionary<Symbol, VBTypedValue>> _stackFrames = [];

    private readonly ConcurrentDictionary<Symbol, VBTypedValue> _globalHeap = [];
    private readonly ConcurrentDictionary<Symbol, VBTypedValue> _workspaceHeap = [];
    private readonly ConcurrentDictionary<Symbol, VBTypedValue> _staticLocalsHeap = []; // "static" in the VB sense here.

    private readonly ConcurrentDictionary<VBObjectValue, ConcurrentDictionary<Symbol, VBTypedValue>> _objectHeap = [];

    private long _nextAddress = _offset;

    private readonly ConcurrentDictionary<long, VBTypedValue> _memoryMap = [];
    private readonly ConcurrentDictionary<Uri, long> _rawAddressMap = [];

    public VBObjectValue CreateObject(VBClassModuleSymbol symbol)
    {
        var address = _nextAddress;
        Interlocked.Add(ref _nextAddress, _ptrSize);

        var obj = new VBObjectValue(symbol);

        _objectHeap[obj] = [];
        _rawAddressMap[symbol.Uri] = address;

        return obj;
    }

    public VBTypedValue GetValue(Symbol symbol)
    {
        return symbol switch
        {
            Symbol s when s.ScopeKind == ScopeKind.Global => _globalHeap[symbol],
            Symbol s when s.ScopeKind == ScopeKind.Module => _workspaceHeap[symbol],
            StaticLocalVariableSymbol => _staticLocalsHeap[symbol],
            LocalVariableSymbol => _stackFrames.Peek()[symbol],
            // handle unallocated with a run-time error? is shouldn't even be possible...
            _ => throw new InvalidOperationException()
        };
    }

    public void SetValue(Symbol symbol, VBTypedValue value)
    {
        var heap = symbol switch
        {
            Symbol s when s.ScopeKind == ScopeKind.Global => _globalHeap,
            Symbol s when s.ScopeKind == ScopeKind.Module => _workspaceHeap,
            StaticLocalVariableSymbol => _staticLocalsHeap,
            LocalVariableSymbol => _stackFrames.Peek(),
            // handle unallocated with a run-time error? is shouldn't even be possible...
            _ => throw new InvalidOperationException()
        };

        heap[symbol] = value;
    }

    public long Allocate(Uri symbolUri, int size)
    {
        var address = _nextAddress;
        Interlocked.Add(ref _nextAddress, Math.Max(size, _ptrSize));

        _rawAddressMap[symbolUri] = address;

        // TODO fire IHeapMonitor.OnAllocate here
        return address;
    }

    //private static readonly object _lock = new object();
    public long Allocate(Uri symbolUri, VBTypedValue value)
    {
        var address = _nextAddress;
        Interlocked.Add(ref _nextAddress, _ptrSize);

        var addressedValue = value with { RawAddress = address };

        //lock (_lock) // concurrent doesn't mean atomic. see if this lock could be needed.
        //{
        _rawAddressMap[symbolUri] = address;
        _memoryMap[address] = addressedValue;
        //}
        return address;
    }

    public void Deallocate(Uri symbolUri)
    {
        // concurrent dictionaries don't magically make things atomic...
        //lock (_lock) 
        //{
        if (!_rawAddressMap.TryRemove(symbolUri, out var address))
        {
            // 🔥this is fine🔥
            // TODO at least log a warning here
            return;
        }

        if (!_memoryMap.TryRemove(address, out var _))
        {
            throw new VirtualHeapCorruptionException($"An allocated value was expected to be found at {address:X} for symbol {symbolUri}, but the value was not found.");
        }
        //}
    }
}

public class VirtualHeapCorruptionException(string message) : InvalidOperationException(message) { }