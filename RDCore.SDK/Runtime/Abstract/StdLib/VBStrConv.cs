namespace RDCore.SDK.Runtime.Abstract.StdLib;

/// <summary>
/// <strong>MS-VBAL 6.1.1.14 VbStrConv</strong>
/// </summary>
/// <remarks>
/// These values encode the possible values for <c>Conversion</c> parameter of the <c>IStdStringsModule.StdStrings__StrConv</c> function.
/// </remarks>
public enum VBStrConv
{
    VBUpperCase = 1,
    VBLowerCase = 2,
    VBProperCase = 3,
    VBWide = 4,
    VBKatakana = 16,
    VBHiragana = 32,
    VBUnicode = 64,
    VBFromUnicode = 128,
}

