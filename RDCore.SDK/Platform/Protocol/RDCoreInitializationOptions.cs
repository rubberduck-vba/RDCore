namespace RDCore.SDK.Platform.Protocol;

/// <summary>
/// What a client says of itself to a server in the <c>initializationOptions</c> of the LSP <c>initialize</c> request. That part of the
/// protocol exists for exactly this: the settings of a server that no standard capability describes.
/// </summary>
/// <remarks>
/// For example, the language the workspace is written in: a server cannot tell RD-VBA from VB6 or BASIC by looking at the files, and the
/// dialect decides what it builds (the name of the standard library, where an undeclared name declares its variable, which statements
/// exist at all). A server started with a command line has already been told, and what the client sends here wins.
/// </remarks>
public sealed record class RDCoreInitializationOptions
{
    /// <summary>
    /// The identifier of the language the workspace is written in (<see cref="Workspace.SupportedLanguage.Id"/>), or <see langword="null"/>
    /// when the client does not say, which leaves the server's own setting.
    /// </summary>
    public string? Language { get; init; }

    /// <summary>
    /// The language of the client's user interface, as a language tag (<c>fr-CA</c>), which a server speaks to it in: the same as the request's own
    /// <c>locale</c> (<strong>LSP 3.17</strong> <c>InitializeParams.locale</c>), which the language client a platform's client is built on has no option to
    /// send. A server reads the request's <c>locale</c> first; <see langword="null"/> says none.
    /// </summary>
    public string? Locale { get; init; }

    /// <summary>
    /// Whether nobody is there to answer what the server would ask the client's user: what the platform handshake's <see cref="Client.Unattended"/> says, for
    /// what the server asks before the handshake.
    /// </summary>
    /// <remarks>
    /// A server loads its workspace while it answers <c>initialize</c>, and the platform handshake (<c>rdcore/platform/initialize</c>) follows that answer: what
    /// the server asks as it loads, such as whether to regenerate a project file it cannot read, it cannot wait for the handshake to know whether to ask. A
    /// server asks nothing of a client that says <see langword="true"/>, and does without the answer, as the handshake's <see cref="Client.Unattended"/> has it
    /// do with a program's dialogs, whatever it says of <see cref="AnswersDuringInitialize"/>.
    /// </remarks>
    public bool Unattended { get; init; }

    /// <summary>
    /// Whether the client answers what the server asks its user (<strong>LSP 3.17</strong> <c>window/showMessageRequest</c>) before <c>initialize</c> is
    /// answered, with a choice or with none, and never with an error.
    /// </summary>
    /// <remarks>
    /// <para>
    /// LSP allows a server to send <c>window/showMessageRequest</c> while it answers <c>initialize</c>, and a server waits for the answer: what it asks as it
    /// loads its workspace, such as whether to regenerate a project file it cannot read, decides what it loads. A client that cannot answer then leaves
    /// <c>initialize</c> unanswered for good, and the client waits for that answer: neither side moves again.
    /// </para>
    /// <para>
    /// A client built on OmniSharp's language client (0.19.9) is such a client, unless it answers with a choice. Until it is initialized, OmniSharp's language
    /// client writes no message except the <c>initialize</c> request, the <c>initialized</c> notification and a response with a result, and holds every other
    /// one until it is. Error responses are held as well, including the one it answers with when its handler fails, when its user cannot be asked, or when it has
    /// no handler for the request at all.
    /// </para>
    /// <para>
    /// A server asks nothing before <c>initialize</c> is answered of a client that does not say <see langword="true"/>, nor of one that runs
    /// <see cref="Unattended"/>, and does without the answer, as it does without the answer of a user who did not choose.
    /// </para>
    /// </remarks>
    public bool AnswersDuringInitialize { get; init; }
}
