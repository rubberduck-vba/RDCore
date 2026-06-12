using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Values.Meta
{
    /// <summary>
    /// A meta-value that represents a <see cref="VBParameterSymbol"/>.
    /// </summary>
    public record class VBParameterDescValue(Symbol Symbol, VBParameterSymbol Parameter) 
        : VBTypedValue(Parameter.ResolvedType, Symbol)
    {
        public override int Size => sizeof(int);

        public override object BoxedValue => 0;
    }
}