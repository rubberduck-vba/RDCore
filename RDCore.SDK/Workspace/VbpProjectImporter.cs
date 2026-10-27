using RDCore.SDK.Runtime.Libraries;
using System.Globalization;
using System.Resources;
using System.Text;

namespace RDCore.SDK.Workspace;

/// <summary>
/// What an import of a VB6 project could not carry over to the <c>.rdproj</c>, and why.
/// </summary>
public enum VbpImportNoteKind
{
    /// <summary>A library the project references that the platform has no description of: its <c>{GUID}</c> names none it ships.</summary>
    UnresolvedReference,

    /// <summary>A reference to another VB6 project (<c>Reference=*\A</c>), which a <c>.rdproj</c> cannot reference yet.</summary>
    ProjectReference,

    /// <summary>An ActiveX control the project's forms use (<c>Object=</c>), which the platform does not host.</summary>
    Control,

    /// <summary>
    /// A file outside of the folder of the project (<c>..\Shared\Utils.bas</c>): it belongs to another project, whose folder is a workspace folder of its own
    /// - the platform never takes in a folder outside the workspace folders it is given. Referencing that project is not implemented yet.
    /// </summary>
    OutsideProjectFolder,

    /// <summary>A file named by an absolute path: a <c>.rdproj</c> lists the files under its own folder.</summary>
    AbsolutePath,

    /// <summary>A UserControl, a PropertyPage, a UserDocument or a Designer, which the platform does not compile: it is listed as a file of the project.</summary>
    NotAModule,

    /// <summary>The <c>Startup</c> of the project, which a <c>.rdproj</c> does not say yet.</summary>
    Startup,

    /// <summary>
    /// Conditional compilation arguments (<c>CondComp=</c>) that VB6 refuses as a whole: each is a name and a whole number from -32768 to 32767
    /// (<c>DEBUG = 1 : TRACE = -1</c>), and none of them is carried over.
    /// </summary>
    InvalidConditionalCompilation,

    /// <summary>
    /// The type of the project (<c>Type=Exe</c>, <c>OleDll</c>, <c>OleExe</c>, <c>Control</c>), which a <c>.rdproj</c> does not say yet: whether the project is
    /// run, or used by being referenced.
    /// </summary>
    ProjectType,
}

/// <summary>
/// What an import of a VB6 project could not carry over to the <c>.rdproj</c>.
/// </summary>
/// <param name="Kind">Why.</param>
/// <param name="Subject">What: a file, a library, a control, as the <c>.vbp</c> writes it.</param>
public sealed record class VbpImportNote(VbpImportNoteKind Kind, string Subject)
{
    private static readonly ResourceManager Messages = new("RDCore.SDK.Workspace.VbpImportMessages", typeof(VbpImportNote).Assembly);

    /// <summary>
    /// The note as the user is told it.
    /// </summary>
    /// <param name="culture">The language of the reader; the current UI culture by default.</param>
    public string ToMessage(CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentUICulture;
        return string.Format(culture, Messages.GetString($"VbpImport_{Kind}", culture) ?? "{0}", Subject);
    }
}

/// <summary>
/// What an import of a VB6 project asks its user, and says when it is not made - in the language of the reader, the current UI culture by default.
/// </summary>
public static class VbpImportText
{
    private static readonly ResourceManager Messages = new("RDCore.SDK.Workspace.VbpImportMessages", typeof(VbpImportText).Assembly);

    /// <summary>Whether to replace the project file of a folder with the project of a <c>.vbp</c>.</summary>
    public static string AskReplace(string folder, string vbp, CultureInfo? culture = null) => Say("VbpImport_Ask_Replace", culture, folder, vbp);

    /// <summary>Which of the VB6 projects of a folder to import.</summary>
    public static string AskChoose(string folder, CultureInfo? culture = null) => Say("VbpImport_Ask_Choose", culture, folder);

    /// <summary>The title of the action that imports a VB6 project of the workspace's folder.</summary>
    public static string ImportTitle(string vbp, CultureInfo? culture = null) => Say("VbpImport_Action_Import", culture, vbp);

    /// <summary>The choice that replaces the project file.</summary>
    public static string Replace(CultureInfo? culture = null) => Say("VbpImport_Choice_Replace", culture);

    /// <summary>The choice that makes nothing.</summary>
    public static string Cancel(CultureInfo? culture = null) => Say("VbpImport_Choice_Cancel", culture);

    /// <summary>The import was cancelled.</summary>
    public static string Cancelled(string vbp, CultureInfo? culture = null) => Say("VbpImport_Refused_Cancelled", culture, vbp);

    /// <summary>A folder has no VB6 project.</summary>
    public static string NoProject(string folder, CultureInfo? culture = null) => Say("VbpImport_Refused_NoProject", culture, folder);

    /// <summary>A file is not there.</summary>
    public static string NotFound(string path, CultureInfo? culture = null) => Say("VbpImport_Refused_NotFound", culture, path);

    private static string Say(string key, CultureInfo? culture, params object[] arguments)
    {
        culture ??= CultureInfo.CurrentUICulture;
        return string.Format(culture, Messages.GetString(key, culture) ?? key, arguments);
    }
}

/// <summary>
/// The project a VB6 project file makes, and what it could not carry over.
/// </summary>
/// <param name="Project">The project, as a <c>.rdproj</c> in the folder of the <c>.vbp</c> lists it.</param>
/// <param name="Notes">What was not carried over, in the order the <c>.vbp</c> writes it.</param>
public sealed record class VbpImport(RDCoreProject Project, IReadOnlyList<VbpImportNote> Notes)
{
    /// <summary>
    /// The project file of the <c>.vbp</c>'s folder once the import is made: what the <c>.vbp</c> describes replaces what the project file says of it, and
    /// what the <c>.vbp</c> does not describe is kept.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <c>.vbp</c> describes the project's name, its references, its modules and other files, their folders, and its conditional compilation arguments:
    /// those are the import's. A file the <c>.vbp</c> no longer lists is no longer the project's, and a reference it no longer makes is no longer made.
    /// </para>
    /// <para>
    /// What only the platform says of the project is kept: the settings files of the project file (<see cref="ProjectFile.Configuration"/>), the document
    /// class a module is (<see cref="RDCoreModule.Super"/>), and the type library information of a reference
    /// (<see cref="RDCoreReference.TypeLibInfoPath"/>) - of a module by its path, of a reference by its name. A client that keeps a <c>.vbp</c> and a
    /// <c>.rdproj</c> in step imports the <c>.vbp</c> again after each change, and loses none of it. Its <see cref="ProjectFile.Version"/> is the one that
    /// wrote it, until the platform writes it (<see cref="ProjectFile.WithCurrentVersion"/>).
    /// </para>
    /// </remarks>
    /// <param name="existing">The project file of the folder; <see langword="null"/> when it has none, or one that cannot be read.</param>
    /// <param name="folder">The folder of the <c>.vbp</c>.</param>
    public ProjectFile Into(ProjectFile? existing, string folder)
    {
        if (existing is null)
        {
            return new ProjectFile(folder, Project);
        }

        // a project file written by hand may list a module or a reference twice: the first is the one kept.
        var modules = existing.ProjectInfo.Modules
            .DistinctBy(module => module.RelativeUri, StringComparer.OrdinalIgnoreCase).ToDictionary(module => module.RelativeUri, StringComparer.OrdinalIgnoreCase);
        var references = existing.ProjectInfo.References
            .DistinctBy(reference => reference.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(reference => reference.Name, StringComparer.OrdinalIgnoreCase);
        return existing with
        {
            Uri = folder,
            ProjectInfo = existing.ProjectInfo with
            {
                Name = Project.Name,
                References = [.. Project.References.Select(reference => references.TryGetValue(reference.Name, out var kept)
                    ? reference with { TypeLibInfoPath = kept.TypeLibInfoPath }
                    : reference)],
                Modules = [.. Project.Modules.Select(module => modules.TryGetValue(module.RelativeUri, out var kept) ? module with { Super = kept.Super } : module)],
                OtherFiles = Project.OtherFiles,
                Folders = Project.Folders,
                PrecompilerConstants = Project.PrecompilerConstants,
            },
        };
    }
}

/// <summary>
/// Makes a <c>.rdproj</c> project of a VB6 project file (<c>.vbp</c>): its name, its modules, classes and forms, the libraries it references, and its
/// conditional compilation arguments.
/// </summary>
/// <remarks>
/// <para>
/// A <c>.vbp</c> is <c>Key=Value</c> lines, up to its first <c>[section]</c>; the paths it names are relative to its own folder, which is the folder of the
/// <c>.rdproj</c>. A library is referenced by the identifier of its type library (<c>Reference=*\G{GUID}#version#lcid#path#description</c>), and is the one
/// the platform ships a description of for that identifier (<see cref="ILibrarySource.TryGetByGuid"/>); <c>VBA</c>, which every project references, is
/// the project's already.
/// </para>
/// <para>
/// What a <c>.rdproj</c> cannot say is not carried over, and is noted (<see cref="VbpImportNote"/>): a reference to another project, an ActiveX control,
/// a library the platform has no description of, a file outside of the project's folder, a <c>Startup</c>.
/// </para>
/// </remarks>
public static class VbpProjectImporter
{
    // the type library of VBA itself, which VB6 writes in no .vbp and an exported project may: the standard library, which every project has, unlisted.
    private static readonly Guid VBA = RDCoreReference.StandardLibraryGuid;

    /// <summary>
    /// The text of a <c>.vbp</c>, as VB6 writes it: in the ANSI code page of the machine, unless it starts with a byte order mark.
    /// </summary>
    /// <param name="bytes">The content of the file.</param>
    /// <param name="ansi">The ANSI code page of the machine.</param>
    public static string Decode(byte[] bytes, Encoding ansi)
    {
        using var reader = new StreamReader(new MemoryStream(bytes), ansi, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// The project a <c>.vbp</c> makes.
    /// </summary>
    /// <param name="vbp">The text of the <c>.vbp</c>.</param>
    /// <param name="fileName">The name of the <c>.vbp</c> file, which names a project that names itself nothing.</param>
    /// <param name="libraries">The descriptions of the libraries the platform ships.</param>
    /// <param name="folder">
    /// The absolute path of the <c>.vbp</c>'s folder, which a path that climbs out of it and comes back in is resolved against (<c>..\MyProj\Util.bas</c>
    /// from <c>MyProj\MyProj.vbp</c> is <c>Util.bas</c>); <see langword="null"/> when it is not known, and such a path is outside of the folder.
    /// </param>
    public static VbpImport Import(string vbp, string fileName, ILibrarySource libraries, string? folder = null)
    {
        var notes = new List<VbpImportNote>();
        var modules = new List<RDCoreModule>();
        var others = new List<RDCoreFile>();
        var references = new List<RDCoreReference>();
        var constants = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var name = Path.GetFileNameWithoutExtension(fileName);

        foreach (var (key, value) in Entries(vbp))
        {
            switch (key.ToUpperInvariant())
            {
                case "NAME":
                    name = Unquoted(value);
                    break;
                case "MODULE" or "CLASS":
                    // Module=Module1; Module1.bas: the name is the module's own, its Attribute VB_Name.
                    AddFile(PathOf(value[(value.IndexOf(';') + 1)..]), asModule: true);
                    break;
                case "FORM":
                    AddFile(PathOf(value), asModule: true);
                    break;
                case "USERCONTROL" or "PROPERTYPAGE" or "USERDOCUMENT" or "DESIGNER":
                    if (AddFile(PathOf(value), asModule: false) is { } file)
                    {
                        notes.Add(new VbpImportNote(VbpImportNoteKind.NotAModule, file));
                    }
                    break;
                case "RELATEDDOC" or "RESFILE32":
                    AddFile(PathOf(value), asModule: false);
                    break;
                case "REFERENCE":
                    AddReference(value);
                    break;
                case "OBJECT":
                    // Object={GUID}#2.0#0; MSCOMCTL.OCX
                    notes.Add(new VbpImportNote(VbpImportNoteKind.Control, value[(value.IndexOf(';') + 1)..].Trim()));
                    break;
                case "CONDCOMP":
                    if (Constants(Unquoted(value)) is not { } arguments)
                    {
                        notes.Add(new VbpImportNote(VbpImportNoteKind.InvalidConditionalCompilation, Unquoted(value)));
                        break;
                    }

                    foreach (var (constant, number) in arguments)
                    {
                        constants[constant] = number;
                    }
                    break;
                case "TYPE":
                    if (Unquoted(value) is { Length: > 0 } type)
                    {
                        notes.Add(new VbpImportNote(VbpImportNoteKind.ProjectType, type));
                    }
                    break;
                case "STARTUP":
                    if (Unquoted(value) is { Length: > 0 } startup && !string.Equals(startup, "(None)", StringComparison.OrdinalIgnoreCase))
                    {
                        notes.Add(new VbpImportNote(VbpImportNoteKind.Startup, startup));
                    }
                    break;
            }
        }

        var project = new RDCoreProject
        {
            Name = name,
            References = [.. references],
            Modules = [.. modules],
            OtherFiles = [.. others],
            Folders = [.. modules.Cast<RDCoreFile>().Concat(others)
                .Select(file => Path.GetDirectoryName(file.RelativeUri)?.Replace('\\', '/'))
                .OfType<string>().Where(folder => folder.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)],
            PrecompilerConstants = constants,
        };

        return new VbpImport(project, notes);

        // a file of the project, by the path the .vbp writes; none outside of the project's folder.
        string? AddFile(string path, bool asModule)
        {
            if (path.Length == 0)
            {
                return null;
            }

            if (Path.IsPathRooted(path) || path.Contains(':'))
            {
                notes.Add(new VbpImportNote(VbpImportNoteKind.AbsolutePath, path));
                return null;
            }

            if (InFolder(path, folder) is not { } relative)
            {
                notes.Add(new VbpImportNote(VbpImportNoteKind.OutsideProjectFolder, path));
                return null;
            }

            if (relative.Length == 0)
            {
                return null;
            }

            if (modules.Any(module => string.Equals(module.RelativeUri, relative, StringComparison.OrdinalIgnoreCase))
                || others.Any(other => string.Equals(other.RelativeUri, relative, StringComparison.OrdinalIgnoreCase)))
            {
                return relative;
            }

            if (asModule)
            {
                modules.Add(new RDCoreModule { RelativeUri = relative });
            }
            else
            {
                others.Add(new RDCoreFile { RelativeUri = relative });
            }

            return relative;
        }

        // Reference=*\G{GUID}#maj.min#lcid#path#description, or *\A<path> for another project.
        void AddReference(string value)
        {
            if (value.StartsWith(@"*\A", StringComparison.OrdinalIgnoreCase))
            {
                notes.Add(new VbpImportNote(VbpImportNoteKind.ProjectReference, value[3..].Trim()));
                return;
            }

            var parts = value.StartsWith(@"*\G", StringComparison.OrdinalIgnoreCase) ? value[3..].Split('#') : [];
            if (parts.Length == 0 || !Guid.TryParse(parts[0], out var guid))
            {
                notes.Add(new VbpImportNote(VbpImportNoteKind.UnresolvedReference, value));
                return;
            }

            var description = parts.Length > 4 ? parts[4].Trim() : value;
            if (guid == VBA)
            {
                return;
            }

            if (!libraries.TryGetByGuid(guid, out var library))
            {
                notes.Add(new VbpImportNote(VbpImportNoteKind.UnresolvedReference, description));
                return;
            }

            if (references.Any(reference => string.Equals(reference.Name, library.Name, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            var (major, minor) = parts.Length > 1 ? VersionOf(parts[1]) : (null, null);
            references.Add(new RDCoreReference
            {
                Name = library.Name,
                Guid = guid,
                Major = major,
                Minor = minor,
                AbsolutePath = parts.Length > 3 && parts[3].Trim().Length > 0 ? parts[3].Trim() : null,
            });
        }
    }

    // a path the .vbp writes, relative to its folder, with its . and .. resolved: the folder is climbed out of only to come back in when its own name is named
    // again (..\MyProj\Util.bas from MyProj), and a sub-folder's .. is the folder it is in (Sub\..\Util.bas). Null when it leaves the folder - or climbs out of
    // a folder that is not known. Names compare in any case, as VB6's file system does.
    private static string? InFolder(string path, string? folder)
    {
        var folderSegments = (folder ?? string.Empty).Split('/', '\\').Where(segment => segment.Length > 0).ToList();
        var resolved = new List<string>(folderSegments);
        foreach (var segment in path.Split('/', '\\'))
        {
            switch (segment)
            {
                case "" or ".":
                    continue;
                case "..":
                    if (resolved.Count == 0)
                    {
                        return null;
                    }

                    resolved.RemoveAt(resolved.Count - 1);
                    break;
                default:
                    resolved.Add(segment);
                    break;
            }
        }

        if (resolved.Count < folderSegments.Count
            || !resolved.Take(folderSegments.Count).SequenceEqual(folderSegments, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        return string.Join('/', resolved.Skip(folderSegments.Count));
    }

    // the Key=Value lines, up to the first [section]: what follows is the settings of add-ins, none of the project's.
    private static IEnumerable<(string Key, string Value)> Entries(string vbp)
    {
        foreach (var raw in vbp.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                yield break;
            }

            var equals = line.IndexOf('=');
            if (equals > 0)
            {
                yield return (line[..equals].Trim(), line[(equals + 1)..].Trim());
            }
        }
    }

    // CondComp="DEBUG = 1 : TRACE = -1": name = n, separated by colons, spaces optional, n a whole number -32768..32767 with an optional sign - as vb6.exe
    // reads it, measured with /make (HexIDE, 2026-10-27). Anything else - a string, &H10, True, 1.5, a name without a value, a colon at the end - and vb6.exe
    // refuses the whole line: null.
    private static List<(string Name, string Number)>? Constants(string arguments)
    {
        var constants = new List<(string Name, string Number)>();
        if (arguments.Trim().Length == 0)
        {
            return constants;
        }

        foreach (var argument in arguments.Split(':'))
        {
            var equals = argument.IndexOf('=');
            if (equals < 0)
            {
                return null;
            }

            var name = argument[..equals].Trim();
            var number = argument[(equals + 1)..].Trim();
            if (!IsIdentifier(name)
                || number.Length == 0
                || !number.TrimStart('+', '-').All(char.IsAsciiDigit)
                || number.Count(c => c is '+' or '-') > 1
                || !short.TryParse(number, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
            {
                return null;
            }

            constants.Add((name, value.ToString(CultureInfo.InvariantCulture)));
        }

        return constants;

        // a letter, then letters, digits and underscores (MS-VBAL §3.3.5).
        static bool IsIdentifier(string name)
            => name.Length is > 0 and <= 255 && char.IsLetter(name[0]) && name.All(c => char.IsLetterOrDigit(c) || c == '_');
    }

    // a type library's version, as the .vbp writes it: major.minor, in hexadecimal.
    private static (int? Major, int? Minor) VersionOf(string version)
    {
        var parts = version.Split('.');
        return (Hex(parts.ElementAtOrDefault(0)), Hex(parts.ElementAtOrDefault(1)));

        static int? Hex(string? digits)
            => int.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static string PathOf(string value) => Unquoted(value.Trim());

    private static string Unquoted(string value)
        => value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;
}
