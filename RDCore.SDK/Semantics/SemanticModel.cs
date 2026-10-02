using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics;

/// <summary>
/// What the semantic analysis pass found out about one procedure: the facts a pass established about its code, which analyzers read to decide what
/// becomes a diagnostic.
/// </summary>
/// <remarks>
/// A model is immutable, and built by the pass that analyzed the procedure; it is never written back onto the syntax tree or onto a value. Today it
/// holds the compile errors of the static pass (<strong>RD-VBAL §5.0.1</strong>); the facts of the passes that follow (binding, types, flow) are
/// added to it as they come online, so that whoever consumes a model keeps consuming the same type.
/// </remarks>
/// <param name="Procedure">The identity of the procedure the model describes.</param>
/// <param name="CompileErrors">
/// Every compile error the static pass found in the procedure's body, in traversal order, followed by the errors that can only be told once the
/// whole body has been walked (a jump to a label that no line defines).
/// </param>
public sealed record class ProcedureSemanticModel(SemanticId Procedure, ImmutableArray<VBCompileErrorInfo> CompileErrors)
{
    /// <summary>
    /// Whether the static pass found nothing wrong with the procedure.
    /// </summary>
    public bool IsValid => CompileErrors.IsEmpty;
}

/// <summary>
/// What the semantic analysis pass found out about one module: what is wrong with what it declares, and the model of each procedure it declares.
/// </summary>
/// <remarks>
/// A module is valid when its declarations are, and every procedure is: a module whose procedures are all valid may still declare a name twice.
/// </remarks>
/// <param name="Module">The address of the module the model describes.</param>
/// <param name="DeclarationErrors">The compile errors of the module's own declarations (<see cref="Static.DeclarationStaticSemanticsEvaluator"/>), which no procedure body shows.</param>
/// <param name="Procedures">The model of each procedure the module declares, in declaration order.</param>
public sealed record class ModuleSemanticModel(
    Uri Module, ImmutableArray<VBCompileErrorInfo> DeclarationErrors, ImmutableArray<ProcedureSemanticModel> Procedures)
{
    /// <summary>
    /// Every compile error of the module: those of its declarations, then those of each procedure.
    /// </summary>
    public ImmutableArray<VBCompileErrorInfo> CompileErrors => [.. DeclarationErrors, .. Procedures.SelectMany(procedure => procedure.CompileErrors)];

    /// <summary>
    /// Whether the static pass found nothing wrong with the module's declarations, nor with any procedure of the module.
    /// </summary>
    public bool IsValid => DeclarationErrors.IsEmpty && Procedures.All(procedure => procedure.IsValid);
}
