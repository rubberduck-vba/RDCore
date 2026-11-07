using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Runtime.Abstract.Execution;
using WorkspaceSymbol = RDCore.SDK.Model.Symbols.Abstract.WorkspaceSymbol;

namespace RDCore.SDK.Model.Symbols.Completion;

/// <summary>
/// One item of a completion list: a name that can be written where the list was asked for, and what it names.
/// </summary>
/// <param name="Label">The name, as it is written.</param>
/// <param name="Kind">What kind of thing it names, as a client shows it.</param>
/// <param name="Detail">What a client shows next to the name: the type of a variable, the signature of a procedure; <see langword="null"/> when there is nothing to say.</param>
/// <param name="Documentation">What the name is, in a sentence or two: the doc string of a library member, chiefly.</param>
/// <param name="Symbol">The symbol the name binds to; <see langword="null"/> for a keyword or an intrinsic type name.</param>
/// <param name="Rank">
/// Where the candidate ranks among the others, ahead of the alphabetical order of their names: what is declared nearer to where the list was asked for comes first,
/// and the keywords last.
/// </param>
public sealed record class CompletionCandidate(string Label, CompletionItemKind Kind, string? Detail = null, string? Documentation = null, Symbol? Symbol = null, int Rank = 0)
{
    /// <summary>
    /// What is inserted in place of the word when it is not the label: the name alone of a named argument whose <c>:=</c> is written already
    /// (<c>Send "a", Cop|:=2</c>); <see langword="null"/> for the label.
    /// </summary>
    public string? InsertText { get; init; }

    /// <summary>
    /// The candidate as an item of an LSP completion list (<strong>LSP 3.17</strong> §Completion Request).
    /// </summary>
    /// <remarks>
    /// <para>
    /// What is filtered by is the label, which is what a client takes when an item has no <c>filterText</c>, and so is what is inserted unless
    /// <see cref="InsertText"/> says otherwise: neither is written for an item that does not need it, since a list can have a thousand items, and each travels.
    /// </para>
    /// <para>
    /// The <c>sortText</c> orders a label the word starts before one whose humps it matches, and both before one it does not match; then by
    /// <see cref="Rank"/>, then by the label in any case.
    /// </para>
    /// </remarks>
    /// <param name="match">How the label matches the word typed (<see cref="CompletionCandidates.MatchOf"/>).</param>
    public CompletionItem ToCompletionItem(CompletionMatch match = CompletionMatch.Prefix) => new()
    {
        Label = Label,
        Kind = Kind,
        InsertText = InsertText,
        Detail = Detail,
        Documentation = Documentation is { Length: > 0 } documentation ? new StringOrMarkupContent(documentation) : null,
        SortText = $"{TierOf(match)}{Rank:D2}{Label.ToLowerInvariant()}",
    };

    internal static char TierOf(CompletionMatch match) => match switch
    {
        CompletionMatch.Prefix => '0',
        CompletionMatch.Humps => '1',
        _ => '2',
    };
}

/// <summary>
/// Candidates a completion list ranks before the others, as a tier of their own (<see cref="CompletionCandidates.ListOf"/>).
/// </summary>
/// <param name="Candidates">The candidates, in the order they rank where the word matches them alike.</param>
/// <param name="IsSelectedWhenNothingIsTyped">
/// Whether the best of them is preselected where no part of the word is typed yet. The constants of the <c>Enum</c> an argument's parameter is declared as
/// are: one of them is what is most likely written there. The named arguments are not: a value is as likely as a name, and a name is selected once the
/// word typed matches it.
/// </param>
public sealed record class CompletionGroup(IReadOnlyList<CompletionCandidate> Candidates, bool IsSelectedWhenNothingIsTyped = true);

/// <summary>
/// How a label matches the word being typed, which decides whether it is offered and where it ranks (<see cref="CompletionCandidates.MatchOf"/>).
/// </summary>
public enum CompletionMatch
{
    /// <summary>
    /// The label does not match: it is not offered.
    /// </summary>
    None,

    /// <summary>
    /// The word is the start of the label, in any case; an empty word is the start of every label.
    /// </summary>
    Prefix,

    /// <summary>
    /// The word is not the start of the label, and matches its humps: <c>MP</c> for <c>MultiPlayer</c>. It ranks after every <see cref="Prefix"/> match.
    /// </summary>
    Humps,
}

/// <summary>
/// The names a completion list offers, as the scope tree of a composed workspace has them: the names visible from where the list is asked for, the members of a
/// module, a project or a type, and the keywords of the language.
/// </summary>
/// <remarks>
/// <para>
/// The names are the ones the resolver would bind (<see cref="ScopeTreeSymbolResolver"/>): a name declared nearer shadows one of the same name declared farther, and
/// is the only one offered. What a scope declares that is not a name in the binding context the list is for is not offered - a user-defined type is no value, a
/// local is no type (<strong>MS-VBAL §5.6.4</strong>). A member declared <c>Private</c> is offered only where it is accessible, which is in its own module
/// (<strong>MS-VBAL §5.2.3</strong>), and a member a library hides (<c>VB_MemberFlags</c>, <see cref="SymbolProperties.HiddenMemberFlag"/>) is not offered at all,
/// as the VBE's own lists leave it out.
/// </para>
/// <para>
/// A name is offered once whatever the number of its declarations: the <c>Get</c>, the <c>Let</c> and the <c>Set</c> of a property are one property.
/// </para>
/// </remarks>
public static class CompletionCandidates
{
    // the intrinsic types a declared type can name (MS-VBAL §5.6.13.3 <type-expression>); Decimal is a subtype of Variant, which nothing is declared as.
    private static readonly string[] IntrinsicTypeNames =
    [
        VBTypeNames.VBBoolean, VBTypeNames.VBByte, VBTypeNames.VBCurrency, VBTypeNames.VBDate, VBTypeNames.VBDouble, VBTypeNames.VBInteger, VBTypeNames.VBLong,
        VBTypeNames.VBLongLong, VBTypeNames.VBLongPtr, VBTypeNames.VBObject, VBTypeNames.VBSingle, VBTypeNames.VBString, VBTypeNames.VBVariant,
    ];

    // ranks: what the procedure declares before what its module does, before what the project does, before what the libraries and the language have.
    private const int ProcedureRank = 0;
    private const int ModuleRank = 1;
    private const int ProjectRank = 2;
    private const int GlobalRank = 3;
    private const int KeywordRank = 4;

    /// <summary>
    /// How <paramref name="word"/> matches <paramref name="label"/>: as its start, as its humps (the way Visual Studio matches PascalCase names), or not at all.
    /// Case does not tell one name from another in VBA (<strong>MS-VBAL §3.3.5</strong>), and every comparison here ignores it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A label is cut into humps, and the underscores between them belong to none:
    /// </para>
    /// <list type="bullet">
    /// <item><description>the first letter starts a hump, and so does the first character after an underscore;</description></item>
    /// <item><description>a capital after a lowercase letter or a digit starts one: <c>vbKeyEscape</c> is <c>vb</c>, <c>Key</c>, <c>Escape</c>, so a lowercase
    /// prefix is a hump of its own;</description></item>
    /// <item><description>a run of capitals is one hump, and its last capital starts the next when a lowercase letter follows it: <c>CLngPtr</c> is <c>C</c>,
    /// <c>Lng</c>, <c>Ptr</c>, <c>IOError</c> is <c>IO</c>, <c>Error</c>, and <c>VBA</c> is one hump;</description></item>
    /// <item><description>a run of digits is one hump, and a letter after it starts the next: <c>Win32Api</c> is <c>Win</c>, <c>32</c>, <c>Api</c>;</description></item>
    /// <item><description><c>Class_Initialize</c> is <c>Class</c>, <c>Initialize</c>, and <c>_NewEnum</c> is <c>New</c>, <c>Enum</c>.</description></item>
    /// </list>
    /// <para>
    /// The word matches the humps when it can be cut into as many parts as there are humps from the first, each part the start of its hump, in order: no hump
    /// is skipped, and the humps after the last part are not needed. <c>MP</c> and <c>MuPl</c> match <c>MultiPlayer</c>, <c>LoPi</c> matches
    /// <c>LoadPicture</c>, <c>vbKEs</c> matches <c>vbKeyEscape</c>, <c>CLP</c> matches <c>CLngPtr</c>. <c>Pr</c> does not match <c>CLngPtr</c>,
    /// <c>Player</c> does not match <c>MultiPlayer</c>, and <c>WA</c> does not match <c>Win32Api</c>.
    /// </para>
    /// <para>
    /// An underscore typed ends a part and matches nothing itself: what follows it is the start of the next hump, as after an underscore in a label, and
    /// underscores typed in a row are one. <c>C_I</c> and <c>C_</c> match <c>Class_Initialize</c>; <c>Cl_ass</c> does not. A word that starts with an
    /// underscore matches the humps of a label that starts with one: <c>_NEn</c> matches <c>_NewEnum</c>, and <c>_x</c> does not match <c>xyz</c>.
    /// </para>
    /// <para>
    /// A label that a word matches is matched by the word without its last character as well, so a client narrows the list of a word by this rule as the
    /// word grows. The match allocates nothing for a word of up to 128 characters.
    /// </para>
    /// </remarks>
    /// <param name="label">The label of a candidate: a name, as it is written.</param>
    /// <param name="word">What is typed of the name.</param>
    /// <returns><see cref="CompletionMatch.Prefix"/> when the word is the start of the label, else <see cref="CompletionMatch.Humps"/> when it matches its
    /// humps, else <see cref="CompletionMatch.None"/>.</returns>
    public static CompletionMatch MatchOf(string label, string word)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(word);
        if (label.StartsWith(word, StringComparison.OrdinalIgnoreCase))
        {
            return CompletionMatch.Prefix;
        }

        // the underscores a word starts with are of a label that starts with one, and start no part.
        var first = PastUnderscores(word, 0);
        if (first == word.Length || first > 0 && label is not ['_', ..])
        {
            return CompletionMatch.None;
        }

        // where a part of the word that is still to match a hump can start, after the humps walked so far; a part ends at the end of the word, or at an
        // underscore. Walked from the end, a start is taken before the starts it leads to are marked, which the next hump takes.
        Span<bool> starts = word.Length <= 128 ? stackalloc bool[word.Length] : new bool[word.Length];
        starts[first] = true;
        var position = 0;
        while (NextHump(label, ref position) is var (hump, length) && length > 0)
        {
            var live = false;
            for (var start = word.Length - 1; start >= 0; start--)
            {
                if (!starts[start])
                {
                    continue;
                }

                starts[start] = false;
                for (var taken = 1; taken <= length && start + taken <= word.Length; taken++)
                {
                    var character = word[start + taken - 1];
                    if (character == '_' || char.ToUpperInvariant(character) != char.ToUpperInvariant(label[hump + taken - 1]))
                    {
                        break;
                    }

                    var next = PastUnderscores(word, start + taken);
                    if (next == word.Length)
                    {
                        return CompletionMatch.Humps;
                    }

                    starts[next] = true;
                    live = true;
                }
            }

            if (!live)
            {
                return CompletionMatch.None;
            }
        }

        return CompletionMatch.None;
    }

    // the index of the first character at or after `index` that is no underscore.
    private static int PastUnderscores(string text, int index)
    {
        while (index < text.Length && text[index] == '_')
        {
            index++;
        }

        return index;
    }

    // the next hump of a label from `position`, as (start, length), `position` moved past it; a length of 0 when there is none.
    private static (int Start, int Length) NextHump(string label, ref int position)
    {
        position = PastUnderscores(label, position);
        var start = position;
        if (start == label.Length)
        {
            return (start, 0);
        }

        position++;
        while (position < label.Length && label[position] != '_' && !StartsHump(label, position))
        {
            position++;
        }

        return (start, position - start);
    }

    // whether the character at index starts a hump, the one before it being of the same hump or of the one before.
    private static bool StartsHump(string label, int index)
    {
        var character = label[index];
        var before = label[index - 1];
        if (char.IsDigit(character))
        {
            return !char.IsDigit(before);
        }

        if (char.IsDigit(before))
        {
            return true;
        }

        return char.IsUpper(character)
            && (char.IsLower(before) || char.IsUpper(before) && index + 1 < label.Length && char.IsLower(label[index + 1]));
    }

    /// <summary>
    /// The parameters of the call being written that can be named where the cursor is, each as the named argument that starts with it (<c>Title:=</c>): what
    /// a client inserts is the label, or the name alone where the <c>:=</c> after it is written already (<c>Send "a", Cop|:=2</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A name is offered where an argument starts (<see cref="SignatureSite.IsWhereAnArgumentStarts"/>), when the parameter that argument would be given to by
    /// position is <c>Optional</c>, or when a named argument is written before it: after one, every argument is named (<strong>MS-VBAL §5.6.13.1</strong>
    /// <c>positional-or-named-argument-list</c>). No name is offered where a positional argument is written after the cursor, an omitted one included
    /// (<see cref="SignatureSite.IsFollowedByAPositionalArgument"/>): it would follow a named one.
    /// </para>
    /// <para>
    /// A parameter that has an argument already is not offered (<strong>MS-VBAL §5.3.1.11</strong>: two arguments mapped to one parameter make the argument
    /// list incompatible): one given by position, an omitted one counted, and one named before or after the cursor, its name in any case. A <c>ParamArray</c>
    /// is not offered either: it takes the positional arguments left over, and there is no one argument to name.
    /// </para>
    /// <para>
    /// An external procedure (<c>Declare</c>) takes named arguments as a procedure of the language does (<strong>MS-VBAL §5.2.3.5</strong>). A procedure whose
    /// parameter names are placeholders (<see cref="SignatureDescription.NamesArePlaceholders"/>) is offered no names.
    /// </para>
    /// </remarks>
    /// <param name="signature">The signature of what is called.</param>
    /// <param name="site">The call being written.</param>
    /// <param name="word">What is typed of the name at the cursor.</param>
    /// <param name="after">The text of the logical line from the cursor on, where named arguments can be written already.</param>
    /// <returns>The named arguments, in the order the parameters are declared; none where a name cannot be written.</returns>
    public static IReadOnlyList<CompletionCandidate> NamedArguments(SignatureDescription signature, SignatureSite site, string word, string after)
    {
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(after);
        if (signature.NamesArePlaceholders || site.ArgumentName is not null || !site.IsWhereAnArgumentStarts(word) || site.IsFollowedByAPositionalArgument(after))
        {
            return [];
        }

        var namedBefore = site.NamesBefore;
        if (namedBefore.Count == 0 && (signature.ActiveParameterOf(site.ArgumentIndex) is not { } active || !signature.Parameters[active].IsOptional))
        {
            return [];
        }

        // the rest of the word, then the `:=` that makes it a name.
        var rest = after.AsSpan();
        while (rest.Length > 0 && (char.IsLetterOrDigit(rest[0]) || rest[0] == '_'))
        {
            rest = rest[1..];
        }

        var isNamed = rest.TrimStart(" \t").StartsWith(":=", StringComparison.Ordinal);
        var given = namedBefore.Concat(site.NamesAfter(after)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. signature.Parameters
            .Where((parameter, index) => index >= site.PositionalBefore && !parameter.IsParamArray && !given.Contains(parameter.Name))
            .Select(parameter => new CompletionCandidate($"{parameter.Name}:=", CompletionItemKind.Variable,
                parameter.TextIn(signature), parameter.Documentation) { InsertText = isNamed ? parameter.Name : null })];
    }

    /// <summary>
    /// The completion list for the word being typed (<strong>LSP 3.17</strong> §Completion Request): the candidates <paramref name="word"/> matches
    /// (<see cref="MatchOf"/>), each label once, the best of them preselected.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The groups of <paramref name="first"/> are tiers of their own, in their order, before every other candidate: <c>Paint DR</c> ranks and preselects the
    /// constant <c>DeepRed</c>, whose humps the word matches, before a function <c>Drum</c> the word starts. In each tier the labels the word starts rank
    /// before the ones whose humps it matches; then the candidates of a group keep their order, and the others rank by
    /// <see cref="CompletionCandidate.Rank"/>, then by label in any case. The item that ranks first is the one <c>preselect</c> marks, unless nothing is typed
    /// and its group is not selected then (<see cref="CompletionGroup.IsSelectedWhenNothingIsTyped"/>): the first item that can be is marked instead.
    /// </para>
    /// <para>
    /// The list is incomplete (<c>isIncomplete</c>): it is the list of this word, and a client asks again as the word grows. Every label a longer word
    /// matches is in it, so a client that narrows it by <see cref="MatchOf"/> itself narrows it to the same list.
    /// </para>
    /// </remarks>
    /// <param name="word">What is typed of the name; an empty word matches every candidate.</param>
    /// <param name="first">
    /// The groups of candidates most likely to be written, in the order they rank: the constants of the <c>Enum</c> an argument's parameter is declared as,
    /// then the named arguments (<see cref="NamedArguments"/>).
    /// </param>
    /// <param name="candidates">The other candidates; a label of <paramref name="first"/> is not offered again.</param>
    public static CompletionList ListOf(string word, IReadOnlyList<CompletionGroup> first, IReadOnlyList<CompletionCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(word);
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(candidates);
        var offered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<(CompletionItem Item, bool IsSelectable)>();
        foreach (var (group, rank) in first.Select((group, rank) => (group, rank)))
        {
            var isSelectable = word.Length > 0 || group.IsSelectedWhenNothingIsTyped;
            foreach (var (candidate, index) in group.Candidates.Select((candidate, index) => (candidate, index)))
            {
                if (MatchOf(candidate.Label, word) is var match and not CompletionMatch.None && offered.Add(candidate.Label))
                {
                    // a `!` sorts before the digit every other item's sort text starts with.
                    items.Add((candidate.ToCompletionItem(match) with { SortText = $"!{rank}{CompletionCandidate.TierOf(match)}{index:D3}" }, isSelectable));
                }
            }
        }

        foreach (var candidate in candidates)
        {
            if (MatchOf(candidate.Label, word) is var match and not CompletionMatch.None && offered.Add(candidate.Label))
            {
                items.Add((candidate.ToCompletionItem(match), true));
            }
        }

        items.Sort((left, right) => string.CompareOrdinal(left.Item.SortText, right.Item.SortText));
        var selected = items.FindIndex(item => item.IsSelectable);
        if (selected >= 0)
        {
            items[selected] = (items[selected].Item with { Preselect = true }, true);
        }

        return new CompletionList(items.Select(item => item.Item), isIncomplete: true);
    }

    /// <summary>
    /// Where a name written at <paramref name="position"/> of a module is looked up from: the procedure the position is in, or the module itself when it is in
    /// none - in the declarations section, say.
    /// </summary>
    /// <param name="tree">The scope tree of the composed workspace.</param>
    /// <param name="moduleUri">The module's own URI.</param>
    /// <param name="position">The position, in the module's source.</param>
    /// <returns>The URI of the symbol the lookup originates from.</returns>
    public static Uri OriginAt(ScopeTree tree, Uri moduleUri, SourcePosition position)
    {
        if (!tree.TryGetScope(moduleUri, out var module) || module.Kind != LexicalScopeKind.Module)
        {
            return moduleUri;
        }

        // the innermost procedure that contains the position, the one whose definition there starts last: a procedure a branch of a #If block writes whole is
        // within the lines of the procedure whose header that block splits (MemberDeclarationNode.HeaderAlternatives), and is the one the position is in.
        Uri? origin = null;
        SourcePosition latest = default;
        foreach (var declaration in module.Declarations.OfType<WorkspaceSymbol>())
        {
            if (!tree.TryGetScope(declaration.Uri, out var scope) || scope.Kind != LexicalScopeKind.Procedure || scope.Uri.AbsoluteUri != declaration.Uri.AbsoluteUri)
            {
                continue;
            }

            foreach (var definition in declaration.AllDefinitions.Where(definition => Contains(definition.Range, position)))
            {
                if (origin is null || definition.Range.Start.CompareTo(latest) > 0)
                {
                    (origin, latest) = (declaration.Uri, definition.Range.Start);
                }
            }
        }

        return origin ?? moduleUri;
    }

    private static bool Contains(SourceRange range, SourcePosition position)
        => range.Start.CompareTo(position) <= 0 && position.CompareTo(range.End) <= 0;

    /// <summary>
    /// The names that can be written as a simple name expression where <paramref name="origin"/> is: what the procedure, the module, the project, the libraries
    /// and the language declare, nearest first. The keywords are not names; which of them can be written where is <see cref="CompletionRules"/>' to say.
    /// </summary>
    /// <param name="tree">The scope tree of the composed workspace.</param>
    /// <param name="origin">Where the lookup originates (<see cref="OriginAt"/>).</param>
    public static IReadOnlyList<CompletionCandidate> Names(ScopeTree tree, Uri origin)
    {
        var candidates = new Candidates();
        foreach (var scope in tree.ScopeFor(origin).SelfAndAncestors())
        {
            var rank = RankOf(scope.Kind);
            foreach (var symbol in scope.Declarations.Where(symbol => IsValueName(symbol, scope.Kind)))
            {
                candidates.Add(Describe(symbol), rank);
            }
        }

        return candidates.ToList();
    }

    /// <summary>
    /// The names that can be written as a declared type where <paramref name="origin"/> is (<see cref="CompletionSiteKind.Type"/>), or as the class of a
    /// <c>New</c> (<see cref="CompletionSiteKind.NewType"/>). After <c>As</c>: the intrinsic types; the classes, user-defined types and Enums the project and the
    /// libraries declare; the projects and the libraries, which qualify a type name; and <c>New</c>. After <c>New</c>: the classes that can be created, and the
    /// projects and the libraries. Nothing that is no type is offered - not a procedural module, not a local, not a keyword but <c>New</c>.
    /// </summary>
    /// <param name="tree">The scope tree of the composed workspace.</param>
    /// <param name="origin">Where the lookup originates (<see cref="OriginAt"/>).</param>
    /// <param name="creatable">Whether only the classes that <c>New</c> can create are offered.</param>
    public static IReadOnlyList<CompletionCandidate> Types(ScopeTree tree, Uri origin, bool creatable)
    {
        var candidates = new Candidates();
        foreach (var scope in tree.ScopeFor(origin).SelfAndAncestors())
        {
            var rank = RankOf(scope.Kind);
            foreach (var symbol in scope.Declarations.Where(symbol => IsTypeName(symbol, creatable) || IsQualifier(symbol)))
            {
                candidates.Add(Describe(symbol), rank);
            }
        }

        if (!creatable)
        {
            // an intrinsic type is the language's own, and ranks before the types of the libraries.
            foreach (var name in IntrinsicTypeNames)
            {
                candidates.Add(new CompletionCandidate(name, CompletionItemKind.TypeParameter), ProjectRank);
            }

            // `As New` declares a variable that is auto-instantiated (MS-VBAL §5.2.3.1.1).
            candidates.Add(new CompletionCandidate("New", CompletionItemKind.Keyword), KeywordRank);
        }

        return candidates.ToList();
    }

    /// <summary>
    /// The classes that can be named where <paramref name="origin"/> is, as the interface of an <c>Implements</c> (<strong>MS-VBAL §5.2.4.2</strong>) or the type
    /// of a <c>TypeOf ... Is</c> (<strong>§5.6.9.8.1</strong>): the class modules of the project, and the classes of the libraries.
    /// </summary>
    /// <param name="tree">The scope tree of the composed workspace.</param>
    /// <param name="origin">Where the lookup originates (<see cref="OriginAt"/>).</param>
    public static IReadOnlyList<CompletionCandidate> Classes(ScopeTree tree, Uri origin)
        => [.. Types(tree, origin, creatable: false).Where(candidate => candidate.Kind is CompletionItemKind.Class or CompletionItemKind.Interface or CompletionItemKind.Folder)];

    /// <summary>
    /// The events the class module that <paramref name="origin"/> is in declares: what a <c>RaiseEvent</c> raises (<strong>MS-VBAL §5.4.2.20</strong>).
    /// </summary>
    /// <param name="tree">The scope tree of the composed workspace.</param>
    /// <param name="origin">Where the lookup originates (<see cref="OriginAt"/>).</param>
    public static IReadOnlyList<CompletionCandidate> EventsOf(ScopeTree tree, Uri origin)
    {
        var candidates = new Candidates();
        var module = tree.ScopeFor(origin).SelfAndAncestors().FirstOrDefault(scope => scope.Kind == LexicalScopeKind.Module);
        foreach (var symbol in module?.Declarations.Where(symbol => symbol.Kind == SymbolKindExt.Event && IsWritable(symbol)) ?? [])
        {
            candidates.Add(Describe(symbol), ModuleRank);
        }

        return candidates.ToList();
    }

    /// <summary>
    /// The types a project or a module declares, as the last part of a qualified type name (<c>VBA.Collection</c>, <c>Module1.Point</c>; <strong>MS-VBAL
    /// §5.6.12</strong>).
    /// </summary>
    /// <param name="tree">The scope tree of the composed workspace.</param>
    /// <param name="owner">The qualifier, as <see cref="ISymbolResolver.ResolveQualifier"/> bound it.</param>
    /// <param name="creatable">Whether only the classes that <c>New</c> can create are offered.</param>
    public static IReadOnlyList<CompletionCandidate> TypesOf(ScopeTree tree, Symbol owner, bool creatable)
    {
        var candidates = new Candidates();
        IEnumerable<Symbol> declared = owner switch
        {
            VBProjectSymbol project => tree.Global.Declarations.Concat(ProjectScopeOf(tree)?.Declarations ?? []).Where(symbol => IsOfProject(symbol, project)),
            VBModuleSymbol module when tree.TryGetScope(module.Uri, out var scope) && scope.Kind == LexicalScopeKind.Module => scope.Declarations,
            _ => [],
        };

        foreach (var symbol in declared.Where(symbol => IsTypeName(symbol, creatable) && !IsPrivate(symbol)))
        {
            candidates.Add(Describe(symbol), ModuleRank);
        }

        return candidates.ToList();
    }

    /// <summary>
    /// The members of a namespace - a project or a procedural module - as the right-hand side of a member access on it (<strong>MS-VBAL §5.6.12</strong>): the
    /// modules of a project and the members of its procedural modules, or the members of a module, its <c>Private</c> ones only where they are accessible.
    /// </summary>
    /// <param name="tree">The scope tree of the composed workspace.</param>
    /// <param name="owner">The project or the module.</param>
    /// <param name="origin">Where the lookup originates (<see cref="OriginAt"/>), which decides what is accessible.</param>
    public static IReadOnlyList<CompletionCandidate> MembersOf(ScopeTree tree, Symbol owner, Uri origin)
    {
        var candidates = new Candidates();
        switch (owner)
        {
            case VBProjectSymbol project:
                var modules = tree.Global.Declarations.OfType<VBModuleSymbol>().Where(module => IsOfProject(module, project)).ToList();
                foreach (var module in modules)
                {
                    candidates.Add(Describe(module), ModuleRank);
                }

                foreach (var module in modules.OfType<VBStandardModuleSymbol>())
                {
                    foreach (var member in DeclaredBy(tree, module).Where(member => !IsPrivate(member) && IsMemberName(member)))
                    {
                        candidates.Add(Describe(member), ProjectRank);
                    }
                }

                break;

            case VBModuleSymbol module:
                var inside = tree.ScopeFor(origin).SelfAndAncestors().Any(scope => scope.Uri.AbsoluteUri == module.Uri.AbsoluteUri);
                foreach (var member in DeclaredBy(tree, module).Where(member => (inside || !IsPrivate(member)) && IsMemberName(member)))
                {
                    candidates.Add(Describe(member), ModuleRank);
                }

                break;
        }

        return candidates.ToList();
    }

    /// <summary>
    /// The members of a value of the type <paramref name="type"/>, as the right-hand side of a member access on it (<strong>MS-VBAL §5.6.12</strong>): the members
    /// of the default interface of a class, the fields of a user-defined type, the constants of an Enum. None for a type that has no members known when the code is
    /// written - a <c>Variant</c>, an <c>Object</c> - whose members are bound when it runs.
    /// </summary>
    /// <param name="type">The declared type of the expression the member is accessed on.</param>
    public static IReadOnlyList<CompletionCandidate> MembersOf(VBType type)
    {
        var candidates = new Candidates();
        if (type is IVBMemberOwnerType owner && !owner.Members.IsDefault)
        {
            foreach (var member in owner.Members.Where(member => !IsPrivate(member) && IsMemberName(member)))
            {
                candidates.Add(Describe(member), ModuleRank);
            }
        }

        return candidates.ToList();
    }

    /// <summary>
    /// The constants of an <c>Enum</c>, by name: what an argument given to a parameter declared as it is most likely to be (<c>MsgBox "Hello", vbYesNo</c>).
    /// </summary>
    /// <remarks>
    /// A library's <c>Enum</c> carries its constants (<see cref="MembersOf(VBType)"/>). One the workspace declares does not: its constants are declarations of
    /// the module that declares it, each of them of the <c>Enum</c> (<see cref="VBEnumConstMemberSymbol.ParentUri"/>), and are found there.
    /// </remarks>
    /// <param name="tree">The scope tree of the composed workspace.</param>
    /// <param name="type">The <c>Enum</c>.</param>
    public static IReadOnlyList<CompletionCandidate> ConstantsOf(ScopeTree tree, VBEnumType type)
    {
        IEnumerable<CompletionCandidate> constants = MembersOf(type);
        if (!constants.Any() && type.Symbol is VBEnumMemberSymbol enumeration && tree.TryGetScope(enumeration.ParentUri, out var module))
        {
            constants = module.Declarations.OfType<VBEnumConstMemberSymbol>()
                .Where(constant => constant.ParentUri.AbsoluteUri == enumeration.Uri.AbsoluteUri && IsMemberName(constant))
                .Select(constant => Describe(constant) with { Rank = ModuleRank });
        }

        return [.. constants.DistinctBy(constant => constant.Label, StringComparer.OrdinalIgnoreCase).OrderBy(constant => constant.Label, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// The project or the module a qualifier names, when it names one: <c>VBA</c>, <c>VBA.Strings</c>, <c>Module1</c> - what a member access is written on that
    /// is a namespace and not a value, and whose members are its declarations (<see cref="MembersOf(ScopeTree, Symbol, Uri)"/>).
    /// </summary>
    /// <param name="resolver">The resolver of the composed workspace.</param>
    /// <param name="origin">Where the lookup originates (<see cref="OriginAt"/>).</param>
    /// <param name="qualifier">The qualifier, as it is written: names separated by <c>.</c>.</param>
    /// <param name="typeContext">
    /// Whether the qualifier is the one of a qualified type name, which is bound in the type binding context (<see cref="ISymbolResolver.ResolveQualifier"/>);
    /// otherwise it is bound as a simple name expression is.
    /// </param>
    /// <returns>The project or the module; <see langword="null"/> when the qualifier names anything else, or nothing.</returns>
    public static Symbol? NamespaceOf(ISymbolResolver resolver, Uri origin, string qualifier, bool typeContext)
    {
        var parts = qualifier.Split('.');
        if (parts.Any(part => !IsWritable(part)))
        {
            return null;
        }

        var found = (typeContext
            ? resolver.ResolveQualifier(parts[0], ScopeKind.Global, origin)
            : resolver.ResolveValue(parts[0], ScopeKind.Global, origin)).Symbol;
        foreach (var part in parts.Skip(1))
        {
            if (!IsNamespace(found, typeContext))
            {
                return null;
            }

            found = resolver.ResolveMember(found!, part, origin).Symbol;
        }

        return IsNamespace(found, typeContext) ? found : null;
    }

    // a class module qualifies a type name (MS-VBAL §5.6.4), and is no namespace for a member access: its members belong to an instance of it.
    private static bool IsNamespace(Symbol? symbol, bool typeContext)
        => symbol is VBProjectSymbol or VBStandardModuleSymbol || typeContext && symbol is VBClassModuleSymbol;

    // what a module scope of the tree declares: its members - for a module of a library too, whose members the tree places in the module's own scope.
    private static IEnumerable<Symbol> DeclaredBy(ScopeTree tree, VBModuleSymbol module)
        => tree.TryGetScope(module.Uri, out var scope) && scope.Kind == LexicalScopeKind.Module && scope.Uri.AbsoluteUri == module.Uri.AbsoluteUri
            ? scope.Declarations
            : module.Members;

    private static LexicalScope? ProjectScopeOf(ScopeTree tree)
        => tree.Global.Declarations.OfType<VBModuleSymbol>()
            .Select(module => tree.TryGetScope(module.Uri, out var scope) ? scope.Parent : null)
            .FirstOrDefault(scope => scope?.Kind == LexicalScopeKind.Project);

    private static bool IsOfProject(Symbol symbol, VBProjectSymbol project)
        => string.Equals(symbol.GetProperty(SymbolProperties.Library) ?? string.Empty, project.GetProperty(SymbolProperties.Library) ?? string.Empty, StringComparison.Ordinal);

    private static int RankOf(LexicalScopeKind kind) => kind switch
    {
        LexicalScopeKind.Procedure => ProcedureRank,
        LexicalScopeKind.Module => ModuleRank,
        LexicalScopeKind.Project => ProjectRank,
        _ => GlobalRank,
    };

    // a name in the default binding context (MS-VBAL §5.6.10): never a user-defined type or a class, which are names only as types - a class is a value only as its
    // predeclared instance, which is a variable of its own.
    private static bool IsValueName(Symbol symbol, LexicalScopeKind scope)
        => IsWritable(symbol)
            && !IsHidden(symbol)
            && symbol is not (VBClassModuleSymbol or VBUserDefinedTypeMemberSymbol or UnboundVBUserDefinedTypeMemberSymbol or PrecompilerConstantSymbol)
            && symbol.Kind is not (SymbolKindExt.Class or SymbolKindExt.Interface or SymbolKindExt.UserDefinedType or SymbolKindExt.Event or SymbolKindExt.LineLabel
                or SymbolKindExt.Attribute or SymbolKindExt.Directive or SymbolKindExt.Operator or SymbolKindExt.Ignored)
            // a module's Private members are its own, and the tree places them in its scope alone; what the global scope has of a library is its public face.
            && (scope is not LexicalScopeKind.Global || !IsPrivate(symbol));

    // a name in the type binding context (MS-VBAL §5.6.4): a class, a user-defined type, an Enum - or, for New, a class that can be created.
    private static bool IsTypeName(Symbol symbol, bool creatable)
    {
        if (!IsWritable(symbol) || IsHidden(symbol))
        {
            return false;
        }

        var isClass = symbol is VBClassModuleSymbol || symbol.Kind is SymbolKindExt.Class or SymbolKindExt.Interface;
        if (creatable)
        {
            return isClass && (!symbol.TryGetProperty(SymbolProperties.Creatable, out var canCreate) || canCreate);
        }

        return isClass || symbol is VBUserDefinedTypeMemberSymbol or UnboundVBUserDefinedTypeMemberSymbol or VBEnumMemberSymbol or UnboundVBEnumMemberSymbol
            || symbol.Kind is SymbolKindExt.UserDefinedType or SymbolKindExt.Enum;
    }

    // what qualifies a type name in a list of types: a project, or a library. A procedural module can qualify one too (MS-VBAL §5.6.4), but the type it declares
    // is offered by its own name, and a module is no type: it is not offered where a type is written.
    private static bool IsQualifier(Symbol symbol) => IsWritable(symbol) && symbol is VBProjectSymbol;

    // what a member access can name: anything a module or a type declares but its events, which are raised and handled and never accessed (MS-VBAL §5.3.1.13).
    private static bool IsMemberName(Symbol symbol)
        => IsWritable(symbol)
            && !IsHidden(symbol)
            && symbol.Kind is not (SymbolKindExt.Event or SymbolKindExt.LineLabel or SymbolKindExt.Attribute or SymbolKindExt.Directive or SymbolKindExt.Operator
                or SymbolKindExt.Ignored);

    // a name that can be written as one: the platform names some of its symbols in ways the language cannot (an operator, a synthetic member).
    private static bool IsWritable(Symbol symbol) => IsWritable(symbol.Name);

    private static bool IsWritable(string name) => name.Length > 0 && char.IsLetter(name[0]) && name.All(character => char.IsLetterOrDigit(character) || character == '_');

    private static bool IsHidden(Symbol symbol)
        => (symbol.GetProperty(SymbolProperties.MemberFlags) & SymbolProperties.HiddenMemberFlag) != 0
            || symbol is ITypedSymbol { ResolvedType: VBClassType { IsHidden: true } } && symbol is VBClassModuleSymbol;

    private static bool IsPrivate(Symbol symbol) => symbol is AccessibleTypedSymbol { AccessModifier: AccessModifier.Private };

    // the candidate a symbol is: its name, its kind, and what is said of it.
    private static CompletionCandidate Describe(Symbol symbol)
        => new(symbol.Name, KindOf(symbol), DetailOf(symbol), symbol.GetProperty(SymbolProperties.DocString), symbol);

    private static CompletionItemKind KindOf(Symbol symbol) => symbol switch
    {
        VBProjectSymbol => CompletionItemKind.Folder,
        VBClassModuleSymbol => CompletionItemKind.Class,
        VBModuleSymbol => CompletionItemKind.Module,
        VBPredeclaredInstanceSymbol => CompletionItemKind.Variable,
        VBParameterSymbol or UnboundVBParameterSymbol => CompletionItemKind.Variable,
        _ => symbol.Kind switch
        {
            SymbolKindExt.Module => CompletionItemKind.Module,
            SymbolKindExt.Project => CompletionItemKind.Folder,
            SymbolKindExt.Class => CompletionItemKind.Class,
            SymbolKindExt.Interface => CompletionItemKind.Interface,
            SymbolKindExt.Procedure => CompletionItemKind.Method,
            SymbolKindExt.Function => CompletionItemKind.Function,
            SymbolKindExt.Property => CompletionItemKind.Property,
            SymbolKindExt.Field => CompletionItemKind.Field,
            SymbolKindExt.Enum => CompletionItemKind.Enum,
            SymbolKindExt.EnumMember => CompletionItemKind.EnumMember,
            SymbolKindExt.Constant => CompletionItemKind.Constant,
            SymbolKindExt.UserDefinedType => CompletionItemKind.Struct,
            SymbolKindExt.Event => CompletionItemKind.Event,
            _ => CompletionItemKind.Variable,
        },
    };

    // the type of what has one, and the signature of what is called: `Function Mid(String As Variant, Start As Long, Optional Length As Variant) As Variant`.
    private static string? DetailOf(Symbol symbol)
    {
        if (SignatureDescription.Of(symbol) is { } signature)
        {
            return signature.Label;
        }

        var type = symbol is ITypedSymbol typed && typed.ResolvedType is not VBUnknownType ? typed.ResolvedType : null;
        return type is null || symbol is VBModuleSymbol or VBProjectSymbol || symbol.Kind is SymbolKindExt.Enum or SymbolKindExt.UserDefinedType or SymbolKindExt.Class
            ? null
            : $"{symbol.Name} As {type.Name}";
    }

    // one candidate per name, the first one added - which is the one nearest to where the list was asked for - ranked by where it was found.
    private sealed class Candidates
    {
        private readonly Dictionary<string, CompletionCandidate> _byName = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _order = [];

        public void Add(CompletionCandidate candidate, int rank)
        {
            if (_byName.TryAdd(candidate.Label, candidate with { Rank = rank }))
            {
                _order.Add(candidate.Label);
            }
        }

        public IReadOnlyList<CompletionCandidate> ToList() => [.. _order.Select(name => _byName[name])];
    }
}
