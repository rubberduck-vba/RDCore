using RDCore.CLI.App.Build;
using RDCore.Runtime.Support.Build;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Abstractions.TestingHelpers;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace RDCore.Tests.Cli;

/// <summary>
/// The executable <c>rdc build</c> writes for macOS carries an ad-hoc code signature, without which Apple Silicon does not run it: one whose code directory
/// has the hash of each page the file has before it, which the file's <c>LC_CODE_SIGNATURE</c> command points at, inside its <c>__LINKEDIT</c> segment.
/// The file is read here as macOS reads it, by its layout, apart from the signer that wrote it.
/// </summary>
[TestClass]
public sealed class MacOSSignatureTests
{
    private static readonly string Runtime = Path.Combine(Path.GetTempPath(), "rdcore-sign-runtime");
    private static readonly string Host = Path.Combine(Path.GetTempPath(), "rdcore-sign-packs", "apphost");
    private static readonly string Output = Path.Combine(Path.GetTempPath(), "rdcore-sign-out");

    // the host of the Microsoft.NETCore.App.Host.osx-arm64 package, which the tests' project downloads and copies beside them.
    private static readonly Lazy<byte[]> AppleSiliconHost = new(() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Cli", "Data", "osx-arm64", "apphost")));

    private static readonly Lazy<byte[]> SupportAssembly = new(() => File.ReadAllBytes(typeof(BuiltProgram).Assembly.Location));

    private const uint SegmentCommand = 0x19;
    private const uint CodeSignatureCommand = 0x1d;
    private const uint SuperBlobMagic = 0xfade0cc0;
    private const uint CodeDirectoryMagic = 0xfade0c02;
    private const uint AdHoc = 0x2;
    private const byte Sha256 = 2;

    private static byte[] Built(string runtime)
    {
        var fileSystem = new MockFileSystem();
        fileSystem.Directory.CreateDirectory(Runtime);
        fileSystem.File.WriteAllBytes(Path.Combine(Runtime, "RDCore.Runtime.Support.dll"), SupportAssembly.Value);
        fileSystem.Directory.CreateDirectory(Path.GetDirectoryName(Host)!);
        fileSystem.File.WriteAllBytes(Host, AppleSiliconHost.Value);

        var (executable, _) = new ProgramOutput(fileSystem, Runtime).Write(Output, "Hello", SupportAssembly.Value, [], Host, runtime, []);

        Assert.AreEqual(Path.Combine(Output, "Hello"), executable);
        return fileSystem.File.ReadAllBytes(executable!);
    }

    private sealed record Layout(byte[] File, uint SignatureOffset, uint SignatureSize, ulong LinkEditOffset, ulong LinkEditSize)
    {
        public ReadOnlySpan<byte> Signature => File.AsSpan((int)SignatureOffset, (int)SignatureSize);
    }

    // the 64-bit Mach-O header, then its load commands: the code signature's, and the __LINKEDIT segment's.
    private static Layout Read(byte[] file)
    {
        Assert.AreEqual(0xfeedfacfu, BinaryPrimitives.ReadUInt32LittleEndian(file), "a 64-bit Mach-O");
        var commands = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(16));
        uint? signatureOffset = null, signatureSize = null;
        ulong? linkEditOffset = null, linkEditSize = null;
        var at = 32;
        for (var index = 0; index < commands; index++)
        {
            var command = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(at));
            var size = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(at + 4));
            if (command == CodeSignatureCommand)
            {
                Assert.AreEqual(commands - 1, (uint)index, "the code signature's command is the last");
                signatureOffset = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(at + 8));
                signatureSize = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(at + 12));
            }
            else if (command == SegmentCommand && Encoding.ASCII.GetString(file, at + 8, 16).TrimEnd('\0') == "__LINKEDIT")
            {
                linkEditOffset = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(at + 40));
                linkEditSize = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(at + 48));
            }

            at += size;
        }

        Assert.IsNotNull(signatureOffset, "an LC_CODE_SIGNATURE command");
        Assert.IsNotNull(linkEditOffset, "a __LINKEDIT segment");
        return new Layout(file, signatureOffset.Value, signatureSize!.Value, linkEditOffset.Value, linkEditSize!.Value);
    }

    // the code directory of the signature's super blob, which is big-endian.
    private static ReadOnlySpan<byte> CodeDirectory(Layout layout)
    {
        var signature = layout.Signature;
        Assert.AreEqual(SuperBlobMagic, BinaryPrimitives.ReadUInt32BigEndian(signature), "an embedded signature");
        var count = BinaryPrimitives.ReadUInt32BigEndian(signature[8..]);
        for (var index = 0; index < count; index++)
        {
            var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(signature[(12 + (index * 8) + 4)..]);
            if (BinaryPrimitives.ReadUInt32BigEndian(signature[offset..]) == CodeDirectoryMagic)
            {
                return signature[offset..];
            }
        }

        Assert.Fail("the signature has no code directory");
        return default;
    }

    [TestMethod]
    public void AnExecutableForAppleSilicon_PointsAtItsSignature_WhichEndsTheFile_InsideItsLinkEditSegment()
    {
        var layout = Read(Built("osx-arm64"));

        Assert.AreEqual(layout.File.Length, (long)layout.SignatureOffset + layout.SignatureSize, "the signature ends the file");
        Assert.IsTrue(layout.LinkEditOffset <= layout.SignatureOffset, "__LINKEDIT begins before the signature");
        Assert.AreEqual(layout.LinkEditOffset + layout.LinkEditSize, (ulong)layout.SignatureOffset + layout.SignatureSize, "__LINKEDIT ends with it");
    }

    [TestMethod]
    public void EachPageOfAnExecutableForAppleSilicon_HasItsHash_InTheCodeDirectory()
    {
        var layout = Read(Built("osx-arm64"));
        var directory = CodeDirectory(layout);

        var hashes = (int)BinaryPrimitives.ReadUInt32BigEndian(directory[16..]);
        var pages = (int)BinaryPrimitives.ReadUInt32BigEndian(directory[28..]);
        var limit = BinaryPrimitives.ReadUInt32BigEndian(directory[32..]);
        var hashSize = directory[36];
        var pageSize = 1 << directory[39];
        Assert.AreEqual(Sha256, directory[37], "SHA-256");
        Assert.AreEqual(layout.SignatureOffset, limit, "the pages hashed are those before the signature");
        Assert.AreEqual((int)((limit + pageSize - 1) / pageSize), pages);
        for (var page = 0; page < pages; page++)
        {
            var start = page * pageSize;
            var hash = SHA256.HashData(layout.File.AsSpan(start, (int)Math.Min(pageSize, limit - start)));
            CollectionAssert.AreEqual(hash, directory.Slice(hashes + (page * hashSize), hashSize).ToArray(), $"page {page}");
        }
    }

    [TestMethod]
    public void TheSignatureOfAnExecutableForAppleSilicon_IsAdHoc_AndNamesTheExecutable()
    {
        var directory = CodeDirectory(Read(Built("osx-arm64")));

        Assert.AreEqual(AdHoc, BinaryPrimitives.ReadUInt32BigEndian(directory[12..]) & AdHoc, "ad hoc");
        var identifier = directory[(int)BinaryPrimitives.ReadUInt32BigEndian(directory[20..])..];
        Assert.AreEqual("Hello", Encoding.UTF8.GetString(identifier[..identifier.IndexOf((byte)0)]));
    }

    [TestMethod]
    public void TheSignedExecutable_RunsTheProgramsAssembly()
    {
        var file = Built("osx-arm64");

        var at = file.AsSpan().IndexOf("Hello.dll\0"u8);
        Assert.IsTrue(at >= 0, "the assembly's path is written in the slot");
        Assert.IsLessThan(0, file.AsSpan().IndexOf(AppHost.Placeholder), "no placeholder is left");
    }

    [TestMethod]
    public void AnExecutableForAnotherSystem_IsNotSigned()
        => CollectionAssert.AreEqual(AppHost.Patch(AppleSiliconHost.Value, "Hello.dll"), Built("linux-x64"));

    [TestMethod]
    [Ignore("macOS kills an executable whose signature it rejects before it runs: this runs only on a Mac with Apple Silicon, and CI has none yet")]
    public void TheSignedExecutable_IsRunByMacOS()
    {
        if (OperatingSystem.IsMacOS() && RuntimeInformation.OSArchitecture == Architecture.Arm64)
        {
            RunOnMacOS();
        }
        else
        {
            Assert.Inconclusive("not a Mac with Apple Silicon");
        }
    }

    // the host started without the assembly beside it: one macOS lets run says so, and exits with its own code; one it rejects is killed (SIGKILL).
    [SupportedOSPlatform("macos")]
    private static void RunOnMacOS()
    {
        var folder = Directory.CreateTempSubdirectory("rdcore-sign-").FullName;
        var executable = Path.Combine(folder, "Hello");
        File.WriteAllBytes(executable, MacOSSignature.Sign(AppHost.Patch(AppleSiliconHost.Value, "Hello.dll"), "Hello"));
        File.SetUnixFileMode(executable, File.GetUnixFileMode(executable) | UnixFileMode.UserExecute);

        using var process = Process.Start(new ProcessStartInfo(executable) { RedirectStandardError = true, RedirectStandardOutput = true })!;
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.AreNotEqual(137, process.ExitCode, "killed by the system");
        Assert.Contains("Hello.dll", error);
    }
}
