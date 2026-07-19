using RDCore.SDK.Model.Symbols.Abstract;
namespace RDCore.SDK.Model.Values.Interop;

public readonly record struct ManagedInteropReference(Type ManagedType, ScopeKind ValueAlloc, object Value) : IManagedInteropValue
{
    public static readonly ManagedInteropReference NullRef = new(typeof(Object), ScopeKind.Unallocated, null!);
    public static readonly ManagedInteropReference NullStringRef = new(typeof(string), ScopeKind.Unallocated, null!);
    public static readonly ManagedInteropReference EmptyStringRef = new(typeof(string), ScopeKind.Unallocated, string.Empty);

    public object BoxedValue => Value;
}