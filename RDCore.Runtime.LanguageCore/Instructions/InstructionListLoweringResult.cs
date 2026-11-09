using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Errors;
using System.Collections.Immutable;
using RDCore.SDK.Semantics.Instructions;

namespace RDCore.Runtime.LanguageCore.Instructions;

public sealed record class InstructionListLoweringResult(InstructionList InstructionList, ImmutableArray<VBCompileErrorInfo> Errors);
