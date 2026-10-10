namespace RDCore.External.Native;

/// <summary>
/// What a native call needs of the platform it is made on, beyond loading a library and calling a function by its address - which every platform does.
/// </summary>
/// <remarks>
/// A <c>Declare</c>'s <c>ByRef String</c> is a <c>BSTR</c>, a <c>Variant</c> is a <c>VARIANT</c>, and an <c>Alias "#n"</c> is an ordinal: all of them are Windows'
/// own, and a platform that has none of them says so (<see cref="UnsupportedNativePlatform"/>). Everything else about a call is the same everywhere.
/// </remarks>
public interface INativePlatform
{
    /// <summary>
    /// Whether this platform has <c>BSTR</c>s and <c>VARIANT</c>s at all.
    /// </summary>
    bool HasAutomationTypes { get; }

    /// <summary>
    /// The size of a <c>VARIANT</c>, in bytes.
    /// </summary>
    int VariantSize { get; }

    /// <summary>
    /// Finds a function of a loaded library by its ordinal.
    /// </summary>
    /// <param name="library">The handle of the library.</param>
    /// <param name="ordinal">The ordinal.</param>
    /// <param name="function">The address of the function.</param>
    /// <returns><see langword="false"/> when the library exports nothing by that ordinal, or the platform has no ordinals.</returns>
    bool TryGetOrdinal(IntPtr library, int ordinal, out IntPtr function);

    /// <summary>
    /// Allocates a <c>BSTR</c> of bytes - ANSI characters, which the length of a <c>BSTR</c> counts in bytes.
    /// </summary>
    IntPtr AllocateByteString(byte[] bytes);

    /// <summary>
    /// The bytes of a <c>BSTR</c> of ANSI characters; none for a null one.
    /// </summary>
    byte[] ReadByteString(IntPtr bstr);

    /// <summary>
    /// Frees a <c>BSTR</c>; a null one is nothing to free.
    /// </summary>
    void FreeString(IntPtr bstr);

    /// <summary>
    /// Writes a neutral value (<see cref="Automation.IAutomationServer"/>'s) as a <c>VARIANT</c> into memory of <see cref="VariantSize"/> bytes.
    /// </summary>
    void WriteVariant(object? value, IntPtr variant);

    /// <summary>
    /// Reads the neutral value a <c>VARIANT</c> holds.
    /// </summary>
    object? ReadVariant(IntPtr variant);

    /// <summary>
    /// Clears a <c>VARIANT</c>, freeing what it holds.
    /// </summary>
    void ClearVariant(IntPtr variant);
}

/// <summary>
/// The native calls of a platform that has no <c>BSTR</c>s, no <c>VARIANT</c>s and no ordinals: a function that takes a number or an ANSI string by value is
/// called as on any other, and one that takes something else cannot be called.
/// </summary>
public sealed class UnsupportedNativePlatform : INativePlatform
{
    /// <summary>
    /// The platform that has none of them; there is nothing about it to hold, and one is as good as another.
    /// </summary>
    public static UnsupportedNativePlatform Instance { get; } = new();

    /// <inheritdoc/>
    public bool HasAutomationTypes => false;

    /// <inheritdoc/>
    public int VariantSize => 0;

    /// <inheritdoc/>
    public bool TryGetOrdinal(IntPtr library, int ordinal, out IntPtr function)
    {
        function = IntPtr.Zero;
        return false;
    }

    /// <inheritdoc/>
    public IntPtr AllocateByteString(byte[] bytes) => throw new PlatformNotSupportedException();

    /// <inheritdoc/>
    public byte[] ReadByteString(IntPtr bstr) => throw new PlatformNotSupportedException();

    /// <inheritdoc/>
    public void FreeString(IntPtr bstr) => throw new PlatformNotSupportedException();

    /// <inheritdoc/>
    public void WriteVariant(object? value, IntPtr variant) => throw new PlatformNotSupportedException();

    /// <inheritdoc/>
    public object? ReadVariant(IntPtr variant) => throw new PlatformNotSupportedException();

    /// <inheritdoc/>
    public void ClearVariant(IntPtr variant) => throw new PlatformNotSupportedException();
}
