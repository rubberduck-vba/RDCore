using RDCore.SDK.Semantics.Context.Abstract;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.SDK.Semantics.Context;

/// <summary>
/// Encapsulates the semantic context of a <em>data type conversion</em> operation.
/// </summary>
public sealed record class ConversionOperationSemanticContext : SemanticContext<ConversionSemanticFlags> { }
