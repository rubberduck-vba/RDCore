using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Services.Refactoring;
using RDCore.Runtime.LanguageCore.Refactoring;
using RDCore.SDK.Formatting;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Platform;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.ProtocolExtensions;
using RDCore.SDK.Workspace;
using RDCore.SDK.Semantics.Refactoring;
using System.Globalization;
using System.Resources;

namespace RDCore.LanguageServer.Refactoring;

/// <summary>
/// What Encapsulate Field refuses or would change of the field named at a position, and the edit that encapsulates it.
/// </summary>
internal interface IEncapsulateFieldService
{
    /// <summary>
    /// Why the field named at a position is not encapsulated, or what its encapsulation would change of what the code does.
    /// </summary>
    /// <param name="documentUri">The document.</param>
    /// <param name="position">The position, in the text of the document: on the field's declaration or on a reference to it.</param>
    /// <param name="token">A token that cancels the parses.</param>
    /// <returns>The notices: a refusal, or the changes the user is asked to accept.</returns>
    Task<RefactorPrepareResult> PrepareAsync(Uri documentUri, SourcePosition position, CancellationToken token);

    /// <summary>
    /// The edit that encapsulates the field named at a position, verified against the workspace as it leaves it; or why it is not made.
    /// </summary>
    /// <param name="documentUri">The document.</param>
    /// <param name="position">The position, in the text of the document.</param>
    /// <param name="accepted">The codes of the changes the user accepted.</param>
    /// <param name="token">A token that cancels the parses.</param>
    /// <returns>The edit, or why it is not made.</returns>
    Task<EncapsulateFieldAnswer> ApplyAsync(Uri documentUri, SourcePosition position, IReadOnlyList<string> accepted, CancellationToken token);
}

/// <summary>
/// The edit that encapsulates a field, or why it is not made.
/// </summary>
/// <param name="Edit">The edit; <see langword="null"/> when it is not made.</param>
/// <param name="Refusal">Why it is not made, in the language of the user interface; <see langword="null"/> when it is.</param>
internal sealed record class EncapsulateFieldAnswer(RefactorWorkspaceEdit? Edit, string? Refusal);

internal sealed class EncapsulateFieldService(IRenameService rename, IParsingClientService parsing, IOptions<SdkAppOptions> options) : IEncapsulateFieldService
{
    /// <summary>
    /// The code of the change a reference makes that passes the field to a parameter that may be <c>ByRef</c>: the procedure writes to a copy of the
    /// property's value.
    /// </summary>
    public const string PassedByRefCode = "encapsulateField.passedByRef";

    private static readonly ResourceManager Messages = new("RDCore.LanguageServer.Services.Refactoring.EncapsulateFieldMessages", typeof(EncapsulateFieldService).Assembly);

    /// <summary>
    /// The title of the code action that encapsulates a field, in the language of the user interface.
    /// </summary>
    public static string Title() => Format("EncapsulateField_Title");

    /// <summary>
    /// Why an encapsulation offered for one version of a document is not made for another, in the language of the user interface.
    /// </summary>
    public static string DocumentChanged() => Format("EncapsulateField_DocumentChanged");

    public async Task<RefactorPrepareResult> PrepareAsync(Uri documentUri, SourcePosition position, CancellationToken token)
    {
        var (planned, part, refusal) = await PlannedAsync(documentUri, position, token);
        if (planned is null)
        {
            return new RefactorPrepareResult { Notices = [Refused(refusal!.Value, part)] };
        }

        return new RefactorPrepareResult { Notices = [.. Changes(planned.Encapsulation, part!)] };
    }

    public async Task<EncapsulateFieldAnswer> ApplyAsync(Uri documentUri, SourcePosition position, IReadOnlyList<string> accepted, CancellationToken token)
    {
        var (planned, part, refusal) = await PlannedAsync(documentUri, position, token);
        if (planned is null)
        {
            return new(null, Refused(refusal!.Value, part).Message);
        }

        if (Changes(planned.Encapsulation, part!).FirstOrDefault(change => !accepted.Contains(change.Code)) is { } declined)
        {
            return new(null, Format("EncapsulateField_NotAccepted", declined.Message));
        }

        // the lines the edits write are indented as the user indents the code, then the module is parsed and bound again as the edits leave it.
        var module = planned.Module;
        var source = module.Source;
        var unindented = DeclarationEdits.ApplyTo(source, planned.Encapsulation.Edits);
        var edits = ModuleIndenter.IndentWritten(source, planned.Encapsulation.Edits, await ParseAsync(module, unindented, token), options.Value.Formatting.ToOptions());
        var text = DeclarationEdits.ApplyTo(source, edits);
        if (Verified(part!, planned.Encapsulation, module, text, await ParseAsync(module, text, token), edits) is { } failure)
        {
            return new(null, Format("EncapsulateField_NotVerified", failure));
        }

        return new(new RefactorWorkspaceEdit
        {
            Documents = [new RefactorDocumentEdit { Uri = module.Document.Id.Uri.ToUri(), Version = module.Document.Version, Edits = [.. edits] }],
        }, null);
    }

    private sealed record class Planned(Encapsulation Encapsulation, RenameWorkspace.DocumentModule Module);

    // the field named at a position of a document, and how it is encapsulated in its own module, or why it is not.
    private async Task<(Planned? Planned, RenameWorkspace.ProjectPart? Part, (EncapsulationRefusal Refusal, string Field)? Refusal)> PlannedAsync(
        Uri documentUri, SourcePosition position, CancellationToken token)
    {
        var none = (new EncapsulationRefusal(EncapsulationRefusalKind.NotAField), string.Empty);
        if (await rename.ComposeAsync(token) is not { } workspace
            || workspace.DocumentOf(documentUri) is not ({ } part, { } named)
            || BasicProgramText.IsProgram(named.Document.Id.Uri.GetFileSystemPath())
            || EncapsulateField.FieldAt(part.Context, named.Module, position) is not { } field
            || part.Modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == NameOccurrences.ModuleOf(field.Uri).AbsoluteUri) is not { } module)
        {
            return (null, null, none);
        }

        var (encapsulation, refusal) = EncapsulateField.Plan(part.Context, module.Module, module.Parse, field);
        return encapsulation is null ? (null, part, (refusal!, field.Name)) : (new Planned(encapsulation, module), part, null);
    }

    private static RefactorNotice Refused((EncapsulationRefusal Refusal, string Field) refused, RenameWorkspace.ProjectPart? part) => new()
    {
        Code = Code(refused.Refusal.Kind.ToString()),
        Kind = RefactorNoticeKind.Refusal,
        Message = Format($"EncapsulateFieldRefusal_{refused.Refusal.Kind}", refused.Field, refused.Refusal.Name),
        Location = refused.Refusal is { Module: { } module, Range: { } range } ? LocationOf(part, module, range) : null,
    };

    // each reference outside the field's standard module that a call may write through: it would write to a copy of the property's value.
    private static IEnumerable<RefactorNotice> Changes(Encapsulation encapsulation, RenameWorkspace.ProjectPart part)
        => encapsulation.PassedByRef.Select(reference => new RefactorNotice
        {
            Code = PassedByRefCode,
            Kind = RefactorNoticeKind.Change,
            Message = Format("EncapsulateFieldChange_PassedByRef", encapsulation.Field.Name, RenameWorkspace.Locator(part)(reference.Module, reference.Range.Start)),
            Location = LocationOf(part, reference.Module, reference.Range),
        });

    // the edits verified against the project composed again with the module as they leave it: every reference the edits leave in place still refers to the
    // field: to its properties outside the module, and to the member of the private type inside it. Why they are not made, or null.
    private static string? Verified(
        RenameWorkspace.ProjectPart part, Encapsulation encapsulation, RenameWorkspace.DocumentModule module, string text, ModuleParseResult parse,
        IReadOnlyList<RefactorTextEdit> edits)
    {
        if (!parse.IsSuccess || part.Recompose is not { } recompose)
        {
            return Format("EncapsulateFieldVerification_DoesNotParse");
        }

        var composition = recompose(new Dictionary<string, ModuleParseResult> { [module.Module.AbsoluteUri] = parse });
        var after = part.Context with
        {
            Resolver = composition.Resolver,
            ScopeTree = composition.ScopeTree,
            Modules = [.. part.Context.Modules.Select(each => each.Module.AbsoluteUri == module.Module.AbsoluteUri ? new RenameModule(each.Module, parse.SyntaxTree!, text) : each)],
        };

        var locate = RenameWorkspace.Locator(part);
        foreach (var change in BindingCheck.Changes(part.Context, after, new Dictionary<string, IReadOnlyList<RefactorTextEdit>> { [module.Module.AbsoluteUri] = edits }))
        {
            switch (change.Kind)
            {
                case BindingChangeKind.Lost or BindingChangeKind.Unknown:
                    return Format("EncapsulateFieldVerification_Lost", encapsulation.Field.Name, locate(change.Module, change.Before!.Value.Start));
                case BindingChangeKind.Retargeted when !IsExpected(change, encapsulation, after):
                    return Format("EncapsulateFieldVerification_Retargeted", encapsulation.Field.Name, locate(change.Module, change.Before!.Value.Start));
            }
        }

        return null;
    }

    // a reference to the field that refers to its property, or, inside its module, to the member of the private type that holds its value.
    private static bool IsExpected(BindingChange change, Encapsulation encapsulation, RenameContext after)
        => change.Was == encapsulation.Field.SemanticId
            && change.Is is { } now
            && NameOccurrences.SymbolOf(after, now) is { } symbol
            && string.Equals(symbol.Name, encapsulation.Field.Name, StringComparison.OrdinalIgnoreCase)
            && (symbol is VBUserDefinedTypeFieldSymbol
                ? change.Module.AbsoluteUri == encapsulation.Module.AbsoluteUri
                : NameOccurrences.ModuleOf(symbol.Uri).AbsoluteUri == encapsulation.Module.AbsoluteUri);

    // a text of a module, parsed as its document is: the designer of a form is its document's, which the text of its code does not hold.
    private async Task<ModuleParseResult> ParseAsync(RenameWorkspace.DocumentModule module, string text, CancellationToken token)
        => await parsing.ParseFragmentAsync(module.Document.Id.Uri.ToUri(), text, token) with { Designer = module.Parse.Designer };

    // where a range of a module is, in the document that module is the text of.
    private static SourceLocation LocationOf(RenameWorkspace.ProjectPart? part, Uri module, SourceRange range)
        => new(part?.Modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == module.AbsoluteUri)?.Document.Id.Uri.ToUri() ?? module, range);

    private static string Code(string kind) => $"encapsulateField.{char.ToLowerInvariant(kind[0])}{kind[1..]}";

    private static string Format(string key, params object?[] arguments)
        => string.Format(CultureInfo.CurrentUICulture, Messages.GetString(key, CultureInfo.CurrentUICulture) ?? key, arguments);
}
