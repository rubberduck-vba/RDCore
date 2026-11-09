using RDCore.SDK.Runtime.Abstract.StdLib;

namespace RDCore.Runtime.LanguageCore.StdLib.Abstract;

/// <summary>
/// <strong>MS-VBAL 6.1.1.9 VbFirstWeekOfYear</strong>
/// </summary>
[StdLibEnum]
public enum VBFirstWeekOfYear
{
    VBUseSystem = 0,
    VBFirstJan1 = 1,
    VBFirstFourDays = 2,
    VBFirstFullWeek = 3,
}
