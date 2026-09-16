using RDCore.Parsing;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Directives;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using System.Text.Json;

namespace RDCore.Tests.Parser;

/// <summary>
/// The declaration pass must never throw. On any input it returns either a usable tree or a
/// <see cref="ModuleParseResult"/> with <c>IsSuccess == false</c> and located syntax errors — never
/// an unhandled exception. A live editor sends malformed / half-typed modules constantly.
/// </summary>
[TestClass]
public sealed class ParserResilienceTests
{
    private static readonly Uri Uri = TestUri.TestModuleUri();

    private static ModuleParseResult Parse(string source)
        => new ModuleParser().Parse(Uri, source);

    [TestMethod]
    // the whole point: none of these — valid, half-typed, or garbage — may throw.
    [DataRow("", DisplayName = "empty")]
    [DataRow("   ", DisplayName = "spaces")]
    [DataRow("\r\n\r\n", DisplayName = "blank lines")]
    [DataRow("' just a comment", DisplayName = "comment only")]
    [DataRow("Option Explicit", DisplayName = "option only")]
    [DataRow("Dim value As", DisplayName = "Dim ... As <missing type>")]
    [DataRow("Dim value As New", DisplayName = "Dim ... As New <missing class>")]
    [DataRow("Private x As", DisplayName = "Private ... As <missing type>")]
    [DataRow("Public Sub Foo(", DisplayName = "unclosed paren")]
    [DataRow("Public Sub", DisplayName = "Sub <missing name>")]
    [DataRow("Public Function", DisplayName = "Function <missing name>")]
    [DataRow("Public Property Get", DisplayName = "Property Get <missing name>")]
    [DataRow("Implements", DisplayName = "Implements <missing type>")]
    [DataRow("Type T", DisplayName = "Type <no body>")]
    [DataRow("Enum E", DisplayName = "Enum <no body>")]
    [DataRow("Public Declare Sub Foo Lib", DisplayName = "Declare ... Lib <missing string>")]
    [DataRow("Public Const BIG = 99999999999999999999999999", DisplayName = "integer literal overflows every type")]
    [DataRow("Public Const N = 99999%", DisplayName = "suffix cannot hold the value")]
    [DataRow("Public Const H = &HFFFFFFFFFFFFFFFFFF", DisplayName = "hex literal overflows Int64")]
    [DataRow("???", DisplayName = "garbage")]
    [DataRow("End Sub", DisplayName = "stray End Sub")]
    [DataRow("Attribute VB_Name", DisplayName = "half-typed attribute")]
    // adversarial review, PRs #208-224: NodeBuilder.PopLastChildren indexed past the end of _children
    // whenever recovery left fewer operands than an operator/argument Exit handler expected — a
    // truncated binary/unary operator, an incomplete argument, or an incomplete Print clause.
    [DataRow("Sub S()\r\na = 1 +\r\nEnd Sub", DisplayName = "truncated binary operator (Add)")]
    [DataRow("Sub S()\r\na = 1 &\r\nEnd Sub", DisplayName = "truncated binary operator (Concat)")]
    [DataRow("Sub S()\r\na = 1 <\r\nEnd Sub", DisplayName = "truncated binary operator (Relational)")]
    [DataRow("Sub S()\r\nFoo x:=\r\nEnd Sub", DisplayName = "named argument with no value")]
    [DataRow("Sub S()\r\nPrint Tab(\r\nEnd Sub", DisplayName = "unclosed Print Tab clause")]
    [DataRow("Sub S()\r\nSelect Case y\r\nCase Is >\r\nEnd Select\r\nEnd Sub", DisplayName = "comparison range clause with no value")]
    [DataRow("Public Const K = 1 +", DisplayName = "truncated operator at module level, no procedure")]
    [DataRow("Sub S()\r\nIf x = Null Then\r\nEnd If\r\nEnd Sub", DisplayName = "relational comparison against Null")]
    // adversarial review, PRs #208-224, item 4: five unguarded null-dereferences, all the same shape -
    // a grammar child recovery left absent, dereferenced without a check.
    [DataRow("Sub S()\r\nRaiseEvent\r\nEnd Sub", DisplayName = "bare RaiseEvent, no event name")]
    [DataRow("Sub S()\r\nFoo.\r\nEnd Sub", DisplayName = "member access, lone trailing dot")]
    [DataRow("Sub S()\r\nWith Foo\r\nx = .\r\nEnd With\r\nEnd Sub", DisplayName = "with-relative member access, lone dot")]
    [DataRow("Sub S()\r\nFoo!\r\nEnd Sub", DisplayName = "dictionary access, lone trailing bang")]
    [DataRow("Sub S()\r\nWith Foo\r\nx = !\r\nEnd With\r\nEnd Sub", DisplayName = "with-relative dictionary access, lone bang")]
    [DataRow("Sub S()\r\nDo While x", DisplayName = "Do whose body never recovers")]
    [DataRow("Sub S()\r\nOn Local Error Resume\r\nEnd Sub", DisplayName = "On Error Resume missing Next")]
    public void NeverThrows_AndAnyErrorIsLocated(string source)
    {
        ModuleParseResult result = null!;
        var thrown = Record(() => result = Parse(source));

        Assert.IsNull(thrown, $"parsing threw {thrown?.GetType().Name}: {thrown?.Message}");
        Assert.IsTrue(
            result.SyntaxErrors.All(error => error.Location.Uri == Uri),
            "every syntax error must be located in the parsed document");
    }

    [TestMethod]
    // the flagged NRE: an As-type clause with no type token.
    [DataRow("Dim value As")]
    [DataRow("Private x As")]
    [DataRow("Public Sub Foo(")]
    [DataRow("???")]
    public void MalformedInput_DegradesToLocatedFailure(string source)
    {
        var result = Parse(source);

        Assert.IsFalse(result.IsSuccess);
        Assert.IsNotEmpty(result.SyntaxErrors);
    }

    [TestMethod]
    public void SyntaxError_LocatesToTheZeroBasedPhysicalLine()
        // regression: ErrorListener.SyntaxError passed ANTLR's 1-based line straight through, landing
        // every diagnostic one line below the token that actually caused it.
    {
        var result = Parse("Option Explicit\r\nPublic Sub Foo(\r\n");

        Assert.IsFalse(result.IsSuccess);
        Assert.IsNotEmpty(result.SyntaxErrors);
        // "Public Sub Foo(" is physical line 2 (1-based) -> zero-based line 1.
        Assert.AreEqual(1, result.SyntaxErrors[0].Location.Range.Start.Line);
    }

    [TestMethod]
    // valid VBA the declaration pass used to reject — casing, culture, empty forms.
    [DataRow("public Sub Foo()\r\nEnd Sub", DisplayName = "lowercase visibility keyword")]
    [DataRow("PRIVATE Function F() As Long\r\nEnd Function", DisplayName = "uppercase visibility keyword")]
    [DataRow("fRiEnD Property Get P()\r\nEnd Property", DisplayName = "mixed-case visibility keyword")]
    [DataRow("' just a comment", DisplayName = "comment only")]
    [DataRow("\r\n", DisplayName = "blank")]
    [DataRow("#Const RDDEBUG = 1.5\r\n#If RDDEBUG Then\r\nPublic X As Long\r\n#End If", DisplayName = "#Const float literal")]
    [DataRow("Public Const Big = 3000000000", DisplayName = "unsuffixed integer past Long")]
    public void ParsesValidVbaClean(string source)
    {
        var result = Parse(source);

        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.IsEmpty ? "" : result.SyntaxErrors[0].Description);
        Assert.IsNotNull(result.SyntaxTree);
    }

    [TestMethod]
    public void HalfTypedMember_StillContributesTheGoodDeclarations()
    {
        // A1/A2: a member header with no name yet used to throw in the builder and, via the catch,
        // empty the whole module. The three good members must survive.
        const string source = """
            Option Explicit
            Public Const A = 1
            Public Sub Foo()
            End Sub
            Public Function Bar() As Long
            End Function
            Public Sub
            """;

        var result = Parse(source);

        Assert.IsFalse(result.IsSuccess);
        Assert.IsNotNull(result.SyntaxTree);
        var members = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Select(m => m.Name).ToArray();
        CollectionAssert.Contains(members, "Foo");
        CollectionAssert.Contains(members, "Bar");
        Assert.ContainsSingle(result.SyntaxTree.Children.OfType<ConstantDeclarationNode>().Where(c => c.Name == "A"));
    }

    [TestMethod]
    // adversarial review #208-224, item 1's own measured repro: a truncated operator expression mid-body
    // (`a = 1 +`) threw inside PopLastChildren under recovery, and ModuleParser's outer catch salvaged
    // whatever had been built *before* the throw — silently dropping every member and local that hadn't
    // been visited yet, not just the broken statement. All the surrounding, well-formed declarations
    // must survive a truncated statement in one unrelated procedure.
    public void TruncatedOperator_DoesNotLoseSiblingMembersOrLocals()
    {
        const string source = """
            Option Explicit
            Public Field1 As Long
            Public Const K = 1
            Sub Alpha()
                Dim local1 As Long
                Dim local2 As Long
                a = 1 +
            End Sub
            Function Beta() As Long
                Dim local3 As Long
            End Function
            """;

        var result = Parse(source);

        Assert.IsFalse(result.IsSuccess);
        Assert.IsNotNull(result.SyntaxTree);
        var members = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().ToArray();
        var alpha = members.SingleOrDefault(m => m.Name == "Alpha");
        var beta = members.SingleOrDefault(m => m.Name == "Beta");
        Assert.IsNotNull(alpha, "Sub Alpha must survive a truncated statement inside its own body");
        Assert.IsNotNull(beta, "Function Beta, declared after the truncated statement, must survive");
        Assert.HasCount(2, alpha!.Children.OfType<VariableDeclarationNode>(), "Alpha's own locals must survive");
        Assert.ContainsSingle(beta!.Children.OfType<VariableDeclarationNode>());
        CollectionAssert.Contains(
            result.SyntaxTree.Children.OfType<VariableDeclarationNode>().Select(v => v.Name).ToArray(), "Field1");
        Assert.ContainsSingle(result.SyntaxTree.Children.OfType<ConstantDeclarationNode>().Where(c => c.Name == "K"));
    }

    [TestMethod]
    // adversarial review, PRs #208-224, "worth knowing": a bare `On Error Resume` (no `Next`) got a
    // real syntax error AND a fabricated OnErrorResumeStatementNode as if "Next" had been typed - not
    // just "never throws", the tree actively lied about what the source said. Root cause: ANTLR invokes
    // this Exit callback a SECOND time after recovering from the missing-token InputMismatchException,
    // with a synthesized `<missing NEXT>` token standing in for the real one - a plain null-check on
    // NEXT() doesn't see the difference; only Symbol.TokenIndex (-1 for anything not actually lexed)
    // does. Confirmed against the pre-fix listener via `git stash` before writing this test.
    public void OnErrorResumeMissingNext_BuildsUnbuiltTrivia_NotAFabricatedResumeNext()
    {
        var result = Parse("Sub S()\r\nOn Local Error Resume\r\nEnd Sub");

        Assert.IsFalse(result.IsSuccess);
        var alpha = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        CollectionAssert.DoesNotContain(alpha.Children.Select(c => c.GetType()).ToArray(), typeof(OnErrorResumeStatementNode));
        // author correction mid-round: "build nothing" is itself the wrong fallback for a recognized-
        // but-unbuilt construct - the source text must stay reconstructable.
        var trivia = Assert.IsInstanceOfType<UnbuiltStatementTriviaNode>(alpha.Children.Single());
        StringAssert.Contains(trivia.Source, "Resume");
    }

    [TestMethod]
    [DataRow("Sub S()\r\nOn Error Resume Next\r\nEnd Sub", DisplayName = "On Error Resume Next")]
    [DataRow("Sub S()\r\nOn Error GoTo Handler\r\nHandler:\r\nEnd Sub", DisplayName = "On Error GoTo <label>")]
    public void OnErrorStmt_StillBuildsTheRightNode_WhenComplete(string source)
    {
        var result = Parse(source);

        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.IsEmpty ? "" : result.SyntaxErrors[0].Description);
        var alpha = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        Assert.ContainsSingle(alpha.Children.Where(c => c is OnErrorResumeStatementNode or OnErrorGoToStatementNode));
    }

    [TestMethod]
    // A3: names that are grammar keywords or bracketed foreign names used to null-deref
    // IDENTIFIER().Symbol and fail the whole module.
    [DataRow("Dim Name As String", "Name")]
    [DataRow("Private Text As String", "Text")]
    [DataRow("Public Const Version As String = \"1\"", "Version")]
    // brackets are escape syntax, not part of the name.
    [DataRow("Dim [My Var] As Long", "My Var")]
    public void KeywordOrBracketedName_ParsesWithThatName(string source, string expectedName)
    {
        var result = Parse("Option Explicit\r\n" + source);

        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.IsEmpty ? "" : result.SyntaxErrors[0].Description);
        var declared = result.SyntaxTree!.Children
            .Where(node => node is VariableDeclarationNode or ConstantDeclarationNode)
            .Select(node => node is VariableDeclarationNode v ? v.Name : ((ConstantDeclarationNode)node).Name)
            .ToArray();
        CollectionAssert.Contains(declared, expectedName);
    }

    [TestMethod]
    // B2: a value assertion — the modifier must bind, not fall through to Implicit, for every casing.
    [DataRow("Public Sub S()\r\nEnd Sub", AccessModifier.Public)]
    [DataRow("public Sub S()\r\nEnd Sub", AccessModifier.Public)]
    [DataRow("PRIVATE Sub S()\r\nEnd Sub", AccessModifier.Private)]
    [DataRow("Friend Function F()\r\nEnd Function", AccessModifier.Friend)]
    [DataRow("Sub S()\r\nEnd Sub", AccessModifier.Implicit)]
    public void Visibility_BindsToTheModifier(string source, AccessModifier expected)
    {
        var member = Parse(source).SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        Assert.AreEqual(expected, member.AccessModifier);
    }

    [TestMethod]
    // A4: Option Base and line-number labels ran an unguarded int.Parse. The grammar's numberLiteral
    // admits hex/oct/float tokens and a type-hint suffix, and a value can overflow Int32 — none of
    // which is a line number or Option Base 1, and none of which may throw.
    [DataRow("Option Base 1&", DisplayName = "Option Base, type-hint suffix")]
    [DataRow("Option Base &H1", DisplayName = "Option Base, hex literal")]
    [DataRow("Option Base 99999999999", DisplayName = "Option Base, overflows Int32")]
    [DataRow("Sub S()\r\n99999999999 X = 1\r\nEnd Sub", DisplayName = "line number overflows Int32")]
    [DataRow("Sub S()\r\n10& X = 1\r\nEnd Sub", DisplayName = "line number, type-hint suffix")]
    [DataRow("Sub S()\r\n&HFF X = 1\r\nEnd Sub", DisplayName = "hex token as a line label")]
    [DataRow("Sub S()\r\n1.5 X = 1\r\nEnd Sub", DisplayName = "float token as a line label")]
    [DataRow("Sub S()\r\n-5 X = 1\r\nEnd Sub", DisplayName = "signed line label")]
    public void OptionBaseAndLineLabels_NeverThrow(string source)
    {
        ModuleParseResult result = null!;
        var thrown = Record(() => result = Parse(source));

        Assert.IsNull(thrown, $"parsing threw {thrown?.GetType().Name}: {thrown?.Message}");
        Assert.IsTrue(
            result.SyntaxErrors.All(error => error.Location.Uri == Uri),
            "every syntax error must be located in the parsed document");
    }

    [TestMethod]
    // the bare, valid forms still bind.
    [DataRow("Option Base 1", ModuleOptions.OptionBase1)]
    [DataRow("Option Base 0", ModuleOptions.OptionBase0)]
    public void OptionBase_BindsTheBareValue(string source, ModuleOptions expected)
    {
        var directive = Parse(source).SyntaxTree!.Children.OfType<ModuleOptionDirectiveNode>().Single();
        Assert.AreEqual(expected, directive.ModuleOption);
    }

    [TestMethod]
    // A4: a parameter is an `arg : … unrestrictedIdentifier …`, so its name can be a reserved word.
    [DataRow("Next")]
    [DataRow("Error")]
    [DataRow("Name")]
    public void KeywordNamedParameter_ParsesWithThatName(string keyword)
    {
        var result = Parse($"Public Sub Foo(ByVal {keyword} As Long)\r\nEnd Sub");

        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.IsEmpty ? "" : result.SyntaxErrors[0].Description);
        var parameter = Flatten(result.SyntaxTree!).OfType<ParameterDeclarationNode>().Single();
        Assert.AreEqual(keyword, parameter.Name);
    }

    [TestMethod]
    // F4: ANTLR's DefaultErrorStrategy inserts synthetic "<missing X>" tokens on recovery; none of
    // them may be stored in the AST as a name, a library, or a type.
    [DataRow("Public Declare Sub Foo Lib", DisplayName = "Declare ... Lib <missing string>")]
    [DataRow("Public Declare Function F Lib \"k\" Alias", DisplayName = "Alias <missing string>")]
    [DataRow("Option Explicit\r\nDim a As, b As Long", DisplayName = "As-clause list, missing first type")]
    [DataRow("Option Explicit\r\nDim a As New", DisplayName = "As New <missing class>")]
    public void RecoveryPlaceholders_NeverLeakIntoTheAst(string source)
    {
        var result = Parse(source);

        if (result.SyntaxTree is null)
        {
            return; // fully degraded — nothing was built, nothing leaked
        }

        var json = JsonSerializer.Serialize(result.SyntaxTree);
        Assert.IsFalse(
            json.Contains("<missing ", StringComparison.Ordinal),
            $"an ANTLR recovery placeholder leaked into the AST: {json}");
        Assert.IsFalse(
            Flatten(result.SyntaxTree).OfType<AsTypeExpressionNode>().Any(node => node.TypeName is "New" or ""),
            "`As New` with no class name must not yield an As-type node");
        Assert.IsFalse(
            Flatten(result.SyntaxTree).OfType<ExternalMemberDeclarationNode>().Any(node => node.Library.Contains('<')),
            "a Declare with a half-typed Lib string must not keep a placeholder library name");
    }

    [TestMethod]
    public void ValidDeclare_StillKeepsItsLibraryAndAlias()
    {
        var result = Parse("Public Declare PtrSafe Sub Beep Lib \"kernel32\" Alias \"BeepA\" (ByVal x As Long)");

        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.IsEmpty ? "" : result.SyntaxErrors[0].Description);
        var declare = Flatten(result.SyntaxTree!).OfType<ExternalMemberDeclarationNode>().Single();
        StringAssert.Contains(declare.Library, "kernel32");
        StringAssert.Contains(declare.Alias!, "BeepA");
    }

    [TestMethod]
    // C8: a parameter name carrying a type-declaration character keeps the name, not the hint.
    [DataRow("count%", "count")]
    [DataRow("name$", "name")]
    [DataRow("amount@", "amount")]
    public void TypedParameterName_DropsTheHintChar(string declared, string expectedName)
    {
        var result = Parse($"Public Sub Foo(ByVal {declared})\r\nEnd Sub");

        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.IsEmpty ? "" : result.SyntaxErrors[0].Description);
        var parameter = Flatten(result.SyntaxTree!).OfType<ParameterDeclarationNode>().Single();
        Assert.AreEqual(expectedName, parameter.Name);
    }

    [TestMethod]
    public void ValidLineNumberLabel_StillContributesALineNumberNode()
    {
        var result = Parse("Sub S()\r\n100: X = 1\r\nEnd Sub");

        var lineNumbers = Flatten(result.SyntaxTree!).OfType<LineNumberNode>().ToArray();
        Assert.ContainsSingle(lineNumbers);
        Assert.AreEqual(100, lineNumbers[0].Number);
    }

    private static IEnumerable<SyntaxNode> Flatten(SyntaxNode node)
    {
        yield return node;
        foreach (var child in node.Children)
        {
            foreach (var descendant in Flatten(child))
            {
                yield return descendant;
            }
        }
    }

    [TestMethod]
    public void DeepNesting_DoesNotCrashTheProcess()
    {
        // A5: pathological nesting recurses through the expression rule to an uncatchable stack
        // overflow. EnterEveryRule's stack guard converts it to a catchable, located failure.
        var source = "Public Const X = " + new string('(', 2000) + "1" + new string(')', 2000);

        // parse on a deliberately small stack: whether 2000 frames exhausts the default stack is
        // platform-dependent (it does on Windows, not on the Linux CI). 256 KB makes the guard fire
        // everywhere, so the invariant under test — no uncatchable crash — is what's asserted.
        ModuleParseResult? result = null;
        Exception? thrown = null;
        var worker = new Thread(
            () =>
            {
                try
                {
                    result = Parse(source);
                }
                catch (Exception exception)
                {
                    thrown = exception;
                }
            },
            maxStackSize: 256 * 1024);
        worker.Start();
        worker.Join();

        Assert.IsNull(thrown, $"parsing threw {thrown?.GetType().Name}");
        Assert.IsNotNull(result);
        Assert.IsFalse(result!.IsSuccess);
    }

    [TestMethod]
    public void FailedParse_StillCarriesPrecompilerTrivia()
    {
        // a #Const so there is precompiler trivia to preserve, then a construct that fails the
        // declaration pass — the trivia must survive the failure path.
        const string source = """
            #Const RDDEBUG = 1
            #If RDDEBUG Then
            #End If
            Public Property Get
            """;

        var result = Parse(source);

        Assert.IsFalse(result.IsSuccess);
        Assert.IsNotEmpty(result.SyntaxErrors);
        Assert.IsTrue(result.SyntaxErrors.All(error => error.Location.Uri == Uri));
        Assert.IsNotEmpty(result.PrecompilerTrivia);
    }

    [TestMethod]
    // adversarial review #208-224, item 3: `If x = Null Then` corrupted the declaration listener and
    // took the whole module with it — same PopLastChildren-underflow root cause as item 1, verified
    // separately here because "never throws" alone doesn't catch a listener failure ModuleParser's
    // outer catch already salvages into a reported (but wrongly emptied) module.
    public void NullComparison_DoesNotLoseTheEnclosingMember()
    {
        var result = Parse("Public Sub Foo()\r\nIf x = Null Then\r\nEnd If\r\nEnd Sub");

        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.IsEmpty ? "" : result.SyntaxErrors[0].Description);
        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        Assert.AreEqual("Foo", member.Name);
    }

    [TestMethod]
    public void EmptyModule_IsSuccessWithAnEmptyTree()
    {
        var result = Parse("");

        Assert.IsTrue(result.IsSuccess);
        Assert.IsNotNull(result.SyntaxTree);
        Assert.IsEmpty(result.SyntaxTree!.Children);
    }

    [TestMethod]
    public void CommentsOnlyModule_IsSuccess()
    {
        var result = Parse("' header\r\n' more\r\n");

        Assert.IsTrue(result.IsSuccess);
        Assert.IsEmpty(result.SyntaxTree!.Children.OfType<MemberDeclarationNode>());
    }

    private static Exception? Record(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}
