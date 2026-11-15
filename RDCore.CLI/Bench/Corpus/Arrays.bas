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

' What a macro does with the values of a range: a 2-D Variant array, every element read and written back doubled. Here the array is a variable of its own.
Public Sub BenchVariantArray2D()
    Dim Values() As Variant
    ReDim Values(1 To Size \ 100, 1 To 100)
    Dim R As Long
    Dim C As Long
    For R = 1 To Size \ 100
        For C = 1 To 100
            Values(R, C) = R + C
        Next
    Next
    For R = 1 To Size \ 100
        For C = 1 To 100
            Values(R, C) = Values(R, C) * 2
        Next
    Next
End Sub

' The same, with the array held in a Variant: what V = Range(...).Value gives.
Public Sub BenchVariantHeldArray2D()
    Dim Values() As Variant
    ReDim Values(1 To Size \ 100, 1 To 100)
    Dim R As Long
    Dim C As Long
    For R = 1 To Size \ 100
        For C = 1 To 100
            Values(R, C) = R + C
        Next
    Next
    Dim V As Variant
    V = Values
    For R = 1 To Size \ 100
        For C = 1 To 100
            V(R, C) = V(R, C) * 2
        Next
    Next
End Sub