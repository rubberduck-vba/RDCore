using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Parsing.Model.Abstract;
using RDCore.Parsing.Model.Types.Complex;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Parsing.Model.Types.Members;

internal record class VBUserDefinedTypeMember : VBTypeMember
{
    public VBUserDefinedTypeMember(Uri workspaceUri, string name, Range? range, Range? selectionRange, Uri parentUri)
        : base(workspaceUri, name, SymbolKind.Field, Accessibility.Public, parentUri, range, selectionRange)
    {
    }
}
