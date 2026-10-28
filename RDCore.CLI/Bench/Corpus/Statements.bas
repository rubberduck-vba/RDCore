Attribute VB_Name = "Statements"
Option Explicit
' The statements that are not Let assignments of a variable: what each evaluates its target, its selector or its items with.

Private Type Pair
    X As Long
    Y As Long
End Type

Public Sub BenchSetAssignment()
    Dim C As Counter
    Set C = New Counter
    Dim D As Counter
    Dim I As Long
    For I = 1 To 100000
        Set D = C
    Next
End Sub

Public Sub BenchMemberAssignment()
    Dim P As Point
    Set P = New Point
    Dim I As Long
    For I = 1 To 100000
        P.X = I
    Next
End Sub

Public Sub BenchRecordFieldAssignment()
    Dim R As Pair
    Dim I As Long
    For I = 1 To 100000
        R.X = I
    Next
End Sub

Public Sub BenchSelectCase()
    Dim I As Long
    Dim S As Long
    For I = 1 To 100000
        Select Case I Mod 4
            Case 0
                S = S + 1
            Case 1, 2
                S = S + 2
            Case Else
                S = S + 3
        End Select
    Next
End Sub

' the conditions of If and of a loop: comparisons, And-ed.
Public Sub BenchConditions()
    Dim I As Long
    Dim S As Long
    Do While I < 100000
        I = I + 1
        If I Mod 3 = 0 And I > 10 Then
            S = S + 1
        ElseIf I Mod 5 = 0 Then
            S = S - 1
        End If
    Loop
End Sub

Public Sub BenchMidStatement()
    Dim S As String
    S = "RD-VBA"
    Dim I As Long
    For I = 1 To 100000
        Mid$(S, 1, 1) = "r"
    Next
End Sub

' a loop under On Error Resume Next, a division by zero one time in ten: what the error handler costs where it is in effect, and where it is used.
Public Sub BenchOnErrorResumeNext()
    On Error Resume Next
    Dim I As Long
    Dim D As Double
    For I = 1 To 100000
        D = 10 / (I Mod 10)
    Next
End Sub
