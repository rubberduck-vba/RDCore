using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.Symbols.Abstract;

namespace RDCore.SDK.Model.Symbols.Completion;

/// <summary>
/// What the module a completion list is asked for in is, that decides what can be written in it.
/// </summary>
/// <param name="IsClassModule">Whether it is a class module: <c>Me</c>, <c>Event</c>, <c>RaiseEvent</c>, <c>Implements</c> and <c>WithEvents</c> are written in one only.</param>
/// <param name="HasBarePrint">Whether the language has a bare <c>Print</c> statement (<see cref="Workspace.SupportedLanguage.HasBarePrint"/>).</param>
public sealed record class CompletionLanguage(bool IsClassModule = false, bool HasBarePrint = false);

/// <summary>
/// The names and the keywords a completion list offers for a name that is being written, by what is being written and where (<strong>MS-VBAL §5.2</strong> for
/// the declarations of a module, <strong>§5.4</strong> for the statements of a procedure, <strong>§5.6</strong> for the expressions in them).
/// </summary>
/// <remarks>
/// <para>
/// A keyword is offered where it can be written, and nowhere else. A declaration is written in the declarations section, a statement in a procedure, and the
/// statement that ends a block - or that can only be in one, <c>Exit For</c>, <c>Case</c>, <c>ElseIf</c> - in that block. An operand is a name or a keyword that
/// starts an expression; after an operand that is complete comes an operator, or the keyword that goes on with the statement (<c>Then</c>, <c>To</c>, <c>In</c>):
/// no name can be written there, and none is offered.
/// </para>
/// <para>
/// A statement that declares a name is offered nothing where the name is written - it is a new one - and the keyword that goes on with the declaration after it
/// (<c>As</c>). The statements that name something of their own kind are offered that kind: a <c>GoTo</c> the labels of the procedure, a <c>RaiseEvent</c> the
/// events of the class, an <c>Implements</c> the classes.
/// </para>
/// </remarks>
public static class CompletionRules
{
    private static readonly string[] DeclarationKeywords = ["Const", "Declare", "Dim", "Enum", "Function", "Global", "Private", "Property", "Public", "Static", "Sub", "Type"];
    private static readonly string[] ClassDeclarationKeywords = ["Event", "Friend", "Implements"];

    private static readonly string[] StatementKeywords =
    [
        "Call", "Close", "Const", "Debug", "Dim", "Do", "End", "Erase", "Error", "For", "Get", "GoSub", "GoTo", "If", "Input", "Let", "Line", "Lock", "LSet", "Mid",
        "On", "Open", "Put", "ReDim", "Rem", "Resume", "Return", "RSet", "Seek", "Select", "Set", "Static", "Stop", "Unlock", "While", "With", "Write",
    ];

    private static readonly string[] OperandKeywords = ["Empty", "False", "New", "Not", "Nothing", "Null", "True", "TypeOf"];
    private static readonly string[] OperatorKeywords = ["And", "Eqv", "Imp", "Is", "Like", "Mod", "Or", "Xor"];

    private const int NearRank = 0;
    private const int KeywordRank = 4;

    /// <summary>
    /// What can be written as the name at <paramref name="site"/> (<see cref="CompletionSiteKind.Name"/>).
    /// </summary>
    /// <param name="tree">The scope tree of the composed workspace.</param>
    /// <param name="origin">Where the lookup originates (<see cref="CompletionCandidates.OriginAt"/>).</param>
    /// <param name="site">What is being written.</param>
    /// <param name="placement">Where the line is in its module.</param>
    /// <param name="language">What the module is.</param>
    public static IReadOnlyList<CompletionCandidate> At(ScopeTree tree, Uri origin, CompletionSite site, CompletionPlacement placement, CompletionLanguage language)
    {
        var tokens = site.Tokens;
        if (tokens.Count == 0)
        {
            return placement.IsInProcedure ? StatementStart(tree, origin, placement, language) : DeclarationStart(placement, language);
        }

        return Statement(tree, origin, site, tokens, placement, language) ?? Expression(tree, origin, site, tokens, language);
    }

    // a declaration of the module's declarations section: what can be declared there, and nothing else is written there.
    private static IReadOnlyList<CompletionCandidate> DeclarationStart(CompletionPlacement placement, CompletionLanguage language)
    {
        IEnumerable<string> keywords = [.. DeclarationKeywords, .. language.IsClassModule ? ClassDeclarationKeywords : []];
        if (!placement.FollowsDeclarations)
        {
            keywords = keywords.Prepend("Option");
        }

        return Keywords(keywords);
    }

    // the start of a statement in a procedure: the names a call or an assignment starts with, the statements, and the ones the blocks the line is in have.
    private static IReadOnlyList<CompletionCandidate> StatementStart(ScopeTree tree, Uri origin, CompletionPlacement placement, CompletionLanguage language)
    {
        var block = new List<string>();
        foreach (var kind in placement.Blocks)
        {
            block.AddRange(kind switch
            {
                CompletionBlock.For or CompletionBlock.ForEach => ["Next", "Exit For"],
                CompletionBlock.Do => ["Loop", "Exit Do"],
                CompletionBlock.While => ["Wend"],
                CompletionBlock.With => ["End With"],
                CompletionBlock.Select => ["Case", "End Select"],
                CompletionBlock.If => ["Else", "ElseIf", "End If"],
                _ => [],
            });
        }

        block.AddRange(ExitAndEndOf(placement.Procedure));

        IEnumerable<string> statements = [.. StatementKeywords, .. When(language.HasBarePrint, "Print"), .. When(language.IsClassModule, "RaiseEvent", "Me")];
        return
        [
            .. block.Distinct(StringComparer.OrdinalIgnoreCase).Select(keyword => new CompletionCandidate(keyword, CompletionItemKind.Keyword, Rank: NearRank)),
            .. CompletionCandidates.Names(tree, origin),
            .. Keywords(statements.Except(block, StringComparer.OrdinalIgnoreCase)),
        ];
    }

    private static IEnumerable<string> ExitAndEndOf(CompletionProcedure? procedure) => procedure switch
    {
        CompletionProcedure.Sub => ["Exit Sub", "End Sub"],
        CompletionProcedure.Function => ["Exit Function", "End Function"],
        CompletionProcedure.Property => ["Exit Property", "End Property"],
        _ => [],
    };

    // the statements whose words say what comes next; null for a statement that is an expression from here on.
    private static IReadOnlyList<CompletionCandidate>? Statement(
        ScopeTree tree, Uri origin, CompletionSite site, IReadOnlyList<string> tokens, CompletionPlacement placement, CompletionLanguage language)
    {
        var first = tokens[0];
        var last = tokens[^1];
        var count = tokens.Count;

        // a declaration: the modifiers, the name it declares, and the keyword that goes on with it.
        if (IsDeclaration(tokens, placement, language, out var declared))
        {
            return declared;
        }

        if (first.Is("Option"))
        {
            return count switch
            {
                1 => Keywords(["Base", "Compare", "Explicit", "Private"]),
                2 when last.Is("Compare") => Keywords(["Binary", "Database", "Text"]),
                2 when last.Is("Private") => Keywords(["Module"]),
                _ => [],
            };
        }

        if (first.Is("Exit") && count == 1)
        {
            IEnumerable<string> exits =
            [
                .. When(placement.IsIn(CompletionBlock.For) || placement.IsIn(CompletionBlock.ForEach), "For"),
                .. When(placement.IsIn(CompletionBlock.Do), "Do"),
                .. ExitAndEndOf(placement.Procedure).Where(keyword => keyword.StartsWith("Exit", StringComparison.Ordinal)).Select(keyword => keyword[5..]),
            ];
            return Keywords(exits);
        }

        if (first.Is("End") && count == 1)
        {
            IEnumerable<string> ends =
            [
                .. When(placement.IsIn(CompletionBlock.If), "If"),
                .. When(placement.IsIn(CompletionBlock.Select), "Select"),
                .. When(placement.IsIn(CompletionBlock.With), "With"),
                .. ExitAndEndOf(placement.Procedure).Where(keyword => keyword.StartsWith("End", StringComparison.Ordinal)).Select(keyword => keyword[4..]),
            ];
            return Keywords(ends);
        }

        if (first.Is("GoTo") || first.Is("GoSub"))
        {
            return count == 1 ? Labels(placement) : [];
        }

        if (first.Is("Resume"))
        {
            return count == 1 ? [.. Keywords(["Next"]), .. Labels(placement)] : [];
        }

        if (first.Is("On"))
        {
            return tokens switch
            {
                [_, var error] when error.Is("Error") => Keywords(["GoTo", "Resume"]),
                [_, var error, var goTo] when error.Is("Error") && goTo.Is("GoTo") => [.. Keywords(["0"]), .. Labels(placement)],
                [_, var error, var resume] when error.Is("Error") && resume.Is("Resume") => Keywords(["Next"]),
                [_, ..] when tokens.Any(token => token.Is("GoTo") || token.Is("GoSub")) && (last.Is("GoTo") || last.Is("GoSub") || last == ",") => Labels(placement),
                [_, var after] when !after.Is("Error") && CompletionStatement.Expectation(tokens) == CompletionExpectation.Operator => Keywords(["GoSub", "GoTo"]),
                _ => null,
            };
        }

        if (first.Is("RaiseEvent"))
        {
            return count == 1 ? CompletionCandidates.EventsOf(tree, origin) : null;
        }

        if (first.Is("Implements"))
        {
            return count == 1 ? CompletionCandidates.Classes(tree, origin) : [];
        }

        if (first.Is("Do") || first.Is("Loop"))
        {
            return count == 1 ? Keywords(["Until", "While"]) : null;
        }

        if (first.Is("Select"))
        {
            return count == 1 ? Keywords(["Case"]) : null;
        }

        if (first.Is("Case") && count == 1)
        {
            return [.. Keywords(["Else", "Is"]), .. Operand(tree, origin, language, tokens)];
        }

        if (first.Is("ReDim") && count == 1)
        {
            return [.. Keywords(["Preserve"]), .. CompletionCandidates.Names(tree, origin)];
        }

        if (first.Is("For"))
        {
            return ForStatement(tree, origin, tokens, language);
        }

        if (first.Is("Open") && last.Is("For"))
        {
            return Keywords(["Append", "Binary", "Input", "Output", "Random"]);
        }

        // `TypeOf x Is |` names a type.
        if (last.Is("Is") && tokens.Any(token => token.Is("TypeOf")))
        {
            return CompletionCandidates.Classes(tree, origin);
        }

        return null;
    }

    // `For |` is the loop's variable or Each; `For Each x |` goes on with In; `For i = 1 |` with To, and `For i = 1 To 9 |` with Step.
    private static IReadOnlyList<CompletionCandidate>? ForStatement(ScopeTree tree, Uri origin, IReadOnlyList<string> tokens, CompletionLanguage language)
    {
        if (tokens.Count == 1)
        {
            return [.. Keywords(["Each"]), .. CompletionCandidates.Names(tree, origin)];
        }

        var isEach = tokens[1].Is("Each");
        if (isEach && tokens.Count == 2)
        {
            return CompletionCandidates.Names(tree, origin);
        }

        if (isEach && tokens.Count == 3)
        {
            return Keywords(["In"]);
        }

        if (CompletionStatement.Expectation(tokens) != CompletionExpectation.Operator)
        {
            return null;
        }

        var next = tokens.Any(token => token.Is("Step")) ? [] : tokens.Any(token => token.Is("To")) ? ["Step"] : tokens.Contains("=") ? new[] { "To" } : [];
        return [.. Keywords(next), .. Operators()];
    }

    // a declaration: `Dim`, `Const`, `Static`, a modifier, `Sub`, `Function`, `Property`, `Type`, `Enum`, `Declare`, `Event`, and their parameter lists.
    private static bool IsDeclaration(IReadOnlyList<string> tokens, CompletionPlacement placement, CompletionLanguage language, out IReadOnlyList<CompletionCandidate> offered)
    {
        offered = [];
        var index = 0;
        while (index < tokens.Count && (tokens[index].Is("Public") || tokens[index].Is("Private") || tokens[index].Is("Friend") || tokens[index].Is("Global")
            || tokens[index].Is("Static") && !placement.IsInProcedure))
        {
            index++;
        }

        // a variable of a class module's declarations section can handle the events of its object (MS-VBAL §5.2.3.1.3).
        var withEvents = !placement.IsInProcedure && language.IsClassModule;
        if (index == tokens.Count)
        {
            // only modifiers so far: what they can declare - a variable's name is new, and nothing is offered for it.
            offered = Keywords(["Const", "Declare", "Enum", "Function", "Property", "Sub", "Type", .. language.IsClassModule ? ["Event", "WithEvents"] : Array.Empty<string>()]);
            return true;
        }

        var keyword = tokens[index];
        var rest = tokens.Skip(index + 1).ToList();
        if (keyword.Is("Property") && rest.Count == 0)
        {
            offered = Keywords(["Get", "Let", "Set"]);
            return true;
        }

        if (IsProcedureKeyword(keyword) || keyword.Is("Declare") || keyword.Is("Event"))
        {
            offered = ProcedureHeader(keyword, rest);
            return true;
        }

        if (keyword.Is("Type") || keyword.Is("Enum"))
        {
            return true;
        }

        if (keyword.Is("Dim") || keyword.Is("Static") || keyword.Is("Const") || keyword.Is("WithEvents"))
        {
            return Declarator(keyword.Is("WithEvents") ? tokens.Skip(index).ToList() : rest, keyword.Is("Const"), withEvents && !keyword.Is("Const"), out offered);
        }

        // a modifier alone declares the variables after it: `Private Total As Long`.
        return index > 0 && CompletionStatement.IsWord(keyword) && Declarator(tokens.Skip(index).ToList(), constant: false, withEvents, out offered);
    }

    private static bool IsProcedureKeyword(string word) => word.Is("Sub") || word.Is("Function") || word.Is("Property");

    // the declarators of a Dim, after its keyword: nothing where a name is written, As after one, nothing after its type, and an operand after a constant's `=`.
    private static bool Declarator(IReadOnlyList<string> tokens, bool constant, bool withEvents, out IReadOnlyList<CompletionCandidate> offered)
    {
        offered = [];
        // what is after the last `,` that is not in an array's bounds is the declarator being written.
        var depth = 0;
        var start = 0;
        for (var index = 0; index < tokens.Count; index++)
        {
            depth += tokens[index] == "(" ? 1 : tokens[index] == ")" ? -1 : 0;
            if (depth == 0 && tokens[index] == ",")
            {
                start = index + 1;
            }
        }

        var declarator = tokens.Skip(start).ToList();
        if (depth > 0)
        {
            // in the bounds of an array: an expression, or `To`.
            return false;
        }

        if (declarator.Count == 0 || declarator is [var only] && only.Is("WithEvents"))
        {
            // a name is being declared: there is nothing to offer for a new name but WithEvents, which a module-level variable of a class can be.
            offered = declarator.Count == 0 && withEvents ? Keywords(["WithEvents"]) : [];
            return true;
        }

        if (constant && declarator.Contains("="))
        {
            return false;
        }

        var last = declarator[^1];
        offered = CompletionStatement.IsWord(last) && (declarator.Count == 1 || declarator[^2].Is("WithEvents")) || last == ")" ? Keywords(["As"]) : [];
        return true;
    }

    // the header of a procedure: its name is a new one, its parameters are declared with their modifiers, and As goes on after a parameter or the list.
    private static IReadOnlyList<CompletionCandidate> ProcedureHeader(string keyword, IReadOnlyList<string> rest)
    {
        if (keyword.Is("Declare"))
        {
            if (rest.Count == 0)
            {
                return Keywords(["Function", "PtrSafe", "Sub"]);
            }

            if (rest is [var ptrSafe] && ptrSafe.Is("PtrSafe"))
            {
                return Keywords(["Function", "Sub"]);
            }

            var named = rest.SkipWhile(token => token.Is("PtrSafe")).ToList();
            if (named.Count == 2 && !named.Contains("("))
            {
                return Keywords(["Lib"]);
            }

            if (named.Count >= 3 && named[^1] == "\"\"" && named[^2].Is("Lib"))
            {
                return Keywords(["Alias"]);
            }

            rest = named.Skip(1).ToList();
        }

        if (keyword.Is("Property") && rest.Count > 0 && (rest[0].Is("Get") || rest[0].Is("Let") || rest[0].Is("Set")))
        {
            rest = rest.Skip(1).ToList();
        }

        var opened = rest.ToList().IndexOf("(");
        if (opened < 0)
        {
            return [];
        }

        var depth = 0;
        var parameter = new List<string>();
        foreach (var token in rest.Skip(opened))
        {
            depth += token == "(" ? 1 : token == ")" ? -1 : 0;
            if (token is "(" or "," && depth == 1)
            {
                parameter.Clear();
                continue;
            }

            if (depth == 0 && token == ")")
            {
                // the parameter list is closed: a Function and a Property Get have a type.
                return keyword.Is("Sub") || keyword.Is("Event") ? [] : Keywords(["As"]);
            }

            parameter.Add(token);
        }

        var modifiers = parameter.TakeWhile(token => token.Is("Optional") || token.Is("ByVal") || token.Is("ByRef") || token.Is("ParamArray")).ToList();
        var name = parameter.Skip(modifiers.Count).ToList();
        if (name.Count == 0)
        {
            IEnumerable<string> allowed = modifiers.Count == 0 ? ["ByRef", "ByVal", "Optional", "ParamArray"]
                : modifiers is [var optional] && optional.Is("Optional") ? ["ByRef", "ByVal"]
                : [];
            return Keywords(allowed);
        }

        return name.Count == 1 || name[^1] == ")" ? Keywords(["As"]) : [];
    }

    // an expression: the names and the keywords that start an operand where one comes, and the operators where one is complete.
    private static IReadOnlyList<CompletionCandidate> Expression(
        ScopeTree tree, Uri origin, CompletionSite site, IReadOnlyList<string> tokens, CompletionLanguage language)
    {
        if (site.Expectation != CompletionExpectation.Operator)
        {
            return Operand(tree, origin, language, tokens);
        }

        var first = tokens[0];
        IEnumerable<string> next = (first.Is("If") || first.Is("ElseIf")) && !tokens.Any(token => token.Is("Then")) ? ["Then"] : [];
        return [.. Keywords(next), .. Operators()];
    }

    private static IReadOnlyList<CompletionCandidate> Operand(ScopeTree tree, Uri origin, CompletionLanguage language, IReadOnlyList<string> tokens)
    {
        // AddressOf is an argument's (MS-VBAL §5.6.16.8): of a call statement, or in an argument list.
        var argument = tokens.Count > 0
            && (tokens[^1] is "(" or "," || tokens.Count == 1 && !StatementKeywords.Contains(tokens[0], StringComparer.OrdinalIgnoreCase) && !OperandAfterStatement(tokens[0]));
        IEnumerable<string> keywords = [.. OperandKeywords, .. When(language.IsClassModule, "Me"), .. When(argument, "AddressOf")];
        return [.. CompletionCandidates.Names(tree, origin), .. Keywords(keywords)];
    }

    // the words of statements that an expression follows, which a call statement does not start with.
    private static bool OperandAfterStatement(string word) => word.Is("If") || word.Is("ElseIf") || word.Is("Case") || word.Is("Print") || word.Is("Return");

    private static IReadOnlyList<CompletionCandidate> Operators() => Keywords(OperatorKeywords);

    private static string[] When(bool condition, params string[] keywords) => condition ? keywords : [];

    private static IReadOnlyList<CompletionCandidate> Keywords(IEnumerable<string> keywords)
        => [.. keywords.Select(keyword => new CompletionCandidate(keyword, CompletionItemKind.Keyword, Rank: KeywordRank))];

    private static IReadOnlyList<CompletionCandidate> Labels(CompletionPlacement placement)
        => [.. placement.Labels.Select(label => new CompletionCandidate(label, char.IsAsciiDigit(label[0]) ? CompletionItemKind.Value : CompletionItemKind.Reference, Rank: NearRank))];
}
