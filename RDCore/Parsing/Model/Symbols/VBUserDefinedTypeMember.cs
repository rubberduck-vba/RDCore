using RDCore.Parsing.Model.Types.Complex;
using RDCore.Server.ProtocolExtensions;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Parsing.Model.Symbols;

internal record class VBUserDefinedTypeMember : VBTypeMember
{
    public VBUserDefinedTypeMember(Uri workspaceUri, string name, Range? range, Range? selectionRange, Uri parentUri)
        : base(workspaceUri, name, SymbolKindExt.Field, Accessibility.Public, parentUri, range, selectionRange)
    {
    }
}
