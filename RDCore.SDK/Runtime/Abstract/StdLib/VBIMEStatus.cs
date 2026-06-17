namespace RDCore.SDK.Runtime.Abstract.StdLib;

/// <summary>
/// <strong>MS-VBAL 6.1.1.10 VbIMEStatus</strong>
/// </summary>
public enum VBIMEStatus
{
    VBIMENoOp = 0,
    VBIMEModeNoControl = VBIMENoOp,
    VBIMEOn = 1,
    VBIMEModeOn = VBIMEOn,
    VBIMEOff = 2,
    VBIMEModeOff = VBIMEOff,
    VBIMEDisable = 3,
    VBIMEIragana = 4,
    VBIMEModeIragana = VBIMEIragana,
    VBIMEKatakanaDbl = 5,
    VBIMEModeKatakana = VBIMEKatakanaDbl,
    VBIMEKatakanaSng = 6,
    VBIMEModeKatakanaHalf = VBIMEKatakanaSng,
    VBIMEAlphaDbl = 7,
    VBIMEModeAlphaFull = VBIMEAlphaDbl,
    VBIMEAlphaSng = 8,
    VBIMEModeAlpha = VBIMEAlphaSng,
    VBIMEModeHangulFull = 9,
    VBIMEModeHangul = 10,
}

