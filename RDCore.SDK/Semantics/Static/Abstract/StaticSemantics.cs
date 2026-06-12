using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Runtime;

namespace RDCore.SDK.Semantics.Static.Abstract
{
    /// <summary>
    /// Represents any static semantics rules.
    /// </summary>
    public interface IStaticSemantics
    {
        /// <summary>
        /// Determines a static <c>VBType</c> from specified operands.
        /// </summary>
        /// <param name="context">The static context containing the available static memory space.</param>
        /// <param name="operandDeclaredTypes">The declared type of each operand involved in the evaluation.</param>
        VBType? DetermineDeclaredType(IVBExecutionContext context, params VBType[] operandDeclaredTypes);
    }

    /// <summary>
    /// The class at the base of the static semantics type hierarchy that implements all the static semantic rules defined in MS-VBAL.
    /// </summary>
    public abstract record class StaticSemantics() : IStaticSemantics
    {
        /// <summary>
        /// Determines a static <c>VBType</c> from specified operands.
        /// </summary>
        /// <param name="context">The static context containing the available static memory space.</param>
        /// <param name="operandDeclaredTypes">The declared type of each operand involved in the evaluation.</param>
        public abstract VBType? DetermineDeclaredType(IVBExecutionContext context, params VBType[] operandDeclaredTypes);
    }
}
