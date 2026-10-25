using MediatR;
using OmniSharp.Extensions.JsonRpc;
using RDCore.SDK.Client;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Errors.Abstract;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Facts;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Semantics.Flow;
using RDCore.SDK.Semantics.Runtime.Operators;
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

    /// <summary>
    /// What the answer waits for. <see cref="AnalysisPhase.Static"/> is answered at once with whatever the host has: the facts that are ready when the module
    /// is loaded, and the ones that come later if they happen to be there. Anything that includes <see cref="AnalysisPhase.Runtime"/> is not answered until
    /// the host has evaluated the code of the modules asked for, or the request is cancelled.
    /// </summary>
    public AnalysisPhase Phase { get; init; } = AnalysisPhase.All;
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
/// <param name="Location">Where it is written.</param>
/// <param name="DeclaredType">The name of its declared type, or <see langword="null"/> when it is an error.</param>
/// <param name="Classification">What it names.</param>
/// <param name="Binding">The address of the symbol it refers to, when it resolved to one.</param>
/// <param name="Flags">What else is the case of it.</param>
/// <param name="Error">Its compile error, when it has one.</param>
/// <param name="Creates">The class of the object it creates by name, when that is known (<see cref="ExpressionFact.Creates"/>): for tooling only.</param>
public record class ExpressionFactDto(
    SyntaxNodeId Node,
    SourceLocation Location,
    string? DeclaredType,
    ExpressionClassification Classification,
    Uri? Binding,
    ValueExpressionSemanticFlags Flags,
    CompileErrorDto? Error,
    ObjectCreationFact? Creates = null);

/// <summary>
/// A run-time error that code is guaranteed to raise, as it travels.
/// </summary>
/// <param name="ErrorId">The number of the error.</param>
/// <param name="Location">Where it is raised.</param>
/// <param name="Description">What the error is.</param>
/// <param name="Verbose">What is wrong, in particular.</param>
/// <param name="IsApplicationError">
/// Whether the code raises the error for itself (<c>Error</c>, <c>Err.Raise</c>): a <see cref="VBApplicationErrorInfo"/>, rather than one the language
/// raises (<see cref="VBRuntimeErrorInfo"/>).
/// </param>
public record class RuntimeErrorDto(int ErrorId, SourceLocation Location, string Description, string Verbose, bool IsApplicationError = false)
{
    /// <summary>
    /// Describes an error for the wire.
    /// </summary>
    /// <param name="error">The error.</param>
    public static RuntimeErrorDto From(VBErrorInfo error)
        => new(error.ErrorId, error.Location, error.Description, error.Verbose, error is VBApplicationErrorInfo);

    /// <summary>
    /// The error, as the family it belongs to describes it.
    /// </summary>
    public VBErrorInfo ToInfo() => IsApplicationError
        ? VBApplicationErrorInfo.Raised(ErrorId, Location, Verbose, Description)
        : VBRuntimeErrorInfo.For((VBRuntimeErrorId)ErrorId, Location, Verbose);
}

/// <summary>
/// A <see cref="ConversionFact"/>, as it travels.
/// </summary>
/// <param name="Node">The syntax node the conversion is evaluated for.</param>
/// <param name="Location">Where the conversion is.</param>
/// <param name="Site">The construct that asks for the conversion.</param>
/// <param name="Source">The name of the declared type of the value that is converted.</param>
/// <param name="Destination">The name of the type it is converted to.</param>
/// <param name="Flags">What the conversion does, and what is known about its source.</param>
/// <param name="IsValueKnown">Whether the value that is converted is known, as opposed to a value its type merely allows.</param>
/// <param name="Error">The run-time error the conversion raises, when it is guaranteed to.</param>
/// <param name="Operand">The operand that is converted: its position in the operation that converts it.</param>
public record class ConversionFactDto(
    SyntaxNodeId Node,
    SourceLocation Location,
    ConversionSite Site,
    string Source,
    string Destination,
    ConversionSemanticFlags Flags,
    bool IsValueKnown,
    RuntimeErrorDto? Error,
    InputIndex Operand = default)
{
    /// <summary>
    /// Describes a conversion for the wire.
    /// </summary>
    /// <param name="fact">The conversion.</param>
    public static ConversionFactDto From(ConversionFact fact) => new(
        fact.Node,
        fact.Location,
        fact.Site,
        fact.Source.Name,
        fact.Destination.Name,
        fact.Flags,
        fact.IsValueKnown,
        fact.Error is null ? null : RuntimeErrorDto.From(fact.Error),
        fact.Operand);
}

/// <summary>
/// An <see cref="OperatorFact"/>, as it travels: an operation of an operator, or the access of a member (<see cref="MemberAccessFact"/>).
/// </summary>
/// <remarks>
/// 👉 One record carries every family: the flags of the family the operation belongs to are set, and those of every other family are <see langword="null"/>.
/// </remarks>
/// <param name="Node">The syntax node the operation is evaluated for.</param>
/// <param name="Location">Where the operation is.</param>
/// <param name="Operator">The operator: one of the <c>Tokens</c> operators, or <c>Tokens.MemberAccess</c> / <c>Tokens.DictionaryAccess</c> for a member access.</param>
/// <param name="EffectiveType">The name of the <em>effective type</em> of the operation, or of the declared type of the member that is accessed, when it has one.</param>
/// <param name="IsValueKnown">Whether every operand is known (the object, for a member access), as opposed to a value its type merely allows.</param>
/// <param name="ArithmeticOpSemanticFlags">What is known about an arithmetic operation (<see cref="ArithmeticOperatorFact"/>).</param>
/// <param name="ConcatOpSemanticFlags">What is known about a concatenation (<see cref="ConcatOperatorFact"/>).</param>
/// <param name="ComparisonOpSemanticFlags">What is known about a comparison (<see cref="ComparisonOperatorFact"/>).</param>
/// <param name="LogicalOpSemanticFlags">What is known about a logical operation (<see cref="LogicalOperatorFact"/>).</param>
/// <param name="MemberAccessOpSemanticFlags">What is known about a member access (<see cref="MemberAccessFact"/>).</param>
/// <param name="Error">The run-time error the operation raises, when it is guaranteed to.</param>
/// <param name="Comparison">How a comparison compares <c>String</c> values where it is evaluated (<c>Option Compare</c>); <see langword="null"/> for any other operation.</param>
/// <param name="ComparisonCulture">The name of the culture a <c>Text</c> comparison compares with, when it is not the invariant one.</param>
/// <param name="Member">The name of the member that is accessed, when it is known (<see cref="MemberAccessFact.Member"/>).</param>
public record class OperatorFactDto(
    SyntaxNodeId Node,
    SourceLocation Location,
    string Operator,
    string? EffectiveType,
    bool IsValueKnown,
    ArithmeticOperatorSemanticFlags? ArithmeticOpSemanticFlags,
    ConcatOperationSemanticFlags? ConcatOpSemanticFlags,
    ComparisonOperatorSemanticFlags? ComparisonOpSemanticFlags,
    LogicalOperatorSemanticFlags? LogicalOpSemanticFlags,
    MemberAccessOperationSemanticFlags? MemberAccessOpSemanticFlags,
    RuntimeErrorDto? Error,
    OptionCompare? Comparison = null,
    string? ComparisonCulture = null,
    string? Member = null)
{
    /// <summary>
    /// Describes an operation for the wire.
    /// </summary>
    /// <param name="fact">The operation.</param>
    public static OperatorFactDto From(OperatorFact fact) => new(
        fact.Node,
        fact.Location,
        fact.Operator,
        fact.EffectiveType?.Name,
        fact.IsValueKnown,
        ArithmeticOpSemanticFlags: (fact as ArithmeticOperatorFact)?.Flags,
        ConcatOpSemanticFlags: (fact as ConcatOperatorFact)?.Flags,
        ComparisonOpSemanticFlags: (fact as ComparisonOperatorFact)?.Flags,
        LogicalOpSemanticFlags: (fact as LogicalOperatorFact)?.Flags,
        MemberAccessOpSemanticFlags: (fact as MemberAccessFact)?.Flags,
        Error: fact.Error is null ? null : RuntimeErrorDto.From(fact.Error),
        Comparison: (fact as ComparisonOperatorFact)?.Comparison.Mode,
        ComparisonCulture: (fact as ComparisonOperatorFact)?.Comparison.Culture is { Name.Length: > 0 } culture ? culture.Name : null,
        Member: (fact as MemberAccessFact)?.Member);
}

/// <summary>
/// A <see cref="StatementFact"/>, as it travels.
/// </summary>
/// <remarks>
/// 👉 One record carries every kind of statement: the flags of the kind the statement is are set, and those of every other kind are <see langword="null"/>.
/// </remarks>
/// <param name="Node">The statement.</param>
/// <param name="Location">Where it is.</param>
/// <param name="Statement">Its keyword: one of the <c>Tokens</c>.</param>
/// <param name="FixedAssignmentFlags">What is known about an <c>LSet</c> or an <c>RSet</c> statement (<see cref="FixedAssignmentStatementFact"/>).</param>
/// <param name="WithStatementFlags">What is known about a <c>With</c> statement (<see cref="WithStatementFact"/>).</param>
/// <param name="IsValueKnown">
/// Whether what the statement works with is known, as opposed to a value its type merely allows; <see langword="true"/> for a statement whose fact does not
/// depend on it.
/// </param>
public record class StatementFactDto(
    SyntaxNodeId Node,
    SourceLocation Location,
    string Statement,
    FixedAssignmentSemanticFlags? FixedAssignmentFlags,
    WithStatementSemanticFlags? WithStatementFlags,
    bool IsValueKnown = true)
{
    /// <summary>
    /// Describes a statement for the wire.
    /// </summary>
    /// <param name="fact">The statement.</param>
    public static StatementFactDto From(StatementFact fact) => new(
        fact.Node,
        fact.Location,
        fact.Statement,
        (fact as FixedAssignmentStatementFact)?.Flags,
        (fact as WithStatementFact)?.Flags,
        (fact as WithStatementFact)?.IsValueKnown ?? true);
}

/// <summary>
/// A <see cref="RuntimeFacts"/>, as it travels.
/// </summary>
/// <param name="Conversions">Every conversion the code of the procedure asks for.</param>
/// <param name="Operations">Every operation the code of the procedure performs, and every member it accesses.</param>
/// <param name="Statements">What the statements that establish facts of their own are.</param>
/// <param name="Errors">Every run-time error an instruction of the procedure raises whenever it runs (<see cref="RuntimeFacts.Errors"/>).</param>
/// <param name="IsFullyAnalyzed">Whether every instruction of the procedure was evaluated.</param>
public record class RuntimeFactsDto(
    ImmutableArray<ConversionFactDto> Conversions,
    ImmutableArray<OperatorFactDto> Operations,
    ImmutableArray<StatementFactDto> Statements,
    ImmutableArray<RuntimeErrorDto> Errors,
    bool IsFullyAnalyzed)
{
    /// <summary>
    /// Describes the facts of a procedure for the wire.
    /// </summary>
    /// <param name="facts">The facts.</param>
    public static RuntimeFactsDto From(RuntimeFacts facts) => new(
        [.. facts.Conversions.Select(ConversionFactDto.From)],
        [.. facts.Operations.Select(OperatorFactDto.From)],
        [.. facts.Statements.Select(StatementFactDto.From)],
        [.. facts.Errors.Select(RuntimeErrorDto.From)],
        facts.IsFullyAnalyzed);
}

/// <summary>
/// A <see cref="ProcedureSemanticModel"/>, as it travels.
/// </summary>
/// <param name="Procedure">The address of the procedure.</param>
/// <param name="IsFullyAnalyzed">Whether the references the facts say there are in its body are all of them.</param>
/// <param name="CompileErrors">What the static pass found wrong with the body.</param>
/// <param name="Expressions">What it found out about each expression.</param>
/// <param name="ReturnValue">On how many code paths a function or a property getter assigns its return value, when the pass could tell.</param>
/// <param name="Runtime">What the language core states about the code, or <see langword="null"/> when the procedure has not been evaluated.</param>
public record class ProcedureSemanticsDto(
    Uri Procedure, bool IsFullyAnalyzed, ImmutableArray<CompileErrorDto> CompileErrors, ImmutableArray<ExpressionFactDto> Expressions, ReturnValueFact? ReturnValue = null,
    RuntimeFactsDto? Runtime = null);

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
/// <param name="Role">What it is there for, when it implements an interface member or handles an event.</param>
/// <param name="DeclaredBy">How a local variable came to be declared; <see langword="null"/> for any other declaration.</param>
public record class DeclarationFactDto(
    Uri Symbol, string Name, DeclarationKind Kind, AccessModifier Access, bool IsImplicit, SourceLocation Location, DeclarationReferences? References,
    DeclarationRole Role = DeclarationRole.None, LocalDeclarationKind? DeclaredBy = null);

/// <summary>
/// A <see cref="ModuleSemanticModel"/>, as it travels.
/// </summary>
/// <param name="Module">The address of the module.</param>
/// <param name="OptionExplicit">Whether the module states <c>Option Explicit</c>; not issued (<see langword="null"/>) in a language that has no such directive.</param>
/// <param name="DeclarationErrors">What is wrong with what the module declares.</param>
/// <param name="Procedures">The model of each procedure of the module.</param>
/// <param name="Declarations">What is known of how the module's declarations are used.</param>
/// <param name="Language">The identifier of the language the module is loaded as, when the host says.</param>
/// <param name="Class">What the objects of the class are, for a class module (<see cref="ClassModuleSemanticFlags"/>); <see langword="null"/> for any other module.</param>
public record class ModuleSemanticsDto(
    Uri Module,
    bool? OptionExplicit,
    ImmutableArray<CompileErrorDto> DeclarationErrors,
    ImmutableArray<ProcedureSemanticsDto> Procedures,
    ImmutableArray<DeclarationFactDto> Declarations,
    string? Language = null,
    ClassModuleSemanticFlags? Class = null)
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
                fact.Node, fact.Location, fact.DeclaredType?.Name, fact.Classification, fact.Binding?.Uri, fact.Flags, fact.Error is null ? null : ErrorOf(fact.Error), fact.Creates))],
            procedure.ReturnValue,
            procedure.Runtime is null ? null : RuntimeFactsDto.From(procedure.Runtime)))],
        [.. model.Declarations.Select(declaration => new DeclarationFactDto(
            declaration.Symbol.Uri, declaration.Name, declaration.Kind, declaration.Access, declaration.IsImplicit, declaration.Location, declaration.References, declaration.Role,
            declaration.DeclaredBy))],
        model.Language,
        model.Class);

    private static CompileErrorDto ErrorOf(VBCompileErrorInfo error) => new(error.VBCompileErrorId, error.Location, error.Verbose);
}
