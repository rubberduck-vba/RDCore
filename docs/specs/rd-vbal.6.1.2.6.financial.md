# 6.1.2.6 Financial

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.6** Financial](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/368e9272-18bc-4632-a4c3-f7dd2c64b215).

The `Financial` module is represented in the SDK by the interface
[IStdFinancialModule](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdFinancialModule.html).

The `Financial` module is declared in full, and every member has a runtime implementation (`StdFinancial`). Its
thirteen functions of annuities, depreciation and series of cash flows all return `Double`, and each is a function of
its arguments alone.


## MS-VBA's Algorithm

The specification states what each function computes, not how — and it is the how that decides the last digits of a
result, and which arguments have one at all. RD-VBA computes each function the way MS-VBA does: the VB runtime's
algorithm, which .NET's `Microsoft.VisualBasic.Financial` ports, evaluated as MS-VBA's x87 code evaluates it — each
statement's expression in the 80-bit extended format
([**RD-VBAL §5.6.9.3**](rd-vbal.5.6.9.3.arithmetic-operators.md#extended-intermediates)), its value stored in a
`Double`, and a power by the platform's `Math.Pow`. A few places differ from the port, as MS-VBA does:

- `FV`'s payments term is `pmt·(1+r·d)·((1+r)ⁿ−1)/r`, multiplied before it is divided by the rate, and `IPmt` computes
  the balance it takes the interest on with that `FV`.
- `NPV` keeps its discount and its total in 80 bits from one flow to the next, and `IRR` and `MIRR` measure a rate
  with that `NPV`.
- `SYD` keeps the depreciable amount over the sum of the digits in 80 bits.

What a function returns is MS-VBA's to the last bit, recorded in Excel (grid `oracle\s43`: 30 random calls of each
function, and the calls of the tests). Where the exact answer is another — a principal of nearly nothing, which as the
payment less the interest is all rounding; a rate the iteration does not reach — RD-VBA answers what MS-VBA answers.


## Errors

**MS-VBAL §6.1.2.6** names no error for any of its functions; these are MS-VBA's:

|When|Error|
|---|---|
|An argument a function cannot compute with — a period outside the life or the annuity, no periods, a rate of `-1` or less for `NPer`, a rate of `-1` for `NPV` or `MIRR`, an `IRR` `Guess` of `-1` or less, fewer than two flows for `IRR` or `MIRR`|`5`|
|`Rate` or `IRR` finds no result ([Iteration](#iteration))|`5`|
|A result overflows, or is no number — an infinity over an infinity, of a (1+_r_)ⁿ past the range of a `Double`|`6`|
|`MIRR` has no payment to finance|`11`, the division by zero it is|
|An optional argument is `Null`|`94`|
|An optional argument is not a number, nor a `String` that reads as one|`13`|

`PV(0.05, 20000, -100)`, `Pmt(0.05, 20000, 1000)` and the interest of a period of that annuity are error `6`: (1.05)²⁰⁰⁰⁰
is past the range of a `Double`, though the exact answers — the perpetuity, the interest alone — are not.


## The Annuity Equation

`FV`, `PV`, `Pmt`, `NPer` and `Rate` are one equation solved for a different unknown each time: the present value
grown over the periods, plus every payment grown to the end, plus the future value, is zero. With a rate _r_ per
period over _n_ periods, and _d_ = 1 when payments are due at the start of a period:

```
pv·(1+r)ⁿ + pmt·(1+r·d)·((1+r)ⁿ−1)/r + fv = 0      (r ≠ 0)
pv + pmt·n + fv = 0                                (r = 0)
```

`IPmt` is the rate times the balance outstanding at the start of the period: the future value of the periods before it.
A payment due at the start of the first period is made before anything has earned interest, and its interest is `0`;
one due at the start of a later period is counted from a present value the first payment has reduced, at the end of
the period before. `PPmt` is the payment less the interest. A `Due` other than `0` is the start of the period, any value
other than `0` being `True`.


## Omitted Optionals

Every optional argument of the module is declared `Variant` with no default. The specification states the value an
omitted one stands for in its description instead: `0` for a present value and for `Due`, `0.1` for a `Guess`, `2` for
a `Factor`, and `0` for a future value.

The interpreter fills an omitted `Variant` optional with Missing, and the module is given that, and a caller's own
Missing passed on, as left out ([**RD-VBAL §5.3.1.11** Procedure Invocation Argument
Processing](rd-vbal.5.3.1.11.procedure-invocation-argument-processing.md#standard-library-calls)). An explicit `Empty`
is not left out: it is `0`, as `Empty` Let-coerced to a `Double` is
([**RD-VBAL §5.5.1.2.11**](rd-vbal.5.5.1.2.runtime-semantics.md#551211-let-coercion-from-empty)). That changes a
result only where the default is not `0`: an explicit `Empty` `Guess` to `Rate` or `IRR` is a guess of `0`, and an
explicit `Empty` `Factor` to `DDB` is a factor of `0`, which is error `5`.

A `String` optional argument is Let-coerced to a `Double`, as a required parameter's argument is: read in the regional
settings of the session ([**RD-VBAL §5.5.1.2.4**](rd-vbal.5.5.1.2.runtime-semantics.md#regional-settings)), so that
`Pmt(0.1, 12, -1000, "0", "1")` is `Pmt(0.1, 12, -1000, 0, 1)`, as **MS-VBA** has it (recorded in Excel). One that reads
as no number is error `13`.


## Iteration

`Rate` and `IRR` "cycle through the calculation until the result is accurate to within 0.00001 percent. If [they]
can't find a result after 20 tries, [they fail]." MS-VBA's iteration is the secant method, of at most 40 steps:

- `Rate` starts from the guess and from half of it, where the equation's imbalance at the guess is positive, or twice
  it; it has found the rate once the imbalance is within `1E-07` of `0`.
- `IRR` starts from the guess and from `1E-05` beside it, toward where the net present value falls; it has found the
  rate once a step moved it less than `1E-07` and the net present value is within `1E-09` of the largest flow. A step
  to `-1` or below goes halfway from the last rate to `-1` instead.
- Two points with the same value take a step of `1E-05` aside; if that has the same value too, there is no result.

So what MS-VBA finds depends on the guess, and on the shape of the equation near it. From the default guess it does
not reach the rate of a 30-year mortgage's 361 flows, nor `Rate(10, -100, 1000)`, whose imbalance near a rate of `0` is
noise above its tolerance; from a guess far from the rate, a loan's rate may be out of reach; and where the equation has
two roots — a savings plan with a bonus to start it — it can find either. Each is error `5` where it finds none, and
the root it lands on where it does: `Rate(12, -1000, 500, 800, 0, 3)` is `-1.25`, which is no rate at all, and
`Rate(10, 0, 1000)` is `-0.906`, where `1000` grown with nothing to offset it has fallen below the tolerance on the
way to `-1`.


## 6.1.2.6.1 Public Functions

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.6.1** Public Functions](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/89736f0d-d6fa-47a3-9472-ac8ee640c77d).

|§|Member|Notes|
|---|---|---|
|6.1.2.6.1.1|[DDB](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/3f4a1112-5869-4672-a010-735bcf07d757)|See [§6.1.2.6.1.1](#612611-ddb).|
|6.1.2.6.1.2|[FV](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/a869b0ff-5bda-40ef-8c36-33ece76a6844)|See [§6.1.2.6.1.2](#612612-fv).|
|6.1.2.6.1.3|[IPmt](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/e8e71a40-b540-4294-89fe-c64453ae83d0)|See [§6.1.2.6.1.3](#612613-ipmt).|
|6.1.2.6.1.4|[IRR](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/526c8b08-9ba0-4a24-9032-bd542062dd25)|See [§6.1.2.6.1.4](#612614-irr).|
|6.1.2.6.1.5|[MIRR](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/34743400-17d9-45e2-83f4-ab9522f6d598)|See [§6.1.2.6.1.5](#612615-mirr).|
|6.1.2.6.1.6|[NPer](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/b924c22b-af59-4907-8423-d891aa06f4e6)||
|6.1.2.6.1.7|[NPV](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/be4a0376-1933-4fa0-a144-e8074b66da14)||
|6.1.2.6.1.8|[Pmt](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/26a4e5ea-07e1-42d6-8ac6-58c37ac3c406)||
|6.1.2.6.1.9|[PPmt](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/a39ac6ed-01c0-4d16-a588-64b0f2391f64)|See [§6.1.2.6.1.9](#612619-ppmt).|
|6.1.2.6.1.10|[PV](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/2dd12466-9a47-4b64-a81a-21ee598c0b83)|See [§6.1.2.6.1.10](#6126110-pv).|
|6.1.2.6.1.11|[Rate](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/23d51c12-c6ec-4c10-85f8-80e3cb7e573b)|See [§6.1.2.6.1.11](#6126111-rate).|
|6.1.2.6.1.12|[SLN](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/b90ea8bf-b3d1-4996-b659-d6ef4c2c908b)||
|6.1.2.6.1.13|[SYD](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/688e1306-bcfd-4a5e-a27b-2463ed3b83cb)||

### 6.1.2.6.1.1 DDB

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.6.1.1** DDB](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/3f4a1112-5869-4672-a010-735bcf07d757).

MS-VBAL's formula, "Depreciation / Period = ((Cost - Salvage) * Factor) / Life", does not depend on the period, so it
cannot be the one that computes it. A declining balance loses `Factor / Life` of what is left of it each period, and
stops at the salvage value: what is left before a period is `Cost` declined by (1 − `Factor / Life`) for each period
already gone, which also answers a fractional period.

"All arguments MUST be positive numbers", but an asset worth nothing at the end is an ordinary one, and a salvage value
above the cost gives a negative first period. MS-VBA takes the whole depreciable amount in every period of a life under
`2`, and in the first period of a life of exactly `2`, whatever the factor; and a factor greater than the life declines
the cost by a negative (1 − `Factor / Life`), whose even powers are positive: `DDB(1000, 100, 3, 3, 4)` is `11.11`.

### 6.1.2.6.1.2 FV

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.6.1.2** FV](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/a869b0ff-5bda-40ef-8c36-33ece76a6844).

MS-VBAL's declaration omits `Optional` from `PV` and `Due`, where its own table says what an omitted one means, as the
Office reference does, and every other annuity function declares its `Due` optional. RD-VBA declares them optional.

### 6.1.2.6.1.3 IPmt

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.6.1.3** IPmt](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/e8e71a40-b540-4294-89fe-c64453ae83d0).

The period is "in the range 1 through NPer", which a fraction of a period either side of it still is, as it is to
MS-VBA: a period greater than `0` and less than `NPer + 1` is accepted, and any other is error `5`.

### 6.1.2.6.1.4 IRR

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.6.1.4** IRR](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/526c8b08-9ba0-4a24-9032-bd542062dd25).

"The array MUST contain at least one negative value (a payment) and one positive value (a receipt)": without both
there is no rate at which the flows balance, and the iteration finds none — error `5`. The internal rate of return is
the rate at which the flows' net present value is `0` ([Iteration](#iteration)).

`ValueArray` is declared `Double()` through
[StdLibArrayAttribute](../api/RDCore.SDK.Runtime.Abstract.StdLib.StdLibArrayAttribute.html) ([**RD-VBAL §6.0.1**
Symbol Injection](rd-vbal.6.0.standard-library.md#601-symbol-injection)), and so is `NPV`'s and `MIRR`'s.

### 6.1.2.6.1.5 MIRR

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.6.1.5** MIRR](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/34743400-17d9-45e2-83f4-ab9522f6d598).

The modified rate is the receipts, reinvested to the last flow, over the payments, financed to the first. With no
payment that is a division by zero, error `11`. With no receipt, nothing comes back, which is a return of `-1`, or
-100 percent, and not an error.

The parameters keep the specification's names, underscores included: `Finance_Rate` and `Reinvest_Rate`.

### 6.1.2.6.1.9 PPmt

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.6.1.9** PPmt](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/a39ac6ed-01c0-4d16-a588-64b0f2391f64).

The period is accepted as it is for `IPmt` ([**RD-VBAL §6.1.2.6.1.3** IPmt](#612613-ipmt)). A loan whose interest is
nearly the whole payment has a principal of nearly nothing, which as the payment less the interest is all rounding:
`PPmt(0.5, 1, 100, -1000)` is `0`, as MS-VBA has it, where the exact principal is `1.2E-15`.

### 6.1.2.6.1.10 PV

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.6.1.10** PV](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/2dd12466-9a47-4b64-a81a-21ee598c0b83).

`PV`'s description is the one that leaves unsaid what an omitted `FV` stands for. RD-VBA takes it as its siblings'
descriptions do: `0`.

👉 MS-VBA assigns the `NaN` of `PV(0.05, 20000, -100)` to the variable it is assigned to before raising error `6`, which
none of the other functions do; RD-VBA raises the error and assigns nothing.

### 6.1.2.6.1.11 Rate

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.6.1.11** Rate](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/23d51c12-c6ec-4c10-85f8-80e3cb7e573b).

`Rate` iterates as [Iteration](#iteration) describes. A savings plan with a bonus to start it — payments towards a
larger future value, with a present value of the same sign — can have a second root, below the rate at which the
payments are only the interest on the present value; from a guess near it, `Rate` can find it rather than the rate of
the savings.

---
> ⏮️ [**RD-VBAL §6.1.2.5** FileSystem](rd-vbal.6.1.2.5.filesystem.md) | ⏭️ [**RD-VBAL §6.1.2.7**
> Information](rd-vbal.6.1.2.7.information.md)
