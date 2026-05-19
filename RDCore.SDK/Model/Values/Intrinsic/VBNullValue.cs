using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Intrinsic;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// Represents a <c>Null</c> (<c>VBNullType</c>) literal value.
/// </summary>
/// <param name="Symbol"></param>
public sealed record class VBNullValue(Symbol Symbol) : VBTypedValue(VBNullType.TypeInfo, Symbol), IVBTypedValue<VBNullValue, nint>, 
    INumericCoercion, IStringCoercion
{
    private static readonly Lazy<VBNullValue> _instance = new(() => new(GlobalSymbols.Null));
    public static VBNullValue Null => _instance.Value;
    
    public nint Value { get; } = nint.Zero;
    public override int Size => 0;

    // * UDT or resizable array -> VBR00013 TypeMismatch
    public bool Equals(IVBTypedValue<VBNullValue, nint>? other) => throw VBRuntimeErrorException.InvalidUseOfNull(Symbol?.SelectionRange!);
    public override int GetHashCode() => Value.GetHashCode();

    public VBDoubleValue? AsCoercedDouble(ref int depth)
    {
        // MS-VBAL 5.5.1.2.10 let-coercion from 'Null':
        // semantics of null let-coercion depend on the destination type's declared type.

        // * any type except fixed-size array or Variant -> VBR00094 InvalidUseOfNull
        ThrowWithSymbol(symbol => VBRuntimeErrorException.InvalidUseOfNull(symbol.SelectionRange!));

        return default;
    }

    public VBStringValue? AsCoercedString(ref int depth)
    {
        // MS-VBAL 5.5.1.2.10 let-coercion from 'Null':
        // semantics of null let-coercion depend on the destination type's declared type.

        // * any type except fixed-size array or Variant -> VBR00094 InvalidUseOfNull
        ThrowWithSymbol(symbol => VBRuntimeErrorException.InvalidUseOfNull(Symbol?.SelectionRange!));

        return default;
    }

    public VBFixedStringValue? AsCoercedFixedLengthString(int length, ref int depth)
    {
        // MS-VBAL 5.5.1.2.10 let-coercion from 'Null':
        // * any type except fixed-size array or Variant -> VBR00094 InvalidUseOfNull
        ThrowWithSymbol(symbol => VBRuntimeErrorException.InvalidUseOfNull(Symbol?.SelectionRange!));

        return default;
    }
}
