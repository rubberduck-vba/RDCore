# 6.2 VBScript Regular Expressions

> [!NOTE]
> This section is RD-VBAL's own. MS-VBAL has no section for the VBScript RegExp 5.5 library, and there is no
> MS-VBAL §6.2.1.

**MS-VBA** programs use the regular expressions of _Microsoft VBScript Regular Expressions 5.5_, either through a reference to it or with
`CreateObject("VBScript.RegExp")`. RD-VBA implements the library itself, as classes of the standard library: `New RegExp` needs no reference, and
`CreateObject("VBScript.RegExp")` creates the same object on any platform, without asking the machine for a server
([**RD-VBAL §6.1.2.8** Interaction](rd-vbal.6.1.2.8.interaction.md)). What follows is what MS-VBA does, recorded in Excel, and what RD-VBA does with it.

|Class|SDK interface|What it is|
|---|---|---|
|`RegExp`|[IStdRegExpClass](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdRegExpClass.html)|A pattern and its options: `Pattern` (`""`), `Global`, `IgnoreCase`, `Multiline` (all `False` for a new object); `Test`, `Execute`, `Replace`. It has no default member.|
|`MatchCollection`|[IStdMatchCollectionClass](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdMatchCollectionClass.html)|What `Execute` found: `Count`, and `Item`, its default member, from `0`. A `For Each` enumerates it.|
|`Match`|[IStdMatchClass](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdMatchClass.html)|One match: `Value`, its default member; `FirstIndex`, from `0`; `Length`; `SubMatches`.|
|`SubMatches`|[IStdSubMatchesClass](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdSubMatchesClass.html)|What each group matched: `Count`, and `Item`, its default member, from `0` - a `String`, or `Empty` for a group that took no part in the match. A `For Each` enumerates it.|

Only `RegExp` can be created. The matches are what was found when `Execute` was called: changing the pattern afterwards changes none of them. A
position that is not one (`m(5)` of a collection of one match) is error `5`.

## Patterns

A pattern is written in VBScript's syntax (that of JScript, which ECMAScript 3 describes), and means what it means in MS-VBA:

|Construct|What it matches|
|---|---|
|`.`|Any character but a line feed (a carriage return is one).|
|`\d`, `\w`, `\s` and their negations `\D`, `\W`, `\S`|An ASCII digit; an ASCII letter, digit or `_`; a space, tab, line feed, vertical tab, form feed or carriage return. `é` is no word character, and a no-break space is no space.|
|`\b`, `\B`|A boundary between a word character and another character, as `\w` has them; anywhere that is not one.|
|`^`, `$`|The start of the text and its very end (a final line feed is not skipped). With `Multiline`, also after and before a carriage return, a line feed, U+2028 or U+2029, each on its own: a `vbCrLf` has an empty line between its two characters.|
|`[...]`, `[^...]`|One of the characters given, or none of them. A `]` first in the set is one of its characters; a `-` first or last is one too; `\b` is a backspace.|
|`*`, `+`, `?`, `{n}`, `{n,}`, `{n,m}`, each with a `?` to make it lazy|Repetition. A brace that is not one of these is a brace (`a{,2}`, `a{2`).|
|`(...)`, `(?:...)`, `(?=...)`, `(?!...)`|A group, numbered by its opening parenthesis; a group that captures nothing; lookahead. A group inside a repetition is what it matched last.|
|`\1` to `\99`|A backreference, when the pattern has that many groups: with fewer, `\10` is group 1 followed by `0`, and `\2` is an octal escape. A backreference to a group that has not matched yet fails.|
|`\n`, `\r`, `\t`, `\v`, `\f`, `\cX`, `\xhh`, `\uhhhh`, `\0` and octal escapes|The character. `\x4` and `\u12`, which give too few digits, are `x4` and `u12`; `\c` before anything but a letter matches nothing.|
|`\` before any other character|That character: `\p` is `p`, `\Z` is `Z`, `\k` is `k`.|

With `IgnoreCase`, a letter matches another when the two are the same in lower case: `é` and `É`, `i` and `İ`. The Kelvin, Ohm and Angstrom signs are not the
letters they look like.

What is not a pattern is reported when it is first used - by `Test`, `Execute` or `Replace` - never when it is assigned. MS-VBA has no description of its own for
these errors: it says "Application-defined or object-defined error".

|Error|What is wrong|Example|
|---|---|---|
|`5017`|Syntax error in regular expression.|`a{3,2}`, `(?<=a)`, `(?i)`, `(?<name>a)`, a `)` that closes nothing, a final `\`|
|`5018`|Unexpected quantifier.|`*a`, `a**`, `a?+`, `^*`, `\b+`|
|`5019`|Expected `]` in regular expression.|`[a`, `[]a`|
|`5020`|Expected `)` in regular expression.|`(a`, `(?:a`|
|`5021`|Invalid range in character set.|`[z-a]`, `[a-\d]`|

## Replacements

In what `Replace` writes for a match, `$1` to `$99` are what a group matched (`$10` is group 10 when there is one, and group 1 followed by `0` when there is
not), `$&` is the match, `` $` `` and `$'` the text before and after it, and `$$` a dollar sign. Anything else, `$0` and `\n` included, is written as it is. A
replacement that is not text is written as text - a `Boolean` as `-1` or `0`, `Empty` as nothing - and `Null` is error `13`.

## Differences

- 🚧 A member of these classes is called as it is declared, whichever way the object is declared. MS-VBA calls one late-bound (`Dim r As Object`) through
  `IDispatch`, which reports `Null` given for a `String` or a `Boolean` as error `13`, and an assignment to a read-only property as `450`; RD-VBA reports
  them as MS-VBA does for an early-bound call: `94`, and `451`.
- 🚧 A match is not bounded in time: a pattern that backtracks without end does here what it does in MS-VBA.

---
> ⏮️ [**RD-VBAL §6.1.3.3** Global Class](rd-vbal.6.1.3.3.global-class.md)
