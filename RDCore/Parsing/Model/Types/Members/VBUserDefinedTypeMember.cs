using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Parsing.Model.Abstract;
using RDCore.Parsing.Model.Types.Complex;

namespace RDCore.Parsing.Model.Types.Members;

internal record class VBUserDefinedTypeMember : VBTypeMember
{
    public VBUserDefinedTypeMember(Uri workspaceUri, string name, Uri parentUri)
        : base(workspaceUri, name, SymbolKind.Field, Accessibility.Public, parentUri, isUserDefined: true)
    {
    }
}
