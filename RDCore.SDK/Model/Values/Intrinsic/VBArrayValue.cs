using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// A <see cref="VBTypedValue"/> representing a runtime value of the <see cref="VBArrayType"/> data type.
/// </summary>
public abstract record class VBArrayValue : VBTypedValue
{
    /// <summary>
    /// Creates a new <see cref="VBTypedValue"/> representing a runtime value of the <see cref="VBArrayType"/> data type.
    /// </summary>
    /// <param name="dimensions">An array of <em>value tuples</em> defining the size of each dimension of this <see cref="VBArrayValue"/>.</param>
    /// <param name="itemType">The data type of the items in this array. Use <see cref="VBVariantType"/> if the declared data type is unspecified.</param>
    protected VBArrayValue((int lBound, int uBound)[] dimensions, VBType itemType)
        : base(VBArrayType.TypeInfo)
    {
        ItemType = itemType;
        Dimensions = [.. dimensions.Select(e => new VBArrayDimension(ItemType, e.lBound, e.uBound))];
    }

    /// <summary>
    /// Creates an array value bound to <paramref name="handle"/>. The element store is not yet
    /// handle-backed, so the handle is currently inert (see the complex-value follow-up).
    /// </summary>
    protected VBArrayValue(IBindingHandle handle, (int lBound, int uBound)[] dimensions, VBType itemType)
        : this(dimensions, itemType)
    {
        Handle = handle;
    }

    /// <summary>
    /// The declared <c>VBType</c> of the items in this array.
    /// </summary>
    public VBType ItemType { get; init; }

    public override int Size => ItemType.DefaultValue.Size * Dimensions.Length;

    /// <summary>
    /// Gets an immutable array of <c>VBArryaDimension</c> each holding a dimensional slice of the array value.
    /// </summary>
    public ImmutableArray<VBArrayDimension> Dimensions { get; init; }

    /// <summary>
    /// <c>true</c> if the array value contains any dimensions.
    /// </summary>
    public bool IsInitialized => Dimensions.Length > 0;

    /// <summary>
    /// Represents an (indexer-enabled) array dimension.
    /// </summary>
    public record class VBArrayDimension
    {
        /// <summary>
        /// Creates a array dimension of the specified <c>VBType</c> item type and the specified lower and upper bounds.
        /// </summary>
        /// <param name="itemType"></param>
        /// <param name="lBound"></param>
        /// <param name="uBound"></param>
        public VBArrayDimension(VBType itemType, int lBound, int uBound)
        {
            UpperBound = uBound;
            LowerBound = lBound;

            _itemType = itemType;

            var defaultManagedValue = itemType is VBBooleanType or VBNumericType or VBStringType 
                ? Activator.CreateInstance(itemType.ManagedType!) 
                : itemType.DefaultValue.UnderlyingValue;
            _state = [.. Enumerable.Range(lBound, uBound).Select(i => defaultManagedValue)];
        }

        private readonly VBType _itemType;

        private readonly ImmutableArray<object?> _state;
        private int ToManagedIndex(int subscript) => subscript - LowerBound;

        /// <summary>
        /// Gets the <c>VBTypedValue</c> at the specified <c>subscript</c> if the subscript is within the array bounds.
        /// </summary>
        /// <remarks>
        /// 💥 Semantics should throw a <c>VBRuntimeErrorException.SubscriptOutOfRange</c> if this indexer returns <c>null</c>.
        /// </remarks>
        public VBTypedValue? this[int subscript]
        {
            get
            {
                if (subscript < LowerBound || subscript > UpperBound)
                {
                    return null;
                }
                else 
                {
                    var index = ToManagedIndex(subscript);
                    if (_itemType is VBNumericType numericType)
                    {
                        var value = (double)_state[index]!;
                        return (VBNumericTypedValue)numericType.CreateValue(value);
                    }
                    else if (_itemType is VBStringType stringType)
                    {
                        var value = (string)(_state[index] ?? string.Empty);
                        return ((VBStringValue)stringType.DefaultValue).WithValue(value);
                    }
                    else
                    {
                        var value = (VBTypedValue)(_state[index] ?? VBUnknownValue.DefaultValue);
                        return value;
                    }
                }
            }
        }

        /// <summary>
        /// Gets the upper bound of this array dimension. Returns <c>-1</c> if the array/dimension is not initialized.
        /// </summary>
        /// <remarks>
        /// This value is the underlying value returned by the <c>UBound</c> operator.
        /// </remarks>
        public int UpperBound { get; init; } = -1;
        /// <summary>
        /// Gets the lower bound of this array dimension. Returns <c>0</c> if the array/dimension is not initialized.
        /// </summary>
        /// <remarks>
        /// This value is the underlying value returned by the <c>LBound</c> operator.
        /// </remarks>
        public int LowerBound { get; init; } = 0;

        /// <summary>
        /// Returns the length of the array dimension, in terms of number of elements.
        /// </summary>
        public int Length => UpperBound - LowerBound + 1;

        /// <summary>
        /// <c>true</c> when the <c>LowerBound</c> is less than or equal to the <c>UpperBound</c>.
        /// </summary>
        public bool IsInitialized => LowerBound <= UpperBound;

        /// <summary>
        /// Allows deconstructing this record into its lower and upper boundaries.
        /// </summary>
        /// <remarks>
        /// <c>var (lower, upper) = vbArrayValue.Dimensions[0];</c>
        /// </remarks>
        public void Deconstruct(out int lBound, out int uBound)
        {
            lBound = LowerBound;
            uBound = UpperBound;
        }
    }
}
