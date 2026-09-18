using RDCore.SDK.Semantics.Context.Abstract;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.SDK.Semantics.Context;

/// <summary>
/// Encapsulates the semantic context of a <c>With</c> statement (<strong>MS-VBAL 5.4.2.21</strong>) operation.
/// </summary>
public sealed record class WithStatementSemanticContext : SemanticContext<WithStatementSemanticFlags> { }
