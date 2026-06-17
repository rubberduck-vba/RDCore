using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace RDCore.SDK.Workspace;

public class SupportedLanguage
{
    public SupportedLanguage(string id, string name, params string[] fileTypes)
    {
        Id = id;
        Name = name;
        FileTypes = fileTypes;
    }

    public string Id { get; }
    public string Name { get; }
    public string[] FileTypes { get; }

    public string FilterString => string.Join(";", FileTypes.Select(fileType => $"**/{fileType}").ToArray());
    public TextDocumentSelector ToTextDocumentSelector() => new(
        new TextDocumentFilter
        {
            Language = Id,
            Pattern = FilterString,
        });
}