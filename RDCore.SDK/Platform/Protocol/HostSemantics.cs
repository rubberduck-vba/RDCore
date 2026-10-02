using MediatR;
using OmniSharp.Extensions.JsonRpc;
using RDCore.SDK.Client;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Flags;
using System.Collections.Immutable;

namespace RDCore.SDK.Platform.Protocol;

/// <summary>
/// Request for <c>rdcore/host/semantics</c>: the language server asks the environment host what the static pass found out about the code it holds.
/// </summary>
/// <remarks>
/// The environment host runs the semantic analysis pass (<strong>RD-VBAL §5.0.3</strong>) when it loads the code of a module, and keeps the model of
/// each; this is the language-server side of asking for them, never sent by a client. The answer is the semantic facts, which a diagnostics extension
/// analyzes: the host says what is, and an analyzer decides what is worth saying. A module the host does not have, or has not loaded, has no model.
/// </remarks>
[Method(RDCorePlatformProtocol.HostSemantics, Direction.ClientToServer)]
public record class HostSemanticsParams : IRequest, IRequest<HostSemanticsResult>
{
    /// <summary>
    /// The name of the module whose model is asked for, or empty for the model of every module of the workspace.
    /// </summary>
    public string ModuleName { get; init; } = string.Empty;
}

/// <summary>
/// The models the host answers with.
/// </summary>
/// <remarks>
/// The models ride a <see cref="System.Text.Json"/> string (<see cref="PlatformJson"/>): the transport's own serializer does not round-trip what they hold.
/// </remarks>
public record class HostSemanticsResult
{
    /// <summary>
    /// The <see cref="System.Text.Json"/> representation of a <see cref="SemanticsPayload"/>.
    /// </summary>
    public string Json { get; init; } = string.Empty;
}

/// <summary>
/// The semantic facts of one or more modules, as they travel (<see cref="HostSemanticsResult.Json"/>).
/// </summary>
/// <param name="Modules">The model of each module asked for that the host has one of.</param>
public record class SemanticsPayload(ImmutableArray<ModuleSemanticsDto> Modules)
{
    /// <summary>
    /// The model of the module at <paramref name="module"/>, if there is one.
    /// </summary>
    /// <param name="module">The address of the module.</param>
    public ModuleSemanticsDto? Of(Uri module)
        => Modules.IsDefault ? null : Modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == module.AbsoluteUri);
}

/// <summary>
/// A compile error, as it travels: what a diagnostics extension needs to say it.
/// </summary>
/// <param name="Id">The error.</param>
/// <param name="Location">Where it is.</param>
/// <param name="Verbose">What is wrong, in particular.</param>
public record class CompileErrorDto(VBCompileErrorId Id, SourceLocation Location, string Verbose)
{
    /// <summary>
    /// The compile error.
    /// </summary>
    public VBCompileErrorInfo ToInfo() => VBCompileErrorInfo.For(Id, Location, Verbose);
}

/// <summary>
/// An <see cref="ExpressionFact"/>, as it travels.
/// </summary>
/// <param name="Node">The expression.</param>
/// <param name="DeclaredType">The name of its declared type, or <see langword="null"/> when it is an error.</param>
/// <param name="Classification">What it names.</param>
/// <param name="Binding">The address of the symbol it refers to, when it resolved to one.</param>
/// <param name="Flags">What else is the case of it.</param>
/// <param name="Error">Its compile error, when it has one.</param>
public record class ExpressionFactDto(
    SyntaxNodeId Node,
    string? DeclaredType,
    ExpressionClassification Classification,
    Uri? Binding,
    ValueExpressionSemanticFlags Flags,
    CompileErrorDto? Error);

/// <summary>
/// A <see cref="ProcedureSemanticModel"/>, as it travels.
/// </summary>
/// <param name="Procedure">The address of the procedure.</param>
/// <param name="IsFullyAnalyzed">Whether the references the facts say there are in its body are all of them.</param>
/// <param name="CompileErrors">What the static pass found wrong with the body.</param>
/// <param name="Expressions">What it found out about each expression.</param>
public record class ProcedureSemanticsDto(
    Uri Procedure, bool IsFullyAnalyzed, ImmutableArray<CompileErrorDto> CompileErrors, ImmutableArray<ExpressionFactDto> Expressions);

/// <summary>
/// A <see cref="DeclarationFact"/>, as it travels.
/// </summary>
/// <param name="Symbol">The address of the declared symbol.</param>
/// <param name="Name">The name it is declared with.</param>
/// <param name="Kind">What it declares.</param>
/// <param name="Access">Who can refer to it besides the module's own code.</param>
/// <param name="IsImplicit">Whether it was never declared.</param>
/// <param name="Location">Where it is declared.</param>
/// <param name="References">Every reference to it, when they are known; <see langword="null"/> says they are not.</param>
public record class DeclarationFactDto(
    Uri Symbol, string Name, DeclarationKind Kind, AccessModifier Access, bool IsImplicit, SourceLocation Location, DeclarationReferences? References);

/// <summary>
/// A <see cref="ModuleSemanticModel"/>, as it travels.
/// </summary>
/// <param name="Module">The address of the module.</param>
/// <param name="OptionExplicit">Whether the module states <c>Option Explicit</c>.</param>
/// <param name="DeclarationErrors">What is wrong with what the module declares.</param>
/// <param name="Procedures">The model of each procedure of the module.</param>
/// <param name="Declarations">What is known of how the module's declarations are used.</param>
public record class ModuleSemanticsDto(
    Uri Module,
    bool OptionExplicit,
    ImmutableArray<CompileErrorDto> DeclarationErrors,
    ImmutableArray<ProcedureSemanticsDto> Procedures,
    ImmutableArray<DeclarationFactDto> Declarations)
{
    /// <summary>
    /// Describes a model for the wire.
    /// </summary>
    /// <param name="model">The model of a module.</param>
    public static ModuleSemanticsDto From(ModuleSemanticModel model) => new(
        model.Module,
        model.OptionExplicit,
        [.. model.DeclarationErrors.Select(ErrorOf)],
        [.. model.Procedures.Select(procedure => new ProcedureSemanticsDto(
            procedure.Procedure.Uri,
            procedure.IsFullyAnalyzed,
            [.. procedure.CompileErrors.Select(ErrorOf)],
            [.. procedure.Expressions.Values.Select(fact => new ExpressionFactDto(
                fact.Node, fact.DeclaredType?.Name, fact.Classification, fact.Binding?.Uri, fact.Flags, fact.Error is null ? null : ErrorOf(fact.Error)))]))],
        [.. model.Declarations.Select(declaration => new DeclarationFactDto(
            declaration.Symbol.Uri, declaration.Name, declaration.Kind, declaration.Access, declaration.IsImplicit, declaration.Location, declaration.References))]);

    private static CompileErrorDto ErrorOf(VBCompileErrorInfo error) => new(error.VBCompileErrorId, error.Location, error.Verbose);
}
