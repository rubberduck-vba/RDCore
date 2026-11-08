using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Errors;
using System.Collections.Immutable;
using RDCore.SDK.Semantics.Instructions;

namespace RDCore.Runtime.LanguageCore.Instructions;

/// <summary>
/// The result of <see cref="InstructionListLowering.Lower"/>: the <see cref="InstructionList"/>
/// lowering produced, and every compile error lowering found while producing it.
/// </summary>
/// <param name="InstructionList">
/// Always built, even when <see cref="Errors"/> is non-empty: every <see cref="StatementNode"/> in the
/// body still gets exactly one <see cref="Instruction"/>, and an <see cref="InstructionKind.Jump"/>/
/// <see cref="InstructionKind.JumpTable"/> operand that did not resolve simply carries a <c>null</c>
/// target rather than being left out. Whether to refuse to run a body with a non-empty
/// <see cref="Errors"/> is a decision for whatever executes it, not for lowering.
/// </param>
/// <param name="Errors">
/// Every <see cref="VBCompileErrorId.LabelNotDefined"/> and
/// <see cref="VBCompileErrorId.DuplicateLabelDefinition"/> lowering found. Empty when every label in the
/// body is valid.
/// </param>
public sealed record class InstructionListLoweringResult(InstructionList InstructionList, ImmutableArray<VBCompileErrorInfo> Errors);
