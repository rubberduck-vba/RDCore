using Microsoft.Extensions.Options;
using RDCore.SDK.Server.Configuration;
using System.IO.Abstractions;
using System.Text.Json;

namespace RDCore.LanguageServer.Extensibility
{
    internal class ExtensionsClient(IOptions<SdkAppOptions> options, IFileSystem fileSystem) : IExtensionsProvider
    {
        // TODO manage extensions

        public IEnumerable<ExtensionInfo> DiscoverExtensions()
        {
            var manifestFileName = options.Value.Platform.Extensions.Manifest;
            var info = fileSystem.DirectoryInfo.New(options.Value.Platform.Extensions.Path);
            foreach (var folder in info.EnumerateDirectories())
            {
                var title = folder.Name;
                if (folder.GetFiles(manifestFileName).FirstOrDefault() is IFileInfo manifest
                    && JsonSerializer.Deserialize<ExtensionInfo>(manifest.OpenRead()) is ExtensionInfo extensionInfo)
                {
                    yield return extensionInfo;
                }
            }
        }
    }
}
