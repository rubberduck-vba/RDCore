using RDCore.Runtime.LanguageCore.Refactoring;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;

namespace RDCore.LanguageServer.Services.Refactoring;

// the places a client edits as one while the user types a name (LSP 3.17 §Linked Editing Range): each place the procedure writes the name of a local
// variable, a parameter or a line label of its own. A name of the module or of another module is named outside the procedure too, as is a parameter a named
// argument names, and is linked nowhere: a rename changes it everywhere it is named. A name written in brackets is linked nowhere either, since the
// brackets are not part of the name, and nor is any name of a procedure that writes a name whose meaning is not known.
public static class LinkedEditingLookups
{
    private static readonly IReadOnlySet<MemberKind> Procedures = new HashSet<MemberKind>
    {
        MemberKind.Procedure, MemberKind.Function, MemberKind.PropertyGet, MemberKind.PropertyLet, MemberKind.PropertySet,
    };

    public static IReadOnlyList<SourceRange> At(RenameContext context, Uri module, SourcePosition position)
    {
        if (context.Modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == module.AbsoluteUri) is not { } source
            || source.Syntax.Children.OfType<MemberDeclarationNode>()
                .FirstOrDefault(member => Procedures.Contains(member.MemberKind) && Contains(member.SourceLocation.Range, position)) is not { } procedure)
        {
            return [];
        }

        // MS-VBAL §5.4.1.1: a label is a name of its procedure only.
        if (LabelRename.At(source, position) is { } label)
        {
            return label.Occurrences.Any(occurrence => occurrence.IsBracketed) ? [] : [.. label.Occurrences.Select(occurrence => occurrence.Range)];
        }

        var within = procedure.SourceLocation.Range;
        if (SymbolNavigation.OccurrencesAt(context, module, position) is not { Symbol: var symbol } target || !IsOfTheProcedure(symbol)
            || target.Unresolved.Any(site => site.Module.AbsoluteUri == module.AbsoluteUri && Contains(within, site.Range.Start)))
        {
            // a name in the procedure whose meaning is not known may be this one: the edit of the others would leave it behind.
            return [];
        }

        // a parameter that a named argument names outside the procedure (MS-VBAL §5.6.13.1) is named there too.
        if (target.Sites.Any(site => site.Module.AbsoluteUri != module.AbsoluteUri || !Contains(within, site.Range.Start)))
        {
            return [];
        }

        var ranges = target.Sites
            .Select(site => site.Range).Distinct().OrderBy(range => range.Start).ToList();
        var lines = source.Source.Split('\n');
        return ranges.Any(range => Contains(range, position)) && !ranges.Any(range => IsBracketed(lines, range)) ? ranges : [];
    }

    // a local variable (MS-VBAL §5.4.3.1) or a parameter (§5.3.1.5): its scope is the procedure that declares it.
    private static bool IsOfTheProcedure(Symbol symbol) => symbol is VBLocalVariableSymbol or VBParameterSymbol or ParamArrayParameterSymbol;

    private static bool IsBracketed(string[] lines, SourceRange range)
        => range.Start.Line < lines.Length && range.Start.Character < lines[range.Start.Line].Length && lines[range.Start.Line][range.Start.Character] == '[';

    private static bool Contains(SourceRange range, SourcePosition position) => range.Start <= position && position <= range.End;
}
