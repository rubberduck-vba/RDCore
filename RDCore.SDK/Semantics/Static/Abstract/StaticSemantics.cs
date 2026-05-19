using RDCore.SDK.Model.Types.Abstract;

namespace RDCore.SDK.Semantics.Static.Abstract;

/// <summary>
/// The class at the base of the static semantics type hierarchy that implements all the static semantic rules defined in MS-VBAL.
/// </summary>
public abstract record class StaticSemantics
{
    /// <summary>
    /// Determines a static <c>VBType</c> from specified operands.
    /// </summary>
    /// <param name="operandDeclaredTypes">The declared type of each operand involved in the evaluation.</param>
    public abstract VBType? DetermineDeclaredType(params VBType[] operandDeclaredTypes);
}
