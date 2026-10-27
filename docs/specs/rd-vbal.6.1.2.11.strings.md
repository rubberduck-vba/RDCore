# 6.1.2.11 Strings

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.11** Strings](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/f87cc0fb-f0ca-4875-9691-34d80e2933b0).

The `Strings` module is represented in the SDK by the interface
[IStdStringsModule](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdStringsModule.html).

The `Strings` module is declared in full. **MS-VBAL §6.1.2.11** has 43 subsections, and RD-VBA's `Strings` module
declares 55 members.

Some MS-VBAL subsections declare more than one member:

- the B-suffixed byte variants (`InStrB`, `LeftB`, `LenB`, `MidB`, `RightB`) share a subsection with the member they
  vary;
- the `$`-suffixed String-returning twins (`Chr$`, `ChrB$`, `ChrW$`) share a subsection with the member they vary;
- the `LTrim` / `RTrim` / `Trim` group is one subsection, and so is its `$`-suffixed twin.

Every member has a runtime implementation (`StdStrings`).


## Arguments

An argument has been through the call site before a member is given it
([**RD-VBAL §5.3.1.11** Procedure Invocation Argument Processing](rd-vbal.5.3.1.11.procedure-invocation-argument-processing.md#standard-library-calls)):

- An argument to a typed parameter was Let-coerced to its type, so `Null` given for a `String` or a `Long` is error `94`
  before any member is reached: `Asc(Null)`, `InStrRev(Null, "a")`, `StrReverse(Null)`, `String(Null, "a")`.
- A typed optional argument left out is its declared default: `InStrRev`'s `Start` and `Replace`'s `Count` are `-1`,
  `Replace`'s `Start` is `1`, `Split`'s `Limit` is `-1`, `Filter`'s `Include` is `True`, the `NumDigitsAfterDecimal`
  of `FormatCurrency`, `FormatNumber` and `FormatPercent` is `-1`, and the `Abbreviate` of `MonthName` and `WeekdayName`
  is `False`. C# cannot state those
  defaults, so the declarations do, with
  [StdLibDefaultAttribute](../api/RDCore.SDK.Runtime.Abstract.StdLib.StdLibDefaultAttribute.html)
  ([**RD-VBAL §6.0.1** Symbol Injection](rd-vbal.6.0.standard-library.md#attributes)).
- A `Compare` left out is the calling module's `Option Compare` ([Compare and Option Compare](#compare-and-option-compare)).
- A `Variant` optional argument left out, or a caller's own Missing passed on to one, is left out to the member, and
  nothing else is: an explicit `Empty` is `Empty`. `Mid(s, 2, v)`, with `v` an omitted `Optional` of the caller's, is the
  rest of `s` from `2`. A caller's Missing passed on to a typed optional (`Replace`'s `Count`, `Split`'s `Limit`) is error
  `13`, as Missing into any typed destination is.

A member reads a `Variant` argument as a parameter of the type it needs would be Let-coerced. Read as a `String`, a
number or a date is written in the environment's regional settings
([**RD-VBAL §5.5.1.2.4**](rd-vbal.5.5.1.2.runtime-semantics.md#regional-settings)). Read as a number (`Mid`'s `Length`,
`InStr`'s start, `String`'s character code), `Empty` is `0`, `True` is `-1`, a number is rounded to even, a value beyond
a `Long` is error `6`, `Null` is error `94`, and anything else that is not a number is error `13`.

An object given for a `Variant` parameter is read as its Let-coercion reads it
([**RD-VBAL §5.5.1.2.13**](rd-vbal.5.5.1.2.runtime-semantics.md#551213-let-coercion-to-and-from-a-class-or-object-or-nothing)):
as its "simple data value", what its default member returns when it is called with no arguments, read again if that is
an object. So `Left(o, 2)` of an object whose default member returns `"duck"` is `"du"`, and one that returns `Null` gives
`Null`. `Nothing` is error `91`, as the call site makes it for a typed parameter: `Left(Nothing, 1)` is error `91`, as
`StrReverse(Nothing)` is. An object with no default member is error `438`, and one whose default member takes an argument
(a `Collection`) is error `450`; an object that a default member returned and that has no value is error `13`
(**MS-VBAL §5.6.2.3**); what the default member raises is raised; and a chain of default members that never returns a
value that is not an object is error `28`. MS-VBA raises the same for most of its functions (recorded in Excel), but not
all: its `Format` raises `438` for an object a default member returned.

A `String * n` argument is its padded value, so `Trim`, `RTrim`, `Left`, `Right` and `Mid` see the padding.

A character member reads a `String`'s characters, so a trailing byte is not one of them, as `Len` does not count it
([**RD-VBAL §2.4.1.2** VBStringType](rd-vbal.2.4.1.intrinsic-types.md#2412-vbstringtype)). A character is a UTF-16 code
unit: `Left`, `Mid` and `Right` can split a surrogate pair. A byte member (`AscB`, `ChrB`, `InStrB`, `LeftB`, `MidB`,
`RightB`) reads every byte, a trailing one included. `StrComp` compares as the relational operators do, so a trailing
byte breaks a tie; `Filter` keeps an element as it was read; and `StrConv` keeps a trailing byte for a conversion of `0`,
and reads it for `vbUnicode`.


## Compare and Option Compare

`Filter`, `InStr`, `InStrB`, `InStrRev`, `Replace`, `Split` and `StrComp` take a `Compare`, declared
`Optional Compare As VbCompareMethod = vbBinaryCompare`. Of an omitted one MS-VBAL says "it uses the
<option-compare-directive> of the calling module".

The call site fills it in. The declarations mark `Compare` with
[StdLibOptionCompareAttribute](../api/RDCore.SDK.Runtime.Abstract.StdLib.StdLibOptionCompareAttribute.html), and a call
that leaves it out passes `vbTextCompare` (`1`) where the calling module compares as `Text` and `vbBinaryCompare` (`0`)
otherwise ([**RD-VBAL §5.3.1.11**](rd-vbal.5.3.1.11.procedure-invocation-argument-processing.md#optional-parameters)):

- The calling module is the one whose code makes the call: a class module's code takes its own `Option Compare`, not
  that of the module that called it.
- `Option Compare Database` is resolved through the environment, as the comparison operators resolve it, which makes it
  `Text` unless the environment says otherwise
  ([**RD-VBAL §5.2.1.1** Option Compare Directive](rd-vbal.5.2.1.option-directives.md#5211-option-compare-directive)). A
  member and `=` agree on what the calling module's mode is.
- A caller with no calling module, such as a host that calls the library directly, gets the declared `vbBinaryCompare`.

A member reads `Compare` as it is given, and never consults `Option Compare` itself. An explicit `Compare` is
`vbBinaryCompare` (`0`) or `vbTextCompare` (`1`); anything else (`2`, `-1`, a locale identifier) is error `5`.
`VbCompareMethod` has no `vbDatabaseCompare`.

> [!WARNING]
> 🚧 MS-VBA takes a `Compare` that is a locale identifier - `3`, `4`, `100`, `1031`, `1033`, not `5000`, `32767` or `65535` - as a text comparison, `Replace`
> takes `2` as one where `InStr` and `StrComp` raise `5`, and Danish and Norwegian do not expand `æ` (recorded in Excel). RD-VBA raises `5`, as above.

In text mode two `String`s are equal as `=` finds them under `Option Compare Text`
([**RD-VBAL §5.6.9.5** Relational Operators](rd-vbal.5.6.9.5.relational-operators.md#string-comparison)). A text-mode search does not use that
comparison. MS-VBAL says nothing of how a text-mode search matches, and RD-VBA searches as MS-VBA does (recorded in Excel), the way `Like` matches
([**RD-VBAL §5.6.9.6**](rd-vbal.5.6.9.6.like-operator.md)): it searches the letters of both `String`s, each character folded to one case, and `æ`, `œ`, `ß` and
`þ` (and their capitals) expanded to the two letters they are. Nothing is ignored: a soft hyphen, `vbNullChar`, a full-width letter or katakana is a character
of its own, so `Split(buffer, vbNullChar)` splits at the null characters, and `"ab"` is not found in `"a"`, a soft hyphen and `"b"`. Accents are not case, and
`ĳ`, the ligatures `ﬀ` to `ﬆ` and `ǆ` are not expanded.

|Search|What it finds|Example|
|---|---|---|
|`InStr`, `InStrRev`, `Filter`|The letters of the pattern anywhere in the letters of the `String`, part of an expanded character too, found at the character the match starts in.|`InStr(1, "xæx", "ex", vbTextCompare)` is `2`; `InStr(1, "xaex", "æ", vbTextCompare)` is `2`.|
|`Replace`, `Split`|The same, where the match starts and ends with a character: part of an expanded character is not replaced, nor split at.|`Replace("xæx", "a", "-", , , vbTextCompare)` is `"xæx"`; `Replace("xaex", "æ", "-", , , vbTextCompare)` is `"x-x"`.|

In binary mode a search matches the same code units.

`Replace` and `Split` take their matches from left to right, each search resuming after the run it found, so their
matches never overlap. `InStrRev`'s may: `InStrRev("aaa", "aa")` is `2`.


## String-Returning Twins

**MS-VBAL §6.1.2.11.1** gives seventeen functions a `$` form that returns a `String`: `Chr$`, `ChrB$`, `ChrW$`,
`Format$`, `LCase$`, `Left$`, `LeftB$`, `LTrim$`, `Mid$`, `MidB$`, `Right$`, `RightB$`, `RTrim$`, `Space$`, `String$`,
`Trim$` and `UCase$`. Each is "functionally identical" to its `Variant` twin, "except that the return type of the
function is String rather than Variant": it is the twin's result Let-coerced to `String`, so where the twin returns
`Null` the `$` function raises error `94` (`Left$(Null, 1)`;
[**RD-VBAL §5.5.1.2.10**](rd-vbal.5.5.1.2.runtime-semantics.md#551210-let-coercion-from-null)).

A name written with a type-declaration character is bound by its identifier, through the same tiers as any simple name
(**MS-VBAL §5.6.10**; [**RD-VBAL §5.6.10** Simple Name Expressions](rd-vbal.5.6.10.simple-name-expressions.md)), so
`Left$` is looked up as `Left`:

- Where `Left` binds a declaration of the procedure, the module, the project or a referenced library (a variable, a
  constant, a parameter, a procedure, another module's public `Left`), `Left$` is that declaration, and the character is
  judged against its
  type. After `Dim Left$`, `Left$ = "x"` assigns the variable; a module's `Function Trim` is what `Trim$` calls; and
  `Left$` beside `Dim Left As Long` is [`VBC09335`](../diagnostics/vbc09335.md).
- Where `Left` binds a member of the standard library, or nothing, `Left$` is the library's member named `Left$`, if
  the library declares one.
- Otherwise what the identifier binds stands. `Len$` is `Len`, which returns a `Variant`, so it is `VBC09335`, and the
  module does not load ([**RD-VBAL §5.0.3** Semantic Analysis](rd-vbal.5.0.semantics.md#the-semantic-model)).

A member after a dot is looked up as it is written: `VBA.Left$` is the library's `Left$` whatever the module declares,
and `VBA.Strings.Left` the library's `Left`.


## Errors

Within one call the errors come in one order: the coercions of the arguments, in the order they are declared; then a
`Null` operand, which makes the result `Null`; then the values' own checks, in the order they are declared, before any
shortcut to a result. So `Mid(Null, 0)` is `Null` though a start of `0` is error `5`, and `Replace("", "a", "b", 0)` is
error `5` though a zero-length expression gives a zero-length string.

|When|Error|
|---|---|
|`InStr` or `InStrB` is given no `String` to search, or no pattern|`449`|
|A `Variant` argument has no form of the type the member needs: an `Error`, Missing, or an array other than a `Byte()` read as a `String`|`13`|
|A `Variant` argument, or an element of a `SourceArray`, is `Nothing`, or an object whose default member returns `Nothing`|`91`|
|A `Variant` argument is an object with no default member|`438`|
|A `Variant` argument is an object whose default member takes an argument, such as a `Collection`|`450`|
|A `Variant` argument is an object whose default member returns an object that has no value|`13`|
|A `Variant` argument the member needs as a number is beyond a `Long`|`6`|
|A date to format outside the range of dates|`6`|
|A date to format before the first day of the Hijri calendar, when the session's calendar is Hijri|`5`|
|A `Variant` argument the member needs as a number or a date, `Format`'s `Format`, `Join`'s or `Split`'s `Delimiter`, or an element of a `SourceArray` is `Null`|`94`|
|A start below `1`, `InStrRev`'s `Start` of `0` or below `-1`, a negative `Length` or `Number`, `Replace`'s `Count` below `-1`, `Split`'s `Limit` below `-1`|`5`|
|An explicit `Compare` other than `0` or `1`|`5`|
|A `FirstDayOfWeek`, `FirstWeekOfYear`, `NamedFormat` or tristate that is no value of its enumeration; a `NumDigitsAfterDecimal` below `-1`|`5`|
|`Asc`, `AscB` or `AscW` of a zero-length `String`; a code `Chr`, `ChrW` or `String` has no character for|`5`|
|`MonthName` of a `Month` other than `1` to `12`; `WeekdayName` of a `Weekday` other than `1` to `7`|`5`|
|`ChrB` of a code outside `0` to `255`|`6`|
|A `StrConv` conversion it does not make ([§6.1.2.11.1.37](#61211137-strconv))|`5`|
|A `SourceArray` that is not a one-dimensional array|`13`|
|A result longer than a `String` can hold, from `Space`, `String` or a `Format` function|`14`|
|A `$` function whose `Variant` twin returns `Null`|`94`|


## Against MS-VBA

RD-VBA follows MS-VBAL where it and MS-VBA's own documentation differ, and reads a sentence of MS-VBAL as an erratum
where its literal reading contradicts the same page's declaration or table. None of this was checked against MS-VBA
itself.

- Parameters have MS-VBAL's names, which are not always the Office VBA reference's: `Asc`'s is `StringValue` rather than
  `String`, `InStr`'s are `Arg1`, `Arg2` and `Arg3` rather than `Start`, `String1` and `String2`, and `StrConv`'s locale
  is `LocaleID` rather than `LCID`. A call that names an argument by its Office name is error `448`, "Named argument not
  found".
- `LTrim`, `RTrim` and `Trim` remove spaces, as MS-VBAL says, and a space is U+0020 alone: an ideographic space (U+3000)
  stays, whatever the code page.
- `ChrW` accepts `-32768` to `65535`. MS-VBAL bounds it at `-32,767`, read here as the bottom of an `Integer`: `AscW` of
  U+8000 is `-32768`, and `ChrW(AscW(c))` is `c` for every code unit.
- A position or count that MS-VBAL leaves unbounded is error `5` where it cannot mean anything: a start below `1`,
  `InStrRev`'s `Start` of `0` or below `-1`, a negative `Length` or `Number`, `Replace`'s `Count` below `-1` and
  `Split`'s `Limit` below `-1`. That is the `Mid` statement's rule for the same arguments
  ([**RD-VBAL §5.4.3.5** Mid Statement](rd-vbal.5.4.3.5.mid-statement.md)), where clamping would invent a meaning.
  `Split`'s `Limit` of `0` is an empty array.
- The `InStr`, `InStrRev`, `Replace` and `String` pages are read through errata, given with the members
  ([§6.1.2.11.1.14](#61211114-instr--instrb), [§6.1.2.11.1.15](#61211115-instrrev), [§6.1.2.11.1.29](#61211129-replace),
  [§6.1.2.11.1.38](#61211138-string)).
- Two parameters are declared otherwise than MS-VBAL's declaration lines: `Join`'s and `Filter`'s `SourceArray`
  ([§6.1.2.11.1.16](#61211116-join)), and `StrConv`'s `LocaleID` ([§6.1.2.11.1.37](#61211137-strconv)).


## 6.1.2.11.1 Public Functions

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.11.1** Public Functions](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/3e63046e-6ccd-4170-a794-f19dc9f0a84e).

|§|Member|Declared members|Notes|
|---|---|---|---|
|6.1.2.11.1.1|[Asc / AscW](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/5c7a1c49-1769-4609-a5ae-f67acbc30967)|`Asc`|See [§6.1.2.11.1.1](#6121111-asc--ascw).|
|6.1.2.11.1.2|[AscB](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/70bbdd14-2cc6-40f7-8119-ca0cd3a251c9)|`AscB`|See [§6.1.2.11.1.1](#6121111-asc--ascw).|
|6.1.2.11.1.3|[AscW](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/aab8e1d9-287d-4098-bf3a-14271eaa4b8e)|`AscW`|See [§6.1.2.11.1.1](#6121111-asc--ascw).|
|6.1.2.11.1.4|[Chr / Chr$](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/750a9a9e-1df8-4f4a-ba4b-83508af2ea7a)|`Chr`, `Chr$`|See [§6.1.2.11.1.4](#6121114-chr--chr).|
|6.1.2.11.1.5|[ChrB / ChrB$](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/900323ac-8160-42cd-ae52-4073f2a95116)|`ChrB`, `ChrB$`|See [§6.1.2.11.1.4](#6121114-chr--chr).|
|6.1.2.11.1.6|[ChrW / ChrW$](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/de4b7500-260f-41c5-b2fd-082eddd5f0b2)|`ChrW`, `ChrW$`|See [§6.1.2.11.1.4](#6121114-chr--chr).|
|6.1.2.11.1.7|[Filter](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/22ca3cbf-f56b-4795-8f9c-f0a59414eaad)|`Filter`|See [§6.1.2.11.1.7](#6121117-filter).|
|6.1.2.11.1.8|[Format](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/c2022d53-37dc-4597-8321-728e385afc92)|`Format`|See [§6.1.2.11.1.8](#6121118-format).|
|6.1.2.11.1.9|[Format$](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/dc8463ed-bfe6-48d6-b804-3e993880ae45)|`Format$`|A [twin](#string-returning-twins).|
|6.1.2.11.1.10|[FormatCurrency](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/2cf6e81f-a693-4884-a211-814c8c95e5a2)|`FormatCurrency`|See [§6.1.2.11.1.10](#61211110-formatcurrency--formatnumber--formatpercent).|
|6.1.2.11.1.11|[FormatDateTime](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/dc56ab7e-dc09-4b50-90ae-d994460c99a6)|`FormatDateTime`|See [§6.1.2.11.1.11](#61211111-formatdatetime).|
|6.1.2.11.1.12|[FormatNumber](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/357ce1a8-bacd-4a69-942e-921a6bc76c6d)|`FormatNumber`|See [§6.1.2.11.1.10](#61211110-formatcurrency--formatnumber--formatpercent).|
|6.1.2.11.1.13|[FormatPercent](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/60b37972-e710-4c3a-a985-c71794a60739)|`FormatPercent`|See [§6.1.2.11.1.10](#61211110-formatcurrency--formatnumber--formatpercent).|
|6.1.2.11.1.14|[InStr / InStrB](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/33e81067-4b43-4c45-a501-0acf1616f2f9)|`InStr`, `InStrB`|See [§6.1.2.11.1.14](#61211114-instr--instrb).|
|6.1.2.11.1.15|[InStrRev](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/9d45a100-3a6d-4cb6-aa09-772ccffe9706)|`InStrRev`|See [§6.1.2.11.1.15](#61211115-instrrev).|
|6.1.2.11.1.16|[Join](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/1c276c6f-c3b0-4de7-ade5-7803f99eea44)|`Join`|See [§6.1.2.11.1.16](#61211116-join).|
|6.1.2.11.1.17|[LCase](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/bf65d932-1cdf-49e9-9e12-35eb4d85f4f3)|`LCase`|See [§6.1.2.11.1.17](#61211117-lcase).|
|6.1.2.11.1.18|[LCase$](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/7d774803-644b-4366-acd9-c72f50488c48)|`LCase$`|A [twin](#string-returning-twins).|
|6.1.2.11.1.19|[Left / LeftB](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/82b33e27-ed2b-4eaf-9595-0e7a47457d64)|`Left`, `LeftB`|See [§6.1.2.11.1.19](#61211119-left--leftb).|
|6.1.2.11.1.20|[Left$](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/5f10a9e1-b6ba-4a92-bd94-c4c99da74ef2)|`Left$`|A [twin](#string-returning-twins).|
|6.1.2.11.1.21|[LeftB$](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/1afd1ea4-54ca-4bd6-8937-7ff97ab16f94)|`LeftB$`|A [twin](#string-returning-twins).|
|6.1.2.11.1.22|[Len / LenB](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/a4d9985a-e2cb-489c-a20a-5da09d77a59e)|`Len`, `LenB`|See [§6.1.2.11.1.22](#61211122-len--lenb).|
|6.1.2.11.1.23|[LTrim / RTrim / Trim](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/053cdd5a-c8e2-4a0d-882b-46817b13243d)|`LTrim`, `RTrim`, `Trim`|See [§6.1.2.11.1.23](#61211123-ltrim--rtrim--trim).|
|6.1.2.11.1.24|[LTrim$ / RTrim$ / Trim$](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/ea1d57ea-b712-457b-b12f-0c4d6a5b9031)|`LTrim$`, `RTrim$`, `Trim$`|[Twins](#string-returning-twins).|
|6.1.2.11.1.25|[Mid / MidB](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/4a65ee4e-b6b9-45d3-a3f6-576fed4bb227)|`Mid`, `MidB`|See [§6.1.2.11.1.25](#61211125-mid--midb).|
|6.1.2.11.1.26|[Mid$](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/4430170b-0187-4a54-83b0-cafbfadf8127)|`Mid$`|A [twin](#string-returning-twins).|
|6.1.2.11.1.27|[MidB$](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/e612f091-c89e-4e65-ad5a-836f76b183ec)|`MidB$`|A [twin](#string-returning-twins).|
|6.1.2.11.1.28|[MonthName](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/da532aeb-53ec-495f-ae66-1bcfbd97ef0b)|`MonthName`|See [§6.1.2.11.1.28](#61211128-monthname).|
|6.1.2.11.1.29|[Replace](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/6af9457c-70ea-4691-80d6-3dca9f152aee)|`Replace`|See [§6.1.2.11.1.29](#61211129-replace).|
|6.1.2.11.1.30|[Right / RightB](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/59107ed4-5d21-468e-8323-82620b2f0442)|`Right`, `RightB`|See [§6.1.2.11.1.19](#61211119-left--leftb).|
|6.1.2.11.1.31|[Right$](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/975e2582-3fee-4006-a0b5-48be3984982c)|`Right$`|A [twin](#string-returning-twins).|
|6.1.2.11.1.32|[RightB$](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/0be0e967-86d9-425d-aec5-e7c2b8855cea)|`RightB$`|A [twin](#string-returning-twins).|
|6.1.2.11.1.33|[Space](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/39487af8-ae4f-4703-a7f9-b2b220e80981)|`Space`|A negative `Number` is error `5`; one longer than a `String` can hold is error `14`.|
|6.1.2.11.1.34|[Space$](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/3610e96f-51cf-44f6-acbd-c5d5244d917b)|`Space$`|A [twin](#string-returning-twins).|
|6.1.2.11.1.35|[Split](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/d9c2417c-2d36-45b1-b3d7-1876064e6085)|`Split`|See [§6.1.2.11.1.35](#61211135-split).|
|6.1.2.11.1.36|[StrComp](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/9541f02a-e173-47a7-8c0f-4d91813c1c88)|`StrComp`|See [§6.1.2.11.1.36](#61211136-strcomp).|
|6.1.2.11.1.37|[StrConv](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/17afbc5b-e1c7-4717-bfe3-9ae938ef0736)|`StrConv`|See [§6.1.2.11.1.37](#61211137-strconv).|
|6.1.2.11.1.38|[String](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/1bb5160c-7203-4c3e-8650-e5aac19477a8)|`String`|See [§6.1.2.11.1.38](#61211138-string).|
|6.1.2.11.1.39|[String$](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/84f4df4e-9156-45db-b4d4-066d4848607b)|`String$`|A [twin](#string-returning-twins).|
|6.1.2.11.1.40|[StrReverse](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/4d48b91d-d72d-4f6f-957c-53e556007e80)|`StrReverse`|Reverses the order of the code units.|
|6.1.2.11.1.41|[UCase](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/29b5f1d1-24b8-4bc5-82cf-8c98059a7116)|`UCase`|See [§6.1.2.11.1.17](#61211117-lcase).|
|6.1.2.11.1.42|[UCase$](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/5a417dfc-c9ad-4e0a-97bc-138dd35c479a)|`UCase$`|A [twin](#string-returning-twins).|
|6.1.2.11.1.43|[WeekdayName](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/09423417-ebcb-4aa5-bb73-2a8c1a19bd6a)|`WeekdayName`|See [§6.1.2.11.1.43](#61211143-weekdayname).|

### 6.1.2.11.1.1 Asc / AscW

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.11.1.1** Asc / AscW](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/5c7a1c49-1769-4609-a5ae-f67acbc30967).

`Asc` is the code of the first character: its ASCII code below 128, and above it its code in the environment's ANSI
code page ([**RD-VBAL §2.3.1.2** Session Services](rd-vbal.2.3.1.2.session-services.md)), where a double-byte code is
one negative `Integer` and a character the code page lacks is the code its encoder maps it to. `AscW` is the first code
unit, as an `Integer`, so one above U+7FFF is negative. `AscB` is the first byte of the `String`'s UTF-16LE data.

A zero-length `String` is error `5` for all three. A `String` of a single byte (`ChrB(65)`) has no first character:
`Asc` and `AscW` of it are error `5`.

### 6.1.2.11.1.4 Chr / Chr$

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.11.1.4** Chr / Chr$](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/750a9a9e-1df8-4f4a-ba4b-83508af2ea7a).

`Chr` of `0` to `127` is the ASCII character, and of `128` to `255` the character of the environment's ANSI code page.
Any other code is error `5`, except on a double-byte code page, where `-32768` to `65535` are codes, as MS-VBAL allows "a
character set with a larger code point range". There a code above `255` is a lead byte and a trail byte, and a code from
`128` on is error `5` unless the code page reads its byte, or its two bytes, as one character it defines, since the
result is "a single character": in code page 932, `Chr(&H81)` is a lone lead byte, `Chr(&H4142)` would be two characters,
and `Chr(&H8100)` names none, while `Chr(&HB1)` is the half-width katakana it names. A single-byte code page names a
character with every code from `128` to `255`.

`ChrW` takes `-32768` to `65535`, a negative code standing for itself plus `65536`; a lone surrogate is a character like
any other. `ChrB` is a `String` of one byte (`Len` `0`, `LenB` `1`), and a code outside `0` to `255` is error `6`.

### 6.1.2.11.1.7 Filter

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.11.1.7** Filter](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/22ca3cbf-f56b-4795-8f9c-f0a59414eaad).

`Filter` keeps the elements that contain `Match` as `InStr` finds it, or with `Include` `False` those that do not. A
zero-length element contains nothing, and a zero-length `Match` is in every other element. An element is kept as the
`String` it was read as, a trailing byte included.

The result is a `String()` from `0`: `Filter` builds the array itself and never reads `Option Base`, so under
`Option Base 1` its lower bound is still `0`
([**RD-VBAL §5.2.1.2** Option Base Directive](rd-vbal.5.2.1.option-directives.md#5212-option-base-directive)). It is
`(0 To -1)` when nothing is kept.

`SourceArray` is a `Variant`, as `Join`'s is ([§6.1.2.11.1.16](#61211116-join)).

### 6.1.2.11.1.8 Format

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.11.1.8** Format](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/c2022d53-37dc-4597-8321-728e385afc92).

MS-VBAL gives `Format` its declaration and its calendar rules, and nothing of what a format expression is. RD-VBA reads
format expressions as the Office VBA reference documents them, in the environment's culture
([**RD-VBAL §2.3.1.2**](rd-vbal.2.3.1.2.session-services.md) `Environment.Culture`): its decimal and group separators and
digit groups, its currency symbol and patterns, its date and time separators, patterns, and day and month names.

Without a `Format`, or with a zero-length one, `Expression` is the `String` a Let-coercion writes, so `Format(1.5)` is
`CStr(1.5)` and `Format(Null)` is `Null`. `Format` is read as a `String`, so a `Null` one is error `94`; it is cut to its
first 257 characters, as MS-VBA documents. `Expression` is read as itself: `Null`, `Empty`, a `Boolean`, a number, a `Date`
or a `String`, and a `Byte()` as its `String`; anything else is the error a `String` parameter would raise.

A **named format** is read in any case. `General Number` is the number as a Let-coercion writes it; `Currency` is
`FormatCurrency` with every argument left out; `Fixed`, `Standard`, `Percent` and `Scientific` are the user-defined
`0.00`, `#,##0.00`, `0.00%` and `0.00E+00`; `Yes/No`, `True/False` and `On/Off` are the second word for zero and the
first for any other number. `General Date`, `Long Date`, `Short Date` and `Long Time` are the user-defined `c`, `dddddd`,
`ddddd` and `ttttt`; `Medium Date` is `dd-mmm-yy` (`15-Mar-24`, English being the language of the host); `Medium Time`
is `hh:nn AMPM`, and `Short Time` `hh:nn`.

Any other format is **user-defined**, of up to four sections separated by semicolons outside its literals (`"ABC"`, and
the character after a `\`). What a section formats is read from the characters it uses outside its literals: `@`, `&`,
`<`, `>` or `!` make it a string section; otherwise any date or time symbol (`c`, `d`, `w`, `m`, `q`, `y`, `h`, `n`, `s`,
`ttttt`, `AM/PM`, `A/P`, `AMPM`) makes it a date section; otherwise a digit placeholder, a separator, `%` or an exponent
makes it a number section. So a date or time letter is never a literal unless it is quoted or escaped:
`"0 h"` is a date format, whose `h` is the hour.

- A **number section** writes `Expression`'s magnitude rounded half away from zero on its decimal digits: a whole number,
  a `Currency` and a `Decimal` exactly, a `Single` to 7 significant digits and a `Double` to 15, as a Let-coercion to
  `String` has them ([**RD-VBAL §5.5.1.2.4**](rd-vbal.5.5.1.2.runtime-semantics.md)), so `Format(1.005, "0.00")` is
  `1.01` and `Format(2.5, "0")` is `3`. Digits the placeholders have no room for are written at the first of them. A `,`
  between placeholders of the whole part groups its digits by the culture's digit groups; one that ends the whole part
  divides by 1000 (`##0,,` writes millions). A `.` is always written: `Format(5, "#.##")` is `5.`. `/` and `:` are the
  culture's date and time separators.
- A **date section** writes `Expression` Let-coerced to a `Date`, to the nearest second, in the session's calendar.
  Every symbol is read in either case. `m` and `mm` are minutes when the date or time symbol before them is `h` or `hh`,
  whatever separators are between, and months otherwise (`mm:ss` is the month). `w` and `ww` count by `FirstDayOfWeek`
  and `FirstWeekOfYear`, `vbUseSystem` being the culture's. `AM/PM` and `A/P` are written in the case the format has them;
  `AMPM` is the culture's designator. A run longer than a symbol's longest form is that form and the rest of the run.
- A **string section** writes `Expression`'s `String`, right-aligned in its placeholders unless it has a `!`; characters
  the placeholders have no room for are written all the same (`Format("ABCDE", "@@")` is `ABCDE`), and a section with no
  placeholder writes its literals and then the whole `String`.

When the first section is a string section, any value is written as its `String`, by the second section if that is
zero-length or `Null` and the format has one. Otherwise a `String` that reads as a number is that number, one that reads
as a date is that date, and any other is written as it is. The section is chosen by the sign before rounding: the second
for a negative number and the third for zero when they are there and not empty, and otherwise the first, with a minus
sign before everything it writes for a negative number, so `Format(-0.001, "0.00")` is `-0.00`. A date and a `Boolean`
are the numbers they are. A `Null` is written by the fourth section, and is `Null` when there is none.

`FirstDayOfWeek` and `FirstWeekOfYear` are values of their enumerations, else error `5`, checked after a `Null`
`Expression` has made the result `Null`.

Under the Hijri calendar ([**RD-VBAL §6.1.2.4**](rd-vbal.6.1.2.4.datetime-module.md)) the date symbols count in it,
`mmm` is the full month name, and the names and the patterns of `c`, `ddddd`, `dddddd` and `ttttt` are the culture's own
Hijri ones if it has them, and Arabic (Saudi Arabia)'s otherwise, with the culture's own date and time separators. Without a format, a date is still written as a
Let-coercion writes it, in the Gregorian calendar.

### 6.1.2.11.1.10 FormatCurrency / FormatNumber / FormatPercent

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.11.1.10** FormatCurrency](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/2cf6e81f-a693-4884-a211-814c8c95e5a2),
> [**§6.1.2.11.1.12** FormatNumber](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/357ce1a8-bacd-4a69-942e-921a6bc76c6d)
> and [**§6.1.2.11.1.13** FormatPercent](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/60b37972-e710-4c3a-a985-c71794a60739).

The three round half away from zero to `NumDigitsAfterDecimal` places, and write the culture's separators and digit
groups. `FormatCurrency` writes the culture's currency symbol where its currency patterns put it; `FormatPercent` writes
the number times 100 followed by `%`, the sign or the parentheses around both (`-50.00%`, `(50.00%)`). A number that
rounds to zero has no sign.

What the specification leaves to the implementation is decided so:

|Left out, or `vbUseDefault`|RD-VBA|
|---|---|
|`NumDigitsAfterDecimal` (`-1`)|The culture's currency places for `FormatCurrency`; `2` for the other two, as `Fixed` and `Standard` have. .NET's places for numbers are not read: ICU and Windows disagree on them (3 and 2 for en-US).|
|`IncludeLeadingDigit`|`True`|
|`UseParensForNegativeNumbers`|Where the culture's negative pattern has parentheses|
|`GroupDigits`|Where the culture has digit groups|

`vbTrue` with a currency pattern that has no parentheses puts the amount and its symbol in parentheses, the symbol where
the positive pattern has it; `vbFalse` with one that has them puts a minus sign before the amount and its symbol.

`Expression` is read as a `Double`, except that a number is read as the value it is, so a `Currency`, a `Decimal` or a
`LongLong` keeps every digit; `Empty` is `0` and a `Date` its serial number. `Null` is error `94`, and a `String` that is
no number error `13`. A `NumDigitsAfterDecimal` below `-1`, and a tristate other than `vbTrue` (`-1`), `vbFalse` (`0`) or
`vbUseDefault` (`-2`), are error `5`: the pages' tables give `1` and `2`, read as errata of **MS-VBAL §6.1.1.15**'s `-1`
and `-2`.

### 6.1.2.11.1.11 FormatDateTime

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.11.1.11** FormatDateTime](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/dc56ab7e-dc09-4b50-90ae-d994460c99a6).

`FormatDateTime` is `Format`'s named date formats: `vbGeneralDate` is `General Date`, `vbLongDate` `Long Date`,
`vbShortDate` `Short Date`, `vbLongTime` `Long Time`, and `vbShortTime` `Short Time` ("the 24-hour format (hh:mm)"), in
the session's calendar. `Expression` is Let-coerced to a `Date`: `Null` is error `94`, a value that is no date error
`13`, and one outside the range of dates error `6`. A `NamedFormat` other than `0` to `4` is error `5`.

### 6.1.2.11.1.14 InStr / InStrB

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.11.1.14** InStr / InStrB](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/33e81067-4b43-4c45-a501-0acf1616f2f9).

What the first argument is depends on whether a third arrives. A third argument left out, or a caller's Missing passed
on as one, makes the call the two-argument form: the first `String` searched for the second from position `1`. Any
other third argument, an explicit `Empty` included, makes the first argument the start. A start left out before a third
argument is `1`, and a `String` to search or a pattern left out is error `449`.

The start is read as a `Long` would be ("If start contains the data value Null, an error occurs": error `94`), then
the `String`s; a `Null` `String` gives `Null`, before the start is checked. A start below `1` is error `5`. A
zero-length `String` to search gives `0`, and a zero-length pattern is found at the start, up to one past the end:
`InStr(4, "abc", "")` is `4`.

The page's three-argument prose says "Arg1 is used as a string", and its table gives `0` for "Arg1 > Arg3". Both are
read as errata: the start is a number, and that row is a start past the end of the `String` to search, plus one for a
zero-length pattern.

`InStrB` is the same over the `String`'s bytes, a trailing one included, and may match inside a character. In text mode
it searches characters, from the one at or after the start's byte, and gives the byte position of the character it
finds.

### 6.1.2.11.1.15 InStrRev

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.11.1.15** InStrRev](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/9d45a100-3a6d-4cb6-aa09-772ccffe9706).

`InStrRev` finds the last match that ends by `Start`, and matches may overlap. A zero-length pattern is found at
`Start`. A `Start` past the end of `StringCheck` gives `0`, and a `Start` of `0` or below `-1` is error `5`.

The page contradicts itself, and is read so:

- Its table says an omitted `Start` is "the data value 1, which means that the search begins at the last character
  position", where its declaration says `Optional Start As Long = -1`. The declaration's `-1` is what is declared.
- Its table says an omitted `Compare` performs "a binary comparison", where its runtime semantics name the calling
  module's `Option Compare`, as every other comparing member's do. The runtime semantics are what is declared.
- Its "Start > Len(StringMatch)" is read as `Len(StringCheck)`.
- Its table gives `Null` for a `Null` `StringCheck` or `StringMatch`, which its declaration, `As String`, makes error
  `94` at the call site.

### 6.1.2.11.1.16 Join

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.11.1.16** Join](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/1c276c6f-c3b0-4de7-ade5-7803f99eea44).

`Join` joins the elements' `String`s in index order, from the array's own lower bound, separated by `Delimiter`, a space
when it is left out. `Delimiter` is read as `Split`'s is ([§6.1.2.11.1.35](#61211135-split)), after the array is
checked.

MS-VBAL declares `SourceArray() As Variant`, for `Join` and for `Filter`. RD-VBA declares a `Variant`: an array is
Let-coerced only to an array of its own element type
([**RD-VBAL §5.5.1.2.7**](rd-vbal.5.5.1.2.runtime-semantics.md#55127-let-coercion-to-and-from-non-byte-arrays)), so a
`Variant()` parameter would refuse the `String()` that `Split` returns, and `Join(Split(s))` would be error `13`. The
member checks the array itself, and raises what the declared parameter would: a `SourceArray` that is `Null`, `Empty`,
not an array, an array not yet dimensioned or one of more than one dimension is error `13`. Any one-dimensional array
is joined, of any element type, fixed or resizable. Every element is read as a `String` before any is joined: `Null` is
error `94`; `Nothing`, an element of an `Object()` never assigned included, is error `91`; an `Error`, Missing, an
object that is not `Nothing`, or an array other than a `Byte()` is error `13`; and an element of a `Variant()` never
assigned is `Empty`, a zero-length string.

### 6.1.2.11.1.17 LCase

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.11.1.17** LCase](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/bf65d932-1cdf-49e9-9e12-35eb4d85f4f3).

`LCase` and `UCase` case by the environment's regional settings (a Turkish `I` and `i` case to the dotless and dotted
forms), one character for one, so `"ß"` stays `"ß"`. `Null` is `Null`.

### 6.1.2.11.1.19 Left / LeftB

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.11.1.19** Left / LeftB](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/82b33e27-ed2b-4eaf-9595-0e7a47457d64).

`Left`, `Right`, `LeftB` and `RightB` coerce the `String`, then answer a `Null` one with `Null` whatever the `Length`,
then refuse a negative `Length` with error `5`. The character forms count code units, and the byte forms bytes, so an
odd `Length` gives a `String` that ends inside a character, or for `RightB` begins inside one.

### 6.1.2.11.1.22 Len / LenB

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.11.1.22** Len / LenB](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/a4d9985a-e2cb-489c-a20a-5da09d77a59e).

`Strings.Len` and `Strings.LenB` are the RD-VBA implementation of **MS-VBAL §6.1.2.11.1.22**. Both have a runtime
implementation.

MS-VBAL says the pair returns "the number of characters in a string or the number of bytes required to store a
variable on the current platform". It says `LenB` "will return the same value as `Len`, except for strings or UDTs":
one function with two exceptions (strings and UDTs), rather than two functions.

`Len` and `LenB` are therefore implemented as one measurement, with one option that selects between them.

|Expression|`Len`|`LenB`|
|---|---|---|
|A `String`|Its number of whole characters.|"the number of bytes used to represent that string": two per character, and one more for a trailing byte ([**RD-VBAL §2.4.1.2** VBStringType](rd-vbal.2.4.1.intrinsic-types.md#2412-vbstringtype)).|
|A fixed-length `String`|Its declared length.|Twice its declared length.|
|`Null`|`Null`|`Null`|
|Any other scalar|The bytes it occupies.|The same as `Len`: the bytes it occupies.|
|A UDT|"the size as it will be written to the file": 👉 its members' memory widths, with no padding|"the in-memory size, including any implementation-specific padding between elements"|

#### UDT sizes

The UDT row is the one that needs the platform to know two different sizes for the same value. 🎯 A UDT has two
sizes, and RD-VBA measures both as MS-VBA does (recorded in Excel, 64-bit), for the environment's pointer width:

- `LenB` is its in-memory size: each member aligned to its own width (up to 8), and the whole padded to its widest
  member's.
- `Len` is the same members' widths added up with no padding, a fixed-length `String` counted as its characters and a
  nested record or an array's elements as their own `Len`.

A variable-length `String`, an object, a `LongPtr` and a resizable array member are each a pointer (8 bytes in a 64-bit
environment), a `Variant` is 24 (16 in a 32-bit one), and a fixed-size array member is its elements inline. So `Len` of
`{ a As Long, s As String }` is `12` whatever `s` holds, and its `LenB` is `16`; `{ a As Byte, b As String * 3 }` is
`4` and `7`.

👉 This is not "the size as it will be written to the file": a `Put` of a record writes a variable-length `String`
member as its length and its characters. MS-VBAL warns that "`Len` might not be able to determine the actual number of
storage bytes required when used with variable-length strings in user-defined data types"; MS-VBA, and RD-VBA, count the
pointer.

[**RD-VBAL §2.5.2.1.3** User-Defined Type (UDT) Values](rd-vbal.2.5.2.1.3.udt-values.md) holds the padding rule.

#### A string expression, or a variable

`Expression` is "any valid string expression, or any valid variable name", and RD-VBA checks it at compile time, as
MS-VBA does (recorded in Excel). A `String`, a `Variant` (`Now`, `CVar(x)`, `Null`, `Empty`), a late-bound object and a
record are read as a value. A value of any other declared type must be a variable - a variable or a parameter, in
parentheses or not, a field of a record, an element of an array - whose size is measured: `Len(5)`, `Len(1 + 2)`,
`Len(True)`, `Len(FnLong())`, `Len(k)` of a constant and `Len(o.Value)` of a property are
[`VBC09333`](../diagnostics/vbc09333.md) Variable required.

An object stands for what its default member returns, which is no variable: `Len` of an object whose default member
returns a `String` or a `Variant` counts its characters, and of one whose default member returns anything else - a
`Long`, an object - is `VBC09333`. An object whose class has no default member is read when it runs.

#### Declared types at external dispatch

👉 An external call carries _runtime_ values rather than _typed_ ones, so the declared type of the value a member was
called with is recovered at external dispatch
([**RD-VBAL §5.3.1.11** Procedure Invocation Argument Processing](rd-vbal.5.3.1.11.procedure-invocation-argument-processing.md)).
The declared type survives external dispatch because each intrinsic stores its own exact managed type: a `short` for
`Integer` and an `int` for `Long`, not one integer type for both
([**RD-VBAL §2.5.2.1.1** Numeric Values](rd-vbal.2.5.2.1.1.numeric-values.md)).

👉 This is what lets `Len` return "the number of bytes required to store a variable" for the variable it is given.
`Date` and `Double` are indistinguishable at external dispatch. That costs `Len` nothing, because they have the same
width; a parameter declared `Date` will need its declared type to recover its argument.

### 6.1.2.11.1.23 LTrim / RTrim / Trim

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.11.1.23** LTrim / RTrim / Trim](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/053cdd5a-c8e2-4a0d-882b-46817b13243d).

`LTrim`, `RTrim` and `Trim` remove spaces, U+0020, and nothing else: a tab, a line break, a null character, a no-break
space, an em space and an ideographic space stay. `Null` is `Null`.

### 6.1.2.11.1.25 Mid / MidB

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.11.1.25** Mid / MidB](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/4a65ee4e-b6b9-45d3-a3f6-576fed4bb227).

`Length` is a `Variant`, read as a `Long` would be after the `String` is coerced and before a `Null` `String` is
answered, so `Mid(Null, 0)` is `Null`, and `Mid` of an `Error` value is error `13` whatever its `Length`. Then a `Start`
below `1` is error `5`, and so is a negative `Length`, past the end or not, as the `Mid` statement's rule is
([**RD-VBAL §5.4.3.5** Mid Statement](rd-vbal.5.4.3.5.mid-statement.md)). A `Start` past the end gives a zero-length
string, and a `Length` left out, or a caller's Missing passed on, takes the rest. `MidB` is the same over the
`String`'s bytes.

### 6.1.2.11.1.28 MonthName

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.11.1.28** MonthName](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/da532aeb-53ec-495f-ae66-1bcfbd97ef0b).

`MonthName` is the month's name in the environment's regional settings and the session's calendar, as `Format`'s
`mmmm` writes it, and with `Abbreviate` as its `mmm` writes it: under the Hijri calendar the 9th month is Ramadan, and a
Hijri month name is not abbreviated. A `Month` other than `1` to `12` is error `5`.

### 6.1.2.11.1.29 Replace

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.11.1.29** Replace](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/6af9457c-70ea-4691-80d6-3dca9f152aee).

`Replace` returns the `String` from `Start` on, with at most `Count` matches replaced, all of them when `Count` is
`-1`. A `Start` below `1`, a `Count` below `-1` and an explicit `Compare` other than `0` or `1` are error `5`, before
anything else. A zero-length `Expression`, or a `Start` past its end, gives a zero-length string; a zero-length `Find`,
or a `Count` of `0`, gives the `String` from `Start`, unchanged. What a replacement puts in is not searched again.

The page contradicts itself, and is read so: its table gives a copy of `Expression` for a zero-length `Find` and for a
`Count` of `0`, where its prose says the result "begins at the position specified by Start" and "is not a copy of the
original string from start to finish". The prose is what is read: `Replace("abcabc", "", "X", 3)` is `"cabc"`.

### 6.1.2.11.1.35 Split

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.11.1.35** Split](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/d9c2417c-2d36-45b1-b3d7-1876064e6085).

`Split` returns a `String()` from `0`. It builds the array itself and never reads `Option Base`, so under
`Option Base 1` its lower bound is still `0`
([**RD-VBAL §5.2.1.2** Option Base Directive](rd-vbal.5.2.1.option-directives.md#5212-option-base-directive)).

A zero-length `Expression`, or a `Limit` of `0`, gives an empty array, `(0 To -1)`. A zero-length `Delimiter`, or one
not found, gives the whole `String` in one element. With a `Limit` of _n_, the _n_-th element holds the rest of the
`String`, delimiters and all. `Delimiter` is a `Variant`, read as a `String` would be (`Empty` is a zero-length string,
a number is written in the environment's regional settings, `Null` is error `94`), before `Limit` is checked.

The result can be assigned to a `String()` variable (`parts = Split(s)`), and indexed where it is (`Split(s)(1)`).

### 6.1.2.11.1.36 StrComp

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.11.1.36** StrComp](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/9541f02a-e173-47a7-8c0f-4d91813c1c88).

`StrComp` coerces both `String`s, gives `Null` if either is `Null` whatever `Compare` is, then checks `Compare`, and
then compares the two as the relational operators do, a trailing byte included, as an `Integer` `-1`, `0` or `1`. So
`StrComp(a, b)` is `0` exactly when `a = b` under the `Option Compare` that `Compare` names.

### 6.1.2.11.1.37 StrConv

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.11.1.37** StrConv](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/17afbc5b-e1c7-4717-bfe3-9ae938ef0736).

`Conversion` is a sum of `VbStrConv` values. A value outside `0` to `255` is error `5`, and so is one that names both of
a pair MS-VBAL makes "mutually exclusive": `vbUnicode` and `vbFromUnicode`, `vbWide` and `vbNarrow`, `vbKatakana` and
`vbHiragana`. As in MS-VBA, `vbUnicode` or `vbFromUnicode` with any other conversion is error `5` too.

- `vbUnicode` reads the `String`'s bytes, a trailing one included, as text in "the default code page of the system", the
  environment's ANSI code page. `vbFromUnicode` writes the characters as that code page's bytes: `"abc"` becomes a
  `String` of three bytes. A `LocaleID` does not change that code page.
- Every other conversion reads the `String` in the ANSI code page of `LocaleID`'s locale, and writes the result back from
  it, as MS-VBA does. Left out or `0`, or naming a locale with no ANSI code page, it is the environment's code page. A
  character the code page lacks is the one its encoder maps it to, or `?`, before any conversion sees it: in Windows-1252
  `Î±` is `a`, `Ä` is `a`, an ideographic or em space is a space, and `Î³` is `?`; in Windows-932 a no-break space is `?`,
  `Â¥` is `\`, and `ã‚”` is `ãƒ´`.
- `vbUpperCase` and `vbLowerCase` case each character to the one Unicode gives, only when that one cases back to it, in
  any locale: the Turkish `i` is `I`, and `Ä±`, `Ä°`, `Âµ` and `Ï‚` stay as they are. That is MS-VBA's casing (recorded in
  Excel), not the locale's.
- `vbProperCase` lowercases the `String`, then uppercases its first character and each one after one of MS-VBAL's seven
  word separators (`Chr$(0)`, `Chr$(9)` to `Chr$(13)`, `Chr$(32)`), and no other: `"o'neil-smith"` is `"O'neil-smith"`,
  and a no-break space starts no word. In an East Asian locale the ideographic space starts one, "the actual value for
  a space" varying "for DBCS" - as in MS-VBA (recorded in Excel, `LocaleID` `&H411`, `&H804`, `&H404` and `&H412`).
  Elsewhere the code page has read it as a space, which starts one too.
- `vbWide` writes each narrow character as its wide form and `vbNarrow` the other way round, the forms being Unicode's
  compatibility mappings: ASCII `!` to `~` and the fullwidth forms, a space and the ideographic space, the halfwidth
  katakana, Japanese punctuation and Hangul and their wide forms, and `Â¢ Â£ Â¬ Â¯ Â¦ Â¥ â‚©` and theirs. MS-VBA leaves the
  backslash and the fullwidth backslash as they are, and so does RD-VBA. A halfwidth katakana and a halfwidth voiced or
  semi-voiced sound mark widen to one katakana (`ï½¶ï¾ž` is `ã‚¬`), and a voiced katakana narrows to the two. A hiragana has no
  narrow form and stays wide.
- `vbKatakana` writes each hiragana as its katakana, and `vbHiragana` each katakana as its hiragana; a katakana with no
  hiragana (`ãƒ·` to `ãƒº`, the halfwidth ones) stays.
- The conversions are made in one order: the case, `vbWide`, `vbKatakana` or `vbHiragana`, then `vbNarrow`. So a halfwidth
  katakana widened is a katakana for `vbHiragana`, and a hiragana made a katakana can be narrowed.
- A `Conversion` of `0` gives the `String` as it was given, a trailing byte and all. Every other conversion reads its
  characters, so a trailing byte is dropped unless `vbUnicode` reads it.
MS-VBAL's declaration makes `LocaleID` required, which would make `StrConv(s, vbProperCase)` error `449`. RD-VBA declares
it optional, with no default: left out, or `0`, it is the environment's locale. A `LocaleID` that names no locale is
error `5`, and so are `LOCALE_USER_DEFAULT` and `LOCALE_SYSTEM_DEFAULT` (`1024` and `2048`).

`vbWide`, `vbNarrow`, `vbKatakana` and `vbHiragana` "cause run-time errors when used in locales where they do not apply",
in `LocaleID`'s locale or the environment's: `vbWide` and `vbNarrow` apply to "East Asia locales", read as the Chinese,
Japanese and Korean ones, and `vbKatakana` and `vbHiragana` to "Japan only", read as the Japanese ones. Elsewhere they are
error `5`. MS-VBA does the same (recorded in Excel with `LocaleID` `&H411`, `&H804`, `&H404`, `&H412`, `1033` and `&H40C`).

### 6.1.2.11.1.38 String

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.11.1.38** String](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/1bb5160c-7203-4c3e-8650-e5aac19477a8).

`String`'s `Character` is a `String`, whose first code unit is repeated, or a code: any other value is read as a `Long`
would be (`Empty` is `0`, `True` is `-1`), taken `Mod 256` above `255`, and mapped as `Chr` maps it. A `Null`
`Character` gives `Null` whatever the `Number`. A negative `Number`, a zero-length `Character`, a negative code and a
code that names no character are error `5`: on a double-byte code page, `String(2, &H81)` is a lone lead byte, as
`Chr(&H81)` is ([§6.1.2.11.1.4](#6121114-chr--chr)). A result longer than a `String` can hold is error `14`. The page's table gives `Null` for a `Null` `Number`,
which its declaration, `As Long`, makes error `94` at the call site.

### 6.1.2.11.1.43 WeekdayName

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.11.1.43** WeekdayName](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/09423417-ebcb-4aa5-bb73-2a8c1a19bd6a).

`WeekdayName` is the name, in the environment's regional settings, of the day `Weekday` days into a week that begins on
`FirstDayOfWeek`, as `Weekday` counts them, so `WeekdayName(Weekday(d))` is the name of `d`'s day; with `Abbreviate` it
is the abbreviated name. `FirstDayOfWeek` left out is the declared `vbUseSystemDayOfWeek`, the first day of the week of
the regional settings: `WeekdayName(1)` is `"lundi"` in fr-FR. The page's table calls `vbSunday` the default, which its
declaration does not: the declaration is what is declared, and MS-VBA's type library declares the same default, `0`
(`VBE7.DLL`: `Optional FirstDayOfWeek As VbDayOfWeek = 0`). A `Weekday` other than `1` to `7`, then a `FirstDayOfWeek` that
is no value of its enumeration, are error `5`.

---
> ⏮️ [**RD-VBAL §6.1.2.10** Math](rd-vbal.6.1.2.10.math.md) | ⏭️ [**RD-VBAL §6.1.2.12** SystemColorConstants](rd-vbal.6.1.2.12.systemcolorconstants.md)
