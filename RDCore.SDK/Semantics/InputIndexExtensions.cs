using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Semantics;

public static class InputIndexExtensions
{
    /// <summary>
    /// Adds a method to <see cref="VBTypedValue"/> arrays to extract a value using an <see cref="InputIndex"/> constant.
    /// </summary>
    /// <param name="inputs">An array of <see cref="VBTypedValue"/> inputs.</param>
    extension(VBTypedValue[] inputs)
    {
        /// <summary>
        /// Gets the input at the specified strongly-typed index.
        /// </summary>
        /// <param name="index">The <see cref="InputIndex"/> to retrieve.</param>
        /// <returns>The <see cref="VBTypedValue"/> at the specified index.</returns>
        public VBTypedValue GetOperandValue(InputIndex index) => inputs[(int)index];
    }

    /// <summary>
    /// Adds a method to <see cref="VBType"/> arrays to extract the data type using an <see cref="InputIndex"/> constant.
    /// </summary>
    /// <param name="inputs">An array of <see cref="VBType"/> input types.</param>
    extension(VBType[] inputs)
    {
        /// <summary>
        /// Gets the input type at the specified strongly-typed index.
        /// </summary>
        /// <param name="index">The <see cref="InputIndex"/> to retrieve.</param>
        /// <returns>The <see cref="VBType"/> at the specified index.</returns>
        public VBType GetOperandType(InputIndex index) => inputs[(int)index];
    }
}