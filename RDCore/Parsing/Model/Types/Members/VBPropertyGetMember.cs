using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Parsing.Model.Abstract;
using RDCore.Parsing.Model.Types.Abstract;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Parsing.Model.Types.Members;

internal interface IVBProperty
{
    string Name { get; }
}

internal record class VBPropertyGetMember : VBReturningMember, IVBProperty
{
    public VBPropertyGetMember(Uri workspaceRoot, string name, SymbolKind kind, Accessibility accessibility, Uri parentUri, Range? range = default, Range? selectionRange = default, bool isHidden = false)
        : base(workspaceRoot, name, accessibility, kind, parentUri, range, selectionRange, isHidden)
    {
    }
}
