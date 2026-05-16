using RDCore.Parsing.Model.Symbols;
using RDCore.Parsing.Model.Symbols.Abstract;
using RDCore.Parsing.Model.Values.Abstract;
using RDCore.Parsing.Model.Values.Intrinsic;
using System.Collections.Concurrent;

namespace RDCore.Runtime;

internal class VirtualHeap()
{
    private static readonly long _offset = 0x1000;
    //private static readonly long _addressSpace = 0x10FF; // TODO update from MS-VBAL, if specified

    private readonly Stack<ConcurrentDictionary<Symbol, VBTypedValue>> _stackFrames = [];

    private readonly ConcurrentDictionary<Symbol, VBTypedValue> _globalHeap = [];
    private readonly ConcurrentDictionary<Symbol, VBTypedValue> _workspaceHeap = [];
    private readonly ConcurrentDictionary<Symbol, VBTypedValue> _staticLocalsHeap = [];

    private readonly ConcurrentDictionary<VBObjectValue, ConcurrentDictionary<Symbol, VBTypedValue>> _objectHeap = [];

    private long _nextAddress = _offset;

    private readonly ConcurrentDictionary<long, VBTypedValue> _memoryMap = [];
    private readonly ConcurrentDictionary<Uri, long> _rawAddressMap = [];

    public VBObjectValue CreateObject(ClassModuleSymbol symbol)
    {
        var address = _nextAddress;
        Interlocked.Add(ref _nextAddress, VBLongPtrValue.BitnessAwarePtrSize);

        var pointer = new VBLongPtrValue(symbol) { NumericValue = address };
        var obj = new VBObjectValue(symbol, pointer);

        _objectHeap[obj] = [];
        _rawAddressMap[symbol.Uri] = address;

        // TODO: _monitors.ForEach(m => m.OnAllocate(...))

        return obj;
    }

    public VBTypedValue GetValue(Symbol symbol)
    {
        return symbol.ScopeKind switch
        {
            ScopeKind.Global => _globalHeap[symbol],
            ScopeKind.Module => _workspaceHeap[symbol],
            ScopeKind.Local => symbol.Get(SymbolProperties.IsStatic) ? _staticLocalsHeap[symbol] : _stackFrames.Peek()[symbol],
            _ => throw new InvalidOperationException()
        };
    }

    public void SetValue(Symbol symbol, VBTypedValue value)
    {
        var heap = symbol.ScopeKind switch
        {
            ScopeKind.Global => _globalHeap,
            ScopeKind.Module => _workspaceHeap,
            ScopeKind.Local => symbol.Get(SymbolProperties.IsStatic) ? _staticLocalsHeap : _stackFrames.Peek(),
            _ => throw new InvalidOperationException()
        };

        heap[symbol] = value;
    }

    public long Allocate(Uri symbolUri, int size)
    {
        var address = _nextAddress;
        Interlocked.Add(ref _nextAddress, Math.Max(size, VBLongPtrValue.BitnessAwarePtrSize));

        var pointer = new VBLongPtrValue { NumericValue = address };

        _rawAddressMap[symbolUri] = address;

        // TODO fire IHeapMonitor.OnAllocate here
        return address;
    }

    public long Allocate(Uri symbolUri, VBTypedValue value)
    {
        var address = _nextAddress;
        Interlocked.Add(ref _nextAddress, VBLongPtrValue.BitnessAwarePtrSize);

        var addressedValue = value with { RawAddress = address };

        _rawAddressMap[symbolUri] = address;
        _memoryMap[address] = addressedValue;

        return address;
    }

    public void Deallocate(Uri symbolUri)
    {
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
    }
}

internal class VirtualHeapCorruptionException(string message) : InvalidOperationException(message) { }