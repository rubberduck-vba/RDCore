using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Types.Abstract;

/// <summary>
/// A base abstract class representing any VB data type.
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
    /// Only <c>true</c> for types that are hidden from the user in IntelliSense.
    /// </summary>
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
