using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics;
using RDCore.SDK.Server.ProtocolExtensions;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Workspace;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00405</c>: a <c>Public</c> procedure that implements a member of an interface or handles an event (<strong>MS-VBAL §5.3.1.8</strong>, <strong>§5.3.1.9</strong>).
/// </summary>
/// <remarks>
/// <para>
/// A public member is a member of the default interface of the class module. An implementation of a member of another type does not belong there: it exposes that type's
/// member on this one's. A handler of an event is called by the language when the event is raised, and never by anything else.
/// </para>
/// <para>
/// That a member is the one or the other is a fact of the declaration (<see cref="DeclarationFact.Role"/>) that the host vouches for: it knows which names the interfaces
/// of the module and the events of its <c>WithEvents</c> variables make, and the analyzer does not guess it from the underscore in a name.
/// </para>
/// <para>
/// The fix writes <c>Private</c> in place of <c>Public</c>, or before a declaration that states no access, on every accessor of a property. The interface or
/// the event calls the member as before; code that calls it by its own name (<c>shape.IShape_Draw</c>) compiles only in its module. The fix is offered when
/// the host counts every reference to the member over the project (<see cref="DeclarationFactDto.ReferencedFrom"/>: a member of a program, not reached late)
/// and all of them are in the member's module.
/// </para>
/// </remarks>
internal sealed class ImplementationsShouldBePrivateAnalyzer : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.ImplementationsShouldBePrivate.ToCode()];

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        var members = context.ParseResult.SyntaxTree?.Descendants().OfType<MemberDeclarationNode>().ToArray() ?? [];
        return (context.Semantics?.Declarations ?? [])
            .Where(declaration => declaration is
                    { Role: DeclarationRole.InterfaceImplementation or DeclarationRole.EventHandler, Kind: DeclarationKind.Procedure or DeclarationKind.Property }
                && declaration.Access is AccessModifier.Public or AccessModifier.Implicit)
            .Select(declaration => new AnalyzerFinding(
                RDCoreDiagnosticId.ImplementationsShouldBePrivate.ToCode(),
                declaration.Location.Range,
                DiagnosticSeverity.Information,
                AnalyzerMessages.Format(
                    declaration.Role is DeclarationRole.EventHandler
                        ? RDCoreDiagnosticsResources.ImplementationsShouldBePrivate_EventHandler_Message
                        : RDCoreDiagnosticsResources.ImplementationsShouldBePrivate_Interface_Message,
                    declaration.Name))
            {
                Fixes = FixOf(declaration, context.Document, members) is { } fix ? new DiagnosticFixes { Edits = [fix] } : null,
            });
    }

    // Private in place of Public, the declaration's first word, or before a declaration that states no access (MS-VBAL §5.3.1.1); on every accessor of a
    // property. Not when the host does not count the member's references, nor when one of them is in another module: a reference is located in the
    // file of its module, and is of this one when a procedure of it is where it is written. Another module of the file is a block's, and so is none of
    // this module's procedures (RDCoreDocument).
    private static EditFix? FixOf(DeclarationFactDto declaration, Uri document, MemberDeclarationNode[] members)
    {
        var file = RDCoreDocument.FileOf(document);
        if (declaration.ReferencedFrom is not { IsDefault: false } sites
            || !sites.All(site => site.Location.Uri == file && members.Any(member => Holds(member.SourceLocation.Range, site.Location.Range.Start))))
        {
            return null;
        }

        var edits = members
            .Where(member => string.Equals(member.Name, declaration.Name, StringComparison.OrdinalIgnoreCase)
                && member.AccessModifier is AccessModifier.Public or AccessModifier.Implicit
                && (declaration.Kind is DeclarationKind.Property
                    ? member.MemberKind is MemberKind.PropertyGet or MemberKind.PropertyLet or MemberKind.PropertySet
                    : member.MemberKind is MemberKind.Procedure or MemberKind.Function))
            .Select(member => (member.SourceLocation.Range.Start, IsPublic: member.AccessModifier is AccessModifier.Public))
            .Select(member => new TextEdit
            {
                Range = (member.IsPublic
                    ? new SourceRange(member.Start, new SourcePosition(member.Start.Line, member.Start.Character + Tokens.Public.Length))
                    : new SourceRange(member.Start, member.Start)).ToLsp(),
                NewText = member.IsPublic ? Tokens.Private : $"{Tokens.Private} ",
            })
            .ToList();
        return edits.Count == 0
            ? null
            : new EditFix { Title = AnalyzerMessages.Format(RDCoreDiagnosticsResources.ImplementationsShouldBePrivate_Fix, declaration.Name), Edits = edits };
    }

    private static bool Holds(SourceRange range, SourcePosition position) => range.Start.CompareTo(position) <= 0 && position.CompareTo(range.End) <= 0;
}
