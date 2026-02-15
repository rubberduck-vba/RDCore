using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Parsing.Model.Abstract;
using RDCore.Parsing.Model.Types.Abstract;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Parsing.Model.Types.Members;

internal record class VBEnumMember : VBReturningMember
{
    public VBEnumMember(Uri workspaceRoot, string name, Uri parentUri, Range? range = default, Range? selectionRange = default, bool isHidden = false)
        : base(workspaceRoot, name, Accessibility.Public, SymbolKind.EnumMember, parentUri, range, selectionRange, isHidden)
    {
    }
}
