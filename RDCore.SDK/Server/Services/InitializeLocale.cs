namespace RDCore.SDK.Server.Services;

/// <summary>
/// The <c>locale</c> a client's <c>initialize</c> request says (<strong>LSP 3.17</strong> <c>InitializeParams.locale</c>), as the message has it.
/// </summary>
/// <remarks>
/// OmniSharp 0.19.9 reads an <c>initialize</c> request into a type of its own that has no <c>locale</c>, and the <c>InitializeParams</c> it gives its
/// handlers is made from that one: the locale a client of the protocol says is gone before any handler runs. <see cref="RDCoreLifecycleReceiver"/> keeps it
/// from the message itself, which it is the first to see.
/// </remarks>
internal sealed class InitializeLocale
{
    private string? _locale;

    /// <summary>The <c>locale</c> of the <c>initialize</c> request, or <see langword="null"/> when it said none or is yet to come.</summary>
    public string? Locale
    {
        get => Volatile.Read(ref _locale);
        set => Volatile.Write(ref _locale, value);
    }
}
