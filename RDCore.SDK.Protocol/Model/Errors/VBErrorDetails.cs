using RDCore.SDK.Model.Source;

namespace RDCore.SDK.Model.Errors;

/// <summary>
/// What the runtime was doing when it raised an error, recorded as data at the moment the error was raised: the expression it was evaluating, and the frames
/// of the operation that failed, innermost first.
/// </summary>
/// <remarks>
/// <para>
/// The runtime records these and formats none of them. Each client formats them its own way: a console client as text under the error, an IDE as the
/// details of the exception a debugger reports. A client that formats nothing shows <see cref="Message"/>, which is also the error's plain-text verbose
/// message.
/// </para>
/// <para>
/// They are recorded only when an error is raised, never on the path of an evaluation that succeeds.
/// </para>
/// </remarks>
/// <param name="Message">What the runtime says of the error, beyond its description: why the operation failed, in a sentence.</param>
/// <param name="Expression">The expression the error was raised at, or <see langword="null"/> when the error was raised at none.</param>
/// <param name="Frames">The frames of the operation the error was raised in, innermost first. Empty when the operation has none.</param>
public sealed record class VBErrorDetails(string Message, VBErrorExpression? Expression, IReadOnlyList<VBErrorFrame> Frames);

/// <summary>
/// The expression an error was raised at.
/// </summary>
/// <remarks>
/// The client has the source text, and <see cref="Location"/> says where the expression is in it.
/// </remarks>
/// <param name="Kind">What kind of expression it is: the name of the syntax node's type, such as <c>VBBinaryOperatorExpressionNode</c>.</param>
/// <param name="SemanticId">The identity of the expression's syntax node, as text.</param>
/// <param name="Location">Where the expression is.</param>
public sealed record class VBErrorExpression(string Kind, string SemanticId, SourceLocation Location);

/// <summary>
/// One frame of the operation an error was raised in: the syntax node it was evaluating, and what it was given.
/// </summary>
/// <param name="NodeId">The identity of the syntax node the frame evaluates, as text.</param>
/// <param name="Inputs">What the operation was given, in order.</param>
/// <param name="Coercion">The let-coercion the frame is, when it is one; <see langword="null"/> for any other operation.</param>
public sealed record class VBErrorFrame(string NodeId, IReadOnlyList<VBErrorOperand> Inputs, VBErrorCoercion? Coercion = null);

/// <summary>
/// One input of an operation: a value, or a type the operation is given.
/// </summary>
/// <param name="TypeName">The name of the value's type, such as <c>Long</c>.</param>
/// <param name="Value">The value as text, or <see langword="null"/> when the input is a type and holds no value.</param>
public sealed record class VBErrorOperand(string TypeName, string? Value);

/// <summary>
/// A let-coercion step: the value that was being let-coerced, and the type it was being let-coerced to.
/// </summary>
/// <param name="Source">The value being let-coerced.</param>
/// <param name="DestinationTypeName">The name of the type it was being let-coerced to, such as <c>Integer</c>.</param>
public sealed record class VBErrorCoercion(VBErrorOperand Source, string DestinationTypeName);
