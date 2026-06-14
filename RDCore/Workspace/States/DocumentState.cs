namespace RDCore.LanguageServer.Workspace.States;

public abstract record class DocumentState
{
    public static DocumentState Unloaded { get; } = new UnloadedDocumentState();
    public static DocumentState Loaded { get; } = new LoadedDocumentState();
    public static DocumentState Missing { get; } = new MissingDocumentState();
    public static DocumentState LoadError { get; } = new LoadErrorDocumentState();
    public static DocumentState Opened { get; } = new OpenedDocumentState();

    protected DocumentState(DocumentStateValue value)
    {
        Value = value;
    }
    public DocumentStateValue Value { get; }
}

public record class UnloadedDocumentState : DocumentState { public UnloadedDocumentState() : base(DocumentStateValue.Unloaded) { } }
public record class LoadedDocumentState : DocumentState { public LoadedDocumentState() : base(DocumentStateValue.Loaded) { } }
public record class MissingDocumentState : DocumentState { public MissingDocumentState() : base(DocumentStateValue.Missing) { } }
public record class LoadErrorDocumentState : DocumentState { public LoadErrorDocumentState() : base(DocumentStateValue.LoadError) { } }
public record class OpenedDocumentState : DocumentState { public OpenedDocumentState() : base(DocumentStateValue.Opened) { } }
