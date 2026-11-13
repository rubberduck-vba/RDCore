using RDCore.LanguageServer.Services.Refactoring;
using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.Symbols;
using RDCore.Runtime.LanguageCore.Refactoring;

namespace RDCore.Tests.Semantics.Refactoring;

/// <summary>
/// What an edit changes of the references of the names of the code: nothing when it moves them, the names it writes when it renames them, and the
/// references it makes refer to something else, or to nothing, when it shadows or removes what they referred to.
/// </summary>
[TestClass]
public sealed class BindingCheckTests
{
    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly Uri Module1 = new UriBuilder(Root) { Fragment = "Module1" }.Uri;
    private static readonly Uri Module2 = new UriBuilder(Root) { Fragment = "Module2" }.Uri;

    private const string Fields = "Attribute VB_Name = \"Module1\"\r\nOption Explicit\r\nPublic count As Long\r\n";

    private const string User = "Attribute VB_Name = \"Module2\"\r\nOption Explicit\r\nPublic Sub Main()\r\n    Dim x As Long\r\n    x = count\r\n    count = x + 1\r\nEnd Sub\r\n";

    private static RenameContext Workspace(string module1, string module2)
    {
        var first = new ModuleParser().Parse(Module1, module1);
        var second = new ModuleParser().Parse(Module2, module2);
        var composition = WorkspaceSymbolResolver.ComposeWithScopes(
            Root, [(Module1, ModuleType.StdModule, first), (Module2, ModuleType.StdModule, second)], new IntrinsicSymbolResolver());
        return new RenameContext(composition.Resolver, composition.ScopeTree,
            [new RenameModule(Module1, first.SyntaxTree!, module1), new RenameModule(Module2, second.SyntaxTree!, module2)]);
    }

    // the edits of one module, applied to its text from the last to the first.
    private static (IReadOnlyDictionary<string, IReadOnlyList<ModuleTextEdit>> Edits, string Text) Edited(Uri module, string text, params ModuleTextEdit[] edits)
    {
        var lines = text.Split("\r\n").ToList();
        foreach (var edit in edits.OrderByDescending(edit => edit.Range.Start))
        {
            var (start, end) = (edit.Range.Start, edit.Range.End);
            var joined = lines[start.Line][..start.Character] + edit.NewText + lines[end.Line][end.Character..];
            lines.RemoveRange(start.Line, end.Line - start.Line + 1);
            lines.InsertRange(start.Line, joined.Split("\r\n"));
        }

        return (new Dictionary<string, IReadOnlyList<ModuleTextEdit>> { [module.AbsoluteUri] = edits }, string.Join("\r\n", lines));
    }

    private static ModuleTextEdit Edit(int line, int start, int end, string text) => new(new SourceRange(line, start, line, end), text);

    [TestMethod]
    public void AnEditThatOnlyMovesTheCode_ChangesNoReference()
    {
        var (edits, text) = Edited(Module2, User, Edit(3, 0, 0, "\r\n"), Edit(4, 4, 4, "  "));

        var changes = BindingCheck.Changes(Workspace(Fields, User), Workspace(Fields, text), edits);

        Assert.IsEmpty(changes, string.Join("; ", changes));
    }

    [TestMethod]
    public void ARename_ChangesOnlyTheNamesItWrites()
    {
        var (edits, text) = Edited(Module2, User, Edit(3, 8, 9, "y"), Edit(4, 4, 5, "y"), Edit(5, 12, 13, "y"));

        var changes = BindingCheck.Changes(Workspace(Fields, User), Workspace(Fields, text), edits);

        Assert.IsTrue(changes.All(change => change.Kind is BindingChangeKind.Written && change.Name == "y"), string.Join("; ", changes));
        Assert.HasCount(3, changes, "the declaration and the two references the rename wrote");
        Assert.AreEqual(1, changes.Select(change => change.Is).Distinct().Count(), "all of them the one local");
    }

    [TestMethod]
    public void ALocalThatShadowsAField_RetargetsTheReferencesToIt()
    {
        var (edits, text) = Edited(Module2, User, Edit(3, 17, 17, "\r\n    Dim count As Long"));

        var changes = BindingCheck.Changes(Workspace(Fields, User), Workspace(Fields, text), edits);

        var retargeted = changes.Where(change => change.Kind is BindingChangeKind.Retargeted).ToList();
        Assert.HasCount(2, retargeted, string.Join("; ", changes));
        Assert.IsTrue(retargeted.All(change => change.Name == "count" && change.Was != change.Is && change.Was is not null && change.Is is not null));
        Assert.AreEqual(new SourceRange(5, 8, 5, 13), retargeted[0].After, "where the reference is after the edit");
    }

    [TestMethod]
    public void RemovingAFieldThatAnotherModuleRefersTo_LeavesItsReferencesReferringToNothing()
    {
        var (edits, text) = Edited(Module1, Fields, Edit(2, 0, 20, string.Empty));

        var changes = BindingCheck.Changes(Workspace(Fields, User), Workspace(text, User), edits);

        var references = changes.Where(change => change.Module == Module2 && change.Name == "count").ToList();
        Assert.HasCount(2, references, string.Join("; ", changes));
        Assert.IsTrue(references.All(change => change.Was is not null && change.Is is null), "they referred to the field, and refer to nothing");
    }
}
