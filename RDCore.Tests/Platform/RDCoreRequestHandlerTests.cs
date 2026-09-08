using MediatR;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.JsonRpc.Server;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.Tests.Platform;

/// <summary>
/// <see cref="RDCoreRequestHandler{TRequest,TResponse}"/> guarantees that whatever a handler throws
/// reaches the client as a JSON-RPC error whose message carries no build-machine source path —
/// OmniSharp's request invoker would otherwise drop the raw <c>exception.ToString()</c> into the
/// <c>-32603</c> response body.
/// </summary>
[TestClass]
public sealed class RDCoreRequestHandlerTests
{
    private sealed record ProbeRequest(string Value) : IRequest, IRequest<string>;

    private sealed class StubHandler(Func<Task<string>> body) : RDCoreRequestHandler<ProbeRequest, string>
    {
        protected override Task<string> HandleAsync(ProbeRequest request, CancellationToken token) => body();
    }

    private static Task<string> InvokeAsync(Func<Task<string>> body)
        => new StubHandler(body).Handle(new ProbeRequest("x"), CancellationToken.None);

    [TestMethod]
    public async Task Handle_ReturnsTheResult_WhenHandleAsyncSucceeds()
        => Assert.AreEqual("ok", await InvokeAsync(() => Task.FromResult("ok")));

    [TestMethod]
    public async Task Handle_WrapsAnUnexpectedException_AsAScrubbedRpcError()
    {
        const string trace =
            "System.InvalidOperationException: boom\r\n" +
            "   at RDCore.Parsing.ModuleParser.Parse() in C:\\Users\\somebody\\src\\RDCore\\RDCore.Parsing\\ModuleParser.cs:line 51";

        var error = await Assert.ThrowsExactlyAsync<RpcErrorException>(
            () => InvokeAsync(() => throw new InvalidOperationException(trace)));

        Assert.AreEqual(-32603, error.Code);
        StringAssert.Contains(error.Message, "RDCore.Parsing/ModuleParser.cs:line 51");
        Assert.IsFalse(error.Message.Contains("C:\\"), "the drive-letter prefix must not reach the client");
        Assert.IsFalse(error.Message.Contains("somebody"), "the build-machine user name must not reach the client");
    }

    [TestMethod]
    public async Task Handle_LetsCancellationPropagate()
        => await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => InvokeAsync(() => throw new OperationCanceledException()));

    [TestMethod]
    public async Task Handle_LetsAProtocolErrorPropagateUnchanged()
    {
        var protocolError = new InvalidParametersException("42");

        var caught = await Assert.ThrowsExactlyAsync<InvalidParametersException>(
            () => InvokeAsync(() => throw protocolError));

        Assert.AreSame(protocolError, caught);
    }
}
