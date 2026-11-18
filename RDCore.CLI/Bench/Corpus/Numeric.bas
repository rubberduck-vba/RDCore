Attribute VB_Name = "Numeric"
Option Explicit
' Arithmetic in a tight loop. BenchLongCounterDoubleSum is the reference case the IR lowering plan's targets are stated against.

' <Declarations>
Private Const Iterations As Long = 1000000
' </Declarations>

' <BenchLongCounterDoubleSum>
' For I = 1 To 1000000: S = S + I: Next, with a Long counter and a Double sum (a Long sum overflows past I = 65536).
Public Sub BenchLongCounterDoubleSum()
    Dim I As Long
    Dim S As Double
    For I = 1 To Iterations
        S = S + I
    Next
End Sub
' </BenchLongCounterDoubleSum>

' <BenchDoubleSum>
Public Sub BenchDoubleSum()
    Dim I As Double
    Dim S As Double
    For I = 1 To Iterations
        S = S + I
    Next
End Sub
' </BenchDoubleSum>

' <BenchLongArithmetic>
' Long arithmetic only: the sum is kept in range by Mod.
Public Sub BenchLongArithmetic()
    Dim I As Long
    Dim S As Long
    For I = 1 To Iterations
        S = (S + I) Mod 1000003
    Next
End Sub
' </BenchLongArithmetic>

' <BenchVariantSum>
' the same loop with every operand a Variant: the effective type of each operation is decided from the values. The sum starts as a Double, so that
' each addition is of a Double and a Long.
Public Sub BenchVariantSum()
    Dim I As Variant
    Dim S As Variant
    S = 0#
    For I = 1 To Iterations
        S = S + I
    Next
End Sub
' </BenchVariantSum>

' <BenchCurrencyAndDate>
' fixed-point and date arithmetic: a Currency sum of a Long counter, and a Date a thousandth of a day further each time.
Public Sub BenchCurrencyAndDate()
    Dim I As Long
    Dim C As Currency
    Dim D As Date
    D = #1/1/2000#
    For I = 1 To Iterations
        C = C + I
        D = D + 0.001
    Next
End Sub
' </BenchCurrencyAndDate>

' <BenchVariantTypedMix>
' typed and Variant operands in one expression: a Long and a Double into a Variant sum, which decides its effective type from the values each time.
Public Sub BenchVariantTypedMix()
    Dim I As Long
    Dim D As Double
    Dim V As Variant
    V = 0
    D = 0.5
    For I = 1 To Iterations
        V = V + I * D
    Next
End Sub
' </BenchVariantTypedMix>
