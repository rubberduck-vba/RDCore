namespace RDCore.SDK.Workspace.Forms;

/// <summary>
/// The reasons a form's designer data cannot be read.
/// </summary>
public enum FormDesignerFormatProblem
{
    /// <summary>
    /// The designer block of the module's text (<c>Begin</c> … <c>End</c>) has no matching <c>End</c>.
    /// </summary>
    UnterminatedBlock,

    /// <summary>
    /// The module refers to designer data in a companion <c>.frx</c> file, and no such data was supplied.
    /// </summary>
    MissingCompanion,

    /// <summary>
    /// The offset the module gives into its companion <c>.frx</c> file is outside of that file, or the length found there
    /// runs past its end.
    /// </summary>
    BlobOutOfRange,

    /// <summary>
    /// The designer data is not a compound file ([MS-CFB]), the OLE storage Microsoft Forms persists a form to.
    /// </summary>
    NotACompoundFile,

    /// <summary>
    /// The designer data is a compound file whose sector chains or directory are damaged.
    /// </summary>
    CorruptCompoundFile,

    /// <summary>
    /// The form's storage has no form stream (<c>f</c>, [MS-OFORMS] 2.1.2.1.1).
    /// </summary>
    MissingFormStream,

    /// <summary>
    /// The form stream was written by a version of Microsoft Forms this reader does not know ([MS-OFORMS] 2.2.10.1).
    /// </summary>
    UnsupportedVersion,

    /// <summary>
    /// The form stream ends before the description of its controls does.
    /// </summary>
    Truncated,

    /// <summary>
    /// A control's site names no class the form describes, or is nested deeper than the controls before it allow
    /// ([MS-OFORMS] 2.2.10.7, 2.5.13).
    /// </summary>
    InvalidControl,
}

/// <summary>
/// The exception that is thrown when a form's designer data, in its module's text or in its companion <c>.frx</c> file,
/// cannot be read.
/// </summary>
/// <param name="problem">What is wrong with the designer data.</param>
public sealed class FormDesignerFormatException(FormDesignerFormatProblem problem)
    : SdkException(MessageOf(problem))
{
    /// <summary>
    /// Gets what is wrong with the designer data.
    /// </summary>
    public FormDesignerFormatProblem Problem { get; } = problem;

    private static string MessageOf(FormDesignerFormatProblem problem) => problem switch
    {
        FormDesignerFormatProblem.UnterminatedBlock => Exceptions.FormDesigner_UnterminatedBlock,
        FormDesignerFormatProblem.MissingCompanion => Exceptions.FormDesigner_MissingCompanion,
        FormDesignerFormatProblem.BlobOutOfRange => Exceptions.FormDesigner_BlobOutOfRange,
        FormDesignerFormatProblem.NotACompoundFile => Exceptions.FormDesigner_NotACompoundFile,
        FormDesignerFormatProblem.CorruptCompoundFile => Exceptions.FormDesigner_CorruptCompoundFile,
        FormDesignerFormatProblem.MissingFormStream => Exceptions.FormDesigner_MissingFormStream,
        FormDesignerFormatProblem.UnsupportedVersion => Exceptions.FormDesigner_UnsupportedVersion,
        FormDesignerFormatProblem.InvalidControl => Exceptions.FormDesigner_InvalidControl,
        _ => Exceptions.FormDesigner_Truncated,
    };
}
