using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.AST.Abstract;

namespace RDCore.SDK.Model.AST.Expressions;

/// <summary>
/// <strong>MS-VBAL §3.3.5.3 Special Identifier Forms</strong>, <strong>§5.6.16.7 Type Expressions</strong><br/>
/// An <see cref="ExpressionNode"/> that is the name of an intrinsic type, written where an expression is: <c>Long</c> in <c>TypeOf o Is Long</c>, or in
/// <c>x = Long</c>.
/// </summary>
/// <remarks>
/// A <c>BUILTIN-TYPE</c> is a <c>&lt;type-expression&gt;</c>, and no value: MS-VBA refuses one wherever it is written in an expression, after <c>TypeOf ... Is</c>
/// too (a syntax error, Excel-verified). The node keeps what was written so that the error can be reported where it is, rather than the name being dropped
/// and the expression read as if it were not there.
/// <para>
/// 👉 <c>Object</c> is no <c>reserved-type-identifier</c>: in an expression it is read as a name, as MS-VBA reads it, and is a
/// <see cref="SimpleNameExpressionNode"/>. <c>Date</c> is a name too, the library's <c>Date</c> property.
/// </para>
/// </remarks>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="Location">The document location (<c>Uri</c>+<c>Range</c>) of the expression.</param>
/// <param name="TypeName">The name of the intrinsic type, without the brackets it may be written in.</param>
/// <param name="IsBracketed"><see langword="true"/> when the name is written in square brackets (<c>[Long]</c>).</param>
public sealed record class BuiltInTypeExpressionNode(SyntaxNodeId Identity, SourceLocation Location, string TypeName, bool IsBracketed = false)
    : ExpressionNode(Identity, Location, []);
