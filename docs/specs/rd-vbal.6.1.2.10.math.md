# 6.1.2.10 Math

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.10** Math](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/4f944a0e-59e2-432f-a6ed-8e54311690d1).

The `Math` module is represented in the SDK by the interface
[IStdMathModule](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdMathModule.html).

The `Math` module's declarations follow MS-VBAL's own _Function Declaration_ blocks (**MS-VBAL §6.1.2.10**),
declaration by declaration, including their return types and their `Optional` parameters.

The specification says little more of these functions than what each one computes: the type of most results, the
errors, and the whole of `Rnd` and `Randomize` are left to the implementation. RD-VBA does what MS-VBA does, recorded
in Excel (64-bit, en-US), and the notes below say what that is.

An argument declared `Double` is Let-coerced to `Double` by the call, before the function sees it: `Null` is error 94,
a string that is not a number is error 13, and `Empty` is `0`. An object, given to any parameter, is read as its
simple data value - what its default member returns - as for the `Conversion` module
([**RD-VBAL §6.1.2.3** An object argument](rd-vbal.6.1.2.3.conversion-module.md#an-object-argument)): `Abs` of an
object whose default member returns `Null` is `Null`, and `Nothing` is error 91.

## 6.1.2.10.1 Public Functions

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.10.1** Public Functions](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/443a9a6f-be5d-4f74-bd58-769b445c46e3).

|§|Member|Notes|
|---|---|---|
|6.1.2.10.1.1|[Abs](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/24ae4ed9-0c63-47a4-bf49-571cacde6816)|The result has the type of the argument: a `Date` stays a `Date`, and a `Boolean` is an `Integer` (`Abs(True)` is `1`). The smallest value of a type has no absolute value of that type, and is error 6 - except for a `Long`: 👉 MS-VBA answers `Abs` of the smallest `Long` with that same negative `Long`, and so does RD-VBA. `Integer`, `LongLong` and `Currency` overflow.|
|6.1.2.10.1.2|[Atn](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/d46b696e-7b4a-4da4-9573-3ce16800d5d4)||
|6.1.2.10.1.3|[Cos](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/121d275b-00dc-42ee-b12f-bd53689a6c5e)|An angle of 2<sup>63</sup> radians or more, either sign, is error 5. 👉 Otherwise the result is the platform's, within a unit of the last place of the true value as MS-VBA's is, and MS-VBA's to the last bit for all but about one argument in forty (recorded in Excel, `oracle\s43`: `Cos(1)` is one).|
|6.1.2.10.1.4|[Exp](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/d9a53c4b-4849-43e8-b8ef-8ead54e9e2e6)|"An error occurs" is error 6: the result would be greater than the largest `Double`.|
|6.1.2.10.1.5|[Log](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/29162478-e756-4ab0-a7ba-466e0d11bcd2)|A number not greater than zero is error 5.|
|6.1.2.10.1.6|[Rnd](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/d6cdff3d-627b-4203-9ea3-bca5def01444)|MS-VBA's generator, so that a seeded sequence is MS-VBA's: a 24-bit linear congruential generator (×`&H43FD43FD`, +`&HC39EC3`, modulo 2<sup>24</sup>) whose state is the last number it generated. A negative number seeds it with the bits of the `Single` it is. Its state is the session's ([ISessionRandomNumberGenerator](../api/RDCore.SDK.Runtime.Abstract.Execution.ISessionRandomNumberGenerator.html)), and starts where a new MS-VBA process does: the first `Rnd` is `0.7055475`. An `End` resets it with the rest of the session. The argument is Let-coerced to `Single`.|
|6.1.2.10.1.7|[Round](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/7e05d3eb-503d-4e32-8ab9-5dea04531845)|A tie rounds to even. A `Double` is scaled to the digits it keeps, rounded, and scaled back, so a tie is a tie as the scaled `Double` is: `Round(2.345, 2)` is `2.35`, and `Round(1.005, 2)` is `1`. A `Currency` and a `Decimal` round as the decimal numbers they are (`Round(CCur(2.345), 2)` is `2.34`). The result has the type of the argument, a `Boolean` as an `Integer`; a `String` is Let-coerced to `Double`. A negative number of digits is error 5, `Null` included. More than 22 digits is error 5 for anything rounded as a `Double` (a `Single`, a `Date`, a `String`); a whole number, a `Currency` and a `Decimal` take any number of digits. A `Currency` rounded past the largest one is error 6. `Round(-0.5)` is a negative zero, `-0`.|
|6.1.2.10.1.8|[Sgn](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/b7bbdcd3-c8fc-4f42-b362-18ace8f2be55)|An `Integer`, of any number, `Date`, `Boolean` or numeric `String`. `Null` is error 94.|
|6.1.2.10.1.9|[Sin](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/3aa1e60f-bd65-48da-abf3-96bfb17e22eb)|As `Cos`.|
|6.1.2.10.1.10|[Sqr](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/46c6000d-c501-44f7-9270-60d9a13fc8d4)|A negative number is error 5; the square root of `0` is `0`.|
|6.1.2.10.1.11|[Tan](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/9bf01a1b-d5b3-40b5-ab72-d9eca988dc40)|As `Cos`. The tangent is MS-VBA's: the sine over the cosine, divided in 80 bits — the platform's own tangent differs from it in the last bit for one argument in three.|


## 6.1.2.10.2 Public Subroutines

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.10.2** Public Subroutines](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/d968e1a4-384e-4a48-896e-523f4ccec631).

|§|Member|Notes|
|---|---|---|
|6.1.2.10.2.1|[Randomize](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/d9eb757d-88e0-4842-8d10-d0bbd7f0ac92)|The argument is Let-coerced to `Double`, and the high half of its bits is folded into the upper two bytes of the generator's state; the low byte stays what it was, so what follows depends on where the generator was. An omitted argument seeds it from the session's clock (`Timer`). 👉 The specification seeds from the timer for `Empty` too; MS-VBA Let-coerces `Empty` like any other argument, to `0`, and RD-VBA does what MS-VBA does.|

---
> ⏮️ [**RD-VBAL §6.1.2.9** KeyCodeConstants](rd-vbal.6.1.2.9.keycodeconstants.md) | ⏭️ [**RD-VBAL §6.1.2.11** Strings](rd-vbal.6.1.2.11.strings.md)
