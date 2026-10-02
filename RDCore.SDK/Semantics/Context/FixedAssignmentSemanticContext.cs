using RDCore.SDK.Semantics.Context.Abstract;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.SDK.Semantics.Context;

/// <summary>
/// Encapsulates the semantic context of an <c>LSet</c> or <c>RSet</c> statement (<strong>MS-VBAL §5.4.3.6-7</strong>) operation.
/// </summary>
public sealed record class FixedAssignmentSemanticContext : SemanticContext<FixedAssignmentSemanticFlags> { }
