using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// Represents a <c>VBObject</c> value, i.e. an object reference.
/// </summary>
/// <see cref="VBNothingValue"/>
public record class VBObjectValue : VBTypedValue,
    IVBTypedValue<VBObjectValue, int>
{
    private static readonly Lazy<VBObjectValue> _nothing = new(() 
        => new VBNothingValue(GlobalSymbols.StaticSymbols.Nothing), LazyThreadSafetyMode.PublicationOnly);
    public static VBObjectValue Nothing => _nothing.Value;

    public VBObjectValue(Symbol symbol) : base(VBObjectType.TypeInfo, symbol) { }

    public int Value { get; init; }
    public override int Size => sizeof(int);

    public override object BoxedValue => Value;

    public bool IsNothing() => Value == Nothing.Value;
    /*
    public VBDoubleValue? AsCoercedDouble(ref int depth) => LetCoerce(ref depth) is INumericValue value ? value.AsDouble() : null;
    public VBStringValue? AsCoercedString(ref int depth) => LetCoerce(ref depth) is VBStringValue value ? value : null;
    public VBFixedStringValue? AsCoercedFixedLengthString(int length, ref int depth) => LetCoerce(ref depth) is VBStringValue value ? new VBFixedStringValue(value) : null;

    /// <summary>
    /// Implicit default member call coerces the object reference into an intrinsic value.
    /// </summary>
    /// <remarks>
    /// Let coercion is recursive: a class type's default member may be another class type with a default member.
    /// </remarks>
    public VBTypedValue LetCoerce(ref int depth)
    {
        if (depth >= 9) // TODO configure
        {
            var localDepth = depth;
            Debug.Assert(false); // we really shouldn't get to this point
            ThrowWithSymbol(symbol => VBRuntimeErrorException.OutOfStackSpace(symbol.SelectionRange!, $"Recursive `Let` coercion did not resolve a typed value, {localDepth} levels deep."));
        }

        if (IsNothing())
        {
            ThrowWithSymbol(symbol => VBRuntimeErrorException.ObjectVariableNotSet(symbol.SelectionRange!, $"Recursive `Let` coercion requires the object reference to be assigned so that the default member can be invoked."));
        }

        if (TypeInfo is VBClassType classType && classType.DefaultMember != null)
        {
            var symbol = classType.DefaultMember as Symbol;
            if (classType.DefaultMember is VBReturningMemberSymbol member)
            {
                if (member.ResolvedType is INumericCoercion coercibleNumeric)
                {
                    depth++;
                    var value = coercibleNumeric.AsCoercedDouble(ref depth);
                    if (symbol != null && value != null)
                    {
                        return new VBDoubleValue(symbol).WithValue(value.Value);
                    }
                }
                else if (member.ResolvedType is IStringCoercion coercibleString)
                {
                    depth++;
                    var value = coercibleString.AsCoercedString(ref depth);
                    if (symbol != null && value != null)
                    {
                        return value;
                    }
                }
            }
        }

        ThrowWithSymbol(symbol => VBRuntimeErrorException.ObjectDoesntSupportPropertyOrMethod(symbol.SelectionRange!, $"`Let` coercion requires an object type that defines a default member, but none was found."));

        Debug.Assert(false);
        return default!; // throw?
    }
    */
    public bool Equals(IVBTypedValue<VBObjectValue, int>? other) => Value.Equals(other?.Value);
    public override int GetHashCode() => Value.GetHashCode();
}
