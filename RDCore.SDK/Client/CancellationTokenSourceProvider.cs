namespace RDCore.SDK.Client;

/// <summary>
/// Creates, encapsulates, and provides <c>CancellationTokenSource</c> references for the duration of the host process lifetime.
/// </summary>
public interface ICancellationTokenSourceProvider : IDisposable
{
    /// <summary>
    /// Gets or creates a <c>CancellationTokenSource</c> that triggers to exit the <em>host (LSP client) application</em>.
    /// </summary>
    CancellationTokenSource GetOrCreateHostTokenSource();
    /// <summary>
    /// Gets or creates a <c>CancellationTokenSource</c> that triggers to exit the connected LSP process.
    /// </summary>
    /// <returns></returns>
    CancellationTokenSource GetOrCreateServerTokenSource();
}

/// <summary>
/// Creates, encapsulates, and provides <c>CancellationTokenSource</c> references for the duration of the host process lifetime.
/// </summary>
public sealed class CancellationTokenSourceProvider : ICancellationTokenSourceProvider
{
    private static readonly Lazy<CancellationTokenSource> _hostTokenSource = new(() => new());
    private static readonly Lazy<CancellationTokenSource> _serverTokenSource = new(() => new());

    public void Dispose()
    {
        _serverTokenSource.Value.Dispose();
        _hostTokenSource.Value.Dispose();
    }

    public CancellationTokenSource GetOrCreateHostTokenSource() => _hostTokenSource.Value;
    public CancellationTokenSource GetOrCreateServerTokenSource() => _serverTokenSource.Value;
}
