using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Symbols;

namespace RDCore.SDK.Workspace;

/// <summary>
/// What kind of module a module of a project is: the one decision every side of the platform makes of it the same way - the environment host composing its
/// session from the project file, and the language server composing the workspace it analyzes.
/// </summary>
/// <remarks>
/// A document module is the code of a host's document (<strong>MS-VBAL §4.2.1</strong>): Excel's worksheets and workbook, Word's documents, Access's forms and
/// reports. Its export (a <c>.doccls</c>, or a <c>.cls</c> from the VBE) says nothing of what it is, so the project says it: a module whose entry has a
/// <see cref="RDCoreModule.Super"/> is one, whatever its text and its file name, and nothing else is - no name and no event handler makes a module one. Any other
/// module is a class module when its text has a <c>VERSION</c> header (<see cref="ModuleHeader.IsClassModule"/>), and a standard module otherwise.
/// <para>
/// RD-VBA hosts no document: a document module is analyzed - its code binds, the members of its host's class among its names - and never run. Code that is to
/// handle a document's events holds the document in a <c>WithEvents</c> variable of the host's class, from an application the program automates.
/// </para>
/// </remarks>
public static class ModuleKinds
{
    /// <summary>
    /// The class of a library whose object a document of <paramref name="super"/> is, whose members the code of its module names
    /// (<c>Me.Range</c> in a worksheet's); <see langword="null"/> for a document of a host RD-VBA does not know (<see cref="DocClassType.Unknown"/>).
    /// </summary>
    /// <param name="super">What the project says the module's document is.</param>
    public static DocumentHostClass? HostClassOf(DocClassType super) => super switch
    {
        DocClassType.ExcelWorkbook => new("Excel", "Workbook"),
        DocClassType.ExcelWorksheet => new("Excel", "Worksheet"),
        DocClassType.WordDocument => new("Word", "Document"),
        DocClassType.AccessForm => new("Access", "Form"),
        DocClassType.AccessReport => new("Access", "Report"),
        _ => null,
    };

    /// <summary>
    /// The kind of a module of a project.
    /// </summary>
    /// <param name="module">The module's entry in the project, or <see langword="null"/> when the project does not list it (a shell's program).</param>
    /// <param name="source">The module's text, or <see langword="null"/> when it could not be read.</param>
    public static ModuleType Of(RDCoreModule? module, string? source) => Of(module?.Super, source);

    /// <summary>
    /// The kind of a module that the project says is the document of <paramref name="super"/>, or of no host when it is <see langword="null"/>.
    /// </summary>
    /// <param name="super">What the project says the module's document is (<see cref="RDCoreModule.Super"/>).</param>
    /// <param name="source">The module's text, or <see langword="null"/> when it could not be read.</param>
    public static ModuleType Of(DocClassType? super, string? source)
        => super is not null ? ModuleType.DocumentModule
            : ModuleHeader.IsClassModule(source) == true ? ModuleType.ClassModule
            : ModuleType.StdModule;

    /// <summary>
    /// Whether a module of this kind is a class module: its members are of each object of it, and <c>Me</c> is one of them. A document module is.
    /// </summary>
    public static bool IsClass(this ModuleType type) => type is ModuleType.ClassModule or ModuleType.DocumentModule;

    /// <summary>
    /// The directives a module of the specified kind has, from those its text declares: a document module is not creatable and is extensible, whatever its
    /// text says - its export carries none of the attributes that would say so.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><c>VB_Creatable = False</c>: only the host makes its documents, and <c>New Sheet1</c> is a compile error.</item>
    /// <item><c>VB_Extensible = True</c>: the module extends the host's class (<strong>MS-VBAL §4.2.1</strong>), whose members its code names.</item>
    /// </list>
    /// RD-VBA hosts no document, so nothing makes the document an instance of the module: it has no default instance, whatever its text says
    /// (<c>VB_PredeclaredId</c> is <see langword="false"/>), and none of its procedures can run.
    /// </remarks>
    /// <param name="type">The module's kind (<see cref="Of(RDCoreModule?, string?)"/>).</param>
    /// <param name="declared">The directives the module's text declares.</param>
    public static ModuleDirectives DirectivesOf(ModuleType type, ModuleDirectives declared)
        => type == ModuleType.DocumentModule ? declared with { PredeclaredId = false, Creatable = false, Extensible = true } : declared;
}

/// <summary>
/// The class of a referenced library that a host's document is an object of (<see cref="ModuleKinds.HostClassOf"/>).
/// </summary>
/// <param name="Library">The name of the library, as a project references it: <c>Excel</c>.</param>
/// <param name="ClassName">The name of the class in it: <c>Worksheet</c>.</param>
public sealed record class DocumentHostClass(string Library, string ClassName);
