# Performance
<sup>_Ce document est disponible en [français](performance.fr.html)_</sup>

---
## What to expect

RD-VBA runs most of a program as compiled code. Each figure below is the time RD-VBA takes for a case of the `rdc bench` corpus, as a multiple of the time
MS-VBA takes for the same code in Excel: below 1× is faster, above 1× slower. Each badge also gives both times, in milliseconds, on the reference machine
(Excel 16, 64-bit, Windows 11 x64, 12 cores). The figures describe the default settings: the `Compiled` execution engine and the `Debug` program
configuration (`--configuration`, see [The shell and the rdc commands](rdc.md)).

The RD-VBA figures are measured on every merge. Every case of the corpus is listed, the slowest included. Each case links to its source, and
[Benchmarks](benchmarks/index.md) says how to run them on your own machine.

## Calls

| Case | What it measures | RD-VBA ÷ MS-VBA |
|---|---|---|
| [`Calls.BenchRecursion`](benchmarks/calls.md#benchrecursion) | Recursion: `Fib(25)`, 242,785 short calls | ![Calls.BenchRecursion](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Calls.BenchRecursion.json) |
| [`Calls.BenchProcedureCalls`](benchmarks/calls.md#benchprocedurecalls) | A function of the same module, called in a loop | ![Calls.BenchProcedureCalls](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Calls.BenchProcedureCalls.json) |
| [`Calls.BenchByRefCalls`](benchmarks/calls.md#benchbyrefcalls) | A variable passed `ByRef` and assigned through the parameter | ![Calls.BenchByRefCalls](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Calls.BenchByRefCalls.json) |
| [`Calls.BenchClassMemberCalls`](benchmarks/calls.md#benchclassmembercalls) | A member of a class, called through a variable of the class | ![Calls.BenchClassMemberCalls](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Calls.BenchClassMemberCalls.json) |
| [`Calls.BenchInterfaceMemberCalls`](benchmarks/calls.md#benchinterfacemembercalls) | The same member, called through an interface | ![Calls.BenchInterfaceMemberCalls](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Calls.BenchInterfaceMemberCalls.json) |
| [`Calls.BenchStandardLibraryCalls`](benchmarks/calls.md#benchstandardlibrarycalls) | `N = Len(CStr(I)) + CLng(I)` in a loop | ![Calls.BenchStandardLibraryCalls](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Calls.BenchStandardLibraryCalls.json) |
| [`Calls.BenchFactoryCalls`](benchmarks/calls.md#benchfactorycalls) | A factory method of a default instance, a new object on every call | ![Calls.BenchFactoryCalls](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Calls.BenchFactoryCalls.json) |
| [`Calls.BenchDefaultInstanceCalls`](benchmarks/calls.md#benchdefaultinstancecalls) | A member called on a default instance | ![Calls.BenchDefaultInstanceCalls](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Calls.BenchDefaultInstanceCalls.json) |
| [`Calls.BenchSiblingCalls`](benchmarks/calls.md#benchsiblingcalls) | A member that calls a private member of its own class by its bare name | ![Calls.BenchSiblingCalls](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Calls.BenchSiblingCalls.json) |

## Statements

| Case | What it measures | RD-VBA ÷ MS-VBA |
|---|---|---|
| [`Statements.BenchSetAssignment`](benchmarks/statements.md#benchsetassignment) | `Set D = C` of an object variable | ![Statements.BenchSetAssignment](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Statements.BenchSetAssignment.json) |
| [`Statements.BenchMemberAssignment`](benchmarks/statements.md#benchmemberassignment) | `P.X = I`, a property of an object | ![Statements.BenchMemberAssignment](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Statements.BenchMemberAssignment.json) |
| [`Statements.BenchRecordFieldAssignment`](benchmarks/statements.md#benchrecordfieldassignment) | `R.X = I`, a field of a user-defined type | ![Statements.BenchRecordFieldAssignment](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Statements.BenchRecordFieldAssignment.json) |
| [`Statements.BenchSelectCase`](benchmarks/statements.md#benchselectcase) | `Select Case I Mod 4` with three branches | ![Statements.BenchSelectCase](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Statements.BenchSelectCase.json) |
| [`Statements.BenchConditions`](benchmarks/statements.md#benchconditions) | `If`, `ElseIf` and `Do While` conditions: comparisons joined with `And` | ![Statements.BenchConditions](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Statements.BenchConditions.json) |
| [`Statements.BenchMidStatement`](benchmarks/statements.md#benchmidstatement) | The `Mid$` statement: `Mid$(S, 1, 1) = "r"` | ![Statements.BenchMidStatement](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Statements.BenchMidStatement.json) |
| [`Statements.BenchOnErrorResumeNext`](benchmarks/statements.md#benchonerrorresumenext) | A loop under `On Error Resume Next`, dividing by zero one time in ten | ![Statements.BenchOnErrorResumeNext](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Statements.BenchOnErrorResumeNext.json) |

## Arrays

| Case | What it measures | RD-VBA ÷ MS-VBA |
|---|---|---|
| [`Arrays.BenchArrayFillAndSum`](benchmarks/arrays.md#bencharrayfillandsum) | Writing, then reading, every element of a `Long` array | ![Arrays.BenchArrayFillAndSum](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Arrays.BenchArrayFillAndSum.json) |
| [`Arrays.BenchVariantArray2D`](benchmarks/arrays.md#benchvariantarray2d) | A 2-D `Variant` array, every element doubled | ![Arrays.BenchVariantArray2D](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Arrays.BenchVariantArray2D.json) |
| [`Arrays.BenchVariantHeldArray2D`](benchmarks/arrays.md#benchvariantheldarray2d) | The same, with the array held in a `Variant`, as `V = Range(...).Value` gives it | ![Arrays.BenchVariantHeldArray2D](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Arrays.BenchVariantHeldArray2D.json) |

## Strings

| Case | What it measures | RD-VBA ÷ MS-VBA |
|---|---|---|
| [`Strings.BenchStringAssignment`](benchmarks/strings.md#benchstringassignment) | A `String` assigned in a loop | ![Strings.BenchStringAssignment](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Strings.BenchStringAssignment.json) |
| [`Strings.BenchStringConcatenation`](benchmarks/strings.md#benchstringconcatenation) | `S = S & "x"`, a string growing by one character at a time | ![Strings.BenchStringConcatenation](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Strings.BenchStringConcatenation.json) |
| [`Strings.BenchStringComparison`](benchmarks/strings.md#benchstringcomparison) | Short strings joined and compared | ![Strings.BenchStringComparison](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Strings.BenchStringComparison.json) |
| [`Strings.BenchStringBuilding`](benchmarks/strings.md#benchstringbuilding) | A line of numbers written as text, with a separator between each | ![Strings.BenchStringBuilding](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Strings.BenchStringBuilding.json) |
| [`Strings.BenchStringArguments`](benchmarks/strings.md#benchstringarguments) | Strings passed to a procedure, by value and by reference | ![Strings.BenchStringArguments](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Strings.BenchStringArguments.json) |

## Numeric

| Case | What it measures | RD-VBA ÷ MS-VBA |
|---|---|---|
| [`Numeric.BenchLongCounterDoubleSum`](benchmarks/numeric.md#benchlongcounterdoublesum) | `S = S + I` with a `Long` counter and a `Double` sum | ![Numeric.BenchLongCounterDoubleSum](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Numeric.BenchLongCounterDoubleSum.json) |
| [`Numeric.BenchDoubleSum`](benchmarks/numeric.md#benchdoublesum) | The same loop over `Double` values | ![Numeric.BenchDoubleSum](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Numeric.BenchDoubleSum.json) |
| [`Numeric.BenchLongArithmetic`](benchmarks/numeric.md#benchlongarithmetic) | `Long` arithmetic, kept in range with `Mod` | ![Numeric.BenchLongArithmetic](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Numeric.BenchLongArithmetic.json) |
| [`Numeric.BenchVariantSum`](benchmarks/numeric.md#benchvariantsum) | The same loop with every operand a `Variant` | ![Numeric.BenchVariantSum](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Numeric.BenchVariantSum.json) |
| [`Numeric.BenchCurrencyAndDate`](benchmarks/numeric.md#benchcurrencyanddate) | A `Currency` sum and a `Date` advanced a thousandth of a day each time | ![Numeric.BenchCurrencyAndDate](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Numeric.BenchCurrencyAndDate.json) |
| [`Numeric.BenchVariantTypedMix`](benchmarks/numeric.md#benchvarianttypedmix) | A `Long` and a `Double` added into a `Variant` sum | ![Numeric.BenchVariantTypedMix](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Numeric.BenchVariantTypedMix.json) |

## Variables and parameters

| Case | What it measures | RD-VBA ÷ MS-VBA |
|---|---|---|
| [`Slots.BenchWideActivations`](benchmarks/variables-and-parameters.md#benchwideactivations) | A call with six typed parameters and four locals | ![Slots.BenchWideActivations](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Slots.BenchWideActivations.json) |
| [`Slots.BenchModuleVariableLoop`](benchmarks/variables-and-parameters.md#benchmodulevariableloop) | A module variable, read and written each time round | ![Slots.BenchModuleVariableLoop](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Slots.BenchModuleVariableLoop.json) |
| [`Slots.BenchLocalVariableLoop`](benchmarks/variables-and-parameters.md#benchlocalvariableloop) | The same loop over a local variable | ![Slots.BenchLocalVariableLoop](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Slots.BenchLocalVariableLoop.json) |
| [`Slots.BenchStaticVariableLoop`](benchmarks/variables-and-parameters.md#benchstaticvariableloop) | The same loop over a `Static` local | ![Slots.BenchStaticVariableLoop](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Slots.BenchStaticVariableLoop.json) |
| [`Slots.BenchCurrencySum`](benchmarks/variables-and-parameters.md#benchcurrencysum) | A `Currency` sum | ![Slots.BenchCurrencySum](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Slots.BenchCurrencySum.json) |
| [`Slots.BenchBooleanFlag`](benchmarks/variables-and-parameters.md#benchbooleanflag) | A `Boolean` flag flipped and tested each time round | ![Slots.BenchBooleanFlag](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Slots.BenchBooleanFlag.json) |

## Standard library

Each case calls one function 100,000 times, with the arguments shown.

| Case | What it measures | RD-VBA ÷ MS-VBA |
|---|---|---|
| [`StdLib.BenchCLng`](benchmarks/standard-library.md#benchclng) | `CLng(X)` of a `Double` | ![StdLib.BenchCLng](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchCLng.json) |
| [`StdLib.BenchCDbl`](benchmarks/standard-library.md#benchcdbl) | `CDbl(I)` of a `Long` | ![StdLib.BenchCDbl](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchCDbl.json) |
| [`StdLib.BenchCStr`](benchmarks/standard-library.md#benchcstr) | `CStr(I)` of a `Long` | ![StdLib.BenchCStr](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchCStr.json) |
| [`StdLib.BenchVal`](benchmarks/standard-library.md#benchval) | `Val(S)` of `"123.5"` | ![StdLib.BenchVal](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchVal.json) |
| [`StdLib.BenchLen`](benchmarks/standard-library.md#benchlen) | `Len(S)` of a `String` | ![StdLib.BenchLen](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchLen.json) |
| [`StdLib.BenchLeft`](benchmarks/standard-library.md#benchleft) | `Left$(S, 2)` | ![StdLib.BenchLeft](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchLeft.json) |
| [`StdLib.BenchMidFunction`](benchmarks/standard-library.md#benchmidfunction) | `Mid$(S, 4, 3)` | ![StdLib.BenchMidFunction](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchMidFunction.json) |
| [`StdLib.BenchInStr`](benchmarks/standard-library.md#benchinstr) | `InStr(S, "V")` | ![StdLib.BenchInStr](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchInStr.json) |
| [`StdLib.BenchReplace`](benchmarks/standard-library.md#benchreplace) | `Replace(S, "-", "_")` | ![StdLib.BenchReplace](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchReplace.json) |
| [`StdLib.BenchTrim`](benchmarks/standard-library.md#benchtrim) | `Trim$(S)` | ![StdLib.BenchTrim](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchTrim.json) |
| [`StdLib.BenchUCase`](benchmarks/standard-library.md#benchucase) | `UCase$(S)` | ![StdLib.BenchUCase](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchUCase.json) |
| [`StdLib.BenchStrComp`](benchmarks/standard-library.md#benchstrcomp) | `StrComp(S, "rd-vba", vbTextCompare)` | ![StdLib.BenchStrComp](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchStrComp.json) |
| [`StdLib.BenchAsc`](benchmarks/standard-library.md#benchasc) | `Asc(S)` | ![StdLib.BenchAsc](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchAsc.json) |
| [`StdLib.BenchChr`](benchmarks/standard-library.md#benchchr) | `Chr$(65 + I Mod 26)` | ![StdLib.BenchChr](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchChr.json) |
| [`StdLib.BenchFormat`](benchmarks/standard-library.md#benchformat) | `Format$(D, "0.00")` | ![StdLib.BenchFormat](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchFormat.json) |
| [`StdLib.BenchIsNumeric`](benchmarks/standard-library.md#benchisnumeric) | `IsNumeric(S)` of `"123.5"` | ![StdLib.BenchIsNumeric](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchIsNumeric.json) |
| [`StdLib.BenchIsEmpty`](benchmarks/standard-library.md#benchisempty) | `IsEmpty(V)` of an `Empty` `Variant` | ![StdLib.BenchIsEmpty](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchIsEmpty.json) |
| [`StdLib.BenchVarType`](benchmarks/standard-library.md#benchvartype) | `VarType(V)` of a `Variant` holding a `Double` | ![StdLib.BenchVarType](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchVarType.json) |
| [`StdLib.BenchTypeName`](benchmarks/standard-library.md#benchtypename) | `TypeName(V)` of the same | ![StdLib.BenchTypeName](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchTypeName.json) |
| [`StdLib.BenchAbs`](benchmarks/standard-library.md#benchabs) | `Abs(-I)` | ![StdLib.BenchAbs](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchAbs.json) |
| [`StdLib.BenchRound`](benchmarks/standard-library.md#benchround) | `Round(D, 2)` | ![StdLib.BenchRound](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchRound.json) |
| [`StdLib.BenchSqr`](benchmarks/standard-library.md#benchsqr) | `Sqr(I)` | ![StdLib.BenchSqr](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchSqr.json) |
| [`StdLib.BenchInt`](benchmarks/standard-library.md#benchint) | `Int(D)` | ![StdLib.BenchInt](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchInt.json) |
| [`StdLib.BenchDateAdd`](benchmarks/standard-library.md#benchdateadd) | `DateAdd("d", 1, T)` | ![StdLib.BenchDateAdd](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchDateAdd.json) |
| [`StdLib.BenchYear`](benchmarks/standard-library.md#benchyear) | `Year(T)` | ![StdLib.BenchYear](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchYear.json) |
| [`StdLib.BenchNow`](benchmarks/standard-library.md#benchnow) | `Now` | ![StdLib.BenchNow](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchNow.json) |
| [`StdLib.BenchUBound`](benchmarks/standard-library.md#benchubound) | `UBound(A)` of an array | ![StdLib.BenchUBound](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-StdLib.BenchUBound.json) |

## Excel automation

These cases call Excel's object model late-bound from VBA. MS-VBA runs the same code from a second Excel instance, so both sides cross a process
boundary. They need Excel, so they are measured in the reference run only: their badges give the reference run's date instead of a live figure. Their
source is on the [Excel automation](benchmarks/excel-automation.md) page.

| Case | What it measures | RD-VBA ÷ MS-VBA |
|---|---|---|
| Block of 1,000 × 10, read | `V = Range(...).Value` | ![Excel.Block1000x10.Read](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Excel.Block1000x10.Read.json) |
| Block of 1,000 × 10, pass | The loop over `V(R, C)`, no call to Excel | ![Excel.Block1000x10.Pass](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Excel.Block1000x10.Pass.json) |
| Block of 1,000 × 10, write | `Range(...).Value = V` | ![Excel.Block1000x10.Write](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Excel.Block1000x10.Write.json) |
| Block of 1,000 × 10, total | Read, pass and write | ![Excel.Block1000x10.Total](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Excel.Block1000x10.Total.json) |
| Cell by cell, 1,000 × 10 | 10,000 cells, a get and a put each | ![Excel.CellByCell1000x10.Total](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Excel.CellByCell1000x10.Total.json) |
| Block of 100,000 × 10, read | `V = Range(...).Value` | ![Excel.Block100000x10.Read](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Excel.Block100000x10.Read.json) |
| Block of 100,000 × 10, pass | The loop over `V(R, C)`, no call to Excel | ![Excel.Block100000x10.Pass](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Excel.Block100000x10.Pass.json) |
| Block of 100,000 × 10, write | `Range(...).Value = V` | ![Excel.Block100000x10.Write](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Excel.Block100000x10.Write.json) |
| Block of 100,000 × 10, total | Read, pass and write | ![Excel.Block100000x10.Total](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Excel.Block100000x10.Total.json) |

Every call to Excel's object model crosses from the program's process to Excel's and back, in MS-VBA and in RD-VBA. A crossing costs far more than the
work most calls do, so the number of calls decides how long the code takes: the cell-by-cell case calls Excel four times a cell, 40,000 times in all,
while a block is read in four calls and written in four.

## How the figures are measured

- **The reference run.** On the reference machine, MS-VBA and RD-VBA run the corpus in five alternated rounds (MS-VBA first in the first four, RD-VBA
  first in the fifth), and the Excel automation cases in three rounds, MS-VBA first. Each case runs seven times a round (five for Excel automation), and
  the figure is the fastest run. The reference run is repeated when the corpus changes.
- **On every merge.** A Windows build machine runs the corpus in three alternated rounds with two builds of `rdc`: the merged one, and the one the
  reference run used. A case's RD-VBA time is the reference run's, scaled by how the merged build compares with the reference build on that machine.
  Comparing the two builds on one machine keeps that machine's own speed out of the figure. The figures file (`bench.json`, on the `badges` branch)
  names that machine's processor.
- **Each run starts from a collected heap.** Before each timed run, `rdc bench` collects the heap, so whether a garbage collection falls inside a run
  depends only on what that run allocates and keeps. The time of a collection inside a run is part of the figure.
- **Allocation.** For each case, `rdc bench` also gives what its fastest run allocated and the garbage collections of each generation made while it ran
  (the `allocated` and `gc 0/1/2` columns). On every merge they are recorded beside the times in the figures file, for the merged build. They do not
  depend on the machine's speed. The badges give the ratio only.
- **Resolution.** `rdc bench` gives times to a tenth of a millisecond, so a case that takes under a millisecond is good to about ±0.05 ms.

## Writing code that runs well

- **Work with ranges in blocks.** Read a range into an array with one `Range(...).Value`, work on the array, and write it back with one assignment. The
  cell-by-cell and block cases above show the difference.
- **Keep a hot loop's values typed.** A `Long` or `Double` loop runs faster than in MS-VBA. The same loop over `Variant` values, or over an array held in
  a `Variant`, is slower.
- **See what runs compiled.** `rdc compilation` lists the procedures that do not run compiled, and what keeps each one in the interpreter. Code in the
  interpreter is slower than compiled code.
- **Measure your own code.** `rdc bench` times each `Public Sub Bench*()` of a workspace's standard modules. Run it before and after a change (see
  [The shell and the rdc commands](rdc.md)).

## See also

- [The shell and the rdc commands](rdc.md)
- [Getting started with RDCore](getting-started.md)

---
[ACCUEIL](index.fr.md) • [HOME](index.md) | ℹ️ [BIENVENUE](introduction.fr.md) • [WELCOME](introduction.html) | 🧩 [BÂTISSONS](getting-started.fr.md) • [BUILD](getting-started.html) | [**RD-VBAL**](/RDCore/specs/rd-vbal.html) | [SDK](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.html) | 🌐 [rubberduckvba.ca](https://rubberduckvba.ca)

---
