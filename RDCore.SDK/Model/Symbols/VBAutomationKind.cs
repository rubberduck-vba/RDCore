namespace RDCore.SDK.Model.Symbols;

/// <summary>
/// Whether a class module's own live object reference is COM Automation-capable
/// (<c>IDispatch</c>, MS-VBAL 6.1.1.16's own <c>vbObject</c>
/// <see cref="RDCore.SDK.Model.Values.Runtime.VBVarType"/> tag) or <c>IUnknown</c>-only
/// (<c>vbDataObject</c>) — a static fact about the class itself, not
/// about any one instance of it.
/// </summary>
public enum VBAutomationKind
{
    /// <summary>
    /// The class supports late-bound Automation (<c>IDispatch</c>) — true of every RD-VBA class
    /// module today.
    /// </summary>
    Dispatch,

    /// <summary>
    /// The class is <c>IUnknown</c>-only, not Automation-capable. Groundwork for a future external/COM
    /// reference kind that isn't modeled yet — nothing constructs a class module with this value today.
    /// </summary>
    Unknown,
}
