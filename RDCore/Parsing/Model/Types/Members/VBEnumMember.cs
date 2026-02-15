using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Parsing.Model.Abstract;
using RDCore.Parsing.Model.Types.Abstract;

namespace RDCore.Parsing.Model.Types.Members;

internal record class VBEnumMember : VBReturningMember
{
    public VBEnumMember(Uri workspaceRoot, string name, Uri parentUri, bool isUserDefined = false, bool isHidden = false)
        : base(workspaceRoot, name, Accessibility.Public, SymbolKind.EnumMember, parentUri, isUserDefined, isHidden)
    {
    }
}
