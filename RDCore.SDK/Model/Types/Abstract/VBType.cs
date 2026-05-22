using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Types.Abstract;

/// <summary>
/// Defines the names of all <c>VBType</c> intrinsic data types.
/// </summary>
/// <remarks>
/// Because the <em>identifier name</em> of any associated <c>Symbol</c> is the name of the associated <c>VBType</c>, 
/// these names must be syntactically valid <strong>MS-VBAL 3.3.5 Identifier Tokens</strong>,
/// including hidden, internal, or reserved data types with or without any defined semantics.
/// <em>Names that are specification-defined grammatical tokens should refer to an existing <c>Tokens</c> constant.</em>
/// </remarks>
public static class VBTypeNames
{
    public const string VBAny = "Any";
    public const string VBArray = "Array";
    public const string VBBoolean = Tokens.Boolean;
    public const string VBByte = Tokens.Byte;
    public const string VBCurrency = Tokens.Currency;
    public const string VBDate = Tokens.Date;
    public const string VBDecimal = Tokens.Decimal;
    public const string VBDouble = Tokens.Double;
    public const string VBEmpty = Tokens.Empty;
    public const string VBInteger = Tokens.Integer;
    public const string VBLong = Tokens.Long;
    public const string VBLongLong = Tokens.LongLong;
    public const string VBLongPtr = Tokens.LongPtr;
    public const string VBMissing = "Missing";
    public const string VBNull = Tokens.Null;
    public const string VBObject = Tokens.Object;
    public const string VBSingle = Tokens.Single;
    public const string VBString = Tokens.String;
    public const string VBUnknown = "Unknown";
    public const string VBVariant = Tokens.Variant;
    public const string VBVoid = "Void";
}

/// <summary>
/// A base abstract class representing any representable data type.
/// </summary>
public abstract record class VBType
{
    protected VBType(Type? managedType, string name, bool isHidden = false)
    {
        ManagedType = managedType;
        Name = name;
        IsHidden = isHidden;
    }

    /// <summary>
    /// The underlying managed (.net) type that represents this VB type, if any.
    /// </summary>
    public Type? ManagedType { get; init; }

    /// <summary>
    /// The symbolic name of the type, as it is used in code.
    /// </summary>
    /// <remarks>
    /// For module types, this value is determined by a <c>VB_Name</c> attribute.
    /// </remarks>
    public string Name { get; init; }

    /// <summary>
    /// <c>true</c> for any implementation that has no specified semantics (internal types).
    /// </summary>
    /// <remarks>
    /// These types are semantically hidden from user code, but still discoverable in the program memory space if they're allocated.
    /// </remarks>
    public bool IsHidden { get; init; }

    /// <summary>
    /// Gets the default value for this data type.
    /// </summary>
    /// <remarks>
    /// ⚠️ <strong>Derived types must</strong> back the implementation of this property with a thread-safe <c>private static readonly Lazy&lt;T&gt;</c>. 
    /// Failure to do so would lock up the static context initialization of the <c>StaticSymbol</c> symbols.
    /// </remarks>
    public abstract VBTypedValue DefaultValue { get; }

    /// <summary>
    /// The size (in bytes) of a value of this type.
    /// </summary>
    /// <remarks>
    /// Determines the length of the allocated memory space for a value of this type.
    /// </remarks>
    public abstract int Size { get; }
}
