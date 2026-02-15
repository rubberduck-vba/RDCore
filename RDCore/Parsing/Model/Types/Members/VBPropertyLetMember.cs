using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Parsing.Model.Abstract;

namespace RDCore.Parsing.Model.Types.Members;

internal record class VBPropertyLetMember : VBProcedureMember, IVBProperty
{
    public VBPropertyLetMember(Uri workspaceUri, string name, SymbolKind kind, Accessibility accessibility, Uri parentUri, bool isUserDefined = false, bool isHidden = false)
        : base(workspaceUri, name, kind, accessibility, parentUri, isUserDefined, isHidden)
    {
    }
}
