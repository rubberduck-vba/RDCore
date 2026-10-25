using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.SDK.Runtime.Abstract.StdLib;

/// <summary>
/// <strong>MS-VBAL 6.1.2.5 FileSystem Module</strong>
/// </summary>
/// <remarks>
/// Formalizes the public interface of the standard library <c>VBA.FileSystem</c> module.
/// <para>
/// What a member does to the machine - a directory listed, a file copied or deleted, the current directory changed - goes through the session's
/// <see cref="Execution.IFileChannels"/>, the one seam every file statement goes through too: what an administrator restricts, and a test fakes, there.
/// What the specification leaves to the implementation, and the errors it does not name, are <strong>MS-VBA</strong>'s, recorded in Excel.
/// </para>
/// </remarks>
[StdLibModule]
public interface IStdFileSystemModule
{
    #region 6.1.2.5.1 StdFileSystem: Public Functions

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.5.1.1 CurDir</strong> Gets the current directory of a drive.
    /// </summary>
    /// <remarks>
    /// The current drive's when <paramref name="drive"/> is omitted or a zero-length <c>String</c>; a drive is its first letter, so <c>"C"</c> and
    /// <c>"C:\Temp"</c> are one drive. As in <strong>MS-VBA</strong>: error <c>68</c> <em>Device unavailable</em> for a letter that is no drive, error <c>5</c>
    /// for a drive that is no letter.
    /// </remarks>
    /// <param name="drive">The drive.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> CurDir(VBVariantValue? drive = default);

    /// <inheritdoc cref="CurDir"/>
    [StdLibMember("CurDir$")]
    RuntimeSemanticsEvaluationResult<VBStringValue> CurDirString(VBVariantValue? drive = default);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.5.1.2 Dir</strong> Gets the name of a file or a directory that matches a pattern, or the next one that matches the pattern last
    /// given.
    /// </summary>
    /// <remarks>
    /// As in <strong>MS-VBA</strong> (recorded in Excel):
    /// <list type="bullet">
    /// <item>the name has no directory part, and is spelled as the file system spells it; names match a pattern whatever their case, <c>*</c> and <c>?</c>
    /// being wildcards, and come in the order of their upper-case spelling;</item>
    /// <item>a pattern that names a directory, ending in a separator, matches what is in it; a directory itself only matches with
    /// <see cref="VBFileAttribute.VBDirectory"/>, which matches <c>.</c> and <c>..</c> too, and files with it;</item>
    /// <item>a hidden or a system file only matches with <see cref="VBFileAttribute.VBHidden"/> or <see cref="VBFileAttribute.VBSystem"/>;</item>
    /// <item>what matches nothing - a directory or a drive that does not exist included - is a zero-length <c>String</c>;</item>
    /// <item>with no pattern, the next match of the last one, a zero-length <c>String</c> when there is none; error <c>5</c> when there is no pattern to
    /// go on with - none was given, or the last match was already given.</item>
    /// </list>
    /// </remarks>
    /// <param name="pathName">The pattern: a file name, with wildcards, that can include a directory and a drive.</param>
    /// <param name="attributes">The attributes of the files, besides the normal ones, that match.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBStringValue> Dir(VBVariantValue? pathName = default, VBFileAttribute attributes = VBFileAttribute.VBNormal);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.5.1.3 EOF</strong> Gets whether the file-pointer-position of a file is at its end (<strong>MS-VBAL §5.4.5</strong>).
    /// </summary>
    /// <remarks>
    /// <c>True</c> for a file opened for <c>Input</c> once nothing is left to read; always, for one opened for <c>Output</c> or <c>Append</c>; and for one
    /// opened <c>Random</c> or <c>Binary</c>, once the last <c>Get</c> could not read an entire record - reading the last byte is not enough (recorded in
    /// Excel). Error <c>52</c> for a file number that is not open.
    /// </remarks>
    /// <param name="fileNumber">The file number.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBBooleanValue> EOF(VBIntegerValue fileNumber);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.5.1.4 FileAttr</strong> Gets the mode a file was opened in: <c>1</c> <c>Input</c>, <c>2</c> <c>Output</c>, <c>4</c>
    /// <c>Random</c>, <c>8</c> <c>Append</c>, <c>32</c> <c>Binary</c>.
    /// </summary>
    /// <remarks>
    /// Only <paramref name="returnType"/> <c>1</c> means anything: <c>2</c>, the file's operating-system handle, is error <c>5</c> in 64-bit <strong>MS-VBA</strong>
    /// (recorded in Excel), and in RD-VBA, which has no handle to give; so is any other. Error <c>52</c> for a file number that is not open.
    /// </remarks>
    /// <param name="fileNumber">The file number.</param>
    /// <param name="returnType">What to get.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBLongValue> FileAttr(VBIntegerValue fileNumber, [StdLibDefault(1)] VBIntegerValue? returnType = default);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.5.1.5 FileDateTime</strong> Gets the date and time a file, or a directory, was last modified.
    /// </summary>
    /// <remarks>
    /// Error <c>53</c> <em>File not found</em> for what does not exist, error <c>52</c> for a pattern.
    /// </remarks>
    /// <param name="pathName">The file's path.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> FileDateTime(VBStringValue pathName);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.5.1.6 FileLen</strong> Gets the length of a file, in bytes.
    /// </summary>
    /// <remarks>
    /// As in <strong>MS-VBA</strong> (recorded in Excel): the length the file has on the file system, which is not what a channel that has it open has
    /// written to it yet (<c>LOF</c> is) - <c>0</c> for one opened for <c>Output</c>, the length it had for one opened for <c>Append</c>; a directory's is
    /// <c>0</c>. Error <c>53</c> <em>File not found</em> for what does not exist, error <c>52</c> for a pattern.
    /// </remarks>
    /// <param name="pathName">The file's path.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBLongValue> FileLen(VBStringValue pathName);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.5.1.7 FreeFile</strong> Gets the next file number available for an <c>Open</c>
    /// statement to use (<strong>§5.4.5</strong>).
    /// </summary>
    /// <remarks>
    /// 👉 The reason a program should not simply write <c>#1</c>: a file number is only free if nothing else
    /// has it open, and nothing about the language stops two parts of one program each opening files. Asking
    /// for a free one is how they avoid colliding.
    /// </remarks>
    /// <param name="rangeNumber">
    /// Which range to take the number from: <c>0</c> — the default — for <c>1</c>-<c>255</c>, and <c>1</c> for
    /// <c>256</c>-<c>511</c>. The two halves exist so that a caller can keep its own channels away from
    /// another's.
    /// </param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBIntegerValue> FreeFile(VBVariantValue? rangeNumber = default);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.5.1.8 Loc</strong> Gets the current read/write position of a file.
    /// </summary>
    /// <remarks>
    /// As in <strong>MS-VBA</strong> (recorded in Excel): for a file opened <c>Random</c>, the number of the last record read or written; <c>Binary</c>, the
    /// position of the last byte read or written - both <c>0</c> before either; for one opened for <c>Input</c>, the bytes read divided by 128, plus 1; for
    /// <c>Output</c> or <c>Append</c>, the bytes written divided by 128. Error <c>52</c> for a file number that is not open.
    /// </remarks>
    /// <param name="fileNumber">The file number.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBLongValue> Loc(VBIntegerValue fileNumber);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.5.1.9 LOF</strong> Gets the length of an open file, in bytes, what has been written to it included.
    /// </summary>
    /// <remarks>Error <c>52</c> for a file number that is not open.</remarks>
    /// <param name="fileNumber">The file number.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBLongValue> LOF(VBIntegerValue fileNumber);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.5.1.10 Seek</strong> Gets the position of the next read or write of a file: the next record of one opened <c>Random</c>, the next
    /// byte of any other, the first being <c>1</c>.
    /// </summary>
    /// <remarks>
    /// For a file opened for <c>Append</c>, the bytes are counted from where the appending began, as <strong>MS-VBA</strong> counts them (recorded in Excel).
    /// Error <c>52</c> for a file number that is not open.
    /// </remarks>
    /// <param name="fileNumber">The file number.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBLongValue> Seek(VBIntegerValue fileNumber);

    #endregion

    #region 6.1.2.5.2 StdFileSystem: Public Subroutines

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.5.2.1 ChDir</strong> Changes the current directory, but not the current drive.
    /// </summary>
    /// <remarks>Error <c>76</c> <em>Path not found</em> for a directory that does not exist, or a zero-length <c>String</c>.</remarks>
    /// <param name="path">The directory.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult ChDir(VBStringValue path);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.5.2.2 ChDrive</strong> Changes the current drive.
    /// </summary>
    /// <remarks>
    /// A zero-length <c>String</c> changes nothing, and a drive is its first letter. Error <c>68</c> <em>Device unavailable</em> for a letter that is no drive.
    /// </remarks>
    /// <param name="drive">The drive.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult ChDrive(VBStringValue drive);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.5.2.3 FileCopy</strong> Copies a file, replacing the destination if it exists.
    /// </summary>
    /// <remarks>
    /// As in <strong>MS-VBA</strong> (recorded in Excel): error <c>55</c> <em>File already open</em> when the destination is open, or the source is open in
    /// any mode but <c>Input</c>; <c>53</c> for a source that does not exist, <c>52</c> for a pattern, <c>76</c> for a destination whose directory does not
    /// exist, <c>75</c> for a destination that is a directory.
    /// </remarks>
    /// <param name="source">The path of the file to copy.</param>
    /// <param name="destination">The path of the copy.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult FileCopy(VBStringValue source, VBStringValue destination);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.5.2.4 Kill</strong> Deletes the files that match a pattern.
    /// </summary>
    /// <remarks>
    /// As in <strong>MS-VBA</strong> (recorded in Excel): directories, and hidden or system files, are not deleted - <c>Kill "*"</c> leaves them; error
    /// <c>53</c> <em>File not found</em> when nothing matches, <c>55</c> when a match is open, <c>75</c> when one is read-only.
    /// </remarks>
    /// <param name="pathName">The pattern: a file name, with wildcards, that can include a directory and a drive.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult Kill(VBVariantValue pathName);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.5.2.5 MkDir</strong> Creates a directory.
    /// </summary>
    /// <remarks>
    /// Error <c>75</c> <em>Path/File access error</em> when a directory or a file of the name exists, <c>76</c> <em>Path not found</em> when the directory it
    /// would be in does not (recorded in Excel).
    /// </remarks>
    /// <param name="path">The directory.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult MkDir(VBStringValue path);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.5.2.6 RmDir</strong> Removes an empty directory.
    /// </summary>
    /// <remarks>
    /// Error <c>75</c> <em>Path/File access error</em> for one that holds a file or a directory, <c>76</c> <em>Path not found</em> for one that does not
    /// exist - a file, or a pattern (recorded in Excel).
    /// </remarks>
    /// <param name="path">The directory.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult RmDir(VBStringValue path);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.5.2.7 SetAttr</strong> Sets the attributes of a file: exactly the ones given, so <see cref="VBFileAttribute.VBNormal"/> clears them.
    /// </summary>
    /// <remarks>
    /// As in <strong>MS-VBA</strong> (recorded in Excel): <see cref="VBFileAttribute.VBReadOnly"/>, <see cref="VBFileAttribute.VBHidden"/>,
    /// <see cref="VBFileAttribute.VBSystem"/> and <see cref="VBFileAttribute.VBArchive"/> only - any other is error <c>5</c>; error <c>55</c> for a file open in
    /// any mode but <c>Input</c>, <c>53</c> for one that does not exist, <c>52</c> for a pattern.
    /// </remarks>
    /// <param name="pathName">The file's path.</param>
    /// <param name="attributes">The attributes.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult SetAttr(VBStringValue pathName, VBFileAttribute attributes);

    #endregion
}
