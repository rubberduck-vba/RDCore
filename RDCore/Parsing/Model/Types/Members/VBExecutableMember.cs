using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Parsing.Model.Abstract;
using RDCore.Parsing.Model.Types.Complex;

namespace RDCore.Parsing.Model.Types.Members;

/// <summary>
/// Describes a <c>VBTypeMember</c> that can be executed with an execution context.
/// </summary>
internal abstract record class VBExecutableMember : VBTypeMember
{
    public VBExecutableMember(Uri workspaceUri, string name, SymbolKind kind, Accessibility accessibility, Uri parentUri, bool isUserDefined, bool isHidden)
        : base(workspaceUri, name, kind, accessibility, parentUri, isUserDefined, isHidden)
    {
    }
}
