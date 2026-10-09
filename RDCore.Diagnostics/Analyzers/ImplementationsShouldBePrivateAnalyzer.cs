using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Semantics;

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
/// </remarks>
internal sealed class ImplementationsShouldBePrivateAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => (context.Semantics?.Declarations ?? [])
            .Where(declaration => declaration is { Role: not DeclarationRole.None, Kind: DeclarationKind.Procedure or DeclarationKind.Property }
                && declaration.Access is AccessModifier.Public or AccessModifier.Implicit)
            .Select(declaration => new AnalyzerFinding(
                RDCoreDiagnosticId.ImplementationsShouldBePrivate,
                declaration.Location.Range,
                DiagnosticSeverity.Information,
                AnalyzerMessages.Format(
                    declaration.Role is DeclarationRole.EventHandler
                        ? RDCoreDiagnosticsResources.ImplementationsShouldBePrivate_EventHandler_Message
                        : RDCoreDiagnosticsResources.ImplementationsShouldBePrivate_Interface_Message,
                    declaration.Name)));
}
