using System.Collections.Immutable;

namespace RDCore.SDK.Model.AST.Abstract;

/// <summary>
/// An AST node that can be evaluated or executed to produce a value or side effect.
/// </summary>
public interface IExecutableNode 
{
    /// <summary>
    /// The <c>string</c> <em>token</em> of the statement, e.g. <c>Open</c>, <c>Input</c>, <c>Print</c>, <c>Assert</c>, etc..
    /// </summary>
    string Token { get; }
    /// <summary>
    /// The <em>inputs</em> of the executable statement; expressions evaluated immediately before the call.
    /// </summary>
    ImmutableArray<BoundExpression> Inputs { get; }
}
