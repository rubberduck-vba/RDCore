using RDCore.Runtime.Execution;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Semantics.Instructions;

namespace RDCore.Tests.Runtime.Execution;

[TestClass]
public sealed class ProgramImageTests
{
    private static readonly Uri ModuleA = new("file:///c:/ws/#A");
    private static readonly Uri ModuleB = new("file:///c:/ws/#B");

    private static SemanticId Procedure(string module, string name) => new(new Uri($"file:///c:/ws/#{module}.{name}"));

    private static InstructionList Body() => new([], new Dictionary<string, int>(), new Dictionary<RDCore.SDK.Model.AST.Abstract.SyntaxNodeId, int>());

    private static KeyValuePair<SemanticId, InstructionList> Entry(string module, string name) => new(Procedure(module, name), Body());

    [TestMethod]
    public void AProcedure_IsFoundByTheSymbolItIsTheBodyOf_AndByNothingElse()
    {
        var image = new ProgramImage();
        image.Load(ModuleA, [Entry("A", "Run")]);

        Assert.IsTrue(image.ContainsKey(Procedure("A", "Run")));
        Assert.IsFalse(image.ContainsKey(Procedure("A", "Other")));
        Assert.IsFalse(image.ContainsKey(Procedure("B", "Run")), "the same name in another module is another procedure");
    }

    [TestMethod]
    public void LoadingAModuleAgain_ReplacesWhatItHadBefore()
    {
        var image = new ProgramImage();
        image.Load(ModuleA, [Entry("A", "Old"), Entry("A", "Kept")]);

        image.Load(ModuleA, [Entry("A", "Kept"), Entry("A", "New")]);

        Assert.IsFalse(image.ContainsKey(Procedure("A", "Old")), "a procedure the module no longer declares is gone");
        Assert.IsTrue(image.ContainsKey(Procedure("A", "Kept")));
        Assert.IsTrue(image.ContainsKey(Procedure("A", "New")));
        Assert.AreEqual(2, image.Count);
    }

    [TestMethod]
    public void LoadingAModule_LeavesTheOthersAlone()
    {
        var image = new ProgramImage();
        image.Load(ModuleA, [Entry("A", "Run")]);
        image.Load(ModuleB, [Entry("B", "Run")]);

        image.Load(ModuleA, []);

        Assert.IsFalse(image.ContainsKey(Procedure("A", "Run")));
        Assert.IsTrue(image.ContainsKey(Procedure("B", "Run")));
    }

    [TestMethod]
    public void UnloadingAModule_RemovesItsProcedures_AndReportsWhetherThereWereAny()
    {
        var image = new ProgramImage();
        image.Load(ModuleA, [Entry("A", "Run")]);

        Assert.IsTrue(image.Unload(ModuleA));
        Assert.AreEqual(0, image.Count);
        Assert.IsFalse(image.IsLoaded(ModuleA));
        Assert.IsFalse(image.Unload(ModuleA));
    }
}
