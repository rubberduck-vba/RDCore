using RDCore.CLI.App.Build.MachO;

namespace RDCore.CLI.App.Build;

// the ad-hoc code signature of a macOS executable (one with no certificate, its identifier the executable's name), as the .NET SDK signs an application's:
// Apple Silicon runs no Mach-O that is unsigned, and writing the assembly's path into the host changes a page its own signature has the hash of. The
// signer is dotnet/runtime's own (MachO/, MIT).
internal static class MacOSSignature
{
    public static bool IsNeeded(string runtime) => runtime.StartsWith("osx", StringComparison.OrdinalIgnoreCase);

    // the executable signed: the signature replaces any it has, after the last page it hashes, and the file ends with it.
    public static byte[] Sign(byte[] executable, string identifier)
    {
        using var image = new MemoryStream();
        image.Write(executable);
        var file = new StreamBasedMachOFile(image);
        var length = MachObjectFile.Create(file).AdHocSignFile(file, identifier);
        return image.GetBuffer().AsSpan(0, checked((int)length)).ToArray();
    }
}
