using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Services.Refactoring;
using RDCore.LanguageServer.Services.Refactoring.ExtractMethod;
using RDCore.LanguageServer.Services.Refactoring.InlineMember;
using RDCore.Runtime.LanguageCore.Refactoring;
using RDCore.SDK.Formatting;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.SDK.Server.Configuration;

namespace RDCore.LanguageServer.Refactoring;

/// <summary>
/// Inline Member (<see cref="RefactoringKinds.InlineMember"/>), of the member at a position of a document: the calls it inlines and why it refuses the others,
/// whether it removes the member, and the edit it makes with the user's answers.
/// </summary>
internal interface IInlineMemberService
{
    /// <summary>
    /// The calls inlined from a position, each a site the user ticks, and the removal of the member; the sites that are refused, with why.
    /// </summary>
    /// <param name="documentUri">The document.</param>
    /// <param name="position">The position, in the text of the document: on a call of the member, or on its name where it is declared.</param>
    /// <param name="token">A token that cancels the composition of the workspace.</param>
    Task<RefactorPrepareResult> PrepareAsync(Uri documentUri, SourcePosition position, CancellationToken token);

    /// <summary>
    /// The edit that inlines the calls the user ticked, and removes the member when the user ticked its removal; or why it is not made.
    /// </summary>
    /// <param name="documentUri">The document.</param>
    /// <param name="position">The position, in the text of the document.</param>
    /// <param name="answers">The answers to the sites and to the removal; a site that is not answered is inlined, and the removal has its default.</param>
    /// <param name="accepted">The codes of the changes the user accepted.</param>
    /// <param name="token">A token that cancels the composition of the workspace.</param>
    Task<InlineMemberAnswer> ApplyAsync(
        Uri documentUri, SourcePosition position, IReadOnlyList<RefactorAnswer> answers, IReadOnlyList<string> accepted, CancellationToken token);

    /// <summary>
    /// The title of the code action that inlines the member at a position, and why it is refused; <see langword="null"/> where no member that is inlined is.
    /// </summary>
    /// <param name="documentUri">The document.</param>
    /// <param name="position">The position, in the text of the document.</param>
    /// <param name="token">A token that cancels the composition of the workspace.</param>
    Task<(string Title, string? Refusal)?> OfferAsync(Uri documentUri, SourcePosition position, CancellationToken token);
}

/// <summary>
/// The edit that inlines calls of a member, or why it is not made.
/// </summary>
/// <param name="Edit">The edit; <see langword="null"/> when it is not made.</param>
/// <param name="Refusal">Why it is not made, in the language of the user interface; <see langword="null"/> when it is.</param>
internal sealed record class InlineMemberAnswer(RefactorWorkspaceEdit? Edit, string? Refusal);

internal sealed class InlineMemberService(IRenameService rename, IParsingClientService parsing, IOptions<SdkAppOptions> options) : IInlineMemberService
{
    /// <summary>
    /// The choice of the calls that are inlined: a list, whose items are the calls, each with the flag <see cref="InlineChoice"/>.
    /// </summary>
    public const string SitesChoice = "sites";

    /// <summary>
    /// The flag of a call that inlines it; <c>true</c> by default.
    /// </summary>
    public const string InlineChoice = "inline";

    /// <summary>
    /// The flag that removes the member, offered when every reference to it is a call that is inlined.
    /// </summary>
    public const string RemoveChoice = "remove";

    public async Task<RefactorPrepareResult> PrepareAsync(Uri documentUri, SourcePosition position, CancellationToken token)
    {
        var (target, refusal) = await TargetAsync(documentUri, position, token, tried: true);
        if (target is null)
        {
            return new RefactorPrepareResult { Notices = [Refused(null, refusal!, null)] };
        }

        var locate = RenameWorkspace.Locator(target.Part);
        if (!target.IsAll && target.Sites is [{ Refusal: { } refused }])
        {
            return new RefactorPrepareResult { Notices = [Refused(target.Part, refused, null)] };
        }

        var refusals = target.Sites.Where(site => site.Refusal is not null).Select(site => Refused(target.Part, site.Refusal!, site.Id)).ToList();
        if (target.Sites.All(site => site.Inlined is null))
        {
            return new RefactorPrepareResult
            {
                Notices = refusals.Count > 0 ? refusals : [Refused(target.Part, new(InlineRefusalKind.NotAProcedure, target.Callee.Name), null)],
            };
        }

        List<RefactorChoice> choices =
        [
            new()
            {
                Id = SitesChoice,
                Kind = RefactorChoiceKind.List,
                Label = InlineRefusal.Format("InlineMember_Sites", null),
                Description = InlineRefusal.Format("InlineMember_SitesDescription", null, target.Callee.Name),
                Items =
                [
                    .. target.Sites.Select(site => new RefactorItem
                    {
                        Id = site.Id,
                        Label = locate(site.Module, site.Name.Start),
                        Reason = site.Inlined is { } inlined ? InlineRefusal.Format($"InlineMember_{inlined.Form}", null) : null,
                        Location = LocationOf(target.Part, site.Module, site.Name),
                        Choices = site.Inlined is null
                            ? []
                            :
                            [
                                new RefactorChoice
                                {
                                    Id = InlineChoice,
                                    Kind = RefactorChoiceKind.Flag,
                                    Label = InlineRefusal.Format("InlineMember_Site", null),
                                    Default = "true",
                                },
                            ],
                    }),
                ],
            },
        ];

        var notices = refusals;
        if (target.IsAll)
        {
            var removal = RemovalOf(target, target.Sites.Where(site => site.Inlined is not null));
            if (removal.IsOffered)
            {
                choices.Add(new RefactorChoice
                {
                    Id = RemoveChoice,
                    Kind = RefactorChoiceKind.Flag,
                    Label = InlineRefusal.Format("InlineMember_Remove", null, target.Callee.Name),
                    Description = InlineRefusal.Format(
                        removal.Kind is InlineRemovalKind.Unticked ? "InlineMember_Removal_Unticked" : "InlineMember_RemoveDescription",
                        null,
                        target.Callee.Name),
                    Default = removal.Kind is InlineRemovalKind.Ticked ? "true" : "false",
                });
            }
            else
            {
                notices.Add(Kept(target.Part, target.Callee.Name, removal));
            }
        }

        return new RefactorPrepareResult { Choices = choices, Notices = notices, Range = SourceRangeOf(target.Document.ToTextRange(target.Range)) };
    }

    public async Task<InlineMemberAnswer> ApplyAsync(
        Uri documentUri, SourcePosition position, IReadOnlyList<RefactorAnswer> answers, IReadOnlyList<string> accepted, CancellationToken token)
    {
        if (answers.FirstOrDefault(answer => answer.Id is not (SitesChoice or RemoveChoice)) is { } unknown)
        {
            return new(null, InlineRefusal.Format("InlineMember_UnknownAnswer", null, unknown.Id));
        }

        var (target, refusal) = await TargetAsync(documentUri, position, token, tried: true);
        if (target is null)
        {
            return new(null, refusal!.ToMessage());
        }

        var part = target.Part;
        var locate = RenameWorkspace.Locator(part);
        var answered = answers.FirstOrDefault(answer => answer.Id == SitesChoice)?.Items ?? [];
        var chosen = target.Sites.Where(site => site.Inlined is not null
            && answered.FirstOrDefault(item => item.Id == site.Id)?.Answers
                .FirstOrDefault(answer => answer.Id == InlineChoice)?.Value is not "false").ToList();
        if (chosen.Count == 0)
        {
            return new(null, target.Sites.FirstOrDefault(site => site.Refusal is not null && !target.IsAll)?.Refusal!.ToMessage(locate)
                ?? InlineRefusal.Format("InlineMember_NothingInlined", null, target.Callee.Name));
        }

        // the member is removed when the user ticks it, or by default when it is ticked; only when every reference to it is a call that is inlined.
        var removal = RemovalOf(target, chosen);
        var removeAnswer = answers.FirstOrDefault(answer => answer.Id == RemoveChoice)?.Value;
        var remove = target.IsAll && (removeAnswer is null ? removal.Kind is InlineRemovalKind.Ticked : removeAnswer == "true");
        if (remove && !removal.IsOffered)
        {
            return new(null, Kept(part, target.Callee.Name, removal).Message);
        }

        // each call is inlined into the module as the calls inlined before it left it: from the last of a module to its first, which the edits after it
        // do not move. Its fresh names are then chosen against the names those calls wrote.
        var texts = part.Modules.ToDictionary(module => module.Module.AbsoluteUri, module => (Text: module.Source, module.Parse));
        var edited = new HashSet<string>();
        var context = part.Context;
        foreach (var site in chosen.OrderBy(site => site.Module.AbsoluteUri, StringComparer.Ordinal).ThenByDescending(site => site.Name.Start))
        {
            var inlined = site.Inlined!;
            if (edited.Count > 0)
            {
                var (again, refused) = InlineCall.At(context, AnalysesOf(context, texts), site.Module, site.Name.Start);
                if (again is null)
                {
                    return new(null, refused!.ToMessage(locate));
                }

                inlined = again;
            }

            var key = site.Module.AbsoluteUri;
            var original = texts[key].Text;
            if (await EditedAsync(part, key, original, inlined.Edits, token) is not { } written)
            {
                return new(null, NotVerified(site, InlineRefusal.Format("InlineMember_Verification_DoesNotParse", null), locate));
            }

            texts[key] = written;
            edited.Add(key);
            if (Recomposed(part, texts, edited) is { } recomposed)
            {
                if (Rebound(context, recomposed, site, original, written.Text, inlined) is { } rebound)
                {
                    return new(null, rebound.ToMessage(locate));
                }

                context = recomposed;
            }
        }

        var calleeKey = target.Analysis.Module.AbsoluteUri;
        if (remove)
        {
            var (text, parse) = texts[calleeKey];
            if (parse.SyntaxTree?.Children.OfType<MemberDeclarationNode>().FirstOrDefault(member => member.MemberKind == target.Declaration.MemberKind
                    && member.Name.Equals(target.Declaration.Name, StringComparison.OrdinalIgnoreCase)) is not { } declaration
                || await EditedAsync(part, calleeKey, text, [MemberEdits.Remove(text, MemberEdits.Extent(parse, text, declaration))], token, indent: false)
                    is not { } removed)
            {
                return new(null, NotVerified(chosen[0], InlineRefusal.Format("InlineMember_Verification_DoesNotParse", null), locate));
            }

            texts[calleeKey] = removed;
            edited.Add(calleeKey);
            context = Recomposed(part, texts, edited) ?? context;
        }

        if (Verified(part, target, chosen, texts, edited, context, remove, locate) is { } failure)
        {
            return new(null, failure);
        }

        return new(new RefactorWorkspaceEdit
        {
            Documents =
            [
                .. edited.Select(key =>
                {
                    var document = part.Modules.First(module => module.Module.AbsoluteUri == key);
                    var edit = Difference(document.Source, texts[key].Text);
                    return new RefactorDocumentEdit
                    {
                        Uri = document.Document.Id.Uri.ToUri(),
                        Version = document.Document.Version,
                        Edits = [new RefactorTextEdit { Range = SourceRangeOf(document.ToTextRange(edit.Range)), NewText = edit.NewText }],
                    };
                }),
            ],
        }, null);
    }

    public async Task<(string Title, string? Refusal)?> OfferAsync(Uri documentUri, SourcePosition position, CancellationToken token)
    {
        var (target, refusal) = await TargetAsync(documentUri, position, token);
        if (target is null)
        {
            return refusal is { Kind: InlineRefusalKind.NotAProcedure }
                ? null
                : (InlineRefusal.Format("InlineMember_Title", null, refusal!.Detail), refusal.ToMessage());
        }

        var title = InlineRefusal.Format(target.IsAll ? "InlineMember_TitleAll" : "InlineMember_Title", null, target.Callee.Name);
        var locate = RenameWorkspace.Locator(target.Part);
        return target.Sites.Any(site => site.Inlined is not null)
            ? (title, null)
            : (title,
                target.Sites.FirstOrDefault()?.Refusal?.ToMessage(locate) ?? InlineRefusal.Format("InlineMember_NothingInlined", null, target.Callee.Name));
    }

    // a call of the member, by its name: inlined, or why it is not.
    private sealed record class Site(string Id, Uri Module, SourceRange Name, InlinedCall? Inlined, InlineRefusal? Refusal);

    // the member at a position, its declaration and the analysis of its module; the calls inlined from there: the one at the position, or each call of the
    // member from its declaration.
    private sealed record class Target(
        RenameWorkspace.ProjectPart Part, RenameWorkspace.DocumentModule Document, Symbol Callee, MemberDeclarationNode Declaration,
        ExtractionModuleAnalysis Analysis, bool IsAll, SourceRange Range, IReadOnlyList<Site> Sites);

    // with tried, each call is also inlined alone into its module, and refused when a name it writes would refer to another declaration there.
    private async Task<(Target? Target, InlineRefusal? Refusal)> TargetAsync(
        Uri documentUri, SourcePosition position, CancellationToken token, bool tried = false)
    {
        if (await rename.ComposeAsync(token) is not { } workspace || workspace.DocumentOf(documentUri) is not ({ } part, { } document))
        {
            return (null, new(InlineRefusalKind.NotAProcedure));
        }

        var context = part.Context;
        var at = document.ToModulePosition(new Position(position.Line, position.Character));
        if (context.Modules.FirstOrDefault(module => module.Module.AbsoluteUri == document.Module.AbsoluteUri) is not { } syntax
            || NameOccurrences.NameAt(syntax, at) is not { } name)
        {
            return (null, new(InlineRefusalKind.NotAProcedure));
        }

        var found = NameOccurrences.Find(context, name, context.Modules);
        var here = found.Occurrences.FirstOrDefault(occurrence
            => occurrence.Module.AbsoluteUri == document.Module.AbsoluteUri && occurrence.Range.Start <= at && at <= occurrence.Range.End);
        if (here.Binding is not { } binding || NameOccurrences.SymbolOf(context, binding) is not { } callee)
        {
            return (null, new(InlineRefusalKind.NotAProcedure, name));
        }

        var analysisOf = AnalysesOf(context, part.Modules.ToDictionary(module => module.Module.AbsoluteUri, module => (Text: module.Source, module.Parse)));
        if (InlineCall.KindOf(callee) is not { } kind || analysisOf(NameOccurrences.ModuleOf(callee.Uri)) is not { } analysis
            || analysis.Procedure(callee.Name, kind) is not { } procedure)
        {
            return (null, InlineCall.At(context, analysisOf, document.Module, at).Refusal ?? new(InlineRefusalKind.NotAProcedure, callee.Name));
        }

        // the member's own name in its body (a function's result) is not a call of it.
        var own = procedure.Declaration.SourceLocation.Range;
        bool IsOwn(NameOccurrence occurrence)
            => occurrence.Module.AbsoluteUri == analysis.Module.AbsoluteUri && own.Start <= occurrence.Range.Start && occurrence.Range.End <= own.End;
        var isAll = here.Kind is OccurrenceKind.Declaration;
        if (!isAll && IsOwn(here))
        {
            return (null, new(InlineRefusalKind.UnsupportedSite, callee.Name, new SourceLocation(here.Module, here.Range)));
        }

        var calls = isAll
            ? found.Occurrences.Where(occurrence
                => occurrence.Binding == callee.SemanticId && occurrence.Kind is OccurrenceKind.Name or OccurrenceKind.Member && !IsOwn(occurrence))
            : [here];
        var sites = calls.Select((occurrence, index) =>
        {
            var (inlined, refusal) = InlineCall.At(context, analysisOf, occurrence.Module, occurrence.Range.Start);
            return new Site($"{index}", occurrence.Module, occurrence.Range, inlined, refusal);
        }).ToList();
        if (tried)
        {
            sites = await TriedAsync(part, sites, token);
        }

        return (new Target(part, document, callee, procedure.Declaration, analysis, isAll, here.Range, sites), null);
    }

    // each call inlined alone into its module as it is; refused when a name it writes would refer to another declaration there. A call whose module does
    // not parse once edited is left to the edit's own verification.
    private async Task<List<Site>> TriedAsync(RenameWorkspace.ProjectPart part, IReadOnlyList<Site> sites, CancellationToken token)
    {
        var texts = part.Modules.ToDictionary(module => module.Module.AbsoluteUri, module => (Text: module.Source, module.Parse));
        var tried = new List<Site>();
        foreach (var site in sites)
        {
            var key = site.Module.AbsoluteUri;
            if (site.Inlined is not { } inlined || await EditedAsync(part, key, texts[key].Text, inlined.Edits, token) is not { } written
                || Recomposed(part, new Dictionary<string, (string Text, ModuleParseResult Parse)>(texts) { [key] = written }, new HashSet<string> { key })
                    is not { } after)
            {
                tried.Add(site);
                continue;
            }

            tried.Add(Rebound(part.Context, after, site, texts[key].Text, written.Text, inlined) is { } rebound
                ? site with { Inlined = null, Refusal = rebound }
                : site);
        }

        return tried;
    }

    // the simple names the call wrote into its module, bound again there: each refers to what it referred to in the member or at the call, or to a
    // declaration the edit wrote. Within one procedure a simple name finds one declaration (MS-VBAL §5.6.10), so every declaration a name referred to must
    // be that one. The refusal of the call otherwise.
    private static InlineRefusal? Rebound(RenameContext before, RenameContext after, Site site, string original, string edited, InlinedCall inlined)
    {
        if (inlined.Names is not { } names || after.Modules.FirstOrDefault(module => module.Module.AbsoluteUri == site.Module.AbsoluteUri) is not { } module)
        {
            return null;
        }

        var difference = Difference(original, edited);
        var lines = TextLines.Of(edited);
        var start = difference.Range.Start;
        var end = lines.PositionOf(lines.Offset(start) + difference.NewText.Length);
        var expected = names.ToLookup(name => name.Name, name => name.Binding, StringComparer.OrdinalIgnoreCase);
        var bindings = NameOccurrences.BindingsOf(after, module);
        var members = module.Syntax.Descendants().OfType<MemberAccessExpressionNode>().Select(access => (SyntaxNode)access.Member)
            .ToHashSet(ReferenceEqualityComparer.Instance);
        foreach (var name in module.Syntax.Descendants().OfType<SimpleNameExpressionNode>())
        {
            var range = name.SourceLocation.Range;
            if (members.Contains(name) || range.Start < start || end < range.End)
            {
                continue;
            }

            var bound = bindings(name);
            var isWritten = bound is { } id && NameOccurrences.SymbolOf(after, id) is not null && NameOccurrences.SymbolOf(before, id) is null;
            var was = expected[name.IdentifierName].Distinct().ToList();
            if (isWritten || (was.Count > 0 ? was.All(binding => binding == bound) : bound is null))
            {
                continue;
            }

            return new(InlineRefusalKind.Rebound, name.IdentifierName, new SourceLocation(site.Module, site.Name));
        }

        return null;
    }

    private static InlineRemoval RemovalOf(Target target, IEnumerable<Site> inlined)
        => InlineRemoval.Of(target.Part.Context, target.Analysis, target.Callee, target.Declaration, [.. inlined.Select(site => (site.Module, site.Name))]);

    // the analysis of each module of a workspace, from its text as it is: each is analyzed once, when it is first asked for.
    private static Func<Uri, ExtractionModuleAnalysis?> AnalysesOf(
        RenameContext context, IReadOnlyDictionary<string, (string Text, ModuleParseResult Parse)> texts)
    {
        var analyses = new Dictionary<string, ExtractionModuleAnalysis?>();
        return module =>
        {
            if (!analyses.TryGetValue(module.AbsoluteUri, out var analysis))
            {
                analysis = texts.TryGetValue(module.AbsoluteUri, out var text)
                    ? ExtractionModuleAnalysis.Of(module, text.Text, text.Parse, context.Resolver, context.ScopeTree, context.DeadTestOf(text.Parse))
                    : null;
                analyses[module.AbsoluteUri] = analysis;
            }

            return analysis;
        };
    }

    // the text of a module with the edits made, the lines they write indented as the user indents the code, and parsed; null when it does not parse.
    private async Task<(string Text, ModuleParseResult Parse)?> EditedAsync(
        RenameWorkspace.ProjectPart part, string module, string text, IReadOnlyList<RefactorTextEdit> edits, CancellationToken token, bool indent = true)
    {
        var document = part.Modules.First(candidate => candidate.Module.AbsoluteUri == module);
        var unindented = DeclarationEdits.ApplyTo(text, edits);
        var written = indent
            ? DeclarationEdits.ApplyTo(
                text, ModuleIndenter.IndentWritten(text, edits, await ParseAsync(document, unindented, token), options.Value.Formatting.ToOptions()))
            : unindented;
        var parse = await ParseAsync(document, written, token);
        return parse.IsSuccess ? (written, parse) : null;
    }

    // the project composed again with the edited modules as they are now.
    private static RenameContext? Recomposed(
        RenameWorkspace.ProjectPart part, IReadOnlyDictionary<string, (string Text, ModuleParseResult Parse)> texts, IReadOnlySet<string> edited)
    {
        if (part.Recompose is not { } recompose)
        {
            return null;
        }

        var composition = recompose(edited.ToDictionary(key => key, key => texts[key].Parse));
        return part.Context with
        {
            Resolver = composition.Resolver,
            ScopeTree = composition.ScopeTree,
            Modules = [.. part.Context.Modules.Select(each => edited.Contains(each.Module.AbsoluteUri)
                ? new RenameModule(
                    each.Module,
                    texts[each.Module.AbsoluteUri].Parse.SyntaxTree!,
                    texts[each.Module.AbsoluteUri].Text,
                    texts[each.Module.AbsoluteUri].Parse.Designer)
                : each)],
        };
    }

    // the edited modules checked as every refactoring checks them: each parses, the procedures the calls are in bind every name they write and have no new
    // compile error, and every reference the edits leave in place refers to what it referred to. Why the edit is not made, or null.
    private static string? Verified(
        RenameWorkspace.ProjectPart part, Target target, IReadOnlyList<Site> chosen, IReadOnlyDictionary<string, (string Text, ModuleParseResult Parse)> texts,
        IReadOnlySet<string> edited, RenameContext after, bool removed, Func<Uri, SourcePosition, string> locate)
    {
        if (part.Recompose is null)
        {
            return NotVerified(chosen[0], InlineRefusal.Format("InlineMember_Verification_DoesNotParse", null), locate);
        }

        var before = AnalysesOf(part.Context, part.Modules.ToDictionary(module => module.Module.AbsoluteUri, module => (Text: module.Source, module.Parse)));
        var afterOf = AnalysesOf(after, texts);
        foreach (var group in chosen.GroupBy(site => site.Module.AbsoluteUri))
        {
            var module = group.First().Module;
            if (before(module) is not { } original || afterOf(module) is not { } now)
            {
                continue;
            }

            var callers = group.Select(site => original.Parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>()
                    .FirstOrDefault(member => member.SourceLocation.Range.Start <= site.Name.Start && site.Name.End <= member.SourceLocation.Range.End))
                .OfType<MemberDeclarationNode>().DistinctBy(member => (member.Name.ToUpperInvariant(), member.MemberKind)).ToList();
            var originals = callers.Select(member => original.Procedure(member.Name, member.MemberKind)).OfType<ExtractionProcedureAnalysis>().ToList();
            var edits = callers.Select(member => now.Procedure(member.Name, member.MemberKind)).OfType<ExtractionProcedureAnalysis>().ToList();
            if ((EditedModuleCheck.ParseOf(now) ?? EditedModuleCheck.Of(original, originals, now, edits)) is { } problem)
            {
                return NotVerified(group.First(), problem.Detail ?? problem.Kind.ToString(), locate);
            }
        }

        var differences = edited.ToDictionary(key => key, key
            => (IReadOnlyList<RefactorTextEdit>)[Difference(part.Modules.First(module => module.Module.AbsoluteUri == key).Source, texts[key].Text)]);
        foreach (var change in BindingCheck.Changes(part.Context, after, differences))
        {
            // the member's own references go with it when it is removed.
            if (removed && change.Was == target.Callee.SemanticId)
            {
                continue;
            }

            if (change.Kind is BindingChangeKind.Lost or BindingChangeKind.Unknown or BindingChangeKind.Retargeted)
            {
                var where = change.Before ?? change.After;
                return NotVerified(chosen[0], InlineRefusal.Format("InlineMember_Verification_Retargeted", null, change.Name,
                    where is { } range ? $"({locate(change.Module, range.Start)})" : string.Empty), locate);
            }
        }

        return null;
    }

    // the edit that turns one text into the other: the lines from the first that differs to the last.
    private static RefactorTextEdit Difference(string before, string after)
    {
        var start = 0;
        while (start < before.Length && start < after.Length && before[start] == after[start])
        {
            start++;
        }

        start = start == 0 ? 0 : before.LastIndexOf('\n', start - 1) + 1;
        var common = 0;
        while (common < before.Length - start && common < after.Length - start && before[^(common + 1)] == after[^(common + 1)])
        {
            common++;
        }

        // the edit ends where a line starts, or at the end of the text.
        var end = before.Length - common;
        if (end > start && before[end - 1] != '\n')
        {
            var next = before.IndexOf('\n', end);
            end = next < 0 ? before.Length : next + 1;
        }

        var lines = TextLines.Of(before);
        return new RefactorTextEdit
        {
            Range = new SourceRange(lines.PositionOf(start), lines.PositionOf(end)),
            NewText = after[start..(after.Length - (before.Length - end))],
        };
    }

    private static string NotVerified(Site site, string why, Func<Uri, SourcePosition, string> locate)
        => new InlineRefusal(InlineRefusalKind.NotVerified, why, new SourceLocation(site.Module, site.Name)).ToMessage(locate);

    private static RefactorNotice Refused(RenameWorkspace.ProjectPart? part, InlineRefusal refusal, string? item) => new()
    {
        Code = Code(refusal.Kind.ToString()),
        Kind = RefactorNoticeKind.Refusal,
        Message = refusal.ToMessage(part is null ? null : RenameWorkspace.Locator(part)),
        Location = refusal.Location is { } location ? LocationOf(part, location.Uri, location.Range) : null,
        Choice = item is null ? null : SitesChoice,
        Item = item,
    };

    // why the member is kept: a note, which says where the reference that keeps it is.
    private static RefactorNotice Kept(RenameWorkspace.ProjectPart part, string member, InlineRemoval removal) => new()
    {
        Code = Code($"Removal{removal.Kind}"),
        Kind = RefactorNoticeKind.Note,
        Message = InlineRefusal.Format($"InlineMember_Removal_{removal.Kind}", null, member,
            removal.Location is { } location ? $"({RenameWorkspace.Locator(part)(location.Uri, location.Range.Start)})" : string.Empty),
        Location = removal.Location is { } at ? LocationOf(part, at.Uri, at.Range) : null,
        Choice = RemoveChoice,
    };

    // a text of a module, parsed as its document is: the designer of a form is its document's, which the text of its code does not hold.
    private async Task<ModuleParseResult> ParseAsync(RenameWorkspace.DocumentModule module, string text, CancellationToken token)
        => await parsing.ParseFragmentAsync(module.Document.Id.Uri.ToUri(), text, token) with { Designer = module.Parse.Designer };

    // where a range of a module is, in the text of the document that module is.
    private static SourceLocation? LocationOf(RenameWorkspace.ProjectPart? part, Uri module, SourceRange range)
        => part?.Modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == module.AbsoluteUri) is { } document
            ? new SourceLocation(document.Document.Id.Uri.ToUri(), SourceRangeOf(document.ToTextRange(range)))
            : null;

    private static SourceRange SourceRangeOf(OmniSharp.Extensions.LanguageServer.Protocol.Models.Range range)
        => new(range.Start.Line, range.Start.Character, range.End.Line, range.End.Character);

    private static string Code(string kind) => $"inlineMember.{char.ToLowerInvariant(kind[0])}{kind[1..]}";
}
