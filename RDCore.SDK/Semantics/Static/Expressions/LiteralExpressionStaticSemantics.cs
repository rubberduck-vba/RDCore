using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Runtime;
using RDCore.SDK.Semantics.Static.Abstract;
using System.ComponentModel.Design;

namespace RDCore.SDK.Semantics.Static.Expressions
{
    public record class LiteralExpressionStaticSemantics : IStaticSemantics
    {
        private static readonly Lazy<LiteralExpressionStaticSemantics> _instance = new (() => new(), LazyThreadSafetyMode.PublicationOnly);
        public static LiteralExpressionStaticSemantics Instance => _instance.Value;

        public VBType? DetermineDeclaredType(IVBExecutionContext context, params VBType[] operandDeclaredTypes) => operandDeclaredTypes[0];
    }
}
