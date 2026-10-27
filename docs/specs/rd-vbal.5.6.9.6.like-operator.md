# 5.6.9.6 Like Operator

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.6.9.6** Like Operator](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/792d4f80-72c1-49da-98a7-37d5783a8615).

## Runtime Semantics

`Like` is a simple data operator ([**RD-VBAL §5.6.9.2** Simple Data Operators](rd-vbal.5.6.9.2.simple-data-operators.md)). If either operand is
`Null`, the result is `Null`. Otherwise both operands are let-coerced to `String`, whatever their own types, and the result is a `Boolean`.

Unlike the other relational operators, `Like` has no `Variant` String/Numeric exception: a `Variant` holding a number is matched as the text of
that number ([**RD-VBAL §5.6.9.5** Relational Operators](rd-vbal.5.6.9.5.relational-operators.md)).

`Like` matches characters against a pattern of characters. A trailing byte of either `String` is not a character, and `Like` does not see it.

RD-VBA matches a pattern the way MS-VBA does. Every rule below was measured in MS-VBA.

### Pattern elements

|Element|Matches|
|---|---|
|Any character other than `?`, `#`, `*` and `[`|That character. `]` on its own is an ordinary character.|
|`?`|Any one character, a line feed (`vbLf`) included.|
|`#`|One of the digits `0` to `9`. Other Unicode digits, such as the Arabic-Indic digits, do not match.|
|`*`|Zero or more characters, line feeds included.|
|`[`*list*`]`|One character in the list.|
|`[!`*list*`]`|One character not in the list.|
|`[]`|No character at all: the element is skipped. `"a" Like "a[]"` is `True`.|
|`[!]`|The character `!`.|

A list is read as follows:

- The list ends at the first `]` after the `[` or `[!` that opens it. A `]` right after `[` or `[!` closes the list there, so `"x" Like "[]x]"`
  is `False`: the pattern is `[]`, then `x`, then `]`.
- `a-c` is a range: every character from `a` to `c`.
- A `-` at the start or the end of the list, or right after a range, is the character `-`. `[a-c-e]` is the range `a-c`, the character `-`,
  and the character `e`.
- `?`, `#`, `*` and `[` are ordinary characters inside a list: `"*" Like "[*]"` is `True`.
- The items of a list are tested in order, and the first one that matches ends the test.

### Comparison mode

The comparison mode is the one of the module ([**RD-VBAL §5.2.1** Option Directives](rd-vbal.5.2.1.option-directives.md)).

In binary mode, characters match when they are the same character, and a range compares character codes.

In text mode, characters match regardless of case:

- A character matches its other case only where the two map to each other in the regional settings. The Kelvin sign (U+212A) lowercases to
  `k`, but `k` uppercases to `K`, so `ChrW(&H212A) Like "k"` is `False`. In Turkish regional settings, `i` matches `İ`.
- Only case is ignored. Accents are not case: `"é" Like "e"` is `False`, and so is `"é" Like "[a-z]"`.
- A range is folded to lowercase before it is tested. `"_" Like "[A-z]"` is `False` in text mode, because the range is `[a-z]`, while it is
  `True` in binary mode. `[a-Z]` is the valid range `[a-z]` in text mode, and `[Z-a]` is invalid there.

- Some characters are their expansion in the collation as well, as **MS-VBAL §5.6.9.6** describes: "some number of actual characters in
  <expression> can match a different number of characters in the pattern". MS-VBA expands four (and their capitals), each to two letters:
  `æ` to `ae`, `œ` to `oe`, `ß` to `ss` and `þ` to `th`. This is so in the text and in the pattern: `ChrW(230) Like "ae"`, `"ss" Like ChrW(223)`
  and `"Straße" Like "STRASSE"` are `True`.
- An element can match part of an expanded character:

  |Element|What it takes of an expanded character|Example|
  |---|---|---|
  |A character, `#`|One letter.|`"æ" Like "a?"` and `"æ" Like "ae"` are `True`.|
  |`*`|Any number of letters.|`"æ" Like "*e"` is `True`, `"æ" Like "*a"` is `False`.|
  |`?`|The rest of the character it is at: all of it, at its start.|`"æ" Like "??"` and `"ß" Like "?s"` are `False`.|
  |A list|The whole character if it is in the list, and otherwise its first letter, if that is. It does not try the letter once the whole character matched.|`"æ" Like "[a-æ]"` and `"æ" Like "[ae][ae]"` are `True`; `"æ" Like "[ae]"` and `"æ" Like "[æa]e"` are `False`.|
  |A negated list|Its first letter, which the list tests.|`"æ" Like "[!x]e"` is `True`; `"æ" Like "[!a]e"` and `"æ" Like "[!x]"` are `False`.|

  An expanded character in a list is matched by its letters too (`"ae" Like "[æ]"` is `True`). A range is a range of characters, as it is in
  binary mode: `"b" Like "[æ-c]"` raises 93.
- Nothing else is expanded, nor ignored. `ĳ`, the ligatures `ﬀ` to `ﬆ`, `ǆ`, `ǉ`, `ǌ`, `ǳ` and `ẞ` match only themselves, although `=` compares
  each equal to its letters in text mode. A soft hyphen, a full-width letter and katakana are characters of their own: `"a" & ChrW(173) & "b"
  Like "ab"` and `ChrW(65313) Like "A"` are `False`.

> [!NOTE]
> These are MS-VBA's answers in Excel, in English (United States) regional settings (oracle `like-collation`). One is not reproduced:
> `ChrW(&HFB03) Like "f*"` (`ﬃ`) is `True` in MS-VBA, where every other match of `ﬃ` against its letters is `False`, and RD-VBA answers `False`.

### Invalid patterns

An element is invalid when it is a list with no closing `]`, or a range whose first character is above its last (in the comparison mode).
An invalid element raises run-time error 93 (*Invalid pattern string*) at the point where the match reaches it:

- An element other than `*` raises 93 when it is tested against a character of the text. `"x" Like "y["` is `False`, because `x` does not
  match `y`. `"a" Like "a[z-a]"` is `False`, because no character is left to test the list against. `"a" Like "[a"` raises 93.
- A list tests its items in order, so a reversed range after the item that matches is not reached: `"a" Like "[az-a]"` is `True`, and
  `"b" Like "[az-a]"` raises 93.
- A `*` reached while text is left to match checks the whole rest of the pattern first, and raises 93 if any of it is invalid:
  `"ab" Like "a*c[z-a]"` raises 93, although `c` never appears in the text.
- A `*` reached at the end of the text checks nothing: `"a" Like "a*["` is `False`.

Run-time error 93 is [VBRuntimeErrorId](../api/RDCore.SDK.Model.Errors.VBRuntimeErrorId.html)`.InvalidPatternString`.

### Performance

`RDCore.Runtime.Semantics.Operators.Relational.LikePattern` parses a pattern once and keeps it for the next match. It matches with the
two-pointer wildcard algorithm: a mismatch after a `*` resumes from that `*`, one character further. A match therefore takes at most the
length of the text times the length of the pattern, and allocates nothing but the letters of a text that has a character to expand, in
text mode. `String(1000, "a") Like "*a*a*a*b"` answers `False` at once, as it does in MS-VBA.

## Differences from MS-VBAL

Where MS-VBA does something other than what **MS-VBAL §5.6.9.6** says, RD-VBA does what MS-VBA does, and RDCore.Diagnostics reports a
pattern written in the code as [`RDC11017`](../diagnostics/rdc11017.md), a warning:

|MS-VBAL says|MS-VBA does|
|---|---|
|`[]` is a list of no character, so it matches nothing.|`[]` matches no character at all: it is skipped.|
|`[!]` is a negated list of no character, so it matches any character.|`[!]` matches the character `!`.|
|An invalid element raises error 93 when the match reaches it, and a `*` checks the rest of the pattern "when encountered".|An invalid element raises 93 only when it is tested against a character, a list stops at the item that matches, and a `*` at the end of the text checks nothing.|

The analysis states each of these as a fact of the operation: `ComparisonOperatorSemanticFlags.LikePatternHasEmptyCharList` and
`ComparisonOperatorSemanticFlags.LikePatternIsInvalid`.

Where MS-VBAL is silent or leaves a choice to the implementation, RD-VBA does what MS-VBA does, and nothing is reported:

- A `]` right after `[` or `[!` closes the list.
- `#` matches the digits `0` to `9` only.
- `?` and `*` match a line feed.
- Text mode ignores case by the regional settings, only where the case maps both ways, and ignores no accent. It expands `æ`, `œ`, `ß` and
  `þ`. MS-VBAL leaves text collation to the regional settings.

---
> ⏮️ [**RD-VBAL §5.6.9.5** Relational Operators](rd-vbal.5.6.9.5.relational-operators.md) | ⏭️ [**RD-VBAL §5.6.9.7** Is Operator](rd-vbal.5.6.9.7.is-operator.md)
