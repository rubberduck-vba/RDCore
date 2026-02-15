using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Parsing.Model.Abstract;
using RDCore.Parsing.Model.Types.Abstract;

namespace RDCore.Parsing.Model.Types.Members;

internal interface IVBProperty
{
    string Name { get; }
}

internal record class VBPropertyGetMember : VBReturningMember, IVBProperty
{
    public VBPropertyGetMember(Uri workspaceRoot, string name, SymbolKind kind, Accessibility accessibility, Uri parentUri, bool isUserDefined = false, bool isHidden = false)
        : base(workspaceRoot, name, accessibility, kind, parentUri, isUserDefined, isHidden)
    {
    }
}
