namespace RDCore.SDK.Platform.Protocol;

/// <summary>
/// What a client says of itself to a server in the <c>initializationOptions</c> of the LSP <c>initialize</c> request - the part of the
/// protocol that exists for exactly this: the settings of a server that no standard capability describes.
/// </summary>
/// <remarks>
/// The language the workspace is written in is one: a server cannot tell RD-VBA from VB6 or BASIC by looking at the files, and the
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
    /// do with a program's dialogs. A client that does not say is asked.
    /// </remarks>
    public bool Unattended { get; init; }
}
