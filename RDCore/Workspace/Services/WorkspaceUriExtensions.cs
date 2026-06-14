namespace RDCore.LanguageServer.Workspace.Services;

internal static class UriExtensions
{

    public static Uri ToUri(this string Uri) => new(Uri);
    public static Uri ToUri(this string relativeUri, string Uri) => new(Uri.ToUri(), relativeUri);

    public static string GetFolderPath(this Uri uri, string Uri) => Path.GetRelativePath(Uri, Path.GetDirectoryName(uri.AbsolutePath) ?? string.Empty).Replace("..", string.Empty);
    public static string GetFolderPath(this string relativeUri) => Path.GetDirectoryName(relativeUri) ?? string.Empty;
}
