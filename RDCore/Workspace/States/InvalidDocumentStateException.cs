namespace RDCore.LanguageServer.Workspace.States;

internal class InvalidDocumentStateException : InvalidOperationException
{
    public InvalidDocumentStateException() : base("The document is currently in a state that is invalid for this operation.") { }
    public InvalidDocumentStateException(DocumentStateValue state)
        : base($"This operation (set state: {state}) is invalid in the current state.") { }
}
