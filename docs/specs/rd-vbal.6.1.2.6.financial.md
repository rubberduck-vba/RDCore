# 6.1.2.6 Financial

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.6** Financial](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/368e9272-18bc-4632-a4c3-f7dd2c64b215).

The `Financial` module is represented in the SDK by the interface
[IStdFinancialModule](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdFinancialModule.html).

The `Financial` module is declared in full, and every member has a runtime implementation (`StdFinancial`). Its
thirteen functions of annuities, depreciation and series of cash flows all return `Double`, and each is a function of
its arguments alone.


## Errors

**MS-VBAL §6.1.2.6** names no error for any of its functions, so what each of them raises is decided here, once, for
the whole module:

|When|Error|
|---|---|
|An argument a function cannot compute with — a period outside the life or the annuity, no periods, a rate of `-1` for `NPer`, `NPV` or `MIRR`, a `Guess` of `-1` or less|`5`|
|An `IRR` series without both a payment and a receipt|`5`|
|`Rate` or `IRR` finds no result within 20 tries|`5`|
|A `Double` result overflows|`6`, rather than an infinity|
|`MIRR` has no payment to finance|`11`, the division by zero it is|
|An optional argument is `Null`|`94`|
|An optional argument is not a number, nor a `String` that reads as one|`13`|

A result that is infinite is error `6` — a present value at a rate of `-1`, say, which divides by (1+_r_)ⁿ = 0 —
except `MIRR`'s missing payment, which is error `11`.


## The Annuity Equation

`FV`, `PV`, `Pmt`, `NPer` and `Rate` are one equation solved for a different unknown each time: the present value
grown over the periods, plus every payment grown to the end, plus the future value, is zero. With a rate _r_ per
period over _n_ periods, and _d_ = 1 when payments are due at the start of a period:

```
pv·(1+r)ⁿ + pmt·(1+r·d)·((1+r)ⁿ−1)/r + fv = 0      (r ≠ 0)
pv + pmt·n + fv = 0                                (r = 0)
```

`IPmt` and `PPmt` split one payment into the interest the balance outstanding earns over the period, and the rest. A
`Due` other than `0` is the start of the period, any value other than `0` being `True`.


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

👉 MS-VBA's last digit of a `PV` or a `PPmt` can differ from RD-VBA's (`649.506100518608` and `649.506100518607`), and
its `Rate` converges a few units in the eleventh digit away: both compute the same equation in a different order, and
iterate it as the specification allows ("accurate to within 0.00001 percent").


## Iteration

`Rate` and `IRR` "cycle through the calculation until the result is accurate to within 0.00001 percent. If [they]
can't find a result after 20 tries, [they fail]." A try is one step of the iteration. A result is accurate once a root
is found within 0.00001 percent of it — of a rate that is itself a fraction, so `1E-07`: two points that straddle a
root are that close together, or a step that short lands between two points `1E-07` either side of it that straddle
one. A short step proves nothing on its own, since beside a steep point every step is short.

- The iteration runs on ln(1 + _r_) rather than on _r_, so that no step can reach a rate of `-1` or less. A result
  within `1E-07` of `-1` is no result.
- Each try is a step of the secant method until two points straddle a root, and from then on a step of regula falsi
  with the Anderson–Björck correction, which keeps the root between its last two points.
- Before two points straddle a root, a step is no longer than `1` in ln(1 + _r_) — twice that after a step cut short,
  and so on, so that a root that really is far away is still reached in a few. A step that lands where the imbalance
  has no finite value is taken back halfway for another try. A step from a stretch where the imbalance hardly changes
  would otherwise throw the iteration to a rate it cannot come back from.
- An imbalance of exactly `0`, at the guess or after it, is a result — a root the imbalance may only touch without
  changing sign, as −100·_r_² does at `0` — unless it is `0` either side of it too, as it is where an annuity of one
  period balances at every rate. A root the imbalance only touches is found only by landing on it, exactly or nearly:
  from the default guess `Rate(2, 200, -100, -300)` is error `5`, and from a guess of `1E-06` it is `1.6E-07`, more
  than `1E-07` from it.
- Rounding can make an imbalance `0` where there is no root, as it can make one change sign. Where an equation
  balances only to within rounding — a single period a unit or two in the last place from balancing, or cash flows
  with a root of three or more times — a result can come back with no root within `1E-07` of it. An annuity of one
  period that balances exactly can return a rate near the guess rather than error `5`.
- A root above about `5.5E+07` per period is error `5`, unless found by chance. The iteration's points are `Double`s
  in ln(1 + _r_), and from a rate of `1E+07` on, two next to each other are 2⁻⁴⁸ of 1 + _r_ apart as rates. Past about
  `5.6E+07` that is wider than the span `1E-07` either side of a point, within which a root is looked for.

Every form of the equation has the same root, but the iteration is only as quick as its function is straight, and each
form is nearly a straight line in ln(1 + _r_) in a different case. So the form is chosen by the case:

- A single amount against the amounts that balance it — `IRR`'s one payment and the receipts that return it, `Rate`'s
  loan and the payments that repay it, with a future value of the payments' sign or a smaller one of its own — is what
  the one side is worth over what the other is. Near the root that ratio, less `1`, is close to proportional to the
  rate over a long series. Well below the root, and well above it, one amount outweighs the others, and it is the
  ratio's logarithm that is. The two are joined without a kink at a ratio of e⁴, a point found by trial: any from
  e^2.5 to e^5 converged on every loan, mortgage and series of cash flows tried, and e⁴ took the fewest tries at the
  extremes.
- A savings plan with a bonus to start it is measured in two ways ([**RD-VBAL §6.1.2.6.1.11** Rate](#6126111-rate)).
- Anything else — payments saved towards a future value, compounding alone — is the logarithm of what is received over
  what is paid.


## Precision

The closed forms keep their accuracy where the obvious formula loses it:

- (1+_r_)ⁿ − 1 and ln(1 + _r_) are computed without the cancellation they suffer near a rate of `0`.
- A present value or a payment whose (1+_r_)ⁿ is too large for a `Double`, where the answer is not, is computed from
  (1+_r_)⁻ⁿ instead.
- `IPmt` takes the balance outstanding in a form whose terms share a sign for a loan, a savings plan and a balloon
  alike, where counting it forward from the loan or back from the future value subtracts two amounts that nearly
  cancel. That form's powers of (1+_r_) stay within a `Double` however many periods the annuity has.
- `PPmt` is computed as a principal that grows by the rate each period, not as the difference between a payment and an
  interest that is nearly all of it.


## Against the VB Runtime

RD-VBA was checked against the VB runtime's own functions as ported to .NET's `Microsoft.VisualBasic.Financial` — the
expected values of its tests, and 30,000 generated calls compared both with it and with exact arithmetic — though not
against MS-VBA itself.

Where the specification is silent they agree: `IPmt` and `PPmt` accept a period a fraction either side of 1 through
`NPer`, any `Due` other than `0` is the start of the period, `MIRR` with no payment is a division by zero, and `DDB`'s
first period is negative for a salvage value above the cost.

They differ where the member sections say, and in these:

- `Rate` and `IRR` iterate differently, so they solve different calls, in both directions. Over generated everyday
  loans, savings plans and investments, from the default guess, the VB runtime's `Rate` solves 1,459 of 1,781
  annuities. RD-VBA solves all of those but five, each a single period that balances within rounding at every rate,
  which leaves RD-VBA no rate to prefer; and RD-VBA solves 322 that it does not. The VB runtime's `IRR` solves 1,717
  of 1,919 series of up to 40 flows; RD-VBA solves all of those, and 202 more.
- Longer series widen the gap: the VB runtime's `IRR` does not solve the 361 flows of a 30-year mortgage from the
  default guess, and RD-VBA does.
- From a guess anywhere between -90 and 200 percent, the VB runtime's `Rate` solves 245 of 716 and its `IRR` 577 of
  837. RD-VBA solves all of those but one, another such single period, and 466 and 260 more.
- A result that overflows is an infinity there and error `6` here. One that is not a number at all is `NaN` there and
  error `5` here — except a present value or a payment whose (1+_r_)ⁿ overflows, and the interest in a period far into
  a long annuity, which are `NaN` there and their value here.


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
stops at the salvage value. What is left before a period is `Cost * (1 - Factor / Life)` raised to the number of
periods already gone, which also answers a fractional period.

"All arguments MUST be positive numbers", but an asset worth nothing at the end is an ordinary one, and a salvage
value above the cost gives a negative first period, as the VB runtime's own function does.

The VB runtime differs in three corners:

- Over a life of exactly `2` it ignores the factor, and over a life of less than `2` it takes the whole depreciable
  amount in every period. Here the factor applies, and a rate of 100 percent or more takes everything in the first
  period and nothing after.
- With a factor greater than the life, it returns a remainder for a later period whose (1 − `Factor / Life`) raised to
  the number of periods before it comes out positive and leaves a balance above the salvage value. Here it is `0`.

### 6.1.2.6.1.2 FV

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.6.1.2** FV](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/a869b0ff-5bda-40ef-8c36-33ece76a6844).

MS-VBAL's declaration omits `Optional` from `PV` and `Due`, where its own table says what an omitted one means, as the
Office reference does, and every other annuity function declares its `Due` optional. RD-VBA declares them optional.

### 6.1.2.6.1.3 IPmt

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.6.1.3** IPmt](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/e8e71a40-b540-4294-89fe-c64453ae83d0).

The period is "in the range 1 through NPer", which a fraction of a period either side of it still is, as it is to the
VB runtime's own function: a period greater than `0` and less than `NPer + 1` is accepted, and any other is error `5`.

### 6.1.2.6.1.4 IRR

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.6.1.4** IRR](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/526c8b08-9ba0-4a24-9032-bd542062dd25).

"The array MUST contain at least one negative value (a payment) and one positive value (a receipt)": without both
there is no rate at which the flows balance, and a series without them is error `5` rather than an iteration that
fails. The internal rate of return is the rate at which what the flows pay out is worth what they bring in, measured
at the first flow, where their value is `0` exactly where `NPV`'s is.

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
nearly the whole payment has a principal of nearly nothing: the VB runtime's own function answers `0` there, and
RD-VBA the exact value.

### 6.1.2.6.1.10 PV

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.6.1.10** PV](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/2dd12466-9a47-4b64-a81a-21ee598c0b83).

`PV`'s description is the one that leaves unsaid what an omitted `FV` stands for. RD-VBA takes it as its siblings'
descriptions do: `0`.

### 6.1.2.6.1.11 Rate

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.6.1.11** Rate](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/23d51c12-c6ec-4c10-85f8-80e3cb7e573b).

`Rate` iterates as `IRR` does ([Iteration](#iteration)).

A savings plan with a bonus to start it — payments towards a larger future value, with a present value of the same
sign — can have a second root. No root lies above the rate at which the payments are only the interest on the present
value, |_pmt_| / (|_pv_| − _d_·|_pmt_|) where that is positive: the imbalance in future value is _pv_ + _fv_ there,
and above it only grows. With payments due at the start and a bonus of no more than one payment there is no such rate,
and only the one root.

From a guess between the two roots, the ratio of what is received to what is paid mostly heads for the second. But the
imbalance in future value, which grows with (1+_r_)ⁿ, is lowest near the second root, and falls towards the rate from
most guesses between them. So a bracket is sought on that imbalance, compressed to its sign times ln(1 + |imbalance| /
scale) — or on the ratio, from a guess where the compressed imbalance is falling towards `0`: below the rate, where it
is all but flat, or just below the second root. The bracket is closed on the logarithm of what is received over what
is paid, if that changes sign across it too: it is nearly straight around both roots, where the compressed imbalance
is all but flat below the rate and all but a step at the second root.

From a guess at or above the interest-only rate, the root found first is usually the second. A lower one is then
looked for below it, with the tries left: from the lower end of the bracket the first root was found in, or from just
below a guess that is itself a root, heading down until the iteration brackets or lands on a root. It is the result if
it is found in time, and the first root if not — as it is where there is no lower root, or none that can be reached.

The second root still comes back from a guess between it and the interest-only rate, or between the roots near it;
where the two roots are less than 0.01 apart in ln(1 + _r_), from a guess below the second; and from some guesses
above the interest-only rate, from which the iteration settles on the second root without ever bracketing it.

Where a loan ends and a savings plan begins — where the future value is the larger of the two — was found by trial.
Over savings plans with a bonus of 0.01 to 100 payments and over annuities with random amounts, it left fewer calls
unsolved than drawing the line at 3, 10, 30 or 100 times the present value, or not at all.

Against the VB runtime:

- Of 8,000 savings plans with a bonus to start them, the VB runtime's `Rate` solves 6,922 from the default guess and
  RD-VBA 7,975, every one of the VB runtime's among them.
- Of 7,441 more, each built from a savings rate and with a bonus of 0.01 to 100 payments, RD-VBA solves all from the
  default guess and the VB runtime 5,048, finding the rate the plan was built from in 7,155 and 4,581. Where both
  solve one, they find different roots of 566. In 468 of those the guess is at or above the interest-only rate, and
  RD-VBA finds the lower root where the VB runtime takes the upper; in 90 the guess is between the roots, and RD-VBA
  takes the upper root where the VB runtime finds the lower.
- From a guess anywhere between -90 and 200 percent, over 7,485 more plans built the same way, RD-VBA solves 7,372 and
  the VB runtime 775, every one of them among RD-VBA's.
- `Rate(10, 0, 1000)` answers `-0.906` there, a rate at which the equation has fallen below its tolerance without
  balancing. Here it is error `5`: `1000` grows at any rate, with nothing to offset it.
- `Rate` of an annuity whose true rate is `0` fails there, because its test of a result is how far the equation is
  from balancing, which near a rate of `0` is noise above its tolerance. Here it is within `1E-07` of `0`.

---
> ⏮️ [**RD-VBAL §6.1.2.5** FileSystem](rd-vbal.6.1.2.5.filesystem.md) | ⏭️ [**RD-VBAL §6.1.2.7**
> Information](rd-vbal.6.1.2.7.information.md)
