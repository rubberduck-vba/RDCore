using RDCore.Parsing.Model.Symbols;
using RDCore.Parsing.Model.Types;
using System.Globalization;

namespace RDCore.Parsing.Model.Values;

internal interface IVBTypedValue<VBTValue, TValue> : IEquatable<IVBTypedValue<VBTValue, TValue>>
    where VBTValue : VBTypedValue
{
    TValue Value { get; }
}

internal abstract record class VBRuntimeEntity(Symbol? Symbol)
{
    /// <summary>
    /// Throws the exception returned by the specified function if <c>Symbol</c> is defined.
    /// </summary>
    /// <remarks>
    /// Does strictly nothing otherwise.
    /// </remarks>
    protected void ThrowWithSymbol(Func<Symbol,VBRuntimeErrorException> getException)
    {
        if (Symbol is not null)
        {
            throw getException(Symbol);
        }
    }
}

internal abstract record class VBTypedValue : VBRuntimeEntity
{
    protected VBTypedValue(VBType type, Symbol? symbol = null) : base(symbol)
    {
        TypeInfo = type;
    }

    /// <summary>
    /// A static reference to the "en-US" <c>CultureInfo</c> instance that derived values should use to ensure correct string/numeric conversions.
    /// </summary>
    protected static CultureInfo CultureInfo { get; } = CultureInfo.GetCultureInfo("en-US");

    public bool IsArray() => this is VBArrayValue || TypeInfo is VBVariantType variant && variant.Subtype is VBArrayType;
    public bool IsObject() => this is VBObjectValue || TypeInfo is VBVariantType variant && variant.Subtype is VBObjectType;
    public bool IsNull() => this is VBNullValue || TypeInfo is VBVariantType variant && variant.Subtype is VBNullType;
    public bool IsEmpty() => this is VBEmptyValue || TypeInfo is VBVariantType variant && variant.Subtype is VBEmptyType;
    public bool IsMissing() => this is VBMissingValue || TypeInfo is VBVariantType variant && variant.Subtype is VBMissingType;
    public bool IsError() => this is VBErrorValue || TypeInfo is VBVariantType variant && variant.Subtype is VBErrorType;
    public bool IsNumeric() => this is INumericValue;

    public bool IsWithBlockVariable { get; init; }

    public VBVariantValue AsVariant() => new(this, Symbol);

    public VBType TypeInfo { get; init; }

    public long RawAddress { get; init; }
    public abstract int Size { get; }

    public abstract override string ToString();
}

