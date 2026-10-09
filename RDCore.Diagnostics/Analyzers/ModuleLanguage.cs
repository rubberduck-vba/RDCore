using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Workspace;

namespace RDCore.Diagnostics.Analyzers;

internal static class ModuleLanguage
{
    /// <summary>
    /// Whether the module is written in a language whose syntax is classic BASIC (<see cref="SupportedLanguage.UsesClassicBasicSyntax"/>), as the host vouches for;
    /// <see langword="null"/> when it has not said, or says a language the platform does not serve.
    /// </summary>
    /// <param name="context">The module being analyzed.</param>
    public static bool? UsesClassicBasicSyntax(ModuleAnalysisContext context)
        => context.Semantics?.Language is { } id && SupportedLanguages.TryGet(id, out var language) ? language.UsesClassicBasicSyntax : null;

    /// <summary>
    /// The range of a keyword that begins where <paramref name="construct"/> does.
    /// </summary>
    /// <param name="construct">The range of a construct that begins with the keyword.</param>
    /// <param name="length">How many characters the keyword is.</param>
    public static SourceRange KeywordAt(SourceRange construct, int length)
        => new(construct.Start, new SourcePosition(construct.Start.Line, construct.Start.Character + length));
}
