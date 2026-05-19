using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Symbols.Abstract;
using System.Collections.Concurrent;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Symbols;

namespace RDCore.SDK.Runtime;

/// <summary>
/// A service that manages the run-time memory structure of an execution context.
/// </summary>
public interface IVirtualHeap
{
    /// <summary>
    /// Creates a new <c>VBObjectValue</c> for the specified <c>Symbol</c>.
    /// </summary>
    /// <param name="symbol">The <c>ClassModuleSymbol</c> to instantiate.</param>
    /// <remarks>
    /// The <strong>RDCore</strong> implementation is intended to be thread-safe.
    /// </remarks>
    VBObjectValue CreateObject(VBClassModuleSymbol symbol);
    /// <summary>
    /// Gets the <c>VBTypedValue</c> currently associated with the specified <c>Symbol</c>.
    /// </summary>
    /// <param name="symbol">The <c>Symbol</c> to retrieve the currently associated value for.</param>
    VBTypedValue GetValue(Symbol symbol);
    /// <summary>
    /// Associates the specified <c>VBTypedValue</c> value to the specified <c>Symbol</c>.
    /// </summary>
    /// <param name="symbol">The <c>Symbol</c> receiving the assignment.</param>
    /// <param name="value">The <c>VBTypedValue</c> to be assigned.</param>
    /// <remarks>
    /// The <strong>RDCore</strong> implementation is intended to be thread-safe.
    /// </remarks>
    void SetValue(Symbol symbol, VBTypedValue value);
    /// <summary>
    /// Allocates the specified number of bytes (<c>size</c>) under the specified <c>symbolUri</c> at the current memory address pointer.
    /// </summary>
    /// <param name="symbolUri">The <c>Uri</c> associated with this allocated memory space.</param>
    /// <param name="size">The size (bytes) of the allocated memory.</param>
    /// <returns></returns>
    /// <remarks>
    /// The <strong>RDCore</strong> implementation is intended to be thread-safe.
    /// </remarks>
    long Allocate(Uri symbolUri, int size);
    /// <summary>
    /// Allocates the specified <c>VBTypedValue</c> under the specified <c>symbolUri</c> at the current memory address pointer.
    /// </summary>
    /// <param name="symbolUri">The <c>Uri</c> associated with this allocated memory space.</param>
    /// <param name="value">The <c>VBTypedValue</c> to be allocated.</param>
    /// <remarks>
    /// The <strong>RDCore</strong> implementation is intended to be thread-safe.
    /// </remarks>
    long Allocate(Uri symbolUri, VBTypedValue value);
    /// <summary>
    /// Deallocates the memory space held at the specified <c>Uri</c>.
    /// </summary>
    /// <param name="symbolUri">The <c>Uri</c> of the symbol to deallocate.</param>
    void Deallocate(Uri symbolUri);
}

public class VirtualHeap() : IVirtualHeap
{
    private static readonly long _offset = 0x1000;
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
        Interlocked.Add(ref _nextAddress, VBLongPtrValue.BitnessAwarePtrSize);

        var pointer = new VBLongPtrValue(symbol) { ManagedValue = address };
        var obj = new VBObjectValue(symbol, pointer);

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
        Interlocked.Add(ref _nextAddress, Math.Max(size, VBLongPtrValue.BitnessAwarePtrSize));

        _rawAddressMap[symbolUri] = address;

        // TODO fire IHeapMonitor.OnAllocate here
        return address;
    }

    //private static readonly object _lock = new object();
    public long Allocate(Uri symbolUri, VBTypedValue value)
    {
        var address = _nextAddress;
        Interlocked.Add(ref _nextAddress, VBLongPtrValue.BitnessAwarePtrSize);

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