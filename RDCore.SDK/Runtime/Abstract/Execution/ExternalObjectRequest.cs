namespace RDCore.SDK.Runtime.Abstract.Execution;

/// <summary>
/// Where an object obtained by name comes from (<see cref="ExternalObjectRequest"/>).
/// </summary>
public enum ExternalObjectSource
{
    /// <summary>A new object of the class a programmatic identifier names, on this machine or on the server machine named (<c>CreateObject</c>).</summary>
    Create,

    /// <summary>The object of the class a programmatic identifier names that is already running (<c>GetObject(, class)</c>).</summary>
    Running,

    /// <summary>The object a file is - or a moniker names, <c>winmgmts:</c> - opened by the class a programmatic identifier names, if one does (<c>GetObject(path)</c>).</summary>
    File,
}

/// <summary>
/// An object obtained by name rather than by its class: what <c>CreateObject</c> and <c>GetObject</c> ask for (<strong>MS-VBAL §6.1.2.8.1.3</strong>,
/// <strong>§6.1.2.8.1.9</strong>), which a program binds to late - through what the object answers to by name, whatever a type library says of it.
/// </summary>
/// <remarks>
/// A growable record rather than a parameter list, as <see cref="ExternalCallRequest"/> is, so that what a request can say grows without another member on every
/// server.
/// </remarks>
/// <param name="Source">Where the object comes from.</param>
/// <param name="ProgId">The programmatic identifier of its class (<c>Scripting.Dictionary</c>), or <see langword="null"/> for a file that says its own.</param>
/// <param name="PathName">The file, or the moniker, of an object from <see cref="ExternalObjectSource.File"/>.</param>
/// <param name="ServerName">The machine an object from <see cref="ExternalObjectSource.Create"/> is created on; <see langword="null"/> or empty for this one.</param>
public sealed record class ExternalObjectRequest(ExternalObjectSource Source, string? ProgId, string? PathName = null, string? ServerName = null)
{
    /// <summary>
    /// The request as the call it is written as: what a log, and the verbose half of an error, say of it.
    /// </summary>
    public string Describe() => Source switch
    {
        ExternalObjectSource.Create => ServerName is { Length: > 0 } server ? $"CreateObject(\"{ProgId}\", \"{server}\")" : $"CreateObject(\"{ProgId}\")",
        ExternalObjectSource.Running => $"GetObject(, \"{ProgId}\")",
        _ => ProgId is { Length: > 0 } progId ? $"GetObject(\"{PathName}\", \"{progId}\")" : $"GetObject(\"{PathName}\")",
    };
}
