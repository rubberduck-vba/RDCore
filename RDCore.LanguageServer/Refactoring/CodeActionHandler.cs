using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Services.Refactoring;
using RDCore.LanguageServer.Services.Refactoring.ExtractMethod;
using RDCore.LanguageServer.Services.Refactoring.MoveMember;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.SDK.Server.Services;
using System.IO.Abstractions;

namespace RDCore.LanguageServer.Refactoring;

/// <summary>
/// Serves <c>textDocument/codeAction</c> and <c>codeAction/resolve</c> (<strong>LSP 3.17</strong> §Code Action Request) with what the
/// <see cref="CodeActionService"/> offers, for the client as it initialized.
/// </summary>
internal sealed class CodeActionHandler(
    IRenameService rename, IWorkspaceDocumentService documents, IFileSystem fileSystem, IExtractMethodService extractMethod, IMoveMemberService moveMember,
    IPlatformClientCapabilitiesService clientCapabilities, OmniSharp.Extensions.JsonRpc.ISerializer serializer, IProjectFileService? projectFiles = null,
    ILibrarySource? libraries = null, IEncapsulateFieldService? encapsulateField = null, IImplementInterfaceService? implementInterface = null,
    IMoveCloserToUsageService? closerToUsage = null) : CodeActionHandlerBase
{
    /// <summary>
    /// The kind of the action that renames a symbol (<see cref="RefactoringKinds.Rename"/>).
    /// </summary>
    public static readonly CodeActionKind RefactorRename = new(RefactoringKinds.Rename);

    /// <summary>
    /// The kind of the action that extracts the statements of a selection as a procedure (<see cref="RefactoringKinds.ExtractMethod"/>).
    /// </summary>
    public static readonly CodeActionKind RefactorExtractMethod = new(RefactoringKinds.ExtractMethod);

    /// <summary>
    /// The kind of the action that converts a <c>GoSub</c> routine to a procedure (<see cref="RefactoringKinds.ConvertGoSub"/>).
    /// </summary>
    public static readonly CodeActionKind RefactorConvertGoSub = new(RefactoringKinds.ConvertGoSub);

    /// <summary>
    /// The kind of the action that removes a parameter (<see cref="RefactoringKinds.RemoveParameter"/>).
    /// </summary>
    public static readonly CodeActionKind RefactorRemoveParameter = new(RefactoringKinds.RemoveParameter);

    /// <summary>
    /// The kind of the action that moves a procedure above the one before it (<see cref="RefactoringKinds.MoveMemberUp"/>).
    /// </summary>
    public static readonly CodeActionKind RefactorMoveMemberUp = new(RefactoringKinds.MoveMemberUp);

    /// <summary>
    /// The kind of the action that moves a procedure below the one after it (<see cref="RefactoringKinds.MoveMemberDown"/>).
    /// </summary>
    public static readonly CodeActionKind RefactorMoveMemberDown = new(RefactoringKinds.MoveMemberDown);

    /// <summary>
    /// The kind of the action that sorts the procedures of a document: a source action (<see cref="RefactoringKinds.SortMembers"/>).
    /// </summary>
    public static readonly CodeActionKind SourceSortMembers = new(RefactoringKinds.SortMembers);

    /// <summary>
    /// The kind of the action that encapsulates a field behind properties (<see cref="RefactoringKinds.EncapsulateField"/>).
    /// </summary>
    public static readonly CodeActionKind RefactorEncapsulateField = new(RefactoringKinds.EncapsulateField);

    /// <summary>
    /// The kind of the action that writes the procedures an <c>Implements</c> directive or a <c>WithEvents</c> variable calls for
    /// (<see cref="RefactoringKinds.ImplementInterface"/>).
    /// </summary>
    public static readonly CodeActionKind RefactorImplementInterface = new(RefactoringKinds.ImplementInterface);

    /// <summary>
    /// The kind of the action that moves the declaration of a variable closer to its usage (<see cref="RefactoringKinds.MoveCloserToUsage"/>).
    /// </summary>
    public static readonly CodeActionKind RefactorMoveCloserToUsage = new(RefactoringKinds.MoveCloserToUsage);

    /// <summary>
    /// The kind of the action that imports a VB6 project: a source action of the whole workspace (<see cref="RefactoringKinds.ImportProject"/>).
    /// </summary>
    public static readonly CodeActionKind SourceImportProject = new(RefactoringKinds.ImportProject);

    private readonly CodeActionService _actions = new(rename, documents, fileSystem, extractMethod, moveMember, clientCapabilities, serializer, projectFiles, libraries, encapsulateField, implementInterface,
        closerToUsage);

    // what the client said it can do, as it initialized: a rename's edits are sent in the form it applies.
    private ClientCapabilities? _client;

    public override async Task<CommandOrCodeActionContainer?> Handle(CodeActionParams request, CancellationToken cancellationToken)
        => new CommandOrCodeActionContainer((await _actions.ActionsAsync(request, _client, cancellationToken)).Select(action => new CommandOrCodeAction(action)));

    public override Task<CodeAction> Handle(CodeAction request, CancellationToken cancellationToken) => _actions.ResolveAsync(request, _client, cancellationToken);

    protected override CodeActionRegistrationOptions CreateRegistrationOptions(CodeActionCapability capability, ClientCapabilities clientCapabilities)
    {
        _client = clientCapabilities;
        return new()
        {
            CodeActionKinds = new Container<CodeActionKind>(
                CodeActionKind.QuickFix, RefactorRename, RefactorExtractMethod, RefactorConvertGoSub, RefactorRemoveParameter, RefactorMoveMemberUp, RefactorMoveMemberDown,
                SourceSortMembers, RefactorEncapsulateField, RefactorImplementInterface, RefactorMoveCloserToUsage, SourceImportProject),
            ResolveProvider = true,
        };
    }
}
