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

> [!NOTE]
> **Not implemented.** Of the `Strings` members, only `Len` and `LenB` have a runtime implementation. Calling any
> other member raises the run-time error "Application-defined or object-defined error".


## 6.1.2.11.1 Public Functions

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.11.1** Public Functions](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/3e63046e-6ccd-4170-a794-f19dc9f0a84e).

|§|Member|Declared members|Notes|
|---|---|---|---|
|6.1.2.11.1.1|[Asc / AscW](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/5c7a1c49-1769-4609-a5ae-f67acbc30967)|`Asc`||
|6.1.2.11.1.2|[AscB](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/70bbdd14-2cc6-40f7-8119-ca0cd3a251c9)|`AscB`||
|6.1.2.11.1.3|[AscW](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/aab8e1d9-287d-4098-bf3a-14271eaa4b8e)|`AscW`||
|6.1.2.11.1.4|[Chr / Chr$](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/750a9a9e-1df8-4f4a-ba4b-83508af2ea7a)|`Chr`, `Chr$`||
|6.1.2.11.1.5|[ChrB / ChrB$](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/900323ac-8160-42cd-ae52-4073f2a95116)|`ChrB`, `ChrB$`||
|6.1.2.11.1.6|[ChrW / ChrW$](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/de4b7500-260f-41c5-b2fd-082eddd5f0b2)|`ChrW`, `ChrW$`||
|6.1.2.11.1.7|[Filter](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/22ca3cbf-f56b-4795-8f9c-f0a59414eaad)|`Filter`||
|6.1.2.11.1.8|[Format](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/c2022d53-37dc-4597-8321-728e385afc92)|`Format`||
|6.1.2.11.1.9|[Format$](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/dc8463ed-bfe6-48d6-b804-3e993880ae45)|`Format$`||
|6.1.2.11.1.10|[FormatCurrency](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/2cf6e81f-a693-4884-a211-814c8c95e5a2)|`FormatCurrency`||
|6.1.2.11.1.11|[FormatDateTime](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/dc56ab7e-dc09-4b50-90ae-d994460c99a6)|`FormatDateTime`||
|6.1.2.11.1.12|[FormatNumber](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/357ce1a8-bacd-4a69-942e-921a6bc76c6d)|`FormatNumber`||
|6.1.2.11.1.13|[FormatPercent](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/60b37972-e710-4c3a-a985-c71794a60739)|`FormatPercent`||
|6.1.2.11.1.14|[InStr / InStrB](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/33e81067-4b43-4c45-a501-0acf1616f2f9)|`InStr`, `InStrB`||
|6.1.2.11.1.15|[InStrRev](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/9d45a100-3a6d-4cb6-aa09-772ccffe9706)|`InStrRev`||
|6.1.2.11.1.16|[Join](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/1c276c6f-c3b0-4de7-ade5-7803f99eea44)|`Join`||
|6.1.2.11.1.17|[LCase](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/bf65d932-1cdf-49e9-9e12-35eb4d85f4f3)|`LCase`||
|6.1.2.11.1.18|[LCase$](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/7d774803-644b-4366-acd9-c72f50488c48)|`LCase$`||
|6.1.2.11.1.19|[Left / LeftB](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/82b33e27-ed2b-4eaf-9595-0e7a47457d64)|`Left`, `LeftB`||
|6.1.2.11.1.20|[Left$](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/5f10a9e1-b6ba-4a92-bd94-c4c99da74ef2)|`Left$`||
|6.1.2.11.1.21|[LeftB$](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/1afd1ea4-54ca-4bd6-8937-7ff97ab16f94)|`LeftB$`||
|6.1.2.11.1.22|[Len / LenB](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/a4d9985a-e2cb-489c-a20a-5da09d77a59e)|`Len`, `LenB`|See [§6.1.2.11.1.22](#61211122-len--lenb).|
|6.1.2.11.1.23|[LTrim / RTrim / Trim](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/053cdd5a-c8e2-4a0d-882b-46817b13243d)|`LTrim`, `RTrim`, `Trim`||
|6.1.2.11.1.24|[LTrim$ / RTrim$ / Trim$](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/ea1d57ea-b712-457b-b12f-0c4d6a5b9031)|`LTrim$`, `RTrim$`, `Trim$`||
|6.1.2.11.1.25|[Mid / MidB](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/4a65ee4e-b6b9-45d3-a3f6-576fed4bb227)|`Mid`, `MidB`||
|6.1.2.11.1.26|[Mid$](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/4430170b-0187-4a54-83b0-cafbfadf8127)|`Mid$`||
|6.1.2.11.1.27|[MidB$](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/e612f091-c89e-4e65-ad5a-836f76b183ec)|`MidB$`||
|6.1.2.11.1.28|[MonthName](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/da532aeb-53ec-495f-ae66-1bcfbd97ef0b)|`MonthName`||
|6.1.2.11.1.29|[Replace](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/6af9457c-70ea-4691-80d6-3dca9f152aee)|`Replace`||
|6.1.2.11.1.30|[Right / RightB](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/59107ed4-5d21-468e-8323-82620b2f0442)|`Right`, `RightB`||
|6.1.2.11.1.31|[Right$](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/975e2582-3fee-4006-a0b5-48be3984982c)|`Right$`||
|6.1.2.11.1.32|[RightB$](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/0be0e967-86d9-425d-aec5-e7c2b8855cea)|`RightB$`||
|6.1.2.11.1.33|[Space](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/39487af8-ae4f-4703-a7f9-b2b220e80981)|`Space`||
|6.1.2.11.1.34|[Space$](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/3610e96f-51cf-44f6-acbd-c5d5244d917b)|`Space$`||
|6.1.2.11.1.35|[Split](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/d9c2417c-2d36-45b1-b3d7-1876064e6085)|`Split`||
|6.1.2.11.1.36|[StrComp](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/9541f02a-e173-47a7-8c0f-4d91813c1c88)|`StrComp`||
|6.1.2.11.1.37|[StrConv](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/17afbc5b-e1c7-4717-bfe3-9ae938ef0736)|`StrConv`||
|6.1.2.11.1.38|[String](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/1bb5160c-7203-4c3e-8650-e5aac19477a8)|`String`||
|6.1.2.11.1.39|[String$](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/84f4df4e-9156-45db-b4d4-066d4848607b)|`String$`||
|6.1.2.11.1.40|[StrReverse](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/4d48b91d-d72d-4f6f-957c-53e556007e80)|`StrReverse`||
|6.1.2.11.1.41|[UCase](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/29b5f1d1-24b8-4bc5-82cf-8c98059a7116)|`UCase`||
|6.1.2.11.1.42|[UCase$](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/5a417dfc-c9ad-4e0a-97bc-138dd35c479a)|`UCase$`||
|6.1.2.11.1.43|[WeekdayName](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/09423417-ebcb-4aa5-bb73-2a8c1a19bd6a)|`WeekdayName`||

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
|A UDT|"the size as it will be written to the file"|"the in-memory size, including any implementation-specific padding between elements"|

#### UDT sizes

The UDT row is the one that needs the platform to know two different sizes for the same value. 🎯 A UDT has two
sizes: `Len` reports its serialized size (its members concatenated, with no padding), and `LenB` reports its
in-memory size, which is also what
[VBUserDefinedTypeValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBUserDefinedTypeValue.html)`.Size` reports.

[**RD-VBAL §2.5.2.1.3** User-Defined Type (UDT) Values](rd-vbal.2.5.2.1.3.udt-values.md) defines both UDT sizes and
holds the padding rule. `Strings.Len` and `Strings.LenB` make both sizes of a UDT observable to a program.

A record whose members include a variable-length `String` is the case MS-VBAL warns cannot be predicted: "`Len` might
not be able to determine the actual number of storage bytes required when used with variable-length strings in
user-defined data types". For such a record, `Len` counts the characters the member currently holds and `LenB` counts
the pointer, so the two move independently of each other.

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

---
> ⏮️ [**RD-VBAL §6.1.2.10** Math](rd-vbal.6.1.2.10.math.md) | ⏭️ [**RD-VBAL §6.1.2.12** SystemColorConstants](rd-vbal.6.1.2.12.systemcolorconstants.md)
