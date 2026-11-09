using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Errors;
using System.Collections.Immutable;
using RDCore.SDK.Semantics.Instructions;
using RDCore.Runtime.LanguageCore.Errors;

namespace RDCore.Runtime.LanguageCore.Instructions;

// always built, errors or not: whether to run a body with errors is the executor's decision, not lowering's.
public sealed record class InstructionListLoweringResult(InstructionList InstructionList, ImmutableArray<VBCompileErrorInfo> Errors);
