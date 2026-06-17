using Newtonsoft.Json.Linq;

namespace RDCore.SDK.Server.Commands;

/// <summary>
/// A service that parses a <see cref="JArray"/> input into a strongly-typed <em>command parameters</em> object.
/// </summary>
/// <typeparam name="TArgs">The record/class type to parse the command parameters into.</typeparam>
public interface ICommandParamsParser<out TArgs>
    where TArgs : class, new()
{
    /// <summary>
    /// Parses the provided <see cref="JArray"/> request body content into a <em>command parameter</em> object of the specified <c>TArgs</c> type.
    /// </summary>
    /// <param name="args">The raw <see cref="JArray"/> command parameter received from the client request.</param>
    /// <returns><c>null</c> if the provided <see cref="JArray"/> cannot be parsed into the specified <c>TArgs</c> type.</returns>
    TArgs? Parse(JArray? args);
}

/// <summary>
/// Exposes the names of all SDK-level supported LSP <em>workspace commands</em>.
/// </summary>
/// <remarks>
/// 🧩 <c>RDCore</c> implements the LSP <em>workspace commands</em> as <em>server commands</em> for
/// extensibility purposes.<br/>
/// 👉 <strong>Avoid hard-coding</strong> <em>server command</em> names in client-side services.<br/><br/>
/// <a href="https://microsoft.github.io/language-server-protocol/specifications/lsp/3.17/specification/#workspace_executeCommand">LSP 3.17 § ExecuteCommand request</a>
/// </remarks>
public static class SdkServerCommandNames
{
    /// <summary>
    /// The name of the <see cref="ParseDocumentCommand"/>.
    /// </summary>
    /// <remarks>
    /// 👉 This command is sent from the <em>CoreLanguageServerApp</em> to the <em>RDCoreParserApp</em>,
    /// prompting the parser to return an <em>abstract syntax tree</em> (AST) for a specific <em>document</em>.
    /// </remarks>
    public const string ParseDocument = "document/parse";
    //public const string ParseProcedure = "document/parseproc";
    //public const string ParseLine = "document/parseline";
}

/// <summary>
/// Represents any <em>server command</em> that a <c>RDCore</c> <em>server application</em> can receive,
/// or that a <c>RDCore</c> <em>client application</em> can send.
/// </summary>
/// <param name="name">The <c>name</c> of the underlying protocol-level (LSP) <em>workspace command</em> request.</param>
/// <remarks>
/// 👉 <strong>Avoid hard-coding</strong> <em>server command</em> names in client-side services.<br/><br/>
/// <a href="https://microsoft.github.io/language-server-protocol/specifications/lsp/3.17/specification/#workspace_executeCommand">LSP 3.17 § ExecuteCommand request</a>
/// </remarks>
public abstract class ServerCommand(string name)
{
    /// <summary>
    /// The <c>Name</c> of the underlying protocol-level (LSP) <em>workspace command</em> request.
    /// </summary>
    public string Name { get; } = name;
    /// <summary>
    /// Executes a parameterless command asynchronously on the server.
    /// </summary>
    /// <param name="token">A <see cref="CancellationToken"/>, for cooperative asynchronous task cancellation.</param>
    /// <param name="args">The deserialized raw command parameter object.</param>
    public abstract Task ExecuteAsync(CancellationToken token, JArray? args = default);
}

/// <summary>
/// Represents any <em>server command</em> that a <c>RDCore</c> <em>server application</em> can receive,
/// or that a <c>RDCore</c> <em>client application</em> can send.
/// </summary>
/// <param name="name">The <c>name</c> of the underlying protocol-level (LSP) <em>workspace command</em> request.</param>
/// <remarks>
/// 👉 <strong>Avoid hard-coding</strong> <em>server command</em> names in client-side services.<br/><br/>
/// <a href="https://microsoft.github.io/language-server-protocol/specifications/lsp/3.17/specification/#workspace_executeCommand">LSP 3.17 § ExecuteCommand request</a>
/// </remarks>
public abstract class ServerCommand<TArgs>(ICommandParamsParser<TArgs> CommandArgsParser, string name) : ServerCommand(name)
    where TArgs : class, new()
{
    /// <summary>
    /// Asynchronously <em>enqueues</em> the execution of a command on the server.
    /// </summary>
    /// <param name="token">A <see cref="CancellationToken"/>, for cooperative asynchronous task cancellation.</param>
    /// <param name="args">The deserialized raw command parameter object.</param>
    public sealed override async Task ExecuteAsync(CancellationToken token, JArray? args = default)
    {
        if (args is null || args.Count == 0)
        {
            await ExecuteAsync(null, token);
        }
        else if (CommandArgsParser.Parse(args) is TArgs commandArgs)
        {
            await ExecuteAsync(commandArgs, token);
        }
    }

    /// <summary>
    /// Executes the command asynchronously on the server.
    /// </summary>
    /// <param name="token">A <see cref="CancellationToken"/>, for cooperative asynchronous task cancellation.</param>
    /// <param name="args">The deserialized raw command parameter object.</param>
    protected abstract Task ExecuteAsync(TArgs? args, CancellationToken token);
}