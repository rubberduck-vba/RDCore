namespace RDCore.LanguageServer.Server;

public class InvalidRequestException(string name)
    : ArgumentException("Bad request: parameterization is incomplete, inconsistent, or otherwise invalid.", name)
{
    public static InvalidRequestException For<T>(T? request) => new(request?.GetType().Name ?? typeof(T).Name);
}