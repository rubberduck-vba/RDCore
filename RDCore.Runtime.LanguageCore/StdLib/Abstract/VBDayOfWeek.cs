using RDCore.SDK.Runtime.Abstract.StdLib;

namespace RDCore.Runtime.LanguageCore.StdLib.Abstract;

/// <summary>
/// <strong>MS-VBAL 6.1.1.7 VbDayOfWeek</strong>
/// </summary>
[StdLibEnum]
public enum VBDayOfWeek
{
    VBUseSystemDayOfWeek = 0,
    VBSunday = 1,
    VBMonday = 2,
    VBTuesday = 3,
    VBWednesday = 4,
    VBThursday = 5,
    VBFriday = 6,
    VBSaturday = 7,

}

