using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Semantics;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace RDCore.SDK.Model.Values.Abstract;

/// <summary>
/// Represents any run-time typed value that can be represented with a managed (.net) value.
/// </summary>
/// <remarks>
/// Mandates an implementation of <c>IEquatable&lt;T&gt;</c> for the specified <c>VBTypedValue</c>
/// </remarks>
/// <typeparam name="VBTValue">The <c>VBType</c> type of the value.</typeparam>
/// <typeparam name="TValue">The underlying managed type of the value.</typeparam>
public interface IVBTypedValue<VBTValue, TValue> : IEquatable<IVBTypedValue<VBTValue, TValue>>
    where VBTValue : VBTypedValue
{
    /// <summary>
    /// Gets the underlying managed value corresponding to this typed value.
    /// </summary>
    /// <remarks>
    TValue Value { get; }
}

/// <summary>
/// Represents any typed value.
/// </summary>
/// <remarks>
/// This class is at the base of the type hierarchy for all typed values.
/// </remarks>
/// <param name="TypeInfo">The <c>VBType</c> of the value.</param>
/// <param name="Symbol">The <c>Symbol</c> associated with the value, if applicable.</param>
public abstract record class VBTypedValue(VBType TypeInfo, Symbol Symbol) : VBRuntimeEntity(TypeInfo, Symbol)
{
    private static readonly Lazy<CultureInfo> _cultureInfo = new(() => CultureInfo.GetCultureInfo("en-US"), LazyThreadSafetyMode.PublicationOnly);    
    /// <summary>
    /// A static and thread-safe reference to the "en-US" <c>CultureInfo</c> instance that derived values should use to ensure correct string/numeric conversions - this will almost certainly need to be revised to meet MS-VBAL.
    /// </summary>
    protected static CultureInfo CultureInfo => _cultureInfo.Value;

    /// <summary>
    /// <c>true</c> if this typed value is a <c>With</c> block variable.
    /// </summary>
    /// <remarks>
    /// A typed value serving as a <c>With</c> block variable should be a <c>VBObjectValue</c> or a <c>VBVariantValue</c> wrapping a <c>VBObjectValue</c>
    /// that refers to a non-null object pointer (<c>VBNothingValue</c>).
    /// </remarks>
    public bool IsWithBlockVariable { get; init; }

    /// <summary>
    /// The raw memory address of this typed value.
    /// </summary>
    public long RawAddress { get; init; }
    /// <summary>
    /// The allocated size (in bytes) of this value.
    /// </summary>
    public abstract int Size { get; }
}