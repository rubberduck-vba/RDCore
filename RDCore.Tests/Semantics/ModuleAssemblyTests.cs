using RDCore.Parsing;
using RDCore.Runtime.LanguageCore.Errors;
using RDCore.Runtime.LanguageCore.Static;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using System.Collections.Immutable;

namespace RDCore.Tests.Semantics;

/// <summary>
/// RD-VBAL §5.1: the parts of one name are one module (<see cref="ModuleAssembly"/>). Each part is read from its file as the parser reads it, the
/// file's own module and each block's (<see cref="ModulePart.PartsOf"/>).
/// </summary>
[TestClass]
public sealed class ModuleAssemblyTests
{
    private static readonly Uri First = new("file:///c:/ws/First.bas");
    private static readonly Uri Second = new("file:///c:/ws/Second.bas");

    // the source of a file: its lines, which a DataRow writes with | between them.
    private static string Source(string lines) => string.Join("\r\n", lines.Split('|')) + "\r\n";

    private static IEnumerable<ModulePart> PartsOf(Uri file, string name, ModuleType kind, string lines)
        => ModulePart.PartsOf(file, name, kind, new ModuleParser().Parse(file, Source(lines)));

    private static string TextAt(string lines, SourceRange range)
        => lines.Split('|')[range.Start.Line][range.Start.Character..range.End.Character];

    private static string[] NamesOf(ModuleNode tree) => [.. tree.Children.Select(child => child switch
    {
        VariableDeclarationNode variable => variable.Name,
        MemberDeclarationNode member => member.Name,
        _ => null,
    }).OfType<string>()];

    private static AssembledModule Single(ImmutableArray<AssembledModule> modules, string name)
        => modules.Single(module => string.Equals(module.Name, name, StringComparison.OrdinalIgnoreCase));

    // the module has every part's declarations before every part's procedures, its plain part's first whatever the order the parts come in, and each node
    // is where its own part writes it.
    [TestMethod]
    public void AModuleOfParts_DeclaresEveryPartsNames_BeforeEveryPartsProcedures_ThePlainPartFirst()
    {
        var helpers = "Attribute VB_Name = \"Helpers\"|Private count As Long|Public Sub Add()|End Sub";
        var partial = "Partial Module Helpers|Private total As Long|Public Sub Report()|End Sub|End Module";
        var plainPart = PartsOf(First, "Helpers", ModuleType.StdModule, helpers).ToArray();

        var (modules, refused) = ModuleAssembly.Of([.. PartsOf(Second, "Second", ModuleType.StdModule, partial), .. plainPart]);

        Assert.IsEmpty(refused);
        var module = Single(modules, "Helpers");
        Assert.AreEqual(ModuleType.StdModule, module.Kind);
        CollectionAssert.AreEqual(new[] { "count", "total", "Add", "Report" }, NamesOf(module.Parse.SyntaxTree!));
        Assert.AreEqual(plainPart[0].Parse.SyntaxTree!.Identity, module.Parse.SyntaxTree!.Identity, "the module is its plain part's");
        var report = module.Parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single(member => member.Name == "Report");
        Assert.AreEqual(Second, report.SourceLocation.Uri);
        Assert.AreEqual(2, report.SourceLocation.Range.Start.Line);
    }

    // parts that are all Partial make the module on their own: of the first part's name and kind, with what each part declares.
    [TestMethod]
    public void PartsThatAreAllPartial_AreOneModule_OfTheFirstPartsKind()
    {
        var (modules, refused) = ModuleAssembly.Of(
        [
            .. PartsOf(First, "First", ModuleType.StdModule, "Partial Class Account|Public Balance As Currency|End Class"),
            .. PartsOf(Second, "Second", ModuleType.StdModule, "Partial Class ACCOUNT|Public Sub Deposit()|End Sub|End Class"),
        ]);

        Assert.IsEmpty(refused);
        var account = Single(modules, "Account");
        Assert.AreEqual("Account", account.Name);
        Assert.AreEqual(ModuleType.ClassModule, account.Kind);
        CollectionAssert.AreEqual(new[] { "Balance", "Deposit" }, NamesOf(account.Parse.SyntaxTree!));
    }

    // a part of another kind than the module's makes it invalid, at the word of the part's kind: none of its parts is a module.
    [TestMethod]
    public void APartOfAnotherKind_RefusesTheModule_AtTheWordOfItsKind()
    {
        var partial = "Partial Class Helpers|End Class";

        var (modules, refused) = ModuleAssembly.Of(
        [
            .. PartsOf(First, "Helpers", ModuleType.StdModule, "Attribute VB_Name = \"Helpers\""),
            .. PartsOf(Second, "Second", ModuleType.StdModule, partial),
        ]);

        Assert.IsFalse(modules.Any(module => module.Name == "Helpers"));
        var error = refused.Single(module => module.Name == "Helpers").Errors.Single();
        Assert.AreEqual((int)VBCompileErrorId.ModulePartOfAnotherKind, error.ErrorId);
        Assert.AreEqual(Second, error.Location.Uri);
        Assert.AreEqual("Class", TextAt(partial, error.Location.Range));
    }

    // a directive two parts state differently makes the module invalid, at the later part's word: a block's header modifier, an attribute or an
    // annotation.
    [TestMethod]
    [DataRow("First", "Public Partial Class Account|End Class", ModuleType.StdModule, "Private Partial Class Account|End Class", "Private",
        DisplayName = "two headers")]
    [DataRow("Account", "VERSION 1.0 CLASS|BEGIN|  MultiUse = -1  'True|END|Attribute VB_Name = \"Account\"|Attribute VB_Creatable = False",
        ModuleType.ClassModule, "Partial Creatable Class Account|End Class", "Creatable", DisplayName = "an attribute, then a header")]
    [DataRow("Account", "Attribute VB_Name = \"Account\"|Option Private Module", ModuleType.StdModule, "Public Partial Module Account|End Module", "Public",
        DisplayName = "Option Private Module, then a header")]
    [DataRow("Account", "VERSION 1.0 CLASS|BEGIN|  MultiUse = -1  'True|END|Attribute VB_Name = \"Account\"|Attribute VB_PredeclaredId = False",
        ModuleType.ClassModule, "Partial Class Account|'@PredeclaredId|End Class", "@PredeclaredId", DisplayName = "an attribute, then an annotation")]
    public void ADirectiveTwoPartsStateDifferently_RefusesTheModule_AtTheLaterStatement(
        string owner, string first, ModuleType kind, string second, string expected)
    {
        var (modules, refused) = ModuleAssembly.Of([.. PartsOf(First, owner, kind, first), .. PartsOf(Second, "Second", ModuleType.StdModule, second)]);

        Assert.IsFalse(modules.Any(module => module.Name == "Account"));
        var error = refused.Single(module => module.Name == "Account").Errors.Single();
        Assert.AreEqual((int)VBCompileErrorId.ModuleDirectiveStatedDifferently, error.ErrorId);
        Assert.AreEqual(Second, error.Location.Uri);
        Assert.AreEqual(expected, TextAt(second, error.Location.Range));
    }

    // a directive a later part states the same way is redundant there, a modifier or an annotation, and the module is valid.
    [TestMethod]
    [DataRow("First", "Public Partial Class Account|End Class", ModuleType.StdModule, "Public Partial Class Account|End Class", "Public",
        RedundantStatementKind.RestatedModifier, DisplayName = "two headers")]
    [DataRow("Account", "VERSION 1.0 CLASS|BEGIN|  MultiUse = -1  'True|END|Attribute VB_Name = \"Account\"|Attribute VB_Exposed = True",
        ModuleType.ClassModule, "Public Partial Class Account|End Class", "Public", RedundantStatementKind.RestatedModifier,
        DisplayName = "an attribute, then a header")]
    [DataRow("First", "Partial Class Account|'@PredeclaredId|End Class", ModuleType.StdModule, "Partial Class Account|'@PredeclaredId|End Class",
        "@PredeclaredId", RedundantStatementKind.RestatedAnnotation, DisplayName = "two annotations")]
    public void ADirectiveALaterPartStatesTheSame_IsRedundantThere_AndTheModuleIsValid(
        string owner, string first, ModuleType kind, string second, string expected, RedundantStatementKind expectedKind)
    {
        var (modules, refused) = ModuleAssembly.Of([.. PartsOf(First, owner, kind, first), .. PartsOf(Second, "Second", ModuleType.StdModule, second)]);

        Assert.IsFalse(refused.Any(module => module.Name == "Account"));
        var redundant = Single(modules, "Account").Redundant.Single();
        Assert.AreEqual(expectedKind, redundant.Kind);
        Assert.AreEqual(Second, redundant.Location.Uri);
        Assert.AreEqual(expected, TextAt(second, redundant.Location.Range));
    }

    // Creatable says what other projects may do with a class, so it is redundant on a class that no other project sees, and on no other.
    [TestMethod]
    [DataRow("Creatable Class Account|End Class", true, DisplayName = "no visibility")]
    [DataRow("Private Creatable Class Account|End Class", true, DisplayName = "Private")]
    [DataRow("Public Creatable Class Account|End Class", false, DisplayName = "Public")]
    public void Creatable_IsRedundant_OnAClassNoOtherProjectSees(string lines, bool isRedundant)
    {
        var account = Single(ModuleAssembly.Of(PartsOf(First, "First", ModuleType.StdModule, lines)).Modules, "Account");

        Assert.IsTrue(account.Directives.Creatable);
        Assert.AreEqual(isRedundant ? ("Creatable", RedundantStatementKind.CreatableNotExposed) : default,
            account.Redundant.Select(entry => (TextAt(lines, entry.Location.Range), entry.Kind)).SingleOrDefault());
    }

    // the header of a Class block alone says whether other projects see the class: an '@Exposed in the block is read by nothing, whatever the header says,
    // and is redundant there.
    [TestMethod]
    [DataRow("Public Class Account|'@Exposed|End Class", true, RedundantStatementKind.ExposedAnnotation, DisplayName = "Public")]
    [DataRow("Private Class Account|'@Exposed|End Class", false, RedundantStatementKind.ExposedAnnotation, DisplayName = "Private")]
    [DataRow("Partial Class Account|'@Exposed|End Class", false, RedundantStatementKind.ExposedAnnotationWithoutVisibility,
        DisplayName = "no visibility")]
    public void AnExposedAnnotation_InAClassBlock_IsRedundant_AndTheHeaderSaysWhetherTheClassIsExposed(
        string lines, bool isExposed, RedundantStatementKind expectedKind)
    {
        var account = Single(ModuleAssembly.Of(PartsOf(First, "First", ModuleType.StdModule, lines)).Modules, "Account");

        Assert.AreEqual(isExposed, account.Directives.Exposed);
        var redundant = account.Redundant.Single();
        Assert.AreEqual(expectedKind, redundant.Kind);
        Assert.AreEqual((First, "@Exposed"), (redundant.Location.Uri, TextAt(lines, redundant.Location.Range)));
    }

    // MS-VBAL §4.2: two plain parts are two modules of one name, neither of which is the module: each is reported where it says its name.
    [TestMethod]
    public void TwoPlainPartsOfOneName_RefuseTheModule_AtEachName()
    {
        var block = "Module Helpers|End Module";

        var (modules, refused) = ModuleAssembly.Of(
        [
            .. PartsOf(First, "Helpers", ModuleType.StdModule, "Attribute VB_Name = \"Helpers\""),
            .. PartsOf(Second, "Second", ModuleType.StdModule, block),
        ]);

        Assert.IsFalse(modules.Any(module => module.Name == "Helpers"));
        var errors = refused.Single(module => module.Name == "Helpers").Errors;
        Assert.IsTrue(errors.All(error => error.ErrorId == (int)VBCompileErrorId.NameConflictsWithModuleProjectOrLibrary), string.Join("; ", errors));
        CollectionAssert.AreEquivalent(new[] { First, Second }, errors.Select(error => error.Location.Uri).ToArray());
        Assert.AreEqual("Helpers", TextAt(block, errors.Single(error => error.Location.Uri == Second).Location.Range));
    }

    // what the parts state is the module's, a header's words and the annotation; what none states is what a module that states nothing has, but a class
    // of blocks alone is not creatable unless a header says so.
    [TestMethod]
    public void TheDirectivesOfAModuleOfParts_AreWhatItsPartsState_AndTheDefaultsOtherwise()
    {
        var (modules, _) = ModuleAssembly.Of(
        [
            .. PartsOf(First, "First", ModuleType.StdModule,
                "Public Partial Class Account|End Class|Class Ledger|End Class|Private Module Helpers|End Module"),
            .. PartsOf(Second, "Second", ModuleType.StdModule, "Partial Creatable Class Account|'@PredeclaredId|End Class"),
        ]);

        var account = Single(modules, "Account").Directives;
        Assert.IsTrue(account.Exposed);
        Assert.IsTrue(account.Creatable);
        Assert.IsTrue(account.PredeclaredId);
        Assert.AreEqual(ModuleDirectives.None.Extensible, account.Extensible);
        Assert.AreEqual(ModuleDirectives.None with { Creatable = false }, Single(modules, "Ledger").Directives, "a header that states nothing");
        Assert.IsTrue(Single(modules, "Helpers").Directives.PrivateModule);
    }

    // the module of a file, and of a block, that is the only part of its name keeps its parse, and a file's has the directives its tree states.
    [TestMethod]
    [DataRow("Attribute VB_Name = \"Account\"|Option Explicit|Option Private Module|Option Base 1", ModuleType.StdModule, DisplayName = "a standard module")]
    [DataRow("VERSION 1.0 CLASS|BEGIN|  MultiUse = -1  'True|END|Attribute VB_Name = \"Account\"|Attribute VB_Creatable = False"
        + "|Attribute VB_PredeclaredId = True|Attribute VB_Exposed = True|Option Compare Text", ModuleType.ClassModule, DisplayName = "a class module")]
    public void AFilesModuleThatIsTheOnlyPartOfItsName_KeepsItsParse_AndTheDirectivesOfItsTree(string lines, ModuleType kind)
    {
        var part = PartsOf(First, "Account", kind, lines).Single();

        var account = Single(ModuleAssembly.Of([part]).Modules, "Account");

        Assert.AreSame(part.Parse, account.Parse);
        Assert.AreEqual(part.Parse.SyntaxTree.GetModuleDirectives(), account.Directives);
        Assert.IsEmpty(account.Redundant);
    }

    // one file holds one part of a module: a second part of it there makes the module invalid, at the word of the second part's kind.
    [TestMethod]
    [DataRow("Partial Module Helpers|End Module|Partial Module Helpers|End Module", DisplayName = "two Partial blocks")]
    [DataRow("Module Helpers|End Module|Partial Module Helpers|End Module", DisplayName = "a block, then a Partial block")]
    public void TwoPartsOfAModule_InOneFile_RefuseTheModule_AtTheSecond(string lines)
    {
        var (modules, refused) = ModuleAssembly.Of(PartsOf(First, "First", ModuleType.StdModule, lines));

        Assert.IsFalse(modules.Any(module => module.Name == "Helpers"));
        var error = refused.Single(module => module.Name == "Helpers").Errors.Single();
        Assert.AreEqual((int)VBCompileErrorId.NameConflictsWithModuleProjectOrLibrary, error.ErrorId);
        Assert.AreEqual(new SourceRange(2, 8, 2, 14), error.Location.Range);
    }

    // a symbol of a module of parts is written in its own part's file. A declaration of one part is at the lines of the other part's #Else branch, and is
    // a second declaration of the name, never an alternative of the first part's #If branch.
    [TestMethod]
    public void EachSymbolOfAModuleOfParts_IsInItsPartsFile_AndNoDeclarationIsAnAlternativeAcrossParts()
    {
        var helpers = "Attribute VB_Name = \"Helpers\"|#If VBA7 Then|Private count As Long|#Else|Private total As Long|#End If";
        var partial = "Partial Module Helpers|Private first As Long|Private second As Long|Private third As Long|Private count As Long|End Module";
        var (modules, _) = ModuleAssembly.Of(
            [.. PartsOf(First, "Helpers", ModuleType.StdModule, helpers), .. PartsOf(Second, "Second", ModuleType.StdModule, partial)]);
        var module = Single(modules, "Helpers");
        var root = new Uri("file:///c:/ws/");
        var moduleUri = new UriBuilder(root) { Fragment = "Helpers" }.Uri;
        var resolver = RDCore.Symbols.WorkspaceSymbolResolver.Compose(
            root, [(moduleUri, ModuleType.StdModule, module.Parse)], new RDCore.Symbols.IntrinsicSymbolResolver());

        var symbols = new RDCore.Symbols.SyntaxTreeSymbolProvider(root, moduleUri, ModuleType.StdModule, module.Parse, resolver).ProvideSymbols()
            .OfType<WorkspaceSymbol>().ToArray();

        var counts = symbols.Where(symbol => symbol.Name == "count").ToArray();
        CollectionAssert.AreEqual(new[] { (First, 2), (Second, 4) }, counts.Select(symbol => (symbol.DocumentUri, symbol.Range.Start.Line)).ToArray());
        Assert.IsTrue(counts.All(symbol => symbol.Alternatives.IsDefaultOrEmpty), "a site of each part, neither an alternative of the other");
        Assert.AreEqual(First, symbols.Single(symbol => symbol.Name == "total").DocumentUri);
        Assert.AreEqual(Second, symbols.Single(symbol => symbol.Name == "third").DocumentUri);
    }

    // the description of a module of parts is the first part's that writes one, with an attribute or an annotation: the module's symbol carries it, and a
    // later part that writes the same is redundant there. A member's annotation describes the member in its own part's file.
    [TestMethod]
    [DataRow("Attribute VB_Name = \"Helpers\"|Attribute VB_Description = \"Sums.\"", DisplayName = "an attribute, then an annotation")]
    [DataRow("Attribute VB_Name = \"Helpers\"|'@ModuleDescription(\"Sums.\")", DisplayName = "two annotations")]
    public void TheDescriptionOfAModuleOfParts_IsItsFirstPartsThatWritesOne_AndTheSameLaterIsRedundant(string helpers)
    {
        var partial = "Partial Module Helpers|'@ModuleDescription(\"Sums.\")|Private total As Long|'@Description(\"Adds.\")|Public Sub Add()|End Sub"
            + "|End Module";
        var (modules, refused) = ModuleAssembly.Of(
            [.. PartsOf(First, "Helpers", ModuleType.StdModule, helpers), .. PartsOf(Second, "Second", ModuleType.StdModule, partial)]);

        Assert.IsFalse(refused.Any(module => module.Name == "Helpers"));
        var module = Single(modules, "Helpers");
        var redundant = module.Redundant.Single();
        Assert.AreEqual((Second, "@ModuleDescription(\"Sums.\")", RedundantStatementKind.RestatedAnnotation),
            (redundant.Location.Uri, TextAt(partial, redundant.Location.Range), redundant.Kind));

        var root = new Uri("file:///c:/ws/");
        var moduleUri = new UriBuilder(root) { Fragment = "Helpers" }.Uri;
        var symbols = RDCore.Symbols.WorkspaceSymbolResolver.ComposeWithScopes(
            root, [(moduleUri, ModuleType.StdModule, module.Parse)], new RDCore.Symbols.IntrinsicSymbolResolver()).Symbols!;
        Assert.AreEqual("Sums.", symbols.OfType<VBModuleSymbol>().Single(symbol => symbol.Name == "Helpers").GetProperty(SymbolProperties.DocString));
        Assert.AreEqual("Adds.", symbols.OfType<VBTypeMemberSymbol>().Single(symbol => symbol.Name == "Add").GetProperty(SymbolProperties.DocString));
    }

    // two parts that describe one module differently give it two descriptions: the module is refused, at the later part's annotation.
    [TestMethod]
    public void TwoPartsThatDescribeAModuleDifferently_RefuseTheModule_AtTheLaterDescription()
    {
        var partial = "Partial Module Helpers|'@ModuleDescription(\"Totals.\")|End Module";

        var (modules, refused) = ModuleAssembly.Of([.. PartsOf(First, "Helpers", ModuleType.StdModule,
            "Attribute VB_Name = \"Helpers\"|'@ModuleDescription(\"Sums.\")"), .. PartsOf(Second, "Second", ModuleType.StdModule, partial)]);

        Assert.IsFalse(modules.Any(module => module.Name == "Helpers"));
        var error = refused.Single(module => module.Name == "Helpers").Errors.Single();
        Assert.AreEqual((int)VBCompileErrorId.ModuleDirectiveStatedDifferently, error.ErrorId);
        Assert.AreEqual((Second, "@ModuleDescription(\"Totals.\")"), (error.Location.Uri, TextAt(partial, error.Location.Range)));
    }
}
