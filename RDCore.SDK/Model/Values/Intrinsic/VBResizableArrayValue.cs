using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// A value representing a resizable (dynamically-sized) array containing <c>VBByteValue</c> elements.
/// </summary>
/// <remarks>
/// Specifications attach semantics to this specific array type.
/// </remarks>
public sealed record class VBResizableByteArrayValue : VBResizableArrayValue
{
    private static readonly Lazy<VBResizableByteArrayValue> _defaultValue = new(() => new(GlobalSymbols.StaticSymbols.EmptyResizableByteArray, []));
    /// <summary>
    /// Gets an empty <c>VBResizableArrayValue</c> with the <c>EmptyResizableByteArray</c> static symbol.
    /// </summary>
    public static new VBResizableByteArrayValue Empty => _defaultValue.Value;

    /// <summary>
    /// Creates a new empty resizable array for the specified symbol, containing <c>VBByteValue</c> elements.
    /// </summary>
    /// <param name="symbol">The <c>Symbol</c> associated with this value.</param>
    /// <param name="dimensions">An array of value tuples containing the lower and upper boundaries of each dimension.</param>
    public VBResizableByteArrayValue(Symbol symbol, (int lBound, int uBound)[] dimensions)
        : base(symbol, dimensions, VBByteType.TypeInfo) { }
}

/// <summary>
/// A value representing a resizable (dynamically-sized) array.
/// </summary>
public record class VBResizableArrayValue : VBArrayValue
{
    private static readonly Lazy<VBResizableArrayValue> _defaultValue = new(() => new(GlobalSymbols.StaticSymbols.EmptyResizableArray, []));
    /// <summary>
    /// Gets an empty <c>VBResizableArrayValue</c> with the <c>EmptyResizableArray</c> static symbol.
    /// </summary>
    public static VBResizableArrayValue Empty => _defaultValue.Value;

    public override object BoxedValue => _defaultValue.Value;

    /// <summary>
    /// Creates a new resizable array containing <c>VBVariantValue</c> elements unless specified otherwise.
    /// </summary>
    /// <param name="symbol">The <c>Symbol</c> associated with this value.</param>
    /// <param name="dimensions">An array of value tuples containing the lower and upper boundaries of each dimension.</param>
    /// <param name="itemType">The type of item held in this array. <c>VBVariantType</c> by default.</param>
    public VBResizableArrayValue(Symbol symbol, (int lBound, int uBound)[] dimensions, VBType? itemType = null)
        : base(symbol, dimensions, itemType ?? VBVariantType.TypeInfo) { }
    /*
    public VBArrayValue ReDim((int lBound, int uBound)[] dimensions, bool preserve = false)
    {
        if (IsWithBlockVariable)
        {
            throw VBCompileErrorException.InvalidReDim(Symbol?.SelectionRange!, "The target of a `ReDim` statement cannot be a `With` block variable.");
        }

        if (IsParamArray)
        {
            throw VBCompileErrorException.InvalidParamArrayUse(Symbol?.SelectionRange!, "Parameter array value cannot be resized.");
        }

        if (preserve)
        {
            if (dimensions.Length != Dimensions.Length)
            {
                throw VBRuntimeErrorException.SubscriptOutOfRange(Symbol?.SelectionRange!, "`ReDim Preserve` cannot change the number of dimensions of a resizable array.");
            }

            for (var i = 0; i < dimensions.Length; i++)
            {
                var (oldLower, oldUpper) = Dimensions[i];
                var (newLower, newUpper) = dimensions[i];

                if (i != dimensions.Length - 1)
                {
                    if (oldLower != newLower)
                    {
                        throw VBRuntimeErrorException.SubscriptOutOfRange(Symbol?.SelectionRange!, "`ReDim Preserve` cannot change the lower boundary of a resizable array.");
                    }

                    if (oldUpper != newUpper)
                    {
                        throw VBRuntimeErrorException.SubscriptOutOfRange(Symbol?.SelectionRange!, "`ReDim Preserve` cannot change the upper boundary of a dimension that isn't the last dimension of a resizable array.");
                    }

                    if (newUpper < oldUpper)
                    {
                        // issue diagnostic for data truncation?
                    }
                }
            }
        }

        if (preserve)
        {
            var oldDim = Dimensions[^0];

            var newUpperBound = dimensions[^0].uBound;
            var newDim = new VBArrayDimension(Symbol, ItemType, newUpperBound, oldDim.LowerBound);

            var newState = oldDim.State.Take(newDim.Length).ToArray();
            if (newState.Length < newDim.Length)
            {
                newState = [.. newState, .. Enumerable.Repeat(ItemType.DefaultValue, newDim.Length - newState.Length)];
            }

            newDim = newDim with { State = [.. newState] };
            var newDimensions = Dimensions[..^1].Append(newDim);
            return this with { Dimensions = [.. newDimensions] };
        }

        return this with { Dimensions = [.. dimensions.Select(e => new VBArrayDimension(Symbol, ItemType, e.uBound, e.lBound))] };
    }
    public VBStringValue? AsCoercedString(ref int depth)
    {
        if (ItemType == VBByteType.TypeInfo)
        {
            // MS-VBAL 5.5.1.2.6 Let-coercion to and from resizable Byte()
            // TODO verify about fixed-size Byte()
            var bytes = Dimensions[0].State
                .OfType<VBByteValue>()
                .Select(e => e.Value)
                .ToArray();

            // spec says it's implementation-defined, but also that
            // let-coercion from Byte() array should be through StrConv (stdlib).
            var value = Encoding.Unicode.GetString(bytes);
            return new VBStringValue(Symbol).WithValue(value);
        }
        else if (Symbol?.Range is OmniSharp.Extensions.LanguageServer.Protocol.Models.Range location)
        {
            throw VBRuntimeErrorException.TypeMismatch(location);
        }

        return default;
    }

    // TODO move to let-coercion semantics
    public VBFixedStringValue? AsCoercedFixedLengthString(int length, ref int depth) =>
        AsCoercedString(ref depth) is VBStringValue value ? new VBFixedStringValue(length).WithFixedValue(value.Value) : null;
    */
}
