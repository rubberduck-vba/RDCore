using RDCore.SDK.Runtime.Abstract;

namespace RDCore.SDK.Semantics.Runtime.Operators.Context
{
    /// <summary>
    /// Encapsulates the semantic context of a <em>data type conversion</em> operation.
    /// </summary>
    public sealed record class ConversionOperationSemanticContext : SemanticContext<ConversionSemanticFlags> { }
}
