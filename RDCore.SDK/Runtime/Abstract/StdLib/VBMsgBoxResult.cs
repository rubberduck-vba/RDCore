namespace RDCore.SDK.Runtime.Abstract.StdLib;

/// <summary>
/// <strong>MS-VBAL 6.1.1.11 VbMsgBoxResult</strong>
/// </summary>
/// <remarks>
/// These values encode the return value of the <see cref="IStdInteractionModule.StdInteraction__MsgBox"/> function.
/// </remarks>
public enum VBMsgBoxResult
{
    VBOk = 1,
    VBCancel = 2,
    VBAbort = 3,
    VBRetry = 4,
    VBIgnore = 5,
    VBYes = 6,
    VBNo = 7,
}

