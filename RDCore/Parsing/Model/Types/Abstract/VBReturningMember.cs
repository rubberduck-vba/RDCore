using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Parsing.Model.Abstract;
using RDCore.Parsing.Model.Types.Complex;

namespace RDCore.Parsing.Model.Types.Abstract;

internal abstract record class VBReturningMember : VBTypeMember
{
    protected VBReturningMember(Uri workspaceRoot, string name, Accessibility accessibility, SymbolKind kind, Uri parentUri, bool isUserDefined = false, bool isHidden = false)
        : base(workspaceRoot, name, kind, accessibility, parentUri, isUserDefined, isHidden)
    {
    }
}
