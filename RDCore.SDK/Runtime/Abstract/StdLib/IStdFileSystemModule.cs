using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.SDK.Runtime.Abstract.StdLib;

/// <summary>
/// <strong>MS-VBAL 6.1.2.5 FileSystem Module</strong>
/// </summary>
/// <remarks>
/// Formalizes the public interface of the standard library <c>VBA.FileSystem</c> module.<br/>
/// ℹ️ <strong>This interface is currently incomplete.</strong>
/// </remarks>
[StdLibModule]
public interface IStdFileSystemModule
{
    #region 6.1.2.5.1 StdFileSystem: Public Functions

    /****************************************************************************************************
     * 🎯 The target interface exposes all functions of this section, up to §6.1.2.5.1.10
     *     👉 THANK YOU for taking the time to write XML documentation for anything you add here.
    /****************************************************************************************************/

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

    #endregion

    #region 6.1.2.5.2 StdFileSystem: Public Subroutines

    /****************************************************************************************************
     * 🎯 The target interface exposes all functions of this section, up to §6.1.2.5.2.7
     *     👉 THANK YOU for taking the time to write XML documentation for anything you add here.
    /****************************************************************************************************/

    #endregion
}
