using RDCore.SDK.Model.Errors;

namespace RDCore.CLI.App.Messages;

internal class ExceptionFormatter
{
    public static string FormatTitle(Exception exception)
        => exception switch
        {
            VBApplicationErrorException e => $"[{e.GetType().Name}] {e.ErrorNumber}" + ((e.ErrorSource.Length > 0) ? $" ({e.ErrorSource})" : string.Empty),
            VBCompileErrorException e => $"[{e.ToDiagnosticCode()}] @ {e.Location}",
            VBRuntimeErrorException e => $"[{e.ToDiagnosticCode()}] @ {e.Location}",
            _ => $"{exception.GetType().Name}"
        };
    public static string FormatBody(Exception exception)
        => exception switch
        {
            VBApplicationErrorException e => $" >> {e.Description}",
            _ => $" >> {exception.Message}"
        };
    public static string? FormatVerbose(Exception exception)
        => exception switch
        {
            VBCompileErrorException e => string.IsNullOrWhiteSpace(e.Verbose) ? default : $"    {e.Verbose}",
            VBRuntimeErrorException e => string.IsNullOrWhiteSpace(e.Verbose) ? default : $"    {e.Verbose}",
            _ => default
        };
    public static string? FormatStackTrace(Exception exception)
        => exception switch
        {
            // TODO surface the internal stack trace
            VBApplicationErrorException or VBCompileErrorException or VBRuntimeErrorDivisionByZeroException => default, 
            _ => exception.StackTrace
        };
}
