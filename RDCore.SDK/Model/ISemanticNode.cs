using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Model;

public interface ISemanticNode
{
    /// <summary>
    /// The static semantics associated with this node.
    /// </summary>
    StaticSemantics StaticSemantics { get; }
    /// <summary>
    /// The runtime semantics associated with this node.
    /// </summary>
    RuntimeSemantics RuntimeSemantics { get; }
}
