using RDCore.Runtime.Composer;
using RDCore.Runtime.Hosting;
using RDCore.Runtime.Support.Host;
using RDCore.SDK.Platform.Protocol;
using static RDCore.Tests.Composer.ComposedProject;

namespace RDCore.Tests.Hosting;

/// <summary>
/// A program loaded from its composition (<see cref="ComposedProgramFile.LoadAsync"/>), as a host with no parser loads what <c>rdc compose</c> wrote: its
/// machines, which run with no source, each in a state of its own, its documents, and a file that is not a composition this platform reads.
/// </summary>
[TestClass]
public sealed class ComposedProgramFileTests
{
    private string _root = string.Empty;
    private string _host = string.Empty;

    [TestInitialize]
    public void CreateFolders() => (_root, _host) = (NewFolder(), NewFolder());

    [TestCleanup]
    public void DeleteFolders()
    {
        Directory.Delete(_root, recursive: true);
        Directory.Delete(_host, recursive: true);
    }

    // the composition of the project, written to the host's folder: the image it was written from, and the file.
    private async Task<(ProgramCompositionImage Image, string File)> WriteCompositionAsync()
    {
        var composed = await ProgramComposer.ComposeImageAsync(Write(_root), options: null, default);
        composed.Loaded.Dispose();
        var file = Path.Combine(_host, "Composed" + ComposedProgramFile.Extension);
        await File.WriteAllTextAsync(file, composed.Image.ToJson());
        return (composed.Image, file);
    }

    // every machine of a loaded composition is composed from it: the project's files are gone, and each machine runs the program, in a state of its own.
    [TestMethod]
    public async Task EveryMachine_RunsTheProgram_WithNoSource_InAStateOfItsOwn()
    {
        var (_, file) = await WriteCompositionAsync();
        foreach (var source in Files)
        {
            File.Delete(Path.Combine(_root, source));
        }

        await using var program = await ComposedProgramFile.LoadAsync(file);
        await using var first = await program.CreateMachineAsync(default);
        await using var second = await program.CreateMachineAsync(default);
        await using var third = await program.CreateMachineAsync(default);

        await AssertEachMachineHasItsOwnStateAsync(first, second, third);
    }

    // the documents are the program's files as they were parsed when it was composed: what a host reads of them, their annotations included, is the same.
    [TestMethod]
    public async Task TheDocuments_AreTheOnesTheProgramWasComposedWith()
    {
        var (image, file) = await WriteCompositionAsync();

        await using var program = await ComposedProgramFile.LoadAsync(file);

        Assert.HasCount(Files.Length, image.Documents);
        CollectionAssert.AreEqual(image.Documents.Select(PlatformJson.Serialize).ToArray(), program.Documents.Select(PlatformJson.Serialize).ToArray());
    }

    // a composition of a format other than this platform's is refused, and so is a file that is not a composition.
    [TestMethod]
    public async Task ACompositionOfAnotherFormat_OrNoComposition_IsRefused()
    {
        var (image, file) = await WriteCompositionAsync();
        await File.WriteAllTextAsync(file, (image with { FormatVersion = ProgramCompositionImage.CurrentFormatVersion + 1 }).ToJson());
        var other = Path.Combine(_host, "Other" + ComposedProgramFile.Extension);
        await File.WriteAllTextAsync(other, "Attribute VB_Name = \"Program\"");

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => ComposedProgramFile.LoadAsync(file));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => ComposedProgramFile.LoadAsync(other));
    }
}
