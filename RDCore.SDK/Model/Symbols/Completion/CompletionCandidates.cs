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
    /// The candidate as an item of an LSP completion list (<strong>LSP 3.17</strong> §Completion Request).
    /// </summary>
    /// <remarks>
    /// What is inserted and what is filtered by is the label, which is what a client takes when an item has no <c>insertText</c> and no <c>filterText</c>: they
    /// are not written, since a list can have a thousand items, and each travels.
    /// </remarks>
    public CompletionItem ToCompletionItem() => new()
    {
        Label = Label,
        Kind = Kind,
        Detail = Detail,
        Documentation = Documentation is { Length: > 0 } documentation ? new StringOrMarkupContent(documentation) : null,
        SortText = $"{Rank:D2}{Label.ToLowerInvariant()}",
    };
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
    /// The candidates whose label has the letters of <paramref name="prefix"/>, in their order, in any case: what a client can still show for the name being
    /// typed, however it narrows its list - by the start of the label, by a part of it, or by letters that are not next to each other (<c>gtc</c> for
    /// <c>GetCount</c>). A list narrowed this way is complete for every word the prefix is the start of.
    /// </summary>
    /// <remarks>
    /// A list is the length of what is in scope - the members of the standard library, of the libraries the project references, of every module of the
    /// workspace - and travels to the client each time a name starts: the letters typed by then are what it is narrowed to before it does.
    /// </remarks>
    /// <param name="candidates">The candidates, in the order they rank.</param>
    /// <param name="prefix">What is typed of the name; an empty prefix is every candidate.</param>
    /// <returns>The candidates that match, in the order they were given.</returns>
    public static IReadOnlyList<CompletionCandidate> Matching(IReadOnlyList<CompletionCandidate> candidates, string prefix)
        => prefix.Length == 0 ? candidates : [.. candidates.Where(candidate => HasInOrder(candidate.Label, prefix))];

    // whether every letter of the prefix is in the label, each after the one before it.
    private static bool HasInOrder(string label, string prefix)
    {
        var at = 0;
        foreach (var letter in prefix)
        {
            var found = label.AsSpan(at).IndexOf(letter.ToString(), StringComparison.OrdinalIgnoreCase);
            if (found < 0)
            {
                return false;
            }

            at += found + 1;
        }

        return true;
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

        foreach (var declaration in module.Declarations.OfType<WorkspaceSymbol>())
        {
            if (tree.TryGetScope(declaration.Uri, out var scope)
                && scope.Kind == LexicalScopeKind.Procedure
                && scope.Uri.AbsoluteUri == declaration.Uri.AbsoluteUri
                && declaration.AllDefinitions.Any(definition => Contains(definition.Range, position)))
            {
                return declaration.Uri;
            }
        }

        return moduleUri;
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
