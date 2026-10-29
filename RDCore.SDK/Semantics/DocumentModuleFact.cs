using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Workspace;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics;

/// <summary>
/// What a document module is (<see cref="ModuleKinds"/>): the code of a host's document, which RD-VBA analyzes and does not host. Its code that presumes the
/// host - a member of the host's class it names, a handler of the host's events - is code to move into a class that holds the document in a <c>WithEvents</c>
/// variable, from an application the program automates.
/// </summary>
/// <param name="HostClass">The class of the host library its document is an object of; <see langword="null"/> for a host RD-VBA does not know.</param>
/// <param name="HostClassUri">
/// The address of that class, which the members it has are addressed under; <see langword="null"/> when the project does not reference its library, and when the
/// host is not known.
/// </param>
/// <param name="HostEvents">The names of the events of the host's class, which a procedure named <c>Worksheet_Change</c> is named as a handler of.</param>
public sealed record class DocumentModuleFact(DocumentHostClass? HostClass, Uri? HostClassUri, ImmutableArray<string> HostEvents)
{
    /// <summary>
    /// What a document module is, as the composition has its host.
    /// </summary>
    /// <param name="module">The module.</param>
    /// <param name="resolver">The resolver its host's class is found by.</param>
    /// <returns>The fact; <see langword="null"/> for a module that is no document module.</returns>
    public static DocumentModuleFact? Of(VBClassModuleSymbol module, ISymbolResolver resolver)
    {
        if (!module.IsDocument)
        {
            return null;
        }

        var host = VBProjectSymbol.ResolveHostClass(resolver, module);
        return new(module.HostClass, host?.Uri, [.. (host?.Members ?? []).OfType<VBEventMemberSymbol>().Select(declared => declared.Name)]);
    }

    /// <summary>
    /// The prefix of the name of a procedure that MS-VBA's host calls when its document raises an event: the host class's name and an underscore
    /// (<c>Worksheet_</c>, <c>Workbook_</c>, <c>Document_</c>, <c>Form_</c>); <see langword="null"/> for a host RD-VBA does not know.
    /// </summary>
    public string? HandlerPrefix => HostClass is { } host ? host.ClassName + "_" : null;
}
