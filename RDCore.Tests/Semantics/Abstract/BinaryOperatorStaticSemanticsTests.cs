using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.Tests.Semantics.Abstract;

public abstract class BinaryOperatorStaticSemanticsTests : StaticSemanticsTests
{
    protected abstract StaticSemantics Semantics { get; }

    protected void AssertDeterminedDeclaredType((VBType lhs, VBType rhs) operandDeclaredTypes, VBType expected) 
        => AssertDeterminedDeclaredType(Semantics, [operandDeclaredTypes.lhs, operandDeclaredTypes.rhs], expected);
}
