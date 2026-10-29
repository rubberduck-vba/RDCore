using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC01005</c>: code that presumes it is hosted by the application whose document it belongs to - a document module, and a name of one in another module.
/// RD-VBA is not hosted: it automates an application from the outside, and the code is to move into a class that holds the document in a <c>WithEvents</c>
/// variable of the host's class.
/// </summary>
/// <remarks>
/// <para>
/// A document module is reported once, at its first line, for the one change that fixes it moves the whole module: each place of its code that presumes the
/// host - a member of the host's class it names (<c>Me.Range</c>, an unqualified <c>Range</c>), a procedure named as a handler of the host's events
/// (<c>Worksheet_Change</c>), which nothing raises - is its related information. What the module is the host says (<see cref="DocumentModuleFact"/>). A
/// module with no such place is not reported: an object of the host's class the code holds, from an application it automates, presumes no host.
/// </para>
/// <para>
/// A name of a document module in another module (<c>GameSheet.Reset</c>) is reported where it is written: MS-VBA's host makes it the document, and here it
/// stands for nothing (<see cref="ValueExpressionSemanticFlags.HostedDocumentName"/>). It is what the platform says in place of <em>Variable not defined</em>
/// at that name.
/// </para>
/// </remarks>
internal sealed class HostedDocumentCodeAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context.Semantics is not { } semantics)
        {
            yield break;
        }

        // a document module whose code presumes nothing of its host - the ThisWorkbook and the Sheet1 of every export, with nothing in them - is not reported:
        // what is reported is code to move, and it has none.
        if (semantics.Document is { HostClass: { } host } document && context.ParseResult.SyntaxTree is { } module)
        {
            var hostClass = $"{host.Library}.{host.ClassName}";
            RelatedFinding[] presumes = [.. HostUsesOf(context.Document, module, semantics, document, hostClass), .. HandlersOf(context.Document, module, document, hostClass)];
            if (presumes.Length > 0)
            {
                yield return new AnalyzerFinding(
                    RDCoreDiagnosticId.HostedDocumentCode,
                    FirstLineOf(module),
                    DiagnosticSeverity.Error,
                    AnalyzerMessages.Format(RDCoreDiagnosticsResources.HostedDocumentCode_Message, NameOf(semantics.Module), hostClass))
                {
                    Related = presumes,
                };
            }
        }

        foreach (var fact in References(semantics))
        {
            yield return new AnalyzerFinding(
                RDCoreDiagnosticId.HostedDocumentCode,
                fact.Location.Range,
                DiagnosticSeverity.Error,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.HostedDocumentCode_Reference_Message, NameAt(context, fact)));
        }
    }

    /// <summary>
    /// The names of document modules written in a module, which the platform reports as <c>RDC01005</c> in place of the compile error of the name.
    /// </summary>
    /// <param name="semantics">What the host found out about the module.</param>
    internal static IEnumerable<ExpressionFactDto> References(ModuleSemanticsDto semantics)
        => semantics.Procedures.SelectMany(procedure => procedure.Expressions).Where(fact => fact.Flags.HasFlag(ValueExpressionSemanticFlags.HostedDocumentName));

    // a member of the host's class the module's code reaches as its host's: by its name alone, or as a member of Me. A member of an object of the host's class
    // that the code holds (ws.Range, of a worksheet an application the program automates gave it) is no presuming of the host, and is not one. A member of the
    // host's class is addressed under the class: the same document, and the class's fragment continued with a dot.
    private static IEnumerable<RelatedFinding> HostUsesOf(Uri documentUri, ModuleNode module, ModuleSemanticsDto semantics, DocumentModuleFact document, string hostClass)
    {
        if (document.HostClassUri is not { } classUri)
        {
            yield break;
        }

        var nodes = module.Descendants().OfType<ExpressionNode>().GroupBy(node => node.Identity).ToDictionary(group => group.Key, group => group.First());
        var members = nodes.Values.OfType<MemberAccessExpressionNode>().Select(access => access.Member.Identity).ToHashSet();
        var ofWithMe = new HashSet<SyntaxNodeId>();
        MembersOfWithMe(module, inWithMe: false, ofWithMe);
        bool IsTheHosts(ExpressionFactDto fact) => nodes.TryGetValue(fact.Node, out var node) && node switch
        {
            SimpleNameExpressionNode name => !members.Contains(name.Identity),
            MemberAccessExpressionNode { Owner: InstanceExpressionNode } => true,
            MemberAccessExpressionNode { Owner: null } access => ofWithMe.Contains(access.Identity),
            _ => false,
        };

        var prefix = classUri.Fragment.TrimStart('#') + ".";
        foreach (var fact in semantics.Procedures.SelectMany(procedure => procedure.Expressions).Where(IsTheHosts))
        {
            if (fact.Binding is { } binding
                && binding.GetLeftPart(UriPartial.Path) == classUri.GetLeftPart(UriPartial.Path)
                && binding.Fragment.TrimStart('#') is var address && address.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && !address[prefix.Length..].Contains('.'))
            {
                yield return new RelatedFinding(documentUri, fact.Location.Range,
                    AnalyzerMessages.Format(RDCoreDiagnosticsResources.HostedDocumentCode_HostMember_Message, address[prefix.Length..], hostClass));
            }
        }
    }

    // the member accesses written without their object in a With block of Me (.Range in With Me), which are Me's. A With block inside one has an object of its
    // own, and its accesses are that object's; its expression, written in the outer block, is the outer block's.
    private static void MembersOfWithMe(SyntaxNode node, bool inWithMe, HashSet<SyntaxNodeId> found)
    {
        if (inWithMe && node is MemberAccessExpressionNode { Owner: null } access)
        {
            found.Add(access.Identity);
        }

        if (node is WithStatementNode with)
        {
            MembersOfWithMe(with.WithExpression, inWithMe, found);
            foreach (var statement in with.Body.Children)
            {
                MembersOfWithMe(statement, with.WithExpression is InstanceExpressionNode, found);
            }

            return;
        }

        foreach (var child in node.ChildNodes())
        {
            MembersOfWithMe(child, inWithMe, found);
        }
    }

    // a procedure named as the handler of an event of the host's class: MS-VBA's host calls it, and here nothing does.
    private static IEnumerable<RelatedFinding> HandlersOf(Uri documentUri, ModuleNode module, DocumentModuleFact document, string hostClass)
    {
        if (document.HandlerPrefix is not { } prefix)
        {
            yield break;
        }

        foreach (var procedure in module.Children.OfType<MemberDeclarationNode>().Where(member => member.MemberKind == MemberKind.Procedure))
        {
            if (procedure.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && document.HostEvents.Contains(procedure.Name[prefix.Length..], StringComparer.OrdinalIgnoreCase))
            {
                yield return new RelatedFinding(documentUri, procedure.SourceLocation.Range,
                    AnalyzerMessages.Format(RDCoreDiagnosticsResources.HostedDocumentCode_Handler_Message, procedure.Name, hostClass));
            }
        }
    }

    // the first line of the module's code: a document module's export has no attribute that names it.
    private static SourceRange FirstLineOf(ModuleNode module)
        => module.Children.FirstOrDefault()?.SourceLocation.Range is { } first
            ? new SourceRange(first.Start, first.Start.Line == first.End.Line ? first.End : first.Start)
            : SourceRange.Empty;

    private static string NameOf(Uri module) => module.Fragment.TrimStart('#');

    // the name as it is written: the reference is to nothing, and no symbol has the name.
    private static string NameAt(ModuleAnalysisContext context, ExpressionFactDto fact)
        => context.ParseResult.SyntaxTree?.Descendants().OfType<SimpleNameExpressionNode>().FirstOrDefault(name => name.Identity == fact.Node)?.IdentifierName
           ?? string.Empty;
}
