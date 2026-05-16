using RDCore.Semantics.Runtime.Abstract;
using RDCore.Semantics.Static.Abstract;

namespace RDCore.Parsing.Model;

internal interface ISemanticNode
{
    StaticSemantics StaticSemantics { get; }
    RuntimeSemantics RuntimeSemantics { get; }
}
