using RDCore.SDK.Model;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Runtime.StdLib;

namespace RDCore.Runtime.LanguageCore.Services;

// MS-VBAL §4.1, §5.6.10
// which of several namesakes of different projects shadows the others, the lowest rank first: a symbol of no library is the workspace's own and outranks any
// library's; then the standard library's, which comes before every reference (the earlier reference has the higher precedence); then each library by the
// precedence of its reference; and a symbol of a library that carries no precedence comes after every one that does, so that it never ties with the standard
// library or the workspace.
internal static class ReferencePrecedence
{
    public static long RankOf(Symbol symbol) => symbol.GetProperty(SymbolProperties.Library) switch
    {
        null => -1,
        StandardLibrary.LibraryName => 0,
        _ => symbol.TryGetProperty(SymbolProperties.LibraryPriority, out var priority) ? priority : long.MaxValue,
    };
}
