using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// A <see cref="VBTypedValue"/> representing a runtime value of the <see cref="VBArrayType"/> data type.
/// </summary>
/// <remarks>
/// 👉 The element store is a single flat block of <see cref="IBindingHandle"/> cells addressed in
/// <strong>column-major</strong> order (the first subscript varies fastest, as an OLE <c>SAFEARRAY</c>).
/// Bounds live in <see cref="Dimensions"/> as metadata (<c>LBound</c>/<c>UBound</c>/rank); the cells
/// are mutable storage. An uninitialized array (<c>Dim a()</c>) has no dimensions and no cells.
/// </remarks>
public abstract record class VBArrayValue : VBTypedValue
{
    private readonly IBindingHandle[] _cells;

    /// <summary>
    /// Creates an array value with the given dimension bounds and element type.
    /// </summary>
    /// <param name="dimensions">The lower and upper bound of each dimension, outermost first.</param>
    /// <param name="itemType">The element type. Use <see cref="VBVariantType"/> when unspecified.</param>
    protected VBArrayValue((int lBound, int uBound)[] dimensions, VBType itemType)
        : base(VBArrayType.TypeInfo)
    {
        ItemType = itemType;
        Dimensions = [.. dimensions.Select(d => new VBArrayDimension(d.lBound, d.uBound))];
        _cells = CreateCells(Dimensions, itemType);
    }

    /// <summary>
    /// Creates an array value bound to <paramref name="handle"/>. The array's own binding is a
    /// <em>storage</em> concern distinct from the element cells; it is currently inert (see the
    /// session-storage follow-up).
    /// </summary>
    protected VBArrayValue(IBindingHandle handle, (int lBound, int uBound)[] dimensions, VBType itemType)
        : this(dimensions, itemType)
    {
        Handle = handle;
    }

    /// <summary>
    /// The declared <see cref="VBType"/> of the elements in this array.
    /// </summary>
    public VBType ItemType { get; init; }

    /// <summary>
    /// The bounds of each dimension (outermost first). Empty for an uninitialized array.
    /// </summary>
    public ImmutableArray<VBArrayDimension> Dimensions { get; init; }

    /// <summary>
    /// The number of dimensions of this array. <c>0</c> for an uninitialized array.
    /// </summary>
    public int Rank => Dimensions.Length;

    /// <summary>
    /// <c>true</c> if this array has at least one dimension.
    /// </summary>
    public bool IsInitialized => Dimensions.Length > 0;

    /// <summary>
    /// The total number of elements across every dimension.
    /// </summary>
    public int Length => _cells.Length;

    /// <inheritdoc/>
    public override int Size => _cells.Length == 0 ? 0 : ItemType.DefaultValue.Size * _cells.Length;

    /// <summary>
    /// Gets the element at the given subscripts, or <c>null</c> when any subscript is out of bounds
    /// (semantics raise <c>SubscriptOutOfRange</c> in that case) or the subscript count does not match
    /// the array rank.
    /// </summary>
    public VBTypedValue? this[params int[] subscripts]
    {
        get
        {
            var index = LinearIndex(subscripts);
            return index < 0 ? null : ItemType.CreateValue(_cells[index]);
        }
    }

    /// <summary>
    /// Rebinds the element at the given subscripts to <paramref name="value"/>. Mutates the cell in
    /// place — array element storage is mutable. Returns <c>false</c> when the subscripts are out of
    /// bounds or their count does not match the array rank.
    /// </summary>
    public bool TrySetElement(IBindingHandle value, params int[] subscripts)
    {
        var index = LinearIndex(subscripts);
        if (index < 0)
        {
            return false;
        }

        _cells[index] = value;
        return true;
    }

    private int LinearIndex(ReadOnlySpan<int> subscripts)
    {
        if (_cells.Length == 0 || subscripts.Length != Dimensions.Length)
        {
            return -1;
        }

        var index = 0;
        var stride = 1;
        for (var k = 0; k < Dimensions.Length; k++)
        {
            var dimension = Dimensions[k];
            var subscript = subscripts[k];
            if (subscript < dimension.LowerBound || subscript > dimension.UpperBound)
            {
                return -1;
            }

            index += (subscript - dimension.LowerBound) * stride;
            stride *= dimension.Length;
        }

        return index;
    }

    private static IBindingHandle[] CreateCells(ImmutableArray<VBArrayDimension> dimensions, VBType itemType)
    {
        if (dimensions.Length == 0)
        {
            return [];
        }

        var total = 1;
        foreach (var dimension in dimensions)
        {
            total *= Math.Max(0, dimension.Length);
        }

        if (total == 0)
        {
            return [];
        }

        var cells = new IBindingHandle[total];
        for (var i = 0; i < total; i++)
        {
            cells[i] = CreateDefaultCell(itemType);
        }

        return cells;
    }

    // A fresh binding per cell so an element write never aliases another cell. Complex element types
    // (Variant, UDT, Object) whose default value has no readable binding get an inert cell until the
    // complex-value semantics land.
    private static IBindingHandle CreateDefaultCell(VBType itemType)
    {
        var defaultValue = itemType.DefaultValue;
        return defaultValue.Handle.BindingCapabilities.HasFlag(BindingCapabilities.GetValue)
            ? new ValueBindingHandle(defaultValue.RuntimeValue)
            : InvalidBindingHandle.Default;
    }

    /// <summary>
    /// The bounds of a single array dimension.
    /// </summary>
    /// <param name="LowerBound">The lower subscript bound, returned by <c>LBound</c>.</param>
    /// <param name="UpperBound">The upper subscript bound, returned by <c>UBound</c>.</param>
    public record class VBArrayDimension(int LowerBound, int UpperBound)
    {
        /// <summary>
        /// The number of elements in this dimension. <c>0</c> when the bounds are inverted.
        /// </summary>
        public int Length => IsInitialized ? UpperBound - LowerBound + 1 : 0;

        /// <summary>
        /// <c>true</c> when <see cref="LowerBound"/> is less than or equal to <see cref="UpperBound"/>.
        /// </summary>
        public bool IsInitialized => LowerBound <= UpperBound;

        /// <summary>
        /// Deconstructs this dimension into its lower and upper bounds:
        /// <c>var (lower, upper) = arrayValue.Dimensions[0];</c>
        /// </summary>
        public void Deconstruct(out int lBound, out int uBound)
        {
            lBound = LowerBound;
            uBound = UpperBound;
        }
    }
}
