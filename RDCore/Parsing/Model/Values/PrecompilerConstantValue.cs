using RDCore.Parsing.Model.Values.Intrinsic;

namespace RDCore.Parsing.Model.Values;

internal sealed record class PrecompilerConstantValue : VBIntegerValue
{
    public PrecompilerConstantValue(string name, Uri parentUri, int value)
    {
        Name = name;
        ParentUri = parentUri;
        NumericValue = value;
    }

    public string Name { get; init; }
    public Uri ParentUri { get; init; }

    public bool IsWorkspaceScope => ParentUri is null;
    public bool IsFileScope => ParentUri is not null;
}
