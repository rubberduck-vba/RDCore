namespace RDCore.SDK.Runtime.Abstract.StdLib;

/// <summary>
/// <strong>MS-VBAL 6.1.1.13 VbQueryClose</strong>
/// </summary>
/// <remarks>
/// These values encode the possible values of the <c>CloseMode</c> parameter of the <c>UserForm.QueryClose</c> event.
/// </remarks>
public enum VBQueryClose
{
    VBFormControlMenu = 0,
    VBFormCode = 1,
    VBAppWindows = 2,
    VBAppTaskManager = 3,
    VBFormMDIForm = 4,
}
