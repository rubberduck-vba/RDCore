using RDCore.Parsing.Model.Types;

namespace RDCore.Parsing.Model.Expressions.Operators.StaticSemantics.Abstract;

/// <summary>
/// A type hierarchy to compose the semantic layer and cleanly separate static and runtime semantics, to clean up <c>SymbolOperation</c>.
/// </summary>
internal abstract record class StaticSemanticRules
{
    public abstract VBType? DetermineDeclaredType(params VBType[] operandDeclaredTypes);
}

internal abstract record class ArithmeticOperatorStaticSemantics : StaticSemanticRules
{
}
