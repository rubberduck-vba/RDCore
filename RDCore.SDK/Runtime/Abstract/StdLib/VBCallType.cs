namespace RDCore.SDK.Runtime.Abstract.StdLib;

/// <summary>
/// <strong>MS-VBAL 6.1.1.4 VbCallType</strong>
/// </summary>
/// <remarks>
/// 👉 The values of this enum are powers of 2, suggesting they are intended to be combined and used with bitwise logic.
/// </remarks>
[Flags]
public enum VBCallType
{
    VBMethod = 1,
    VBGet = 2,
    VBLet = 4,
    VBSet = 8,
}

