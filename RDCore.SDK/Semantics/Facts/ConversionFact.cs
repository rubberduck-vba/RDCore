using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Errors.Abstract;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.SDK.Semantics.Facts;

/// <summary>
/// A <em>let-coercion</em> (<strong>MS-VBAL 5.5.1.2</strong>) that the code asks for, as the language core states it.
/// </summary>
/// <remarks>
/// 👉 A fact is stated only when it is true of the code: anything that follows from a value an analysis only assumed
/// (<see cref="IsValueKnown"/> is <see langword="false"/>) is not stated, so <see cref="Error"/> is <see langword="null"/> then,
/// and <see cref="Flags"/> holds what the types alone say.
/// </remarks>
/// <param name="Node">The <c>Identity</c> of the syntax node the conversion is evaluated for.</param>
/// <param name="Location">Where in the document the conversion is.</param>
/// <param name="Site">The construct that asks for the conversion.</param>
/// <param name="Operand">The operand that is converted: its position in the operation that converts it.</param>
/// <param name="Source">The declared type of the value that is converted.</param>
/// <param name="Destination">The type it is converted to.</param>
/// <param name="Flags">What the conversion does, and what is known about its source (<c>Narrowing</c>, <c>Lossy</c>, <c>NullOperand</c>, ...).
/// Always one of <c>Implicit</c> or <c>Explicit</c>.</param>
/// <param name="IsValueKnown">Whether the value that is converted is known, as opposed to a value the type merely allows.</param>
/// <param name="Error">The run-time error the conversion raises. Only stated when the value is known, or when the types alone guarantee it.</param>
public sealed record class ConversionFact(
    SyntaxNodeId Node,
    SourceLocation Location,
    ConversionSite Site,
    InputIndex Operand,
    VBType Source,
    VBType Destination,
    ConversionSemanticFlags Flags,
    bool IsValueKnown,
    VBErrorInfo? Error);
