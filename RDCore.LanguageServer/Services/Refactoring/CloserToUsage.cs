using RDCore.LanguageServer.Services.Refactoring.ExtractMethod;
using RDCore.Runtime.LanguageCore.Refactoring;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Refactoring;
using System.Globalization;
using System.Resources;

namespace RDCore.LanguageServer.Services.Refactoring;

/// <summary>
/// Why the declaration of a variable is not moved closer to its usage.
/// </summary>
internal enum CloserToUsageRefusalKind
{
    /// <summary>
    /// What is named at the position is not a variable of a module's declaration section, nor a variable a procedure declares with <c>Dim</c> or
    /// <c>Static</c>.
    /// </summary>
    NotAVariable,

    /// <summary>
    /// The variable of a class module is <c>Public</c>, <c>Global</c> or <c>Friend</c>: it is a member of each object of the class, which other modules can
    /// refer to. Also a variable the project does not declare.
    /// </summary>
    NotPrivate,

    /// <summary>
    /// The variable is declared <c>WithEvents</c>: a procedure cannot declare one (<strong>MS-VBAL §5.4.3.1</strong>).
    /// </summary>
    WithEvents,

    /// <summary>
    /// Nothing refers to the variable.
    /// </summary>
    NotUsed,

    /// <summary>
    /// The variable of the module is used by more than one procedure, or outside any procedure.
    /// </summary>
    SeveralProcedures,

    /// <summary>
    /// The declaration, or the statement it would go before, is in a branch of an <c>#If</c> block, which is compiled only when that branch is live.
    /// </summary>
    Conditional,

    /// <summary>
    /// The variable of the procedure is declared directly before the statement that first uses it, with nothing but declarations between them, or after it.
    /// </summary>
    AlreadyClose,

    /// <summary>
    /// The variable is the only one of a statement that shares its line with another statement.
    /// </summary>
    SharesItsLine,

    /// <summary>
    /// The procedure names, unqualified, something else of the variable's name, which a local of that name would stand for.
    /// </summary>
    HidesAName,
}

/// <summary>
/// The keyword a variable of a module is declared with in the procedure it is moved into.
/// </summary>
internal enum CloserToUsageKeyword
{
    /// <summary>
    /// <c>Static</c>: the variable keeps its value from one call to the next (<strong>MS-VBAL §5.4.3.1</strong>).
    /// </summary>
    Static,

    /// <summary>
    /// <c>Dim</c>: the variable is initialized at each call, unless the procedure is <c>Static</c>.
    /// </summary>
    Dim,
}

/// <summary>
/// What moving a variable of a module into a procedure changes of what the code does.
/// </summary>
internal enum CloserToUsageChangeKind
{
    /// <summary>
    /// The variable keeps its value between calls: as a variable of a <c>Dim</c> declaration, it is initialized at each call.
    /// </summary>
    NotKept,

    /// <summary>
    /// The variable of a standard module has one value for the program: as a <c>Static</c> local of a member of a class module, it has one for each object of
    /// the class (<strong>RD-VBAL §5.4.3.1</strong>).
    /// </summary>
    OneForEachObject,
}

/// <summary>
/// Why a declaration is not moved, or what moving it changes, and the names the message gives.
/// </summary>
/// <param name="Kind">Why the declaration is not moved; <see langword="null"/> for a change.</param>
/// <param name="Name">The variable.</param>
/// <param name="Procedure">The procedure the variable is moved into, for a change; the procedures that use it, for
/// <see cref="CloserToUsageRefusalKind.SeveralProcedures"/>.</param>
/// <param name="Change">What moving it changes; <see langword="null"/> for a refusal.</param>
internal sealed record class CloserToUsageNotice(CloserToUsageRefusalKind? Kind, string Name, string? Procedure = null, CloserToUsageChangeKind? Change = null)
{
    private static readonly ResourceManager Messages = new("RDCore.LanguageServer.Services.Refactoring.CloserToUsageMessages", typeof(CloserToUsageNotice).Assembly);

    /// <summary>
    /// Whether the position names nothing this refactoring moves: a code action offers nothing there.
    /// </summary>
    public bool FindsNothing => Kind is CloserToUsageRefusalKind.NotAVariable;

    /// <summary>
    /// The message, in the language of the user interface.
    /// </summary>
    public string ToMessage() => Kind is { } kind
        ? Format($"MoveCloserToUsage_{kind}", null, Name, Procedure ?? string.Empty)
        : Format($"MoveCloserToUsage_{Change}", null, Name, Procedure ?? string.Empty);

    /// <summary>
    /// The title of the code action that moves the declaration of a variable, in the language of the user interface.
    /// </summary>
    /// <param name="name">The variable.</param>
    public static string Title(string name) => Format("MoveCloserToUsage_Title", null, name);

    /// <summary>
    /// A message of the refactoring, in the language of the user interface or of <paramref name="culture"/>.
    /// </summary>
    /// <param name="key">The key of the message.</param>
    /// <param name="culture">The culture; <see langword="null"/> for the user interface's.</param>
    /// <param name="args">What the message names.</param>
    public static string Format(string key, CultureInfo? culture, params object[] args)
        => string.Format(culture ?? CultureInfo.CurrentUICulture, Messages.GetString(key, culture) ?? key, args);
}

/// <summary>
/// A declaration to move: the variable, its declaration, and the statement it goes before.
/// </summary>
/// <param name="Variable">The variable.</param>
/// <param name="Declaration">Its declaration, a node of the syntax tree of <paramref name="Declaring"/>.</param>
/// <param name="Procedure">The procedure that uses it, of <paramref name="Into"/>.</param>
/// <param name="Before">The statement of the procedure that the declaration goes before.</param>
/// <param name="Declaring">The module that declares the variable.</param>
/// <param name="DeclaringParse">The parse of <paramref name="Declaring"/>.</param>
/// <param name="Into">The module of the procedure: <paramref name="Declaring"/>, or for a <c>Public</c> variable of a standard module, another module.</param>
/// <param name="Unqualified">The module names the uses in the procedure are qualified with (<c>Globals.</c> of <c>Globals.gCount</c>), which a local is
/// not named with: the ranges of <paramref name="Into"/> to remove.</param>
/// <param name="IntoAClass">Whether <paramref name="Into"/> is a class module, whose objects each have the <c>Static</c> locals of its members.</param>
internal sealed record class CloserToUsageTarget(
    Symbol Variable, VariableDeclarationNode Declaration, MemberDeclarationNode Procedure, SyntaxNode Before,
    RenameModule Declaring, ModuleParseResult DeclaringParse, RenameModule Into, IReadOnlyList<SourceRange> Unqualified, bool IntoAClass)
{
    /// <summary>
    /// Whether the variable is one of the module's, which the user declares with a keyword of their choice in the procedure.
    /// </summary>
    public bool IsOfTheModule => Variable is VBModuleFieldVariableMemberSymbol or VBInstanceFieldVariableMemberSymbol;

    /// <summary>
    /// Whether the procedure is of another module than the variable.
    /// </summary>
    public bool MovesToAnotherModule => Into.Module.AbsoluteUri != Declaring.Module.AbsoluteUri;
}

/// <summary>
/// The edits of a move in one module: the module of the declaration, and the module of the procedure when it is another.
/// </summary>
/// <param name="Module">The module.</param>
/// <param name="Edits">Its edits, in the order of their ranges.</param>
internal sealed record class CloserToUsageEdits(Uri Module, IReadOnlyList<RefactorTextEdit> Edits);

// moves the declaration of a variable to the statement that first uses it (MS-VBAL §5.2.3.1, §5.4.3.1):
// - a variable of a module that one procedure uses goes into that procedure, as Static (it keeps its value between calls) or Dim: a Private one of its
//   module, and a Public or Global one of a standard module, which a procedure of another module of the project can use too;
// - a variable of a procedure goes down to the statement that first uses it, in the innermost block that holds every use of it, at its indentation.
// a declaration is not executed: a variable of a procedure is the same wherever it is declared before its first use.
internal static class CloserToUsage
{
    // the declaration of the variable named at a position of a module, and the statement it goes before; or why it does not move. The uses of a Public
    // variable are the ones bound to it in every module of the project, whose parses parseOf gives (the module's own is parse).
    public static (CloserToUsageTarget? Target, CloserToUsageNotice? Refusal) Prepare(
        RenameContext context, RenameModule source, ModuleParseResult parse, SourcePosition position, Func<Uri, ModuleParseResult?>? parseOf = null)
    {
        if (RenameBinding.SymbolAt(context, source.Module, position) is not { Symbol: var symbol }
            || symbol is not (VBModuleFieldVariableMemberSymbol or VBInstanceFieldVariableMemberSymbol or VBLocalVariableSymbol { DeclaredBy: LocalDeclarationKind.Dim }))
        {
            return (null, new(CloserToUsageRefusalKind.NotAVariable, string.Empty));
        }

        ModuleParseResult? ParseOf(RenameModule module) => module.Module.AbsoluteUri == source.Module.AbsoluteUri ? parse : parseOf?.Invoke(module.Module);

        // a variable of a procedure, or a Private one of a module, is used in its module only; another is looked for in the project.
        var isOfTheModule = symbol is not VBLocalVariableSymbol;
        var (occurrences, unknown) = Bound(context, symbol, [source]);
        if (DeclarationOf(context, symbol, occurrences) is not { Declaration.AccessModifier: AccessModifier.Private or AccessModifier.Implicit } && isOfTheModule)
        {
            (occurrences, unknown) = Bound(context, symbol, context.Modules);
        }

        if (DeclarationOf(context, symbol, occurrences) is not ({ } declaration, { } declaring) || ParseOf(declaring) is not { } declaringParse)
        {
            return (null, new(CloserToUsageRefusalKind.NotPrivate, symbol.Name));
        }

        // a variable of a class is a member of each of its objects, which no local of one procedure stands for.
        if (isOfTheModule && declaration.AccessModifier is not (AccessModifier.Private or AccessModifier.Implicit) && EncapsulateField.IsOfAnObjectModule(symbol))
        {
            return (null, new(CloserToUsageRefusalKind.NotPrivate, symbol.Name));
        }

        if (declaration.IsWithEvents)
        {
            return (null, new(CloserToUsageRefusalKind.WithEvents, symbol.Name));
        }

        var uses = occurrences.Where(occurrence => occurrence.Kind is not OccurrenceKind.Declaration).ToList();
        if (uses.Count == 0)
        {
            return (null, new(CloserToUsageRefusalKind.NotUsed, symbol.Name));
        }

        var users = uses.Select(use => UserOf(context, use)).DistinctBy(user => (user.Module?.Module.AbsoluteUri, user.Procedure?.Identity)).ToList();
        if (users is not [({ } into, { } procedure)])
        {
            var names = string.Join(", ", users.Where(user => user.Procedure is not null)
                .Select(user => user.Module!.Module.AbsoluteUri == declaring.Module.AbsoluteUri ? user.Procedure!.Name : $"{NameOf(user.Module!)}.{user.Procedure!.Name}"));
            return (null, new(CloserToUsageRefusalKind.SeveralProcedures, symbol.Name, names));
        }

        var (before, list) = BeforeOf(procedure, [.. uses.Select(use => use.Range.Start)]);
        if (ParseOf(into) is not { } intoParse
            || ConditionalCompilationBlocks.Of(declaringParse.PrecompilerTrivia).IsConditional(declaration.SourceLocation.Range.Start)
            || ConditionalCompilationBlocks.Of(intoParse.PrecompilerTrivia).IsConditional(before.SourceLocation.Range.Start))
        {
            return (null, new(CloserToUsageRefusalKind.Conditional, symbol.Name));
        }

        if (!isOfTheModule && IsClose(declaration, before, list))
        {
            return (null, new(CloserToUsageRefusalKind.AlreadyClose, symbol.Name));
        }

        // a local of the name hides what the procedure names so otherwise: another variable of its module, a name it does not bind.
        if (isOfTheModule && Hides(context, symbol, into, procedure, unknown))
        {
            return (null, new(CloserToUsageRefusalKind.HidesAName, symbol.Name, procedure.Name));
        }

        // a use qualified with the name of the module (Globals.gCount, MS-VBAL §5.6.12) names the local without it.
        IReadOnlyList<SourceRange> unqualified = [.. uses
            .Where(use => use.Kind is OccurrenceKind.Member && use.Node is { } node)
            .Select(use => new SourceRange(use.Node!.SourceLocation.Range.Start, use.Range.Start))];
        var intoAClass = NameOccurrences.SymbolOf(context, new SemanticId(into.Module)) is VBClassModuleSymbol;
        return (new CloserToUsageTarget(symbol, declaration, procedure, before, declaring, declaringParse, into, unqualified, intoAClass), null);
    }

    // the occurrences of the modules that are bound to the variable, and the names of its name that are bound to nothing.
    private static (List<NameOccurrence> Bound, IReadOnlyList<NameOccurrence> Unknown) Bound(RenameContext context, Symbol symbol, IEnumerable<RenameModule> modules)
    {
        var found = NameOccurrences.Find(context, symbol.Name, modules);
        return ([.. found.Occurrences.Where(occurrence => occurrence.Binding == symbol.SemanticId)], found.Unknown);
    }

    // the declaration of the variable among its occurrences, and the module that declares it.
    private static (VariableDeclarationNode Declaration, RenameModule Module)? DeclarationOf(RenameContext context, Symbol symbol, IReadOnlyList<NameOccurrence> occurrences)
        => occurrences.FirstOrDefault(occurrence => occurrence.Kind is OccurrenceKind.Declaration) is { Module: { } uri } declared
            && context.Modules.FirstOrDefault(module => module.Module.AbsoluteUri == uri.AbsoluteUri) is { } module
            && module.Syntax.Descendants().OfType<VariableDeclarationNode>().FirstOrDefault(variable => variable.NameRange?.Start == declared.Range.Start) is { } node
                ? (node, module)
                : null;

    // the module and the procedure of a use; no procedure for a use outside any.
    private static (RenameModule? Module, MemberDeclarationNode? Procedure) UserOf(RenameContext context, NameOccurrence use)
        => context.Modules.FirstOrDefault(module => module.Module.AbsoluteUri == use.Module.AbsoluteUri) is { } module
            ? (module, module.Syntax.Children.OfType<MemberDeclarationNode>().FirstOrDefault(procedure => Holds(procedure, use.Range.Start)))
            : (null, null);

    // whether the procedure names, unqualified, something of the variable's name that is not the variable: a local of the name would stand for it.
    private static bool Hides(RenameContext context, Symbol symbol, RenameModule into, MemberDeclarationNode procedure, IReadOnlyList<NameOccurrence> unknown)
        => NameOccurrences.Find(context, symbol.Name, [into]).Occurrences.Concat(unknown)
            .Any(occurrence => occurrence.Kind is OccurrenceKind.Name && occurrence.Binding != symbol.SemanticId
                && occurrence.Module.AbsoluteUri == into.Module.AbsoluteUri && Holds(procedure, occurrence.Range.Start));

    private static string NameOf(RenameModule module) => module.Module.Fragment.TrimStart('#');

    // what moving the declaration with a keyword changes: a variable of the module loses its value between calls when it is declared with Dim in a procedure
    // that is not Static. A Static local of a member of a class module has the extent of the object the member is called on (RD-VBAL §5.4.3.1), as the
    // variable of the class did: each object keeps its own value. A variable of a standard module, one for the program, moved into a member of a class has
    // one value for each object instead.
    public static CloserToUsageNotice? ChangeOf(CloserToUsageTarget target, CloserToUsageKeyword keyword)
        => !target.IsOfTheModule ? null
            : keyword is CloserToUsageKeyword.Dim && !target.Procedure.IsStatic
                ? new(null, target.Variable.Name, target.Procedure.Name, CloserToUsageChangeKind.NotKept)
            : target.IntoAClass && !EncapsulateField.IsOfAnObjectModule(target.Variable)
                ? new(null, target.Variable.Name, target.Procedure.Name, CloserToUsageChangeKind.OneForEachObject)
            : null;

    // the edits that move the declaration: the variable is removed from its statement, and declared on a line of its own before the statement that first uses
    // it, indented as that statement is, and its uses lose the name of the module they are qualified with. A variable of a procedure keeps its keyword; one
    // of the module takes the keyword chosen. The edits of the module of the declaration come first, then those of the procedure's when it is another.
    public static (IReadOnlyList<CloserToUsageEdits>? Edits, CloserToUsageNotice? Refusal) Plan(CloserToUsageTarget target, CloserToUsageKeyword keyword)
    {
        var text = target.Declaring.Source;
        if (DeclarationEdits.RemoveVariable(target.DeclaringParse, text, target.Declaration) is not { } removed)
        {
            return (null, new(CloserToUsageRefusalKind.SharesItsLine, target.Variable.Name));
        }

        var into = target.Into.Source;
        var written = target.IsOfTheModule ? keyword.ToString() : target.Variable is VBLocalVariableSymbol { IsStatic: true } ? "Static" : "Dim";
        var line = target.Before.SourceLocation.Range.Start.Line;
        var declarator = Slice(text, new SourceRange(target.Declaration.NameRange!.Value.Start, target.Declaration.SourceLocation.Range.End));
        var at = new SourcePosition(line, 0);
        IReadOnlyList<RefactorTextEdit> inserted =
        [
            new RefactorTextEdit { Range = new SourceRange(at, at), NewText = $"{DeclarationEdits.IndentOf(into, line)}{written} {declarator}{new ExtractionText(into).NewLine}" },
            .. target.Unqualified.Select(range => new RefactorTextEdit { Range = range, NewText = string.Empty }),
        ];

        return target.MovesToAnotherModule
            ? ([new(target.Declaring.Module, removed), new(target.Into.Module, [.. inserted.OrderBy(edit => edit.Range.Start)])], null)
            : ([new(target.Declaring.Module, [.. removed.Concat(inserted).OrderBy(edit => edit.Range.Start)])], null);
    }

    // the statement the declaration goes before, and the list of statements it is one of: the first that uses the variable, of the innermost block that holds
    // every use of it. A use in the header of a statement that holds a block (an If's condition, a loop's bounds) keeps the declaration before that statement.
    private static (SyntaxNode Before, IReadOnlyList<SyntaxNode> List) BeforeOf(MemberDeclarationNode procedure, IReadOnlyList<SourcePosition> uses)
    {
        IReadOnlyList<SyntaxNode> list = [.. procedure.Children.Skip(procedure.HeaderChildren.Count())];
        while (true)
        {
            var holding = list.Where(node => uses.Any(use => Holds(node, use))).ToList();
            if (holding is [var only]
                && only.SiblingLists().Skip(1).Where(block => uses.All(use => block.Any(node => Holds(node, use)))).FirstOrDefault() is { } inner)
            {
                list = inner;
                continue;
            }

            return (holding[0], list);
        }
    }

    // whether a declaration of a procedure is already where it would go: directly before the statement, with nothing but declarations between them,
    // or after it.
    private static bool IsClose(VariableDeclarationNode declaration, SyntaxNode before, IReadOnlyList<SyntaxNode> list)
    {
        var (from, to) = (IndexOf(list, declaration), IndexOf(list, before));
        if (from < 0)
        {
            return before.SourceLocation.Range.Start < declaration.SourceLocation.Range.Start;
        }

        return to < from || list.Skip(from + 1).Take(to - from - 1).All(node => node is VariableDeclarationNode);
    }

    private static int IndexOf(IReadOnlyList<SyntaxNode> list, SyntaxNode node)
    {
        for (var index = 0; index < list.Count; index++)
        {
            if (list[index].Identity == node.Identity)
            {
                return index;
            }
        }

        return -1;
    }

    private static bool Holds(SyntaxNode node, SourcePosition position)
        => node.SourceLocation.Range.Start <= position && position < node.SourceLocation.Range.End;

    // the text of a range, of a text whose lines end with a line feed, a carriage return and a line feed, or a carriage return.
    private static string Slice(string text, SourceRange range)
    {
        var starts = new List<int> { 0 };
        for (var at = 0; at < text.Length; at++)
        {
            if (text[at] == '\n' || (text[at] == '\r' && (at + 1 == text.Length || text[at + 1] != '\n')))
            {
                starts.Add(at + 1);
            }
        }

        return text[(starts[range.Start.Line] + range.Start.Character)..(starts[range.End.Line] + range.End.Character)];
    }
}
