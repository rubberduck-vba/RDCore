namespace RDCore.SDK.Model.Types;

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
    public const string VBError = Tokens.Error;
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
