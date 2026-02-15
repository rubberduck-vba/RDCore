using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Parsing.Model.Abstract;
using RDCore.Parsing.Model.Types.Abstract;

namespace RDCore.Parsing.Model.Types.Members;

internal record class VBFunctionMember : VBReturningMember
{
    protected VBFunctionMember(Uri workspaceRoot, string name, Accessibility accessibility, SymbolKind kind, Uri parentUri, bool isUserDefined = false, bool isHidden = false)
        : base(workspaceRoot, name, accessibility, kind, parentUri, isUserDefined, isHidden)
    {
    }
}
