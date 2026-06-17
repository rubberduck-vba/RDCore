namespace RDCore.SDK.Runtime.Abstract.StdLib;

/// <summary>
/// <strong>MS-VBAL 6.1.1.12 VbMsgBoxStyle</strong>
/// </summary>
/// <remarks>
/// These values encode the rendered appearance and possible return values of the <see cref="IStdInteractionModule.StdInteraction__MsgBox"/> function.
/// </remarks>
public enum VBMsgBoxStyle
{
    VBDefaultButton1 = 0,
    VBApplicationModal = VBDefaultButton1,
    VBOkOnly = VBDefaultButton1,
    VBOkCancel = 1,
    VBAbortRetryIgnore = 2,
    VBYesNoCancel = 3,
    VBYesNo = 4,
    VBRetryCancel = 5,
    VBCritical = 16,
    VBExclamation = 48,
    VBInformation = 64,
    VBDefaultButton2 = 256,
    VBDefaultButton3 = 512,
    VBDefaultButton4 = 768,
    VBSystemModal = 4096,
    VBMsgBoxHelpButton = 16384,
    VBMsgBoxSetForeground = 65536,
    VBMsgBoxRight = 524288,
    VBMsgBoxRtlReading = 1048576,
}

