using RDCore.Parsing.Model.Types.Abstract;

namespace RDCore.Semantics.Static.Abstract;

/// <summary>
/// A type hierarchy to compose the semantic layer and cleanly separate static and runtime semantics, to clean up <c>SymbolOperation</c>.
/// </summary>
internal abstract record class StaticSemantics
{
    public abstract VBType? DetermineDeclaredType(params VBType[] operandDeclaredTypes);
}

internal abstract record class ArithmeticOperatorStaticSemantics : StaticSemantics { }
