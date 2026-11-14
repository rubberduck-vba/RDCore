using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.Runtime.LanguageCore.Static;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC11019</c>: a reserved function of the library written with a type character that does not match its declared type (<c>Len$</c>, <c>CVar$</c>,
/// <c>Abs%</c>), which <strong>MS-VBAL §5.6.10</strong> makes invalid, and <strong>MS-VBA</strong> compiles, the character ignored.
/// </summary>
/// <remarks>
/// RD-VBA does as MS-VBA does (recorded in Excel: oracle\s46 F18, oracle\s44 D01-D18): <c>Len$("abc")</c> is <c>3</c>, a <c>Long</c>. A warning, because the
/// specification refuses the name, and the character says a type the call does not have. The analyzer reads the character off the syntax tree, and compares
/// it with the declared type of the reserved name it is written on, where the name is bound to what a library declares under another name.
/// </remarks>
internal sealed class IgnoredTypeCharacterAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context.ParseResult.SyntaxTree is not { } module || context.Semantics is not { } semantics)
        {
            return [];
        }

        var facts = semantics.Procedures.SelectMany(procedure => procedure.Expressions).DistinctBy(fact => fact.Node).ToDictionary(fact => fact.Node);
        return module.Descendants().OfType<SimpleNameExpressionNode>()
            .Where(name => facts.TryGetValue(name.Identity, out var fact) && IgnoresTheCharacter(name, fact))
            .Select(name => facts[name.Identity])
            .Select(fact => new AnalyzerFinding(
                RDCoreDiagnosticId.IgnoredTypeCharacter.ToCode(),
                fact.Location.Range,
                DiagnosticSeverity.Warning,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.IgnoredTypeCharacter_Message, SymbolUri.NameOf(fact.Binding!), fact.DeclaredType!)));
    }

    // a name declared without the character, written with one (`Len$`), whose type is not the type the character says.
    private static bool IgnoresTheCharacter(SimpleNameExpressionNode name, ExpressionFactDto fact)
        => name.TypeHint is { Length: > 0 } hint
            && fact is { Binding: { } binding, DeclaredType: { } declared } && declared != VBTypeNames.VBUnknown
            && fact.Flags.HasFlag(ValueExpressionSemanticFlags.BoundToLibrary)
            && !string.Equals(SymbolUri.NameOf(binding), name.WrittenName, StringComparison.OrdinalIgnoreCase)
            && ReservedNameStaticSemantics.IsReservedName(name.IdentifierName)
            && IntrinsicVBTypes.TryResolveTypeHint(hint, out var hinted) && !string.Equals(hinted.Name, declared, StringComparison.OrdinalIgnoreCase);
}
