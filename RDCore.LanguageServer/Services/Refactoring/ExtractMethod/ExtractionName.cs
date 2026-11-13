using RDCore.Runtime.LanguageCore.Refactoring;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using System.Collections.Immutable;
using System.Globalization;
using System.Resources;

namespace RDCore.LanguageServer.Services.Refactoring.ExtractMethod;

/// <summary>
/// What is wrong with a name for the new procedure.
/// </summary>
internal enum ExtractionNameProblemKind
{
    /// <summary>
    /// The name is empty.
    /// </summary>
    Empty,

    /// <summary>
    /// The name is not an identifier (<see cref="VBIdentifierValidity.NotAnIdentifier"/>).
    /// </summary>
    NotAnIdentifier,

    /// <summary>
    /// The name is a keyword (<see cref="VBIdentifierValidity.Reserved"/>).
    /// </summary>
    Reserved,

    /// <summary>
    /// The name is longer than <see cref="ExtractionName.MaximumLength"/> characters.
    /// </summary>
    TooLong,

    /// <summary>
    /// The module, or the procedure the selection is in, already declares the name, or a name the extraction declares with it: its exit codes.
    /// </summary>
    Collision,

    /// <summary>
    /// The module writes the name where it means something else, which would mean the new procedure instead.
    /// </summary>
    Rebinds,

    /// <summary>
    /// The name is declared outside the module, by another module or a library, and the new procedure hides it in the module. Nothing in the module writes
    /// the name: a warning, never a refusal.
    /// </summary>
    Shadows,

    /// <summary>
    /// The new procedure is a <c>Public</c> member of a class module that another class implements: a member of its interface the other class does not
    /// implement.
    /// </summary>
    Implemented,
}

/// <summary>
/// What is wrong with a name for the new procedure, and where.
/// </summary>
/// <param name="Kind">What.</param>
/// <param name="Name">The name.</param>
/// <param name="Other">What else the name is: the symbol already declared as it, the symbol it means where it is written.</param>
/// <param name="Module">The module the problem points to, when it points to one.</param>
/// <param name="Position">Where in <paramref name="Module"/>.</param>
internal sealed record class ExtractionNameProblem(ExtractionNameProblemKind Kind, string Name, string? Other = null, Uri? Module = null, SourcePosition Position = default)
{
    private static readonly ResourceManager Messages = new("RDCore.LanguageServer.Services.Refactoring.ExtractMethod.ExtractMethodMessages", typeof(ExtractionNameProblem).Assembly);

    /// <summary>
    /// Whether the name can be used all the same: a warning, which the user accepts.
    /// </summary>
    public bool IsWarning => Kind is ExtractionNameProblemKind.Shadows;

    /// <summary>
    /// What the problem says, in the language of the reader.
    /// </summary>
    /// <param name="locate">How a position is named to the reader; by default, the name of the module and the line and the column, from 1.</param>
    /// <param name="culture">The language of the reader; the current UI culture by default.</param>
    public string ToMessage(Func<Uri, SourcePosition, string>? locate = null, CultureInfo? culture = null)
    {
        var where = Module is null ? string.Empty : (locate ?? DefaultLocation)(Module, Position);
        var format = Messages.GetString($"ExtractMethodName_{Kind}", culture ?? CultureInfo.CurrentUICulture) ?? Kind.ToString();
        return string.Format(culture ?? CultureInfo.CurrentUICulture, format, Name, Other, where, ExtractionName.MaximumLength);
    }

    private static string DefaultLocation(Uri module, SourcePosition position)
        => $"{module.Fragment.TrimStart('#')} {position.Line + 1}:{position.Character + 1}";
}

/// <summary>
/// The name of the new procedure: suggested from what the code does, and validated as the user writes it.
/// </summary>
/// <remarks>
/// <para>
/// A name is valid when it is a VBA identifier of at most 255 characters that is not reserved (MS-VBAL §3.3.5); when neither the module nor the procedure the
/// selection is in declares it, nor the names the extraction declares with it (the enumeration of its exit codes); and when nothing the module writes as it
/// means something else, which would mean the new procedure instead. A name declared outside the module, which the new <c>Private</c> procedure hides there, is a
/// warning.
/// </para>
/// <para>
/// A <c>Public</c> procedure of a standard module, and the <c>Public Enum</c> of its exit codes in any module, are the project's (MS-VBAL §5.2.3): the project
/// must not declare their names either, and no module may write them where they mean something else. A library's declaration they hide is only a warning. A
/// <c>Public</c> member of a class module is named through an object of the class, and its name is checked as a <c>Private</c> one; it is refused when another
/// class implements the class.
/// </para>
/// </remarks>
internal static class ExtractionName
{
    /// <summary>
    /// The longest a name can be.
    /// </summary>
    public const int MaximumLength = 255;

    private const string Fallback = "ExtractedMethod";

    /// <summary>
    /// What is wrong with <paramref name="name"/> for the new procedure: nothing when it is valid, and only warnings when it can be used.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="plan">What the extraction takes.</param>
    /// <param name="source">The module, and the procedure the selection is in.</param>
    /// <param name="context">The workspace, whose modules are bound.</param>
    /// <param name="module">The address of the module in <paramref name="context"/> (<c>workspace#Module</c>).</param>
    /// <param name="accessibility">How far the new procedure is seen.</param>
    public static ImmutableArray<ExtractionNameProblem> Validate(
        string name, ExtractionPlan plan, ExtractionSource source, RenameContext context, Uri module, ExtractionAccessibility accessibility = ExtractionAccessibility.Private)
    {
        switch (VBIdentifier.Validate(name))
        {
            case VBIdentifierValidity.Empty:
                return [new(ExtractionNameProblemKind.Empty, name)];
            case VBIdentifierValidity.NotAnIdentifier:
                return [new(ExtractionNameProblemKind.NotAnIdentifier, name)];
            case VBIdentifierValidity.Reserved:
                return [new(ExtractionNameProblemKind.Reserved, name)];
        }

        if (name.Length > MaximumLength)
        {
            return [new(ExtractionNameProblemKind.TooLong, name)];
        }

        var moduleScope = context.ScopeTree.ScopeFor(module);
        var procedureScope = context.ScopeTree.ScopeFor(source.ProcedureId.Uri);
        var syntax = context.Modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == module.AbsoluteUri);
        var isPublic = accessibility is ExtractionAccessibility.Public;
        var type = NameOccurrences.SymbolOf(context, new SemanticId(module)) as VBClassModuleSymbol;
        var problems = new List<ExtractionNameProblem>();
        if (isPublic && type is not null && NameOccurrences.ImplementerOf(context, type) is { } implementer)
        {
            problems.Add(new(ExtractionNameProblemKind.Implemented, name, implementer));
        }

        foreach (var declared in ExtractMethodEdit.ModuleNamesOf(plan, source, name))
        {
            // the procedure of a standard module, and the enumeration of the exit codes, are the project's when they are Public; a member of a class is not.
            var isProjects = isPublic && (type is null || !declared.Equals(name, StringComparison.OrdinalIgnoreCase));
            IEnumerable<Symbol> projects = isProjects
                ? moduleScope.SelfAndAncestors().Skip(1).SelectMany(outer => outer.DeclaredAs(declared)).Where(symbol => !IsOfALibrary(symbol))
                : [];
            if (procedureScope.DeclaredAs(declared).Concat(moduleScope.DeclaredAs(declared)).Concat(projects).FirstOrDefault() is { } existing)
            {
                problems.Add(At(ExtractionNameProblemKind.Collision, declared, existing));
                continue;
            }

            IReadOnlyList<RenameModule> written = isProjects ? context.Modules : syntax is null ? [] : [syntax];
            if (written.Select(each => RebindingOf(context, each, each.Module.AbsoluteUri == module.AbsoluteUri ? moduleScope : null, declared)).FirstOrDefault(rebinding => rebinding is not null) is { } rebinding)
            {
                problems.Add(rebinding);
                continue;
            }

            if (moduleScope.SelfAndAncestors().Skip(1).SelectMany(outer => outer.DeclaredAs(declared)).FirstOrDefault() is { } hidden)
            {
                problems.Add(At(ExtractionNameProblemKind.Shadows, declared, hidden));
            }
        }

        return [.. problems];
    }

    /// <summary>
    /// A valid name for the new procedure, from what the code does: <c>Get</c> and the variable it gives back, when it gives back one; or the verb of the first
    /// procedure it calls, and what it calls it with; or <c>ExtractedMethod</c>. A number follows it when it is taken.
    /// </summary>
    /// <param name="plan">What the extraction takes.</param>
    /// <param name="source">The module, and the procedure the selection is in.</param>
    /// <param name="context">The workspace, whose modules are bound.</param>
    /// <param name="module">The address of the module in <paramref name="context"/> (<c>workspace#Module</c>).</param>
    /// <param name="returnValue">The variable the new procedure returns, when the user chose one.</param>
    /// <param name="accessibility">How far the new procedure is seen, which the name is valid for.</param>
    public static string Suggest(
        ExtractionPlan plan, ExtractionSource source, RenameContext context, Uri module, SemanticId? returnValue = null,
        ExtractionAccessibility accessibility = ExtractionAccessibility.Private)
    {
        var stem = Stem(plan, source, returnValue);
        if (VBIdentifier.Validate(stem) is not VBIdentifierValidity.Valid || stem.Length > MaximumLength - 3)
        {
            stem = Fallback;
        }

        var name = stem;
        for (var index = 2; Validate(name, plan, source, context, module, accessibility).Any(problem => problem is { IsWarning: false, Kind: not ExtractionNameProblemKind.Implemented }); index++)
        {
            name = $"{stem}{index}";
        }

        return name;
    }

    private static string Stem(ExtractionPlan plan, ExtractionSource source, SemanticId? returnValue)
    {
        // a GoSub routine is named after its label: Twice gives TwiceProc, and the line number 100 gives Routine100.
        if (plan.Selection.Routine is { } routine)
        {
            return VBIdentifier.Validate($"{routine.Label}Proc") is VBIdentifierValidity.Valid ? $"{routine.Label}Proc" : $"Routine{routine.Label}";
        }

        var outputs = plan.Parameters.Where(parameter => parameter.IsOutput).ToArray();
        var output = returnValue is { } chosen ? plan.Parameters.FirstOrDefault(parameter => parameter.Variable == chosen) : outputs.Length == 1 ? outputs[0] : null;
        if (output is not null)
        {
            return $"Get{Capitalized(output.Name)}";
        }

        var text = new ExtractionText(source.Text);
        var call = plan.Selection.Statements
            .SelectMany(statement => (IEnumerable<SyntaxNode>)[statement, .. statement.Descendants()])
            .OfType<CallStatementNode>()
            .FirstOrDefault();
        if (call is null)
        {
            return Fallback;
        }

        // the callee's own name, after any qualifier, and its first word: Debug.Print gives Print, LoadSheet gives Load.
        var callee = text.Of(call.Callee.SourceLocation.Range).Split('.')[^1].Trim();
        var verb = Capitalized(new string([.. callee.TakeWhile((character, index) => index == 0 || !char.IsUpper(character))]));
        var argument = call.Arguments.Length > 0 ? text.Of(call.Arguments[0].SourceLocation.Range).Trim() : string.Empty;
        return VBIdentifier.Validate(argument) is VBIdentifierValidity.Valid ? $"{verb}{Capitalized(argument)}" : verb;
    }

    private static string Capitalized(string name) => name.Length == 0 ? name : $"{char.ToUpperInvariant(name[0])}{name[1..]}";

    // where a module writes the name and means something not known, or something farther than the new declaration: outside the module the new procedure is
    // declared in (moduleScope), or, in another module, a library's (null): the new declaration, nearer, would be meant instead.
    private static ExtractionNameProblem? RebindingOf(RenameContext context, RenameModule syntax, LexicalScope? moduleScope, string name)
    {
        var occurrences = NameOccurrences.Find(context, name, [syntax]);
        foreach (var occurrence in occurrences.Occurrences.Concat(occurrences.Unknown).Where(occurrence => occurrence.Kind is OccurrenceKind.Name))
        {
            var meant = occurrence.Binding is { } binding ? NameOccurrences.SymbolOf(context, binding) : null;
            var nearest = moduleScope is null
                ? occurrence.Scope.SelfAndAncestors().TakeWhile(scope => scope.Kind is not LexicalScopeKind.Project).Count() - 1
                : occurrence.Scope.SelfAndAncestors().TakeWhile(scope => scope.Uri.AbsoluteUri != moduleScope.Uri.AbsoluteUri).Count();
            if (meant is null || RenameBinding.ChainIndex(occurrence.Scope, meant) > nearest)
            {
                return new(ExtractionNameProblemKind.Rebinds, name, meant is null ? name : NameOccurrences.Describe(meant), occurrence.Module, occurrence.Range.Start);
            }
        }

        return null;
    }

    // a library's symbol says which library it is of; the workspace's say none.
    private static bool IsOfALibrary(Symbol symbol) => symbol.GetProperty(SymbolProperties.Library) is not null;

    private static ExtractionNameProblem At(ExtractionNameProblemKind kind, string name, Symbol other)
        => new(kind, name, NameOccurrences.Describe(other), other is WorkspaceSymbol ? NameOccurrences.ModuleOf(other.Uri) : null,
            (other as WorkspaceSymbol)?.PrimarySelectionRange.Start ?? default);
}
