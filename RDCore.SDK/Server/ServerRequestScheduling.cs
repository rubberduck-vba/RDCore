using Microsoft.Extensions.DependencyInjection;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Server;
using RDCore.SDK.Server.Services;

namespace RDCore.SDK.Server;

/// <summary>
/// How an RDCore server schedules the requests and notifications it receives.
/// </summary>
public static class ServerRequestScheduling
{
    /// <summary>
    /// Has the server answer every request it receives, whatever notifications arrive while the request runs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// OmniSharp runs a request such as <c>textDocument/diagnostic</c> in parallel, and a notification such as <c>textDocument/didOpen</c> in order. By
    /// default, a notification that arrives while requests run abandons them, and answers them <c>ContentModified</c> (-32801). In OmniSharp 0.19.9 a
    /// request whose handling starts just as that happens is answered nothing at all, and its client waits forever, with the server idle: HexIDE's client
    /// saw it on a quick run of <c>didOpen</c>s, each followed by its diagnostics pull (findings desk #31).
    /// </para>
    /// <para>
    /// With this, a request runs to its end and is answered. What it answers for a document that has moved on in the meantime is its handler's to decide:
    /// the diagnostics pull drops a report of a version that is no longer the document's.
    /// </para>
    /// <para>
    /// The notification waits for the requests that run. A request that would make it wait long gives way to a change to its document instead, answered
    /// <c>ContentModified</c> as the change arrives (<see cref="IncomingDocumentChanges.WhileCurrentAsync{T}"/>).
    /// </para>
    /// </remarks>
    /// <param name="options">The server's options.</param>
    /// <returns>The same options.</returns>
    public static LanguageServerOptions WithEveryRequestAnswered(this LanguageServerOptions options)
    {
        options.WithContentModifiedSupport(false);
        return options;
    }

    /// <summary>
    /// Has the server read what it receives as an RDCore server reads it: an <c>exit</c> notification is let through before the server is initialized,
    /// and once it is, a request marked <see cref="HandledOnArrivalAttribute"/> is handled as it arrives.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every server built on <see cref="RDCoreServerApp"/> is configured with this. A server that builds its OmniSharp language server by
    /// itself, and serves requests marked <see cref="HandledOnArrivalAttribute"/>, calls it so that those requests are handled while a request that runs a
    /// program runs: without it they are scheduled after that request, and a program that never ends can be neither paused nor ended.
    /// </para>
    /// <para>
    /// Before the <c>initialize</c> request completes, a request other than <c>initialize</c> is answered <c>ServerNotInitialized</c> (-32002) and a
    /// notification other than <c>exit</c> is dropped (<strong>LSP 3.17</strong>, Server lifecycle, Initialize Request). OmniSharp's own reader of
    /// incoming messages drops an early <c>exit</c> as well, and a client that ends the server before it initializes it waits for a process that does not
    /// end.
    /// </para>
    /// <para>
    /// The <c>locale</c> of the <c>initialize</c> request (<strong>LSP 3.17</strong> <c>InitializeParams.locale</c>) is kept as it arrives: OmniSharp 0.19.9
    /// reads the request without it.
    /// </para>
    /// <para>
    /// OmniSharp keeps the first reader of incoming messages it is given: call this before anything else registers one.
    /// </para>
    /// </remarks>
    /// <param name="options">The server's options.</param>
    /// <returns>The same options.</returns>
    public static LanguageServerOptions WithLifecycleReceiver(this LanguageServerOptions options)
        => options.WithServices(services => services
            .AddSingleton<ArrivalRequests>()
            .AddSingleton<InitializeLocale>()
            .AddSingleton<IReceiver, RDCoreLifecycleReceiver>());
}
