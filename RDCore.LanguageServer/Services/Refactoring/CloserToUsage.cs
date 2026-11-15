using RDCore.LanguageServer.Services.Refactoring.ExtractMethod;
using RDCore.Runtime.LanguageCore.Refactoring;
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
    /// What is named at the position is not a variable of a module's declaration section, nor a variable a procedure declares with <c>Dim</c> or <c>Static</c>.
    /// </summary>
    NotAVariable,

    /// <summary>
    /// The variable of the module is <c>Public</c>, <c>Global</c> or <c>Friend</c>: other modules can refer to it.
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
    /// The variable of a class module has object extent: as a variable of a <c>Static</c> declaration, or of a <c>Static</c> procedure, it has module extent
    /// (<strong>MS-VBAL §2.3</strong>, <strong>§5.4.3.1</strong>).
    /// </summary>
    ModuleExtent,

    /// <summary>
    /// The variable keeps its value between calls: as a variable of a <c>Dim</c> declaration, it is initialized at each call.
    /// </summary>
    NotKept,
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
/// <param name="Declaration">Its declaration, a node of the module's syntax tree.</param>
/// <param name="Procedure">The procedure that uses it.</param>
/// <param name="Before">The statement of the procedure that the declaration goes before.</param>
internal sealed record class CloserToUsageTarget(Symbol Variable, VariableDeclarationNode Declaration, MemberDeclarationNode Procedure, SyntaxNode Before)
{
    /// <summary>
    /// Whether the variable is one of the module's, which the user declares with a keyword of their choice in the procedure.
    /// </summary>
    public bool IsOfTheModule => Variable is VBModuleFieldVariableMemberSymbol or VBInstanceFieldVariableMemberSymbol;
}

// moves the declaration of a variable to the statement that first uses it (MS-VBAL §5.2.3.1, §5.4.3.1):
// - a Private variable of a module that one procedure uses goes into that procedure, as Static (it keeps its value between calls) or Dim;
// - a variable of a procedure goes down to the statement that first uses it, in the innermost block that holds every use of it, at its indentation.
// a declaration is not executed: a variable of a procedure is the same wherever it is declared before its first use.
internal static class CloserToUsage
{
    // the declaration of the variable named at a position of a module, and the statement it goes before; or why it does not move.
    public static (CloserToUsageTarget? Target, CloserToUsageNotice? Refusal) Prepare(
        RenameContext context, RenameModule source, ModuleParseResult parse, SourcePosition position)
    {
        if (RenameBinding.SymbolAt(context, source.Module, position) is not { Symbol: var symbol }
            || symbol is not (VBModuleFieldVariableMemberSymbol or VBInstanceFieldVariableMemberSymbol or VBLocalVariableSymbol { DeclaredBy: LocalDeclarationKind.Dim }))
        {
            return (null, new(CloserToUsageRefusalKind.NotAVariable, string.Empty));
        }

        // a variable another module can refer to is not one this module alone uses.
        var occurrences = NameOccurrences.Find(context, symbol.Name, [source]).Occurrences.Where(occurrence => occurrence.Binding == symbol.SemanticId).ToList();
        if (occurrences.FirstOrDefault(occurrence => occurrence.Kind is OccurrenceKind.Declaration) is not { Module: not null } declared
            || parse.SyntaxTree is not { } tree
            || tree.Descendants().OfType<VariableDeclarationNode>().FirstOrDefault(variable => variable.NameRange?.Start == declared.Range.Start) is not { } declaration)
        {
            return (null, new(CloserToUsageRefusalKind.NotPrivate, symbol.Name));
        }

        var isOfTheModule = symbol is not VBLocalVariableSymbol;
        if (isOfTheModule && declaration.AccessModifier is not (AccessModifier.Private or AccessModifier.Implicit))
        {
            return (null, new(CloserToUsageRefusalKind.NotPrivate, symbol.Name));
        }

        if (declaration.IsWithEvents)
        {
            return (null, new(CloserToUsageRefusalKind.WithEvents, symbol.Name));
        }

        var uses = occurrences.Where(occurrence => occurrence.Kind is not OccurrenceKind.Declaration).Select(occurrence => occurrence.Range.Start).ToList();
        if (uses.Count == 0)
        {
            return (null, new(CloserToUsageRefusalKind.NotUsed, symbol.Name));
        }

        var procedures = tree.Children.OfType<MemberDeclarationNode>().ToList();
        var users = uses.Select(use => procedures.FirstOrDefault(procedure => Holds(procedure, use))).Distinct().ToList();
        if (users is not [{ } procedure])
        {
            var names = string.Join(", ", users.OfType<MemberDeclarationNode>().Select(each => each.Name));
            return (null, new(CloserToUsageRefusalKind.SeveralProcedures, symbol.Name, names));
        }

        var (before, list) = BeforeOf(procedure, uses);
        var blocks = ConditionalCompilationBlocks.Of(parse.PrecompilerTrivia);
        if (blocks.IsConditional(declaration.SourceLocation.Range.Start) || blocks.IsConditional(before.SourceLocation.Range.Start))
        {
            return (null, new(CloserToUsageRefusalKind.Conditional, symbol.Name));
        }

        if (!isOfTheModule && IsClose(declaration, before, list))
        {
            return (null, new(CloserToUsageRefusalKind.AlreadyClose, symbol.Name));
        }

        return (new CloserToUsageTarget(symbol, declaration, procedure, before), null);
    }

    // what moving the declaration with a keyword changes: a variable of a class module loses its object extent, and one of a standard module its value
    // between calls when it is declared with Dim in a procedure that is not Static.
    public static CloserToUsageNotice? ChangeOf(CloserToUsageTarget target, CloserToUsageKeyword keyword)
    {
        var keeps = keyword is CloserToUsageKeyword.Static || target.Procedure.IsStatic;
        return target.Variable switch
        {
            VBInstanceFieldVariableMemberSymbol when keeps => new(null, target.Variable.Name, target.Procedure.Name, CloserToUsageChangeKind.ModuleExtent),
            VBModuleFieldVariableMemberSymbol or VBInstanceFieldVariableMemberSymbol when !keeps => new(null, target.Variable.Name, target.Procedure.Name, CloserToUsageChangeKind.NotKept),
            _ => null,
        };
    }

    // the edits that move the declaration: the variable is removed from its statement, and declared on a line of its own before the statement that first uses
    // it, indented as that statement is. A variable of a procedure keeps its keyword; one of the module takes the keyword chosen.
    public static (IReadOnlyList<RefactorTextEdit>? Edits, CloserToUsageNotice? Refusal) Plan(
        string text, ModuleParseResult parse, CloserToUsageTarget target, CloserToUsageKeyword keyword)
    {
        if (DeclarationEdits.RemoveVariable(parse, text, target.Declaration) is not { } removed)
        {
            return (null, new(CloserToUsageRefusalKind.SharesItsLine, target.Variable.Name));
        }

        var written = target.IsOfTheModule ? keyword.ToString() : target.Declaration.IsStatic ? "Static" : "Dim";
        var line = target.Before.SourceLocation.Range.Start.Line;
        var declarator = Slice(text, new SourceRange(target.Declaration.NameRange!.Value.Start, target.Declaration.SourceLocation.Range.End));
        var at = new SourcePosition(line, 0);
        var inserted = new RefactorTextEdit
        {
            Range = new SourceRange(at, at),
            NewText = $"{DeclarationEdits.IndentOf(text, line)}{written} {declarator}{new ExtractionText(text).NewLine}",
        };

        return ([.. removed.Append(inserted).OrderBy(edit => edit.Range.Start)], null);
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

    // whether a declaration of a procedure is already where it would go: directly before the statement, with nothing but declarations between them, or after it.
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
