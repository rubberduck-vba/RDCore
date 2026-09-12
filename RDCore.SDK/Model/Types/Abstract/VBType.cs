using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using System.Text;
using System.Text.Json.Serialization;

namespace RDCore.SDK.Model.Types.Abstract;

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
    /// <remarks>
    /// 👉 A runtime convenience, not serialized: an AST or symbol payload that crosses a process
    /// boundary carries the type identity, and the receiving side resolves the singleton
    /// <c>VBType</c> (and hence this) from it. <see cref="System.Type"/> is not JSON-serializable.
    /// </remarks>
    [JsonIgnore]
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
    /// Creates a value of this type bound to <paramref name="handle"/>.
    /// </summary>
    /// <remarks>
    /// The base implementation throws: only types that can hold a runtime value override this
    /// (matches the intent of the removed <c>VBTypedValueFactory.CreateValue(VBType)</c> switch,
    /// which returned <c>null</c> for the rest).
    /// </remarks>
    public virtual VBTypedValue CreateValue(IBindingHandle handle)
        => throw new NotSupportedException($"A value of type '{Name}' cannot be constructed from a binding handle.");

    /// <summary>
    /// Prints this <c>VBType</c>'s members for <see cref="object.ToString"/>.
    /// </summary>
    /// <remarks>
    /// Deliberately omits <see cref="DefaultValue"/>: every <c>VBTypedValue</c> carries its own
    /// <c>TypeInfo</c> pointing back at the <c>VBType</c> that produced it, so printing
    /// <c>DefaultValue</c> here and letting the compiler-generated <c>ToString</c> take its usual
    /// course would recurse between the two <c>PrintMembers</c> implementations forever.
    /// </remarks>
    protected virtual bool PrintMembers(StringBuilder builder)
    {
        builder.Append("ManagedType = ").Append(ManagedType);
        builder.Append(", Name = ").Append(Name);
        builder.Append(", IsHidden = ").Append(IsHidden);
        return true;
    }
}
