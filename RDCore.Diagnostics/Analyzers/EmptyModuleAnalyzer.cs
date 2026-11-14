using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Directives;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Extensibility.Diagnostics;
using System.Text.RegularExpressions;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC01010</c>: a module that declares nothing: no variable, constant, procedure, <c>Declare</c>, <c>Enum</c>, <c>Type</c> or <c>Event</c>, and no
/// <c>Implements</c> (<strong>MS-VBAL §5.2</strong>).
/// </summary>
/// <remarks>
/// Its options, attributes, <c>Def</c><em>Type</em> directives and comments declare nothing. A module with a designer (a form) is its form as well as its code, and is not
/// judged; nor is a document module, the code of a host's document, which the document has whether it has code or not. That a module is a document module is a fact
/// the host vouches for, so the analyzer that is not given it has nothing to say.
/// </remarks>
internal sealed partial class EmptyModuleAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context is { ParseResult.SyntaxTree: { } module, Semantics: { Document: null } }
            && !HasDesigner(context.ParseResult)
            && !module.Descendants().Any(node => node is VariableDeclarationNode or ConstantDeclarationNode or MemberDeclarationNode or ImplementsDirectiveNode or StatementNode))
        {
            // the module as a whole is what is reported: its first line.
            yield return new AnalyzerFinding(
                RDCoreDiagnosticId.EmptyModule.ToCode(),
                new SourceRange(new SourcePosition(0, 0), new SourcePosition(1, 0)),
                DiagnosticSeverity.Information,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.EmptyModule_Message, module.GetDeclaredName() ?? Path.GetFileNameWithoutExtension(context.Document.LocalPath)));
        }
    }

    // the designer the language server read for the module, or the header of one: a Begin block that names the class of the form (Begin VB.Form Form1,
    // Begin {C62A69F0-...} UserForm1). A class module's header has a BEGIN of its own, which names nothing.
    private static bool HasDesigner(ModuleParseResult parse)
        => parse.Designer is not null || parse.Trivia.OfType<ModuleHeaderTriviaNode>().Any(header => DesignerBegin().IsMatch(header.Source));

    [GeneratedRegex(@"^[ \t]*Begin[ \t]+\S", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex DesignerBegin();
}
