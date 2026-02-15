using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Parsing.Model.Abstract;
using RDCore.Parsing.Model.Types.Abstract;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Parsing.Model.Types.Members;

internal record class VBFunctionMember : VBReturningMember
{
    protected VBFunctionMember(Uri workspaceRoot, string name, Accessibility accessibility, SymbolKind kind, Uri parentUri, Range? range = default, Range? selectionRange = default, bool isHidden = false)
        : base(workspaceRoot, name, accessibility, kind, parentUri, range, selectionRange, isHidden)
    {
    }
}
