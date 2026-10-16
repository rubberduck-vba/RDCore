Attribute VB_Name = "Arrays"
Option Explicit
' Writing every element of an array, then reading every element back.
' 30000 elements: the size the recorded baselines were measured with.

Private Const Size As Long = 30000

Public Sub BenchArrayFillAndSum()
    Dim Values(1 To Size) As Long
    Dim I As Long
    Dim S As Double
    For I = 1 To Size
        Values(I) = I
    Next
    For I = 1 To Size
        S = S + Values(I)
    Next
End Sub