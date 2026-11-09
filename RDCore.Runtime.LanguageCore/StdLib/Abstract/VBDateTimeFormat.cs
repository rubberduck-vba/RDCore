using RDCore.SDK.Runtime.Abstract.StdLib;

namespace RDCore.Runtime.LanguageCore.StdLib.Abstract;

/// <summary>
/// <strong>MS-VBAL 6.1.1.6 VbDateTimeFormat</strong>
/// </summary>
[StdLibEnum]
public enum VBDateTimeFormat
{
    VBGeneralDate = 0,
    VBLongDate = 1,
    VBShortDate = 2,
    VBLongTime = 3,
    VBShortTime = 4,
}

