using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Completion;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Platform.Commands;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Semantics.Static;
using RDCore.SDK.Semantics.Static.Abstract;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Workspace;
using RDCore.Symbols;

namespace RDCore.LanguageServer.Completion;

/// <summary>
/// What can be written at a position of a module (<strong>LSP 3.17</strong> §Completion Request): the names a list offers there, of a document the server holds, or
/// of a module a client supplies.
/// </summary>
internal interface ICompletionService
{
    /// <summary>
    /// The completion list at a position of a document the server holds.
    /// </summary>
    /// <param name="documentUri">The document.</param>
    /// <param name="position">The position of the cursor, in the text of the document.</param>
    /// <param name="token">A token that cancels the request.</param>
    /// <returns>The list; empty for a document the server does not hold, and where nothing is written - in a comment, in a string literal.</returns>
    Task<CompletionList> CompleteDocumentAsync(Uri documentUri, Position position, CancellationToken token);

    /// <summary>
    /// The completion list at a position of a module a client supplies, which stands for the module of the workspace by its name.
    /// </summary>
    /// <param name="moduleName">The module's programmatic name.</param>
    /// <param name="source">The source of the module.</param>
    /// <param name="position">The position of the cursor, in <paramref name="source"/>.</param>
    /// <param name="token">A token that cancels the request.</param>
    Task<CompletionList> CompleteFragmentAsync(string moduleName, string source, Position position, CancellationToken token);

    /// <summary>
    /// The completion list of the arguments of a client's command that is being typed at its prompt, as its verb describes them (<see cref="VerbDescriptor"/>).
    /// </summary>
    /// <param name="moduleName">The module's programmatic name: what the command acts on.</param>
    /// <param name="source">The source of the module, whose lines a line number names and whose procedure an expression is evaluated in.</param>
    /// <param name="command">The verb, and what is typed after it.</param>
    /// <param name="token">A token that cancels the request.</param>
    Task<CompletionList> CompleteCommandAsync(string moduleName, string source, SessionCommandCompletion command, CancellationToken token);

    /// <summary>
    /// What the call being written at a position of a document the server holds takes (<strong>LSP 3.17</strong> §Signature Help): the signature of what is
    /// called, the parameter the argument at the cursor is given to, and what both do.
    /// </summary>
    /// <param name="documentUri">The document.</param>
    /// <param name="position">The position of the cursor, in the text of the document.</param>
    /// <param name="token">A token that cancels the request.</param>
    /// <returns>The signature help; <see langword="null"/> where no call is being written, or what is called is no procedure the workspace has.</returns>
    Task<SignatureHelp?> SignatureHelpDocumentAsync(Uri documentUri, Position position, CancellationToken token);

    /// <summary>
    /// What the call being written at a position of a module a client supplies takes, the module standing for the module of the workspace by its name.
    /// </summary>
    /// <param name="moduleName">The module's programmatic name.</param>
    /// <param name="source">The source of the module.</param>
    /// <param name="position">The position of the cursor, in <paramref name="source"/>.</param>
    /// <param name="token">A token that cancels the request.</param>
    Task<SignatureHelp?> SignatureHelpFragmentAsync(string moduleName, string source, Position position, CancellationToken token);
}

/// <inheritdoc cref="ICompletionService"/>
/// <remarks>
/// <para>
/// Code that is being typed seldom parses: <c>x = Widget.</c> is a syntax error until the member is written. What is known for sure is the text of the line before
/// the cursor (<see cref="CompletionSite"/>), and the rest of the module, which the line is taken out of before it is parsed - so that what the module declares
/// is known whatever the line is. The line goes back in as the one thing the list needs of it: for a member access, an assignment of the expression the member is
/// accessed on, which the static semantics give the type of, and the type its members.
/// </para>
/// <para>
/// The module is composed with the workspace's other modules and the libraries the project references, in their place, as the symbols of the workspace are
/// (<see cref="WorkspaceSymbolResolver"/>); nothing of it is sent to the environment host, whose model is of the code that is loaded and not of the code being typed.
/// </para>
/// <para>
/// An <c>Object</c> or a <c>Variant</c> has no members known when the code is written. For a variable of one, the list offers the members of the classes the module
/// <c>Set</c>s it to an object of, when <c>CreateObject</c> creates it with a constant identifier (<see cref="ObjectClassInference"/>) - read from the library's
/// description when the project does not reference it, without the workspace being composed with it. That is a fact for the list only: the member stays late-bound.
/// </para>
/// </remarks>
internal sealed class CompletionService(
    IWorkspaceDocumentService documents,
    IParsingClientService parsing,
    ISymbolResolver resolver,
    IReferencedLibraryService referencedLibraries,
    ILibrarySource librarySource,
    IClientVerbRegistry verbs,
    IOptions<SdkAppOptions> options,
    ILogger<CompletionService> logger) : ICompletionService
{
    // the name the expression a member is accessed on is assigned to, in the line that stands for the one being typed: a name no module declares.
    private const string Probe = "RDCoreCompletionProbe";

    private static readonly CompletionList Empty = new(isIncomplete: false);

    public async Task<CompletionList> CompleteDocumentAsync(Uri documentUri, Position position, CancellationToken token)
    {
        if (!documents.TryGetDocument(documentUri, out var document))
        {
            return Empty;
        }

        // a program of the platform's BASIC is the body of a module's one procedure: its lines are lines of that module further down.
        var isProgram = BasicProgramText.IsProgram(document.Id.Uri.GetFileSystemPath());
        var source = ParsingClientService.ModuleSourceOf(document);
        var line = isProgram ? position.Line + BasicProgramText.HeaderLines : position.Line;

        var moduleName = (parsing.TryGetCached(documentUri, out var cached) ? cached.SyntaxTree?.GetDeclaredName() : null) ?? document.Name;
        return await CompleteAsync(documentUri, moduleName, ParsingClientService.ModuleTypeOf(document), source, line, position.Character, token);
    }

    public Task<CompletionList> CompleteFragmentAsync(string moduleName, string source, Position position, CancellationToken token)
    {
        var moduleUri = new UriBuilder(new Uri(documents.WorkspaceRoot)) { Fragment = moduleName }.Uri;
        var kind = ModuleHeader.IsClassModule(source) == true ? ModuleType.ClassModule : ModuleType.StdModule;
        return CompleteAsync(moduleUri, moduleName, kind, source, position.Line, position.Character, token);
    }

    public async Task<CompletionList> CompleteCommandAsync(string moduleName, string source, SessionCommandCompletion command, CancellationToken token)
    {
        // a verb the client did not register is one nothing is known of: its arguments are offered nothing.
        if (!verbs.TryGet(command.Verb, out var verb))
        {
            return Empty;
        }

        var items = new List<CompletionItem>();
        foreach (var site in VerbCompletion.Read(verb, command.Arguments))
        {
            switch (site.Argument.Kind)
            {
                case VerbArgumentKind.Keyword or VerbArgumentKind.Choice:
                    items.AddRange(site.Argument.Values.Select((value, index) => new CompletionItem
                    {
                        Label = value.Value,
                        Kind = site.Argument.Kind == VerbArgumentKind.Keyword ? CompletionItemKind.Keyword : CompletionItemKind.EnumMember,
                        Detail = value.Description,
                        SortText = $"1{index:D4}",
                    }));
                    break;

                case VerbArgumentKind.LineNumber:
                    items.AddRange(CompletionPlacement.NumberedLinesOf(source.Split('\n')).Select((line, index) => new CompletionItem
                    {
                        Label = line.Number,
                        Kind = CompletionItemKind.Value,
                        Detail = line.Statement,
                        SortText = $"0{index:D6}",
                    }));
                    break;

                case VerbArgumentKind.Expression:
                    items.AddRange(await CompleteExpressionAsync(moduleName, source, site.Typed, site.EvaluatedAt, token));
                    break;

                // a key is completed a part at a time, as a member is: the client asks again after each `.`, for what is in the section before it.
                case VerbArgumentKind.Setting:
                    items.AddRange(VerbCompletion.SettingPartsAfter(site.Argument.Settings, site.Typed).Select((part, index) => new CompletionItem
                    {
                        Label = part.Part,
                        Kind = part.Setting is null ? CompletionItemKind.Module : CompletionItemKind.Property,
                        Detail = part.Setting?.Description,
                        SortText = $"0{index:D4}",
                    }));
                    break;

                case VerbArgumentKind.SettingValue when site.Setting is { } setting:
                    items.AddRange(setting.Choices.Select((choice, index) => new CompletionItem
                    {
                        Label = choice,
                        Kind = CompletionItemKind.EnumMember,
                        SortText = $"0{index:D4}",
                    }));
                    break;
            }
        }

        return new CompletionList(items.DistinctBy(item => item.Label, StringComparer.OrdinalIgnoreCase), isIncomplete: false);
    }

    // the name of the procedure an expression evaluated in the module is completed in: it is the module's scope, and no name of it.
    private const string ModuleScope = "RDCoreCompletionScope";

    // an expression of a command is completed where it is evaluated (VerbArgument.EvaluatedAt): as the operand of a statement written just before the line it is
    // evaluated at - in that line's procedure and blocks, where the program waits - or, at no line the module has, in a procedure of its own added to the module,
    // where the names are the module's and the project's.
    private async Task<IEnumerable<CompletionItem>> CompleteExpressionAsync(string moduleName, string source, string expression, string? line, CancellationToken token)
    {
        const string Statement = "Debug.Print ";
        var lines = source.Split('\n').ToList();
        var at = line is null ? -1 : CompletionPlacement.LineLabelled(lines, line);
        if (at < 0)
        {
            at = lines.Count;
            lines.AddRange([$"Private Sub {ModuleScope}()\r", string.Empty, "End Sub\r"]);
            at++;
        }

        lines.Insert(at, Statement + expression + "\r");
        var list = await CompleteFragmentAsync(moduleName, string.Join('\n', lines), new Position(at, Statement.Length + expression.Length), token);
        return list.Items.Where(item => item.Label != ModuleScope);
    }

    private async Task<CompletionList> CompleteAsync(Uri address, string moduleName, ModuleType kind, string source, int line, int character, CancellationToken token)
    {
        var lines = source.Split('\n');
        if (line < 0 || line >= lines.Length)
        {
            return Empty;
        }

        var text = lines[line].TrimEnd('\r');
        var context = CompletionSite.Read(text[..Math.Clamp(character, 0, text.Length)]);
        if (context.Kind == CompletionSiteKind.None)
        {
            return Empty;
        }

        // where the line is - in which procedure, in which blocks - is read off the lines as they are, which need not parse.
        var placement = CompletionPlacement.Read(lines, line);

        // the line is taken out, and only what the list needs of it goes back in.
        if (await ComposeAsync(address, moduleName, kind, lines, line, context is { Kind: CompletionSiteKind.Member } ? context.Qualifier : string.Empty, token)
            is not { } composed)
        {
            return Empty;
        }

        var (composition, module, workspaceRoot, moduleUri) = composed;
        var tree = composition.ScopeTree;
        var origin = CompletionCandidates.OriginAt(tree, moduleUri, new SourcePosition(line, character));
        var candidates = context.Kind switch
        {
            CompletionSiteKind.Name => CompletionRules.At(tree, origin, context, placement,
                new CompletionLanguage(kind == ModuleType.ClassModule, options.Value.Workspace.SupportedLanguage.HasBarePrint)),
            CompletionSiteKind.Type or CompletionSiteKind.NewType => TypesOf(composition, origin, context),
            _ => MembersOf(composition, workspaceRoot, moduleUri, origin, context, module, line),
        };

        // a list is for the name being typed: what has the letters typed so far, in their order, is all a client can still show of it as more is typed.
        var matching = CompletionCandidates.Matching(candidates, context.Prefix);
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("💡 {module} {line}:{character} ({kind} '{prefix}'): {count} of {all} candidate(s).",
                moduleName, line, character, context.Kind, context.Prefix, matching.Count, candidates.Count);
        }

        return new CompletionList(matching.Select(candidate => candidate.ToCompletionItem()), isIncomplete: false);
    }

    public async Task<SignatureHelp?> SignatureHelpDocumentAsync(Uri documentUri, Position position, CancellationToken token)
    {
        if (!documents.TryGetDocument(documentUri, out var document))
        {
            return null;
        }

        var isProgram = BasicProgramText.IsProgram(document.Id.Uri.GetFileSystemPath());
        var source = ParsingClientService.ModuleSourceOf(document);
        var line = isProgram ? position.Line + BasicProgramText.HeaderLines : position.Line;

        var moduleName = (parsing.TryGetCached(documentUri, out var cached) ? cached.SyntaxTree?.GetDeclaredName() : null) ?? document.Name;
        return await SignatureHelpAsync(documentUri, moduleName, ParsingClientService.ModuleTypeOf(document), source, line, position.Character, token);
    }

    public Task<SignatureHelp?> SignatureHelpFragmentAsync(string moduleName, string source, Position position, CancellationToken token)
    {
        var moduleUri = new UriBuilder(new Uri(documents.WorkspaceRoot)) { Fragment = moduleName }.Uri;
        var kind = ModuleHeader.IsClassModule(source) == true ? ModuleType.ClassModule : ModuleType.StdModule;
        return SignatureHelpAsync(moduleUri, moduleName, kind, source, position.Line, position.Character, token);
    }

    // the call being written, as its procedure is found: a name in the scope of the line, or a member of what its qualifier is - as a completion list after the
    // qualifier's dot would offer it.
    private async Task<SignatureHelp?> SignatureHelpAsync(Uri address, string moduleName, ModuleType kind, string source, int line, int character, CancellationToken token)
    {
        var lines = source.Split('\n');
        if (line < 0 || line >= lines.Length)
        {
            return null;
        }

        var text = lines[line].TrimEnd('\r');
        if (SignatureSite.Read(text[..Math.Clamp(character, 0, text.Length)]) is not { } site
            || await ComposeAsync(address, moduleName, kind, lines, line, site.IsMemberAccess ? site.Qualifier : string.Empty, token) is not { } composed)
        {
            return null;
        }

        var (composition, module, workspaceRoot, moduleUri) = composed;
        var origin = CompletionCandidates.OriginAt(composition.ScopeTree, moduleUri, new SourcePosition(line, character));
        var procedure = site.IsMemberAccess
            ? MembersOf(composition, workspaceRoot, moduleUri, origin, new CompletionSite(CompletionSiteKind.Member, Qualifier: site.Qualifier), module, line)
                .FirstOrDefault(candidate => string.Equals(candidate.Label, site.Name, StringComparison.OrdinalIgnoreCase))?.Symbol
            : composition.Resolver.ResolveValue(site.Name, SDK.Model.Symbols.Abstract.ScopeKind.Global, origin).Symbol;

        if (procedure is null || SignatureDescription.Of(procedure) is not { } signature)
        {
            return null;
        }

        var active = signature.ActiveParameterOf(site.ArgumentIndex, site.ArgumentName);
        return new SignatureHelp
        {
            Signatures = new Container<SignatureInformation>(signature.ToSignatureInformation(active)),
            ActiveSignature = 0,
            ActiveParameter = active,
        };
    }

    // the module with its line taken out - and, for a member access, put back as an assignment of what the member is accessed on - parsed and composed with the
    // workspace; nothing when the module cannot be parsed even so.
    private async Task<(WorkspaceComposition Composition, ModuleNode Module, Uri WorkspaceRoot, Uri ModuleUri)?> ComposeAsync(
        Uri address, string moduleName, ModuleType kind, string[] lines, int line, string qualifier, CancellationToken token)
    {
        lines[line] = (qualifier.Length > 0 ? $"{Probe} = {qualifier}" : string.Empty) + (lines[line].EndsWith('\r') ? "\r" : string.Empty);
        var parse = await parsing.ParseFragmentAsync(address, string.Join('\n', lines), token);
        if (parse.SyntaxTree is null)
        {
            return null;
        }

        // a form's controls are declared by its designer, which the parse of a fragment of its text does not read: the document's own parse has it.
        if (parsing.TryGetCached(address, out var cached) && cached.Designer is { } designer)
        {
            parse = parse with { Designer = designer };
        }

        var workspaceRoot = new Uri(documents.WorkspaceRoot);
        var moduleUri = new UriBuilder(workspaceRoot) { Fragment = moduleName }.Uri;
        var composition = WorkspaceSymbolResolver.ComposeWithScopes(
            workspaceRoot, [.. OtherModules(workspaceRoot, moduleName), (moduleUri, kind, parse)], resolver, projectName: referencedLibraries.ProjectName,
            implicitScope: options.Value.Workspace.SupportedLanguage.ImplicitDeclarationScope,
            libraries: referencedLibraries.SymbolsUnder(workspaceRoot));

        return (composition, parse.SyntaxTree, workspaceRoot, moduleUri);
    }

    // the workspace's modules but the one being completed, as the symbol sync composes them.
    private IEnumerable<(Uri ModuleUri, ModuleType ModuleType, ModuleParseResult Parse)> OtherModules(Uri workspaceRoot, string moduleName)
    {
        foreach (var document in documents.GetAllDocuments())
        {
            if (!parsing.TryGetCached(document.Id.Uri.ToUri(), out var parse) || parse.SyntaxTree is null)
            {
                continue;
            }

            var name = parse.SyntaxTree.GetDeclaredName() ?? document.Name;
            if (!string.Equals(name, moduleName, StringComparison.OrdinalIgnoreCase))
            {
                yield return (new UriBuilder(workspaceRoot) { Fragment = name }.Uri, ParsingClientService.ModuleTypeOf(document), parse);
            }
        }
    }

    private static IReadOnlyList<CompletionCandidate> TypesOf(WorkspaceComposition composition, Uri origin, CompletionSite context)
    {
        var creatable = context.Kind == CompletionSiteKind.NewType;
        if (context.Qualifier.Length == 0)
        {
            return CompletionCandidates.Types(composition.ScopeTree, origin, creatable);
        }

        return CompletionCandidates.NamespaceOf(composition.Resolver, origin, context.Qualifier, typeContext: true) is { } owner
            ? CompletionCandidates.TypesOf(composition.ScopeTree, owner, creatable)
            : [];
    }

    private IReadOnlyList<CompletionCandidate> MembersOf(
        WorkspaceComposition composition, Uri workspaceRoot, Uri moduleUri, Uri origin, CompletionSite context, ModuleNode module, int line)
    {
        var scope = composition.ScopeTree.ScopeFor(origin);
        var site = new MemberSite(composition, workspaceRoot, moduleUri, module, scope,
            new StaticEnvironment(options.Value.Environment.Is64Bit) { Libraries = librarySource });

        // the object of the With blocks the line is in, outermost first: the expression of a nested one can start with a `.` of its own.
        (ExpressionNode Expression, VBType? Outer)? innermost = null;
        VBType? with = null;
        foreach (var block in module.Descendants().OfType<WithStatementNode>().Where(node => Spans(node, line)))
        {
            innermost = (block.WithExpression, with);
            with = TypeOf(site, block.WithExpression, with);
        }

        if (context.Qualifier.Length == 0)
        {
            return innermost is { } block ? MembersOfValue(site, block.Expression, block.Outer, with) : [];
        }

        if (CompletionCandidates.NamespaceOf(composition.Resolver, origin, context.Qualifier, typeContext: false) is { } owner)
        {
            return CompletionCandidates.MembersOf(composition.ScopeTree, owner, origin);
        }

        var probe = module.Descendants().OfType<AssignmentStatementNode>().FirstOrDefault(node => node.SourceLocation.Range.Start.Line == line);
        return probe is not null ? MembersOfValue(site, probe.Value, with, TypeOf(site, probe.Value, with)) : [];
    }

    // where the members are listed: the composed workspace, the module being completed, and the scope of the line.
    private sealed record class MemberSite(
        WorkspaceComposition Composition, Uri WorkspaceRoot, Uri ModuleUri, ModuleNode Module, LexicalScope Scope, StaticEnvironment Environment);

    // the members of the value of an expression: those of its declared type; or, when that is an Object or a Variant, which has none known when the code is
    // written, those of the classes the module Sets it to an object of - a fact for the list, which changes nothing of what the expression is.
    private IReadOnlyList<CompletionCandidate> MembersOfValue(MemberSite site, ExpressionNode expression, VBType? with, VBType? type)
    {
        var context = new StaticEvaluationContext(site.Composition.Resolver, site.Scope, EnclosingWithTargetType: with, Environment: site.Environment);
        if (type is not (VBObjectType or VBVariantType))
        {
            if (type is null)
            {
                return [];
            }

            // a form's control is its class and the extender it is wrapped in: Left, SetFocus, Visible (FormControlExtender)
            return ObjectValueStaticSemantics.DesignerControlOf(context, expression) is not null
                && FormControlExtender.Of(site.Composition.Resolver, site.ModuleUri) is { } extender
                ? [.. CompletionCandidates.MembersOf(type).Concat(CompletionCandidates.MembersOf(VBClassType.Of(extender)))
                    .DistinctBy(candidate => candidate.Label, StringComparer.OrdinalIgnoreCase)]
                : CompletionCandidates.MembersOf(type);
        }

        return [.. ObjectClassInference.CandidateClassesOf(context, expression, site.Composition.ScopeTree, site.ModuleUri, site.Module)
            .Select(candidate => ObjectClassInference.ClassOf(candidate, site.Composition.Resolver, site.Scope.Uri,
                library => referencedLibraries.DescribedSymbolsOf(site.WorkspaceRoot, library)))
            .OfType<VBClassType>()
            .SelectMany(CompletionCandidates.MembersOf)
            .DistinctBy(candidate => candidate.Label, StringComparer.OrdinalIgnoreCase)];
    }

    // a block that the line is inside of: between its first line and its last.
    private static bool Spans(SyntaxNode node, int line) => node.SourceLocation.Range.Start.Line < line && line < node.SourceLocation.Range.End.Line;

    // the declared type of an expression, as the static semantics give it; nothing for an expression they find an error in.
    private static VBType? TypeOf(MemberSite site, ExpressionNode expression, VBType? with)
    {
        var context = new StaticEvaluationContext(site.Composition.Resolver, site.Scope, EnclosingWithTargetType: with, Environment: site.Environment);
        var result = ExpressionStaticSemanticsEvaluator.Evaluate(context, expression);
        return result.IsSuccess ? result.Result : null;
    }
}
