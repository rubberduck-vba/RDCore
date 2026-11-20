using System.IO.Abstractions;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Flags;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Semantics;

/// <summary>
/// The members a hidden default member of a library's class stands for, as the library's file states them (MS-VBAL 5.6.2.2, 5.6.13): Excel's
/// <c>Range._Default</c> is <c>Item</c> given arguments and <c>Value</c> given none. The default member's fact carries them, on an expression of the class and on an access that
/// goes through the default member, for a code fix that writes the access out in full.
/// </summary>
[TestClass]
[TestCategory(TestCategories.Wide)]
public sealed class LibraryDefaultMemberNamesTests
{
    private static readonly DefaultMemberNames ItemOrValue = new("Item", "Value");

    private static string Symbols
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RDCore.slnx")))
            {
                directory = directory.Parent;
            }

            Assert.IsNotNull(directory, "the repository root, where RDCore.slnx is");
            return Path.Combine(directory.FullName, "Symbols");
        }
    }

    // the Excel library that ships (Symbols/Excel.json).
    private static ModuleWorkspace.WorkspaceLibraries Excel => new(["Excel"], new DirectoryLibrarySource(new FileSystem(), Symbols));

    // the model of Work, given a Range, whose body is `body`.
    private static async Task<ProcedureSemanticsDto> WorkAsync(string body)
    {
        var program = ModuleWorkspace.StandardModule("Program", ["Option Explicit", "Public Sub Work(ByVal r As Range, ByVal c As Collection)",
            .. body.Split('|'), "End Sub"]);
        var payload = await ModuleWorkspace.SemanticsAsync([], program, libraries: Excel);
        return payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Program").Procedures.Single();
    }

    [TestMethod]
    public async Task AnExpressionOfTheClass_HasItsDefaultMember_WithTheMembersItStandsFor()
    {
        var work = await WorkAsync("Debug.Print r");

        var member = work.Expressions.Select(fact => fact.DefaultMember).OfType<DefaultMemberFact>().First();
        Assert.AreEqual("_Default", member.Name);
        Assert.AreEqual(ItemOrValue, member.WrittenAs);
    }

    [TestMethod]
    public async Task AnAccessThroughTheDefaultMember_StatesTheMembersItStandsFor()
    {
        var work = await WorkAsync("Dim x As Variant|x = r(1)");

        var access = (work.Runtime?.Operations ?? []).First(operation => operation.MemberAccessOpSemanticFlags is { } flags
            && flags.HasFlag(MemberAccessOperationSemanticFlags.DefaultMember) && flags.HasFlag(MemberAccessOperationSemanticFlags.ArgumentList));
        Assert.IsNotNull(access.DefaultMembers, $"{access.Member} {access.MemberAccessOpSemanticFlags}");
        Assert.AreEqual(ItemOrValue, access.DefaultMembers.Value.Single().WrittenAs);
    }

    [TestMethod]
    public async Task ADefaultMemberTheLibraryStatesNothingFor_IsWrittenAsItsName()
    {
        var work = await WorkAsync("Debug.Print c(1)");

        var member = work.Expressions.Select(fact => fact.DefaultMember).OfType<DefaultMemberFact>().First(fact => fact.Name == "Item");
        Assert.IsNull(member.WrittenAs);
    }
}
