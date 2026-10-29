using RDCore.Parsing;
using RDCore.SDK.Model.AST;

namespace RDCore.Tests.Model;

/// <summary>
/// The surface of a module is what the other modules can be told by it: what it declares, and apart from it the names its procedures use and what they
/// declare; not where any of it is written, nor what a procedure does with the names it uses.
/// </summary>
[TestClass]
public sealed class ModuleSurfaceTests
{
    private const string Module = """
        Option Explicit
        Public Const Size As Long = 10
        Public Enum Shade
            Light = 1
            Dark = 2
        End Enum
        Private mCount As Long

        Public Function Twice(ByVal Value As Long, Optional ByVal Times As Long = 2) As Long
            ' twice is twice
            Twice = Value * Times + 0
            mCount = mCount + 1
        End Function

        Public Sub Report()
            Debug.Print "Reported"; Twice(1)
        End Sub
        """;

    private static ModuleSurface? SurfaceOf(string source) => ModuleSurface.Of(new ModuleParser().Parse(TestUri.TestModuleUri(), source));

    private static void AssertSame(string edited)
        => Assert.AreEqual(SurfaceOf(Module), SurfaceOf(edited), "no other module can tell the two apart");

    private static void AssertDeclarationsChanged(string edited)
        => Assert.AreNotEqual(SurfaceOf(Module)!.Declarations, SurfaceOf(edited)!.Declarations);

    [TestMethod]
    public void IsTheSame_ForTheSameText()
        => AssertSame(Module);

    [TestMethod]
    [DataRow("Value * Times + 0", "Value * Times + 1", DisplayName = "a literal")]
    [DataRow("Value * Times + 0", "Value + Times - 0", DisplayName = "an operator")]
    [DataRow("' twice is twice", "' twice is two times", DisplayName = "a comment")]
    [DataRow("\"Reported\"", "\"Done\"", DisplayName = "a string")]
    [DataRow("mCount = mCount + 1", "If mCount < 0 Then mCount = 0", DisplayName = "a statement that uses the same names")]
    public void IsTheSame_ForAChangeOfABody_ThatDeclaresAndNamesNothingNew(string written, string edited)
        => AssertSame(Module.Replace(written, edited));

    [TestMethod]
    public void IsTheSame_WhenADeclarationMoves()
        => AssertSame("\n\n" + Module.Replace("Option Explicit", "Option Explicit\n"));

    [TestMethod]
    [DataRow("Public Function Twice", "Private Function Twice", DisplayName = "a procedure's access")]
    [DataRow("Public Function Twice", "Public Function Double2", DisplayName = "a procedure's name")]
    [DataRow("ByVal Value As Long,", "ByVal Value As String,", DisplayName = "a parameter's type")]
    [DataRow("ByVal Value As Long,", "ByRef Value As Long,", DisplayName = "a parameter's passing")]
    [DataRow("Times As Long = 2", "Times As Long = 3", DisplayName = "an optional parameter's default")]
    [DataRow(") As Long\n", ") As Double\n", DisplayName = "the type a function returns")]
    [DataRow("Size As Long = 10", "Size As Long = 11", DisplayName = "a constant's value")]
    [DataRow("Dark = 2", "Dark = 3", DisplayName = "an enum member's value")]
    [DataRow("Private mCount As Long", "Public mCount As Long", DisplayName = "a field's access")]
    [DataRow("Option Explicit", "Option Private Module", DisplayName = "an option")]
    public void Changes_WithWhatTheModuleDeclares(string written, string edited)
    {
        Assert.Contains(written, Module.ReplaceLineEndings("\n"));
        AssertDeclarationsChanged(Module.ReplaceLineEndings("\n").Replace(written, edited));
    }

    // what a body refers to is what the counts of the other modules' references are made of, and nothing they are bound against.
    [TestMethod]
    [DataRow("Debug.Print \"Reported\"; Twice(1)", "Debug.Print \"Reported\"; Other.Twice(1)", DisplayName = "a member of another module")]
    [DataRow("mCount = mCount + 1", "mCount = mCount + Size", DisplayName = "another name")]
    [DataRow("mCount = mCount + 1", "Dim Local As Long", DisplayName = "a declaration")]
    public void TheReferencesChange_WithTheNamesABodyUses_AndWhatItDeclares_AndTheDeclarationsDoNot(string written, string edited)
    {
        var (before, after) = (SurfaceOf(Module)!, SurfaceOf(Module.Replace(written, edited))!);

        Assert.AreNotEqual(before.References, after.References);
        Assert.AreEqual(before.Declarations, after.Declarations);
    }

    // what a body is made of, besides its statements: nothing outside of the procedure can go to a label or a line number of it, nor see what it declares.
    [TestMethod]
    [DataRow("10 Show\r\n", "10 Show\r\n20 Show\r\n", DisplayName = "a line numbered")]
    [DataRow("10 Show\r\n", "Again: Show\r\n10 Show\r\n", DisplayName = "a label")]
    [DataRow("10 Show\r\n", "Static Count As Long\r\n10 Show\r\n", DisplayName = "a Static local")]
    [DataRow("Const K = 1\r\n10 Show\r\n", "Const K = 2\r\n10 Show\r\n", DisplayName = "a local constant's value")]
    public void WhatABodyIsMadeOf_IsNoDeclaration(string body, string edited)
    {
        const string Header = "Option Explicit\r\nPublic Sub Main()\r\n";
        var (before, after) = (SurfaceOf(Header + body + "End Sub\r\n")!, SurfaceOf(Header + edited + "End Sub\r\n")!);

        Assert.AreEqual(before.Declarations, after.Declarations);
    }

    [TestMethod]
    public void AMembersAttribute_IsADeclaration()
    {
        const string Module = "Public Function Twice()\r\nAttribute Twice.VB_Description = \"Doubles\"\r\nEnd Function\r\n";

        Assert.AreNotEqual(SurfaceOf(Module)!.Declarations, SurfaceOf(Module.Replace("Doubles", "Halves"))!.Declarations);
    }

    // which of the declarations written in #If branches are the module's: what a #Const sets, wherever it is, and what an #If outside of a procedure says.
    [TestMethod]
    [DataRow("#Const A = 1", "#Const A = 0", DisplayName = "a #Const's value")]
    [DataRow("#If A Then", "#If Not A Then", DisplayName = "an #If's condition")]
    [DataRow("    #Const B = 1", "    #Const B = 2", DisplayName = "a #Const in a procedure")]
    [DataRow("        #Const C = 1", "        #Const C = 2", DisplayName = "a #Const in an #If of a procedure")]
    public void TheDirectivesThatSayWhichDeclarationsAreLive_AreDeclarations(string written, string edited)
    {
        var module = string.Join("\r\n", "#Const A = 1", "#If A Then", "Public Sub Bar()", "End Sub", "#End If", "Public Sub Foo()", "    #Const B = 1", "    #If B Then",
            "    Debug.Print 1", "        #Const C = 1", "    #End If", "End Sub", "");

        Assert.Contains(written, module);
        Assert.AreNotEqual(SurfaceOf(module)!.Declarations, SurfaceOf(module.Replace(written, edited))!.Declarations);
    }

    // what an #If in a procedure says is which of its statements are compiled, and what a branch holds is the syntax tree's.
    [TestMethod]
    [DataRow("    #If B Then", "    #If Not B Then", DisplayName = "an #If's condition in a procedure")]
    [DataRow("Debug.Print 1", "Debug.Print 2", DisplayName = "a literal in a branch")]
    public void WhatADirectiveInAProcedureSays_AndWhatABranchHolds_AreNoDeclaration(string written, string edited)
    {
        var module = string.Join("\r\n", "#Const A = 1", "#If A Then", "Public Sub Bar()", "Debug.Print 1", "End Sub", "#End If", "Public Sub Foo()", "    #Const B = 1",
            "    #If B Then", "    Debug.Print 3", "    #End If", "End Sub", "");

        Assert.Contains(written, module);
        Assert.AreEqual(SurfaceOf(module)!.Declarations, SurfaceOf(module.Replace(written, edited))!.Declarations);
    }

    [TestMethod]
    public void IsNull_ForAModuleWithNoSyntaxTree()
        => Assert.IsNull(ModuleSurface.Of(new ModuleParseResult()));

    // the surface tells a node's values of the types it knows, and no other: a node that comes to hold a value of another type - a record that holds a location -
    // is told without it until the surface is taught what of it is told.
    private static readonly Type[] Told =
    [
        typeof(string), typeof(bool), typeof(char), typeof(Enum), typeof(IFormattable), typeof(SDK.Model.Values.Abstract.VBTypedValue), typeof(IEnumerable<string>),
    ];

    private static readonly Type[] WhereNotWhat =
    [
        typeof(SDK.Model.AST.Abstract.SyntaxNode), typeof(SDK.Model.Source.SourceRange), typeof(SDK.Model.Source.SourceLocation), typeof(SDK.Model.AST.Abstract.SyntaxNodeId),
    ];

    private static readonly HashSet<string> Placement = ["Identity", "SourceLocation", "Location", "NameRange", "Children", "EqualityContract"];

    // what of a type the surface cannot tell: none, for a type it tells, a node, or where something is; for a record of the tree's own, what of its values it cannot.
    private static IEnumerable<string> Untold(Type owner, HashSet<Type> seen)
    {
        foreach (var property in owner.GetProperties().Where(property => property.GetIndexParameters().Length == 0 && !Placement.Contains(property.Name)))
        {
            var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            var element = type.IsArray ? type.GetElementType()!
                : type != typeof(string) && type.IsGenericType && typeof(System.Collections.IEnumerable).IsAssignableFrom(type) ? type.GetGenericArguments()[0]
                : type;
            if (Told.Concat(WhereNotWhat).Any(known => known.IsAssignableFrom(element)))
            {
                continue;
            }

            if (element.Namespace?.StartsWith("RDCore.SDK.Model.AST", StringComparison.Ordinal) == true)
            {
                foreach (var untold in seen.Add(element) ? Untold(element, seen) : [])
                {
                    yield return untold;
                }

                continue;
            }

            yield return $"{owner.Name}.{property.Name}: {element.Name}";
        }
    }

    [TestMethod]
    public void EveryValueANodeHolds_IsOfATypeTheSurfaceTells_OrANodeOrWhereItIs()
    {
        var seen = new HashSet<Type>();
        var untold = typeof(SDK.Model.AST.Abstract.SyntaxNode).Assembly.GetTypes()
            .Where(type => typeof(SDK.Model.AST.Abstract.SyntaxNode).IsAssignableFrom(type) && !type.IsAbstract)
            .SelectMany(node => Untold(node, seen))
            .ToList();

        Assert.IsEmpty(untold, string.Join("; ", untold));
    }

    [TestMethod]
    [DataRow("DefInt A-Z", "DefLng A-Z", DisplayName = "a DefType's type")]
    [DataRow("DefInt A-Z", "DefInt A-M", DisplayName = "a DefType's letters")]
    [DataRow("Public Values(1 To 10) As Long", "Public Values(1 To 11) As Long", DisplayName = "an array's bounds")]
    public void Changes_WithWhatAValueOfTheTreeSays(string written, string edited)
    {
        var module = $"Option Explicit\r\n{written}\r\nPublic Sub Foo()\r\nEnd Sub\r\n";

        Assert.AreNotEqual(SurfaceOf(module)!.Declarations, SurfaceOf(module.Replace(written, edited))!.Declarations);
        Assert.AreEqual(SurfaceOf(module), SurfaceOf("\r\n" + module), "and not with where it is");
    }
}
