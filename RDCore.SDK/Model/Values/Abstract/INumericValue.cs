using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Model.Values.Abstract
{
    /// <summary>
    /// Represents any numeric value with a non-generic interface.
    /// </summary>
    public interface INumericValue
    {
        /// <summary>
        /// Gets the managed (.net) internal representation the numeric value. This is always a <c>double</c>.
        /// </summary>
        double ManagedValue { get; }

        /// <summary>
        /// Gets a copy of this value, with the specified underlying value.
        /// </summary>
        /// <remarks>
        /// Throws an <c>Overflow</c> run-time error (exception) if the value is outside the bounds representable by the <c>VBType</c>.
        /// </remarks>
        /// <exception cref="VBRuntimeErrorException"></exception>
        /// <param name="value">The underlying value of the numeric value to be produced.</param>
        INumericValue WithValue(double value);
    }

    /// <summary>
    /// Represents any numeric value with a generic interface mapping it to a specific <c>VBType</c>.
    /// </summary>
    /// <typeparam name="VBTValue"></typeparam>
    public interface INumericValue<VBTValue> : INumericValue
        where VBTValue : VBTypedValue
    {
        /// <summary>
        /// The numeric <c>VBType</c> of this value.
        /// </summary>
        VBType TypeInfo { get; }
    }

    [Obsolete("This interface should be removed once let-coercion semantics are encapsulated in their own class.")]
    public interface INumericCoercion
    {
        [Obsolete("TODO: refactor to use the centralized let-coercion semantics instead.")]
        VBDoubleValue? AsCoercedDouble(ref int depth);
    }

    [Obsolete("This interface should be removed once let-coercion semantics are encapsulated in their own class.")]
    public interface IStringCoercion
    {
        [Obsolete("TODO: refactor to use the centralized let-coercion semantics instead.")]
        VBStringValue? AsCoercedString(ref int depth);
        [Obsolete("TODO: refactor to use the centralized let-coercion semantics instead.")]
        VBFixedStringValue? AsCoercedFixedLengthString(int length, ref int depth);
    }

    [Obsolete("This interface should be removed once let-coercion semantics are encapsulated in their own class.")]
    public interface IBooleanCoercion
    {
        [Obsolete("TODO: refactor to use the centralized let-coercion semantics instead.")]
        VBBooleanValue AsCoercedBoolean(ref int depth);
    }

    [Obsolete("This interface should be removed once let-coercion semantics are encapsulated in their own class.")]
    public interface IDateCoercion
    {
        [Obsolete("TODO: refactor to use the centralized let-coercion semantics instead.")]
        VBDateValue AsCoercedDate(ref int depth);
    }
}
