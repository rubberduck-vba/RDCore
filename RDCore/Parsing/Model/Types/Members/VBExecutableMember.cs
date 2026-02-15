using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Parsing.Model.Abstract;
using RDCore.Parsing.Model.Types.Complex;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Parsing.Model.Types.Members;

/// <summary>
/// Describes a <c>VBTypeMember</c> that can be executed with an execution context.
/// </summary>
internal abstract record class VBExecutableMember : VBTypeMember
{
    public VBExecutableMember(Uri workspaceUri, string name, SymbolKind kind, Accessibility accessibility, Uri parentUri, Range? range = default, Range? selectionRange = default, bool isHidden = false)
        : base(workspaceUri, name, kind, accessibility, parentUri, range, selectionRange, isHidden)
    {
    }
}
