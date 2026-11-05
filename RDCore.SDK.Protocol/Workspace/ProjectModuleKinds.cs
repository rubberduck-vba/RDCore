using System.Text;

namespace RDCore.SDK.Workspace;

/// <summary>
/// What a module of a project is, as the platform decides it: a standard module, a class module, or the code of a host's document.
/// </summary>
public enum ProjectModuleKind
{
    /// <summary>A standard module (a <c>.bas</c>): its text has no <c>VERSION</c> header.</summary>
    Standard = 0,
    /// <summary>A class or designer module: its text opens on a <c>VERSION</c> header (<see cref="ModuleHeader.IsClassModule"/>).</summary>
    Class = 1,
    /// <summary>The code of a host's document (<strong>MS-VBAL §4.2.1</strong>): the project names its document (<see cref="RDCoreModule.Super"/>).</summary>
    Document = 2,
}

/// <summary>
/// Decides what a module of a project is, and reads its text, the way every side of the platform does: a client that shows a project's modules, such as an
/// IDE's add-in, gets the answer the environment host and the language server get.
/// </summary>
/// <remarks>
/// A module whose entry in the project names a document (<see cref="RDCoreModule.Super"/>) is a document module, whatever its text and its file name. Any other
/// module is a class module when its text has a <c>VERSION</c> header, and a standard module otherwise: RD-VBA decides a module's kind from its source, not from
/// its file's extension.
/// </remarks>
public static class ProjectModuleKinds
{
    /// <summary>
    /// The kind of a module that the project says is the code of <paramref name="super"/>, or of no document when it is <see langword="null"/>.
    /// </summary>
    /// <param name="super">What the project says the module's document is (<see cref="RDCoreModule.Super"/>).</param>
    /// <param name="source">The module's text (<see cref="ReadText"/>), or <see langword="null"/> when it could not be read.</param>
    public static ProjectModuleKind Of(DocClassType? super, string? source)
        => super is not null ? ProjectModuleKind.Document
            : ModuleHeader.IsClassModule(source) == true ? ProjectModuleKind.Class
            : ProjectModuleKind.Standard;

    /// <summary>
    /// The kind of <paramref name="module"/>, from its entry in the project and its text.
    /// </summary>
    /// <param name="module">The module's entry in the project.</param>
    /// <param name="source">The module's text (<see cref="ReadText"/>), or <see langword="null"/> when it could not be read.</param>
    public static ProjectModuleKind Of(RDCoreModule module, string? source)
    {
        ArgumentNullException.ThrowIfNull(module);
        return Of(module.Super, source);
    }

    /// <summary>
    /// The text of a module's file, decoded as the platform decodes it: UTF-8, unless a byte order mark says another Unicode encoding, and without that mark.
    /// </summary>
    /// <remarks>
    /// This is what <see cref="File.ReadAllText(string)"/> gives, the way the environment host and the language server read a module, so a client reads the
    /// same <c>VERSION</c> header they do: a header after a UTF-8 or UTF-16 mark is found, and the mark is no part of the text.
    /// </remarks>
    /// <param name="bytes">The file's bytes.</param>
    public static string ReadText(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        using var reader = new StreamReader(new MemoryStream(bytes, writable: false), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
