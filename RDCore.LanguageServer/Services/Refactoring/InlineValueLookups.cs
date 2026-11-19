using RDCore.Runtime.LanguageCore.Refactoring;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;

namespace RDCore.LanguageServer.Services.Refactoring;

// the variables whose values a client shows beside their names while the program is stopped (LSP 3.17 §Inline Value Request): each name written in the
// procedure the program is stopped in, from the procedure's first line to the line it is stopped at, that names a local variable, a parameter or a variable of
// the module. A name that calls a procedure, or names a constant, has no value to look up, and the lines below the stop have not run.
public static class InlineValueLookups
{
    private static readonly IReadOnlySet<MemberKind> Procedures = new HashSet<MemberKind>
    {
        MemberKind.Procedure, MemberKind.Function, MemberKind.PropertyGet, MemberKind.PropertyLet, MemberKind.PropertySet,
    };

    public static IReadOnlyList<(string Name, SourceRange Range)> In(RenameContext context, Uri module, SourceRange range, SourcePosition stopped)
    {
        if (context.Modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == module.AbsoluteUri) is not { } source
            || source.Syntax.Descendants().OfType<MemberDeclarationNode>()
                .FirstOrDefault(member => Procedures.Contains(member.MemberKind) && Contains(member.SourceLocation.Range, stopped)) is not { } procedure)
        {
            return [];
        }

        var first = Later(procedure.SourceLocation.Range.Start, range.Start);
        var last = Earlier(Earlier(procedure.SourceLocation.Range.End, range.End), new SourcePosition(stopped.Line, int.MaxValue));
        if (last < first)
        {
            return [];
        }

        var lookups = new List<(string Name, SourceRange Range)>();
        foreach (var written in NameOccurrences.NamesWrittenIn(context, source, new SourceRange(first, last)))
        {
            if (written.Binding is { } binding && NameOccurrences.SymbolOf(context, binding) is { } symbol && IsVariable(symbol))
            {
                lookups.Add((symbol.Name, written.Range));
            }
        }

        return lookups;
    }

    // a local variable (MS-VBAL §5.4.3.1), a parameter (§5.3.1.5), or a variable of the module (§5.2.3.1).
    private static bool IsVariable(Symbol symbol)
        => symbol is VBLocalVariableSymbol or VBParameterSymbol or ParamArrayParameterSymbol or VBModuleFieldVariableMemberSymbol or VBInstanceFieldVariableMemberSymbol;

    private static bool Contains(SourceRange range, SourcePosition position) => range.Start <= position && position <= range.End;

    private static SourcePosition Later(SourcePosition left, SourcePosition right) => left < right ? right : left;

    private static SourcePosition Earlier(SourcePosition left, SourcePosition right) => left < right ? left : right;
}
