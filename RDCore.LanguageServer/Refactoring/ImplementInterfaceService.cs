using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Services.Refactoring;
using RDCore.LanguageServer.Services.Refactoring.ExtractMethod;
using RDCore.LanguageServer.Services.Refactoring.ImplementInterface;
using RDCore.Runtime.LanguageCore.Refactoring;
using RDCore.Runtime.LanguageCore.Static;
using RDCore.SDK.Formatting;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Platform;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.ProtocolExtensions;
using RDCore.SDK.Workspace;
using System.Collections.Immutable;
using System.Globalization;
using System.Resources;

namespace RDCore.LanguageServer.Refactoring;

/// <summary>
/// Implement Interface, as a client asks it of a range of a document: the procedures an <c>Implements</c> directive or a <c>WithEvents</c> variable on that
/// range calls for, which the class module does not have (<see cref="InterfaceImplementation"/>).
/// </summary>
internal interface IImplementInterfaceService
{
    /// <summary>
    /// The implementation offered at a range: its title, and why it is refused. None when the range is on no directive or <c>WithEvents</c> variable, or when
    /// the class has every procedure it calls for.
    /// </summary>
    /// <param name="documentUri">The document of a class module.</param>
    /// <param name="range">The range: on a line of the directive or of the variable's declaration.</param>
    /// <param name="token">A token that cancels the parses.</param>
    Task<ImplementInterfaceOffer?> OfferAsync(Uri documentUri, SourceRange range, CancellationToken token);

    /// <summary>
    /// The edit that writes the procedures the directive or the variable at a range calls for, indented and verified on a copy of the module; why they are not
    /// written when it is refused, or when the copy does not pass its check.
    /// </summary>
    /// <param name="documentUri">The document of a class module.</param>
    /// <param name="range">The range: on a line of the directive or of the variable's declaration.</param>
    /// <param name="token">A token that cancels the parses.</param>
    Task<ImplementInterfaceAnswer> ImplementAsync(Uri documentUri, SourceRange range, CancellationToken token);
}

/// <summary>
/// An implementation offered at a range.
/// </summary>
/// <param name="Title">Its title, in the language of the user interface.</param>
/// <param name="Target">Where the directive or the variable it implements is written.</param>
/// <param name="Refusal">Why it is refused, in the language of the user interface; <see langword="null"/> when it is not.</param>
internal sealed record class ImplementInterfaceOffer(string Title, SourceRange Target, string? Refusal);

/// <summary>
/// The edits that write the procedures, or why they are not written.
/// </summary>
/// <param name="Document">The document.</param>
/// <param name="Version">The version of the document the edits were made from; <see langword="null"/> when it is not known.</param>
/// <param name="Edits">The edits; <see langword="null"/> when the procedures are not written.</param>
/// <param name="Refusal">Why they are not written, in the language of the user interface; <see langword="null"/> when they are.</param>
internal sealed record class ImplementInterfaceAnswer(Uri Document, int? Version, IReadOnlyList<RefactorTextEdit>? Edits, string? Refusal);

internal sealed class ImplementInterfaceService(IRenameService rename, IParsingClientService parsing, IOptions<SdkAppOptions> options) : IImplementInterfaceService
{
    private static readonly ResourceManager Messages = new(
        "RDCore.LanguageServer.Services.Refactoring.ImplementInterface.ImplementInterfaceMessages", typeof(ImplementInterfaceService).Assembly);

    /// <summary>
    /// The refusal of an implementation whose document has changed since it was offered.
    /// </summary>
    public static string DocumentChanged() => Format("ImplementInterface_DocumentChanged");

    public async Task<ImplementInterfaceOffer?> OfferAsync(Uri documentUri, SourceRange range, CancellationToken token)
    {
        if (await PlannedAsync(documentUri, range, token) is not { Plan: { } plan } || plan is { Stubs.Count: 0, Refusal: null })
        {
            return null;
        }

        return new ImplementInterfaceOffer(Title(plan.Target), plan.Target.Range, plan.Refusal is { } refusal ? RefusalOf(plan.Target, refusal) : null);
    }

    public async Task<ImplementInterfaceAnswer> ImplementAsync(Uri documentUri, SourceRange range, CancellationToken token)
    {
        if (await PlannedAsync(documentUri, range, token) is not { Plan: { } plan, Part: { } part, Module: { } module } || plan is { Stubs.Count: 0, Refusal: null })
        {
            return new ImplementInterfaceAnswer(documentUri, null, null, Format("ImplementInterface_Nothing"));
        }

        var version = module.Document.Version;
        if (plan.Refusal is { } refusal || plan.Edit is not { } edit)
        {
            return new ImplementInterfaceAnswer(documentUri, version, null, RefusalOf(plan.Target, plan.Refusal!));
        }

        // the procedures are indented as the user indents the code, then the module is parsed and bound again as the edit leaves it.
        var source = module.Source;
        var edits = ModuleIndenter.IndentWritten(source, [edit], await ParseAsync(module, DeclarationEdits.ApplyTo(source, [edit]), token), options.Value.Formatting.ToOptions());
        var text = DeclarationEdits.ApplyTo(source, edits);
        return Verified(part, module, plan, text, await ParseAsync(module, text, token), edits) is { } failure
            ? new ImplementInterfaceAnswer(documentUri, version, null, failure)
            : new ImplementInterfaceAnswer(documentUri, version, edits, null);
    }

    private sealed record class Planned(ImplementationPlan? Plan, RenameWorkspace.ProjectPart? Part, RenameWorkspace.DocumentModule? Module);

    // the plan of the directive or the variable at a range of a class module of the workspace.
    private async Task<Planned?> PlannedAsync(Uri documentUri, SourceRange range, CancellationToken token)
    {
        if (await rename.ComposeAsync(token) is not { } workspace
            || workspace.DocumentOf(documentUri) is not ({ } part, { } module)
            || BasicProgramText.IsProgram(module.Document.Id.Uri.GetFileSystemPath())
            || part.Context.Modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == module.Module.AbsoluteUri) is not { } source)
        {
            return null;
        }

        return new Planned(InterfaceImplementation.Plan(part.Context, source, module.Parse, range, Format("ImplementInterface_TodoComment")), part, module);
    }

    // the edits verified on the project composed again with the module as they leave it: it parses; every name it had refers to what it referred to; it has no
    // compile error it did not have, in its declarations or in the procedures written; and it implements what the edits were written for.
    private static string? Verified(
        RenameWorkspace.ProjectPart part, RenameWorkspace.DocumentModule module, ImplementationPlan plan, string text, ModuleParseResult parse,
        IReadOnlyList<RefactorTextEdit> edits)
    {
        var name = plan.Target.Name;
        if (!parse.IsSuccess || parse.SyntaxTree is null || part.Recompose is not { } recompose)
        {
            return Format("ImplementInterfaceVerification_DoesNotParse", name);
        }

        var composition = recompose(new Dictionary<string, ModuleParseResult> { [module.Module.AbsoluteUri] = parse });
        var after = part.Context with
        {
            Resolver = composition.Resolver,
            ScopeTree = composition.ScopeTree,
            Modules = [.. part.Context.Modules.Select(each => each.Module.AbsoluteUri == module.Module.AbsoluteUri ? new RenameModule(each.Module, parse.SyntaxTree, text) : each)],
        };

        var locate = RenameWorkspace.Locator(part);
        if (BindingCheck.Changes(part.Context, after, new Dictionary<string, IReadOnlyList<RefactorTextEdit>> { [module.Module.AbsoluteUri] = edits })
            .FirstOrDefault(change => change.Kind is not BindingChangeKind.Written) is { } changed)
        {
            return Format("ImplementInterfaceVerification_Rebinds", name, locate(changed.Module, changed.Before!.Value.Start));
        }

        if (NameOccurrences.SymbolOf(part.Context, new SemanticId(module.Module)) is not VBClassModuleSymbol before
            || NameOccurrences.SymbolOf(after, new SemanticId(module.Module)) is not VBClassModuleSymbol edited)
        {
            return Format("ImplementInterfaceVerification_DoesNotParse", name);
        }

        // an error is new when there are more of its kind than there were.
        var errorsBefore = ErrorsOf(before, module.Parse, part.Context).CountBy(error => error.ErrorId).ToDictionary();
        var analysis = ExtractionModuleAnalysis.Of(module.Module, text, parse, after.Resolver, after.ScopeTree);
        var errorsAfter = ErrorsOf(edited, parse, after)
            .Concat(plan.Stubs.SelectMany(stub => analysis.Procedure(stub.Name, stub.Kind)?.Model.CompileErrors ?? []))
            .ToArray();
        if (errorsAfter.CountBy(error => error.ErrorId).FirstOrDefault(count => count.Value > errorsBefore.GetValueOrDefault(count.Key)) is { Key: var newError, Value: > 0 })
        {
            return Format("ImplementInterfaceVerification_NewError", name, errorsAfter.First(error => error.ErrorId == newError).Description);
        }

        if (plan.Stubs.FirstOrDefault(stub => analysis.Procedure(stub.Name, stub.Kind) is null) is { } missing)
        {
            return Format("ImplementInterfaceVerification_Incomplete", name, missing.Name);
        }

        // the interface needs nothing more of the class, or the variable's events each have their handler.
        if (plan.Target.IsVariable)
        {
            if (edited.WithEventsVariables.FirstOrDefault(variable => string.Equals(variable.Name, name, StringComparison.OrdinalIgnoreCase)) is { ResolvedType: SDK.Model.Types.Complex.VBClassType { Symbol: { } eventSource } } variable
                && eventSource.Events.FirstOrDefault(handled => edited.FindEventHandler(variable, handled) is null) is { } unhandled)
            {
                return Format("ImplementInterfaceVerification_Incomplete", name, unhandled.Name);
            }
        }
        else if (errorsAfter.FirstOrDefault(error => error.ErrorId == (int)global::RDCore.Runtime.LanguageCore.Errors.VBCompileErrorId.InterfaceMemberNotImplemented && IsAtDirective(edited, name, error)) is { } incomplete)
        {
            return Format("ImplementInterfaceVerification_Incomplete", name, incomplete.Description);
        }

        return null;
    }

    // what is wrong with a class module's declarations: what its syntax shows, and what its symbols do, bound in the workspace.
    private static IEnumerable<VBCompileErrorInfo> ErrorsOf(VBClassModuleSymbol type, ModuleParseResult parse, RenameContext context)
    {
        var blocks = ConditionalCompilationBlocks.Of(parse.PrecompilerTrivia);
        return parse.SyntaxTree is { } tree
            ? [.. DeclarationStaticSemanticsEvaluator.CheckSyntax(tree, blocks), .. ReservedNameStaticSemantics.CheckSyntax(tree),
                .. DeclarationStaticSemanticsEvaluator.Evaluate(type, [.. type.Members], context.Resolver)]
            : [];
    }

    // an error located at the directive that names an interface.
    private static bool IsAtDirective(VBClassModuleSymbol type, string implemented, VBCompileErrorInfo error)
    {
        var index = type.ImplementedInterfaceNames.ToList().FindIndex(name => string.Equals(name[(name.LastIndexOf('.') + 1)..], implemented, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index < type.ImplementedInterfaceRanges.Length && error.Location.Range ==type.ImplementedInterfaceRanges[index];
    }

    private async Task<ModuleParseResult> ParseAsync(RenameWorkspace.DocumentModule module, string text, CancellationToken token)
        => await parsing.ParseFragmentAsync(module.Document.Id.Uri.ToUri(), text, token) with { Designer = module.Parse.Designer };

    private static string Title(ImplementationTarget target)
        => Format(target.IsVariable ? "ImplementInterface_EventsTitle" : "ImplementInterface_Title", target.Name);

    private static string RefusalOf(ImplementationTarget target, ImplementationRefusal refusal)
        => Format($"ImplementInterfaceRefusal_{refusal.Kind}", target.Name, refusal.Member);

    private static string Format(string key, params object?[] arguments)
        => string.Format(CultureInfo.CurrentUICulture, Messages.GetString(key, CultureInfo.CurrentUICulture) ?? key, arguments);
}
