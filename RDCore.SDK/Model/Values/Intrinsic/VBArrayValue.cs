using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// The base type for array classes that encapsulate a <c>VBArrayType</c> run-time value.
/// </summary>
public abstract record class VBArrayValue : VBTypedValue
{
    protected VBArrayValue(Symbol symbol, (int lBound, int uBound)[] dimensions, VBType? itemType = null)
        : base(VBArrayType.TypeInfo, symbol)
    {
        ItemType = itemType ?? VBVariantType.TypeInfo;
        Dimensions = [.. dimensions.Select(e => new VBArrayDimension(symbol, ItemType, e.lBound, e.uBound))];
    }

    /// <summary>
    /// The declared <c>VBType</c> of the items in this array.
    /// </summary>
    public VBType ItemType { get; init; }

    /// <summary>
    /// <c>true</c> if this array value defines a parameter array supplied through a <c>ParamArray</c> declaration.
    /// </summary>
    public bool IsParamArray { get; init; }

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
        private readonly Symbol _symbol;

        /// <summary>
        /// Creates a array dimension of the specified <c>VBType</c> item type and the specified lower and upper bounds.
        /// </summary>
        /// <param name="symbol"></param>
        /// <param name="itemType"></param>
        /// <param name="lBound"></param>
        /// <param name="uBound"></param>
        public VBArrayDimension(Symbol symbol, VBType itemType, int lBound, int uBound)
        {
            UpperBound = uBound;
            LowerBound = lBound;

            _symbol = symbol;

            if (itemType.DefaultValue.TypeInfo.ManagedType is Type managedType) 
            {
                var defaultManagedValue = Activator.CreateInstance(managedType) ?? new object();
                _state = Enumerable.Range(lBound, uBound)
                    .Select(i => defaultManagedValue)
                    .ToImmutableArray();
            }
        }

        private readonly ImmutableArray<object> _state;
        private int ToManagedIndex(int subscript) => subscript - LowerBound;

        /// <summary>
        /// Gets the <c>VBTypedValue</c> at the specified <c>subscript</c> if the subscript is within the array bounds.
        /// </summary>
        /// <remarks>
        /// Semantics should throw a <c>VBRuntimeErrorException.SubscriptOutOfRange</c> if this indexer returns <c>null</c>.
        /// </remarks>
        public VBTypedValue? this[int subscript]
        {
            get
            {
                if (subscript < LowerBound || subscript > UpperBound)
                {
                    return default;
                }
                else
                {
                    var index = ToManagedIndex(subscript);
                    return VBTypedValueFactory.WrapManagedValue(_state[index], _symbol);
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
