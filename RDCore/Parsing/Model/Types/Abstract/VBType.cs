using RDCore.Parsing.Model.Symbols.Abstract;
using RDCore.Parsing.Model.Types.Intrinsic;
using RDCore.Parsing.Model.Values.Abstract;
using RDCore.Parsing.Model.Values.Intrinsic;

namespace RDCore.Parsing.Model.Types.Abstract;

/// <summary>
/// A base abstract class representing any VB data type.
/// </summary>
internal abstract record class VBType
{
    private static readonly Dictionary<Type, Func<Symbol, VBTypedValue>> _valueFactories = new()
    {
        [typeof(VBIntegerType)] = symbol => new VBIntegerValue(symbol),
        [typeof(VBLongType)] = symbol => new VBLongValue(symbol),
        [typeof(VBLongLongType)] = symbol => new VBLongLongValue(symbol),
        [typeof(VBDoubleType)] = symbol => new VBDoubleValue(symbol),
        [typeof(VBStringType)] = symbol => new VBStringValue(symbol),
        [typeof(VBBooleanType)] = symbol => new VBBooleanValue(symbol),
        [typeof(VBDateType)] = symbol => new VBDateValue(symbol),
        [typeof(VBVariantType)] = symbol => new VBVariantValue(VBEmptyValue.Empty, symbol),
        [typeof(VBNullType)] = symbol => new VBNullValue(),
        [typeof(VBEmptyType)] = symbol => new VBEmptyValue(),
        [typeof(VBObjectType)] = symbol => new VBObjectValue(symbol)
    };

    internal VBTypedValue CreateValue(Symbol declarationSymbol)
    {
        if (_valueFactories.TryGetValue(GetType(), out var factory))
        {
            return factory(declarationSymbol);
        }

        throw new InvalidOperationException($"No value factory registered for type {GetType().Name}");
    }

    internal INumericValue CreateNumericValue(Symbol declarationSymbol)
    {
        if (_valueFactories.TryGetValue(GetType(), out var factory))
        {
            return (INumericValue)factory(declarationSymbol);
        }

        throw new InvalidOperationException($"No value factory registered for type {GetType().Name}");
    }

    protected VBType(Type? managedType, string name, bool isUserDefined = false, bool isHidden = false)
    {
        ManagedType = managedType;
        Name = name;
        IsUserDefined = isUserDefined;
        IsHidden = isHidden;
    }

    /// <summary>
    /// The underlying managed (.net) type that represents this VB type, if any.
    /// </summary>
    internal Type? ManagedType { get; init; }

    /// <summary>
    /// The symbolic name of the type, as it is used in code.
    /// </summary>
    /// <remarks>
    /// For user module types, this should be determined by a <c>VB_Name</c> attribute.
    /// </remarks>
    public string Name { get; init; }

    /// <summary>
    /// Whether this type is defined by user code.
    /// </summary>
    public bool IsUserDefined { get; init; }

    /// <summary>
    /// Only <c>true</c> for types that are hidden from the user in IntelliSense.
    /// </summary>
    public bool IsHidden { get; init; }

    /// <summary>
    /// If <c>true</c>, the type is bound using run-time semantics (i.e. late binding).
    /// </summary>
    /// <remarks>
    /// <c>false</c> unless overridden in a more specialized type.
    /// </remarks>
    public virtual bool RuntimeBinding { get; } = false;

    /// <summary>
    /// Gets the default value for this data type.
    /// </summary>
    /// <remarks>
    /// <strong>IMPORTANT:</strong> derived types MUST initialize this property with a <c>Lazy&lt;T&gt;</c> to avoid static context timing issues.
    /// </remarks>
    public abstract VBTypedValue DefaultValue { get; }


    /// <summary>
    /// Whether this type can be passed by value.
    /// </summary>
    public virtual bool CanPassByValue { get; } = true;

    /// <summary>
    /// Override in derived types to specify VBTypes that are safe to convert this type into.
    /// </summary>
    public virtual VBType[] ConvertsSafelyToTypes { get; } = [];
}
