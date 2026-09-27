using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// Represents a value of a <see cref="VBUserDefinedType"/>. Like <see cref="VBObjectValue"/> the
/// value is a <see cref="MemoryAddress"/> — a UDT has location identity and is never passed by value.
/// </summary>
/// <remarks>
/// 👉 The field store is a flat block of cells in <em>declaration order</em>, exactly as
/// <see cref="VBArrayValue"/> holds its elements and for the same reason: a UDT's real content is its
/// fields, and a scalar <c>IRuntimeValue</c> has nowhere to hold them. Declaration order is not an
/// implementation detail — it is the order <strong>MS-VBAL §5.4.5.11</strong> writes a record in, and the
/// order <see cref="VBUserDefinedTypeLayout"/> assigns offsets in.
/// </remarks>
public record class VBUserDefinedTypeValue : VBTypedValue,
    IVBTypedValue<VBUserDefinedTypeValue, MemoryAddress>
{
    private readonly VBTypedValue[] _fields;

    public VBUserDefinedTypeValue(IBindingHandle handle, VBUserDefinedType typeInfo) : base(typeInfo)
    {
        Handle = handle;
        Fields = [.. typeInfo.Fields()];
        _fields = CreateFields(Fields);
    }

    public VBUserDefinedTypeValue(MemoryAddress reference, VBUserDefinedType typeInfo)
        : this(new ValueBindingHandle(new VBRuntimeReference(reference)), typeInfo) { }

    public VBUserDefinedTypeValue(VBUserDefinedType typeInfo)
        : this(MemoryAddress.Zero, typeInfo) { }

    // C# records synthesize `with` from a memberwise copy constructor, which would share this value's
    // _fields by reference with whatever copy it just built — and VBA copies a UDT on assignment, so every
    // `with`-derived copy (TryAllocateIn's included) needs field storage of its own. This is the same
    // explicit copy constructor VBArrayValue needs for its cells, for the same reason; a nested UDT field is
    // copied through its own copy constructor so a copy is deep the whole way down, as VBA's is.
    protected VBUserDefinedTypeValue(VBUserDefinedTypeValue original) : base(original)
    {
        Fields = original.Fields;
        _fields = [.. original._fields.Select(field => field is VBUserDefinedTypeValue nested ? nested with { } : field)];
    }

    /// <summary>
    /// This type's fields, in declaration order — the same symbols <see cref="VBUserDefinedType.Fields"/>
    /// reports, held here so that a value can name its own fields without resolving its type again.
    /// </summary>
    public ImmutableArray<VBUserDefinedTypeFieldSymbol> Fields { get; init; }

    public MemoryAddress Value => ((VBRuntimeReference)RuntimeValue).Value;

    /// <summary>
    /// The in-memory size of this UDT, MS-VBA's own padding included — what <c>LenB</c> reports
    /// (<strong>MS-VBAL §6.1.2.11</strong>).
    /// </summary>
    /// <remarks>
    /// Not the size a <c>Put</c> statement writes, which is the unpadded concatenation of the fields and is
    /// what <c>Len</c> reports of a UDT instead: "<c>Len</c> returns the size as it will be written to the
    /// file". <see cref="VBUserDefinedTypeLayout"/> holds the padding rule and the reasoning behind it.
    /// </remarks>
    public override int Size => Layout.Size;

    /// <summary>
    /// Where each of this value's fields sits in memory.
    /// </summary>
    public VBUserDefinedTypeLayout Layout => VBUserDefinedTypeLayout.Of((VBUserDefinedType)TypeInfo);

    /// <summary>
    /// The value of the field named <paramref name="name"/>, or <c>null</c> when there is no such field.
    /// </summary>
    /// <param name="name">The field name, compared case-insensitively as VBA compares identifiers.</param>
    public VBTypedValue? this[string name]
    {
        get
        {
            var index = IndexOf(name);
            return index < 0 ? null : _fields[index];
        }
    }

    /// <summary>
    /// The value of the field at <paramref name="index"/> in declaration order, or <c>null</c> when there is
    /// no field there.
    /// </summary>
    /// <remarks>
    /// Declaration order is what a record is written and read in, so a caller walking the fields to
    /// serialize them wants this rather than the name-keyed indexer.
    /// </remarks>
    public VBTypedValue? FieldAt(int index)
        => index < 0 || index >= _fields.Length ? null : _fields[index];

    /// <summary>
    /// Replaces the value of the field named <paramref name="name"/>. Mutates the cell in place — field
    /// storage is mutable, the way array element storage is.
    /// </summary>
    /// <param name="name">The field name, compared case-insensitively.</param>
    /// <param name="value">The value to store.</param>
    /// <returns><c>false</c> when there is no such field.</returns>
    public bool TrySetField(string name, VBTypedValue value) => TrySetFieldAt(IndexOf(name), value);

    /// <summary>
    /// Replaces the value of the field at <paramref name="index"/> in declaration order.
    /// </summary>
    /// <param name="index">The field's position in declaration order.</param>
    /// <param name="value">The value to store.</param>
    /// <returns><c>false</c> when there is no field there.</returns>
    public bool TrySetFieldAt(int index, VBTypedValue value)
    {
        if (index < 0 || index >= _fields.Length)
        {
            return false;
        }

        _fields[index] = value;
        return true;
    }

    /// <summary>
    /// The binding of the field named <paramref name="name"/>, or <c>null</c> when there is no such field.
    /// </summary>
    /// <remarks>
    /// What a <c>ByRef</c> argument of a UDT field needs, and what an assignment to one writes through —
    /// neither of which constructs a value, so neither goes through the indexer.
    /// </remarks>
    /// <param name="name">The field name, compared case-insensitively.</param>
    public IBindingHandle? GetFieldHandle(string name) => this[name]?.Handle;

    public bool Equals(IVBTypedValue<VBUserDefinedTypeValue, MemoryAddress>? other) => Value.Value.Equals(other?.Value.Value);

    /// <summary>
    /// Reserves storage sized for this UDT through <paramref name="storage"/>, and returns a copy of
    /// this value bound to the resulting address — a UDT's identity <em>is</em> its address, so the
    /// allocated copy self-reports the very address it was allocated at.
    /// </summary>
    /// <returns><c>false</c> if the underlying memory space is exhausted.</returns>
    public bool TryAllocateIn(ISessionStorage storage, [NotNullWhen(true)] out VBUserDefinedTypeValue? allocated)
    {
        if (!storage.TryAllocate(Size, InvalidBindingHandle.Default, out var address))
        {
            allocated = null;
            return false;
        }

        allocated = (VBUserDefinedTypeValue)WithRuntimeValue(new VBRuntimeReference(address));
        storage.TryRebind(address, allocated.Handle);
        return true;
    }

    private int IndexOf(string name)
    {
        for (var index = 0; index < Fields.Length; index++)
        {
            if (Fields[index].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    // A fresh value per field so a write to one never aliases another. Every field starts at its declared
    // type's own default, which is what a VBA UDT variable starts as: MS-VBAL gives a UDT no initializer, so
    // each field is initialized as a variable of its type would be.
    private static VBTypedValue[] CreateFields(ImmutableArray<VBUserDefinedTypeFieldSymbol> fields)
        => [.. fields.Select(field => DefaultOf(field.ResolvedType))];

    private static VBTypedValue DefaultOf(VBType? type)
        => type is null ? VBEmptyValue.Empty
            // a fresh binding rather than the shared DefaultValue instance: a type's default value is cached
            // and reused by every caller, and both binding kinds mutate in place, so storing it directly would
            // let a write to one field reach every other value that started from it - the same hazard
            // SymbolAddressTable.FreshBinding exists for.
            : type.DefaultValue switch
            {
                VBUserDefinedTypeValue nested => nested with { },
                VBArrayValue array => array with { },
                { Handle: ValueBindingHandle } scalar => scalar.WithRuntimeValue(scalar.RuntimeValue),
                var other => other,
            };
}
