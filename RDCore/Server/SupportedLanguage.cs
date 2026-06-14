using RDCore.SDK.Workspace;

namespace RDCore.LanguageServer.Server;

public static class ProtocolSupportedLanguage
{
    public static readonly SupportedLanguage VBA = new("vba", "Microsoft Visual Basic for Applications", "*.bas", "*.cls", "*.frm", "*.doccls");

}