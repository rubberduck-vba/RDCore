using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.Unbound;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Bindings;

namespace RDCore.Runtime.Execution;

/// <summary>
/// Creates a virtual memory space for an execution context.
/// </summary>
/// <param name="Is64Bit">The bitness of the runtime environment.</param>
/// <remarks>If no bitness is specified, uses the bitness of the host process.</remarks>
public class VirtualHeap(bool? Is64Bit = true) : IVirtualHeap
{
    private record struct HeapEntry(IBindingHandle Handle);

    private static readonly long _offset = 0x1000;
    private readonly int _ptrSize = (Is64Bit ?? Environment.Is64BitProcess) ? VBLongPtrType_x64.BitnessAwarePtrSize : VBLongPtrType_x86.BitnessAwarePtrSize;

    public bool Is64Bit { get; } = Is64Bit ?? Environment.Is64BitProcess;


    private readonly Dictionary<Symbol, HeapEntry> _globalHeap = [];
    private readonly Dictionary<Symbol, HeapEntry> _workspaceHeap = [];
    private readonly Dictionary<Symbol, HeapEntry> _staticLocalsHeap = []; // "static" in the VB sense here.

    // holds all references associated to the symbol mapped to an object
    private readonly Dictionary<VBObjectValue, Dictionary<Symbol, HeapEntry>> _objectHeap = [];

    private long _nextAddress = _offset;

    private readonly Dictionary<Uri, Symbol> _symbolTable = [];
    private readonly Dictionary<string, string> _nameTable = [];

    private readonly Dictionary<long, HeapEntry> _memoryMap = [];
    private readonly Dictionary<Uri, long> _rawAddressMap = [];

    private readonly Queue<long> _availableAddresses = [];

    public VBObjectValue CreateObject(VBClassModuleSymbol symbol)
    {
        var address = _nextAddress;
        _nextAddress += _ptrSize;

        var obj = new VBObjectValue(symbol);

        _objectHeap[obj] = [];
        _rawAddressMap[symbol.Uri] = address;

        return obj;
    }

    private void Define(VBClassType classType)
    {
        Define(classType.Symbol);
        foreach (var member in classType.Members) { Define(member); }
    }

    private void Define(VBProcedureMemberSymbol procedureMember)
    {
        Define(procedureMember as Symbol);
        foreach (var parameter in procedureMember.Parameters) { Define(parameter); }
    }

    private void Define(VBReturningMemberSymbol returningMember)
    {
        Define(returningMember as Symbol);
        foreach (var parameter in returningMember.Parameters) { Define(parameter); }
    }

    public bool TryRead(long address, [NotNullWhen(true)][MaybeNullWhen(false)] out IBindingHandle value)
    {
        var result = _memoryMap.TryGetValue(address, out var binding);
        value = result ? binding.Handle : default;
        return result;
    }

    public IBindingHandle GetValue(Symbol symbol)
    {
        return symbol switch
        {
            VBStaticLocalVariableSymbol => _staticLocalsHeap[symbol].Handle,
            Symbol s when s.ScopeKind == ScopeKind.Module => _workspaceHeap[symbol].Handle,
            Symbol s when s.ScopeKind == ScopeKind.Global => _globalHeap[symbol].Handle,

            _ => throw new InvalidOperationException()
        };
    }

    public void SetValue(Symbol symbol, IBindingHandle handle)
    {
        if (symbol is VBStaticLocalVariableSymbol)
        {
            _staticLocalsHeap[symbol] = new(handle);
        }

        switch (symbol.ScopeKind)
        {
            case ScopeKind.Local:
            case ScopeKind.External:
            case ScopeKind.Unallocated:
                break;

            case ScopeKind.Global:
                _globalHeap[symbol] = new(handle);
                break;

            case ScopeKind.Module:
                _workspaceHeap[symbol] = new(handle);
                break;

            case ScopeKind.Instance:
                // TODO
                break;
        }
    }

    public long Allocate(Uri symbolUri, int size)
    {
        var address = _nextAddress;
        _nextAddress += (int)(size / _ptrSize + 0.5) * _ptrSize;

        _rawAddressMap[symbolUri] = address;

        // TODO fire IHeapMonitor.OnAllocate here
        return address;
    }

    //private static readonly object _lock = new object();
    public long Allocate(Uri symbolUri, VBTypedValue value)
    {
        var address = _availableAddresses.TryDequeue(out var result) ? result : _nextAddress;
        Interlocked.Add(ref _nextAddress, _ptrSize);

        _rawAddressMap[symbolUri] = address;
        _memoryMap[address] = new(value.Handle);

        return address;
    }

    public void Deallocate(Uri symbolUri)
    {
        if (!_rawAddressMap.Remove(symbolUri, out var address))
        {
            // 🔥this is fine🔥
            // TODO at least log a warning here
            return;
        }

        if (!_memoryMap.Remove(address, out var _))
        {
            throw new VirtualHeapCorruptionException($"An allocated value was expected to be found at {address:X} for symbol {symbolUri}, but the value was not found.");
        }

        _availableAddresses.Enqueue(address);
    }

    public Symbol? Resolve(string name, ScopeKind scope, Uri handle)
    {
        var observerScope = _symbolTable[handle];
        /// TODO
        return default;
    }

    public void Define(Symbol symbol)
    {
        _symbolTable[symbol.Uri] = symbol;
        if (TryGetIdentifierName(symbol.Name, out var caseMatchedName))
        {
            // symbol name matched name 
        }

        var invariantName = symbol.Name.ToLowerInvariant();
        if (invariantName != symbol.Name)
        {
            // TODO inject a semantic flag here somehow.
        }
        _nameTable[invariantName] = symbol.Name;
    }

    private void SetNameTableIdentifier(string identifier) => _nameTable[identifier.ToLowerInvariant()] = identifier;
    private bool TryGetIdentifierName(string identifier, [MaybeNull]out string name) => _nameTable.TryGetValue(identifier.ToLowerInvariant(), out name);
}

public class VirtualHeapCorruptionException(string message) : InvalidOperationException(message) { }