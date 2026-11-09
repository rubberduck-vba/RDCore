using RDCore.SDK.Model.Types.Abstract;
using RDCore.Runtime.LanguageCore.Static.Abstract;

namespace RDCore.Tests.Semantics.Abstract;

public abstract class UnaryOperatorStaticSemanticsTests : StaticSemanticsTests
{
    protected abstract StaticSemantics Semantics { get; }

    protected void AssertDeterminedDeclaredType(VBType operandDeclaredType, VBType expected) 
        => AssertDeterminedDeclaredType(Semantics, [operandDeclaredType], expected);
}

