using RDCore.Parsing.Model.Symbols;
using RDCore.Parsing.Model.Types;
using RDCore.Runtime;

namespace RDCore.Parsing.Model.Values;

internal record class VBNullValue : VBTypedValue, IVBTypedValue<VBNullValue, nint>, 
    INumericCoercion, IStringCoercion
{
    public static VBNullValue Null => new() { TypeInfo = VBNullType.TypeInfo, Symbol = GlobalSymbols.Null };
    public VBNullValue() : base(VBNullType.TypeInfo, GlobalSymbols.Null) { }

    public nint Value { get; } = nint.Zero;
    public override int Size => 0;

    // * UDT or resizable array -> VBR00013 TypeMismatch
    public bool Equals(IVBTypedValue<VBNullValue, nint>? other) => throw VBRuntimeErrorException.InvalidUseOfNull(Symbol?.SelectionRange!);
    public override int GetHashCode() => Value.GetHashCode();

    public VBDoubleValue? AsCoercedDouble(ref int depth)
    {
        // MS-VBAL 5.5.1.2.10 let-coercion from 'Null':
        // * any type except fixed-size array or Variant -> VBR00094 InvalidUseOfNull
        ThrowWithSymbol(symbol => VBRuntimeErrorException.InvalidUseOfNull(symbol.SelectionRange!));

        return default;
    }

    public VBStringValue? AsCoercedString(ref int depth)
    {
        // MS-VBAL 5.5.1.2.10 let-coercion from 'Null':
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
