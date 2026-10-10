namespace RDCore.External.Protocol;

/// <summary>
/// How a value is passed to a native function, or returned by one: the native type it is, which is what the function is called with.
/// </summary>
public enum NativeSlot
{
    /// <summary>Nothing: what a <c>Sub</c> returns.</summary>
    Void,

    /// <summary>8 bits: a <c>Byte</c>.</summary>
    Byte,

    /// <summary>16 bits: an <c>Integer</c>, or a <c>Boolean</c> as a <c>VARIANT_BOOL</c>.</summary>
    Int16,

    /// <summary>32 bits: a <c>Long</c>.</summary>
    Int32,

    /// <summary>64 bits: a <c>LongLong</c>, or a <c>Currency</c> as its scaled integer.</summary>
    Int64,

    /// <summary>A pointer: a <c>LongPtr</c>, or <c>vbNullString</c> and <c>Nothing</c> as a null one.</summary>
    Pointer,

    /// <summary>A 32-bit float: a <c>Single</c>.</summary>
    Single,

    /// <summary>A 64-bit float: a <c>Double</c>, or a <c>Date</c> as its serial number.</summary>
    Double,

    /// <summary>A pointer to a null-terminated copy of a string in the ANSI code page: a <c>ByVal String</c>, which the function may write into.</summary>
    AnsiBuffer,

    /// <summary>A <c>BSTR</c> of ANSI characters: what a <c>ByRef String</c> points to, and what a function declared to return a <c>String</c> returns.</summary>
    AnsiBstr,

    /// <summary>A <c>VARIANT</c>.</summary>
    Variant,
}

/// <summary>
/// Whether the function a <c>Declare</c> names was found.
/// </summary>
public enum NativeLookup
{
    /// <summary>It was found, and called.</summary>
    Found,

    /// <summary>The library is not there: error 53, "File not found".</summary>
    NoLibrary,

    /// <summary>The library has no such function: error 453, "Can't find DLL entry point".</summary>
    NoEntryPoint,

    /// <summary>The function takes, or returns, a type that the platform the external host runs on has no native form of.</summary>
    Unsupported,
}

/// <summary>
/// An argument of a native call.
/// </summary>
public sealed record class NativeArgument
{
    /// <summary>
    /// What it is passed as.
    /// </summary>
    public NativeSlot Slot { get; init; }

    /// <summary>
    /// Whether the function is given the address of the value rather than the value. An <see cref="NativeSlot.AnsiBuffer"/> is an address already, and an
    /// <see cref="NativeSlot.AnsiBstr"/> is always passed by its address.
    /// </summary>
    public bool ByReference { get; init; }

    /// <summary>
    /// The value: a number of the slot's type, a string, or the neutral value of a <c>VARIANT</c>.
    /// </summary>
    public ExternalValue Value { get; init; } = new();

    /// <summary>
    /// Whether what the function leaves in the argument is to be told: the argument is a variable, which takes it.
    /// </summary>
    public bool WritesBack { get; init; }
}

/// <summary>
/// Request for <see cref="ExternalProtocol.NativeCall"/>: calls a function of a native library.
/// </summary>
public sealed record class NativeCallParams
{
    /// <summary>The <c>Lib</c> string.</summary>
    public string Library { get; init; } = string.Empty;

    /// <summary>The <c>Alias</c> string, or the name of the procedure when there is none.</summary>
    public string EntryPoint { get; init; } = string.Empty;

    /// <summary>What the function returns.</summary>
    public NativeSlot Returns { get; init; }

    /// <summary>The arguments, in the order of the function's parameters.</summary>
    public NativeArgument[] Arguments { get; init; } = [];

    /// <summary>The ANSI code page a string is passed in.</summary>
    public int AnsiCodePage { get; init; }
}

/// <summary>
/// Response for <see cref="ExternalProtocol.NativeCall"/>.
/// </summary>
public sealed record class NativeCallResult : ExternalResult
{
    /// <summary>Whether the function was found, and called.</summary>
    public NativeLookup Lookup { get; init; }

    /// <summary>What it returned.</summary>
    public ExternalValue Returned { get; init; } = new();

    /// <summary>What it left in each argument that writes back; an <c>Empty</c> for any other.</summary>
    public ExternalValue[] Written { get; init; } = [];

    /// <summary>The system error code it left: <c>Err.LastDllError</c>.</summary>
    public int LastDllError { get; init; }
}
