using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Types.Abstract
{
    /// <summary>
    /// Represents any <strong>numeric</strong> data type mentioned in the <strong>MS-VBAL</strong> language specifications.
    /// </summary>
    /// <typeparam name="T">The managed type (internal representation) associated with this data type.</typeparam>
    /// <param name="Name">The name (token) of the data type.</param>
    public abstract record class VBNumericType<T>(string Name) : VBNumericType(Name, typeof(T)), INumericType { }

    /// <summary>
    /// Represents any <strong>numeric</strong> data type mentioned in the <strong>MS-VBAL</strong> language specifications.
    /// </summary>
    /// <typeparam name="T">The managed type (internal representation) associated with this data type.</typeparam>
    /// <param name="Name">The name (token) of the data type.</param>
    public abstract record class VBNumericType(string Name, Type ManagedType) : VBIntrinsicType(Name, ManagedType), INumericType 
    {
        /// <summary>
        /// Gets the minimum representable managed (.net) value for this data type.
        /// </summary>
        public abstract double ManagedMinValue { get; }
        /// <summary>
        /// Gets the maximum representable managed (.net) value for this data type.
        /// </summary>
        public abstract double ManagedMaxValue { get; }

        /// <summary>
        /// Implements <strong>MS-VBAL 5.5.1.2.1.1</strong> Banker's Rounding.
        /// A midpoint rounding scheme also known as "round-to-even" rounds to the nearest rounded value such
        /// that the least-significant digit is even.
        /// </summary>
        /// <param name="value">The floating-point numeric value to be rounded.</param>
        public static int BankersRounding(double value) => (int)BankersRounding(value, 1);
        /// <summary>
        /// Implements <strong>MS-VBAL 5.5.1.2.1.1</strong> Banker's Rounding.
        /// A midpoint rounding scheme also known as "round-to-even" rounds to the nearest rounded value such
        /// that the least-significant digit is even.
        /// </summary>
        /// <param name="value">The floating-point numeric value to be rounded.</param>
        public static int BankersRounding(VBNumericTypedValue value) => (int)BankersRounding(value.ManagedValue, 1);
        /// <summary>
        /// Implements <strong>MS-VBAL 5.5.1.2.1.1</strong> Banker's Rounding.
        /// A midpoint rounding scheme also known as "round-to-even" rounds to the nearest rounded value such
        /// that the least-significant digit is even.
        /// </summary>
        /// <param name="value">The floating-point numeric value to be rounded.</param>
        public static double BankersRounding(double value, int digits) => Math.Round(value, digits);

        /// <summary>
        /// A helper function to test if a given source numeric value is within the range of a destination data type.
        /// </summary>
        /// <param name="source">The managed (.net) numeric source value to validate.</param>
        /// <param name="destination">The data type to check conversion eligibility against.</param>
        /// <returns><c>true</c> if the value can legally be used with the destination type, <c>false otherwise</c>.</returns>
        /// <remarks>If this function returns <c>false</c>, the caller should throw a <c>VBRuntimeErrorOverflowException.</c></remarks>
        public static bool IsWithinRange(double source, VBNumericType destination) =>
            source >= destination.ManagedMinValue && source <= destination.ManagedMaxValue;
    }
}
