using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00305</c>: the <c>Global</c> access modifier, which is <c>Public</c> (<strong>MS-VBAL §5.2.3</strong>).
/// </summary>
/// <remarks>
/// One finding is made for each statement, however many variables or constants it declares. The finding is the keyword, which a declaration begins with.
/// </remarks>
internal sealed class ObsoleteGlobalModifierAnalyzer : SyntaxTreeAnalyzer
{
    private const string Keyword = "Global";

    protected override IEnumerable<AnalyzerFinding> Analyze(ModuleNode module)
    {
        var statements = DeclarationStatements.In(module.Children)
            .Select(statement => statement[0])
            .Where(first => first is VariableDeclarationNode { AccessModifier: AccessModifier.Global } or ConstantDeclarationNode { AccessModifier: AccessModifier.Global });

        var members = module.Children
            .OfType<MemberDeclarationNode>()
            .Where(member => member.AccessModifier is AccessModifier.Global);

        return statements.Concat(members)
            .Select(declaration => new AnalyzerFinding(
                RDCoreDiagnosticId.ObsoleteGlobalModifier,
                ModuleLanguage.KeywordAt(declaration.SourceLocation.Range, Keyword.Length),
                DiagnosticSeverity.Information,
                RDCoreDiagnosticsResources.ObsoleteGlobalModifier_Message));
    }
}
