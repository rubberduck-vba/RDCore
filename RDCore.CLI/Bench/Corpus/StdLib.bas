Attribute VB_Name = "StdLib"
Option Explicit
' One call of a standard-library function per iteration, a function a case: conversions, strings, information, math, dates and arrays. What each
' returns is assigned to a variable of its type.

Private Const Iterations As Long = 100000

Public Sub BenchCLng()
    Dim I As Long
    Dim X As Double
    Dim N As Long
    X = 1.5
    For I = 1 To Iterations
        N = CLng(X)
    Next
End Sub

Public Sub BenchCDbl()
    Dim I As Long
    Dim D As Double
    For I = 1 To Iterations
        D = CDbl(I)
    Next
End Sub

Public Sub BenchCStr()
    Dim I As Long
    Dim S As String
    For I = 1 To Iterations
        S = CStr(I)
    Next
End Sub

Public Sub BenchVal()
    Dim I As Long
    Dim S As String
    Dim D As Double
    S = "123.5"
    For I = 1 To Iterations
        D = Val(S)
    Next
End Sub

Public Sub BenchLen()
    Dim I As Long
    Dim S As String
    Dim N As Long
    S = "RD-VBA"
    For I = 1 To Iterations
        N = Len(S)
    Next
End Sub

Public Sub BenchLeft()
    Dim I As Long
    Dim S As String
    Dim T As String
    S = "RD-VBA"
    For I = 1 To Iterations
        T = Left$(S, 2)
    Next
End Sub

Public Sub BenchMidFunction()
    Dim I As Long
    Dim S As String
    Dim T As String
    S = "RD-VBA"
    For I = 1 To Iterations
        T = Mid$(S, 4, 3)
    Next
End Sub

Public Sub BenchInStr()
    Dim I As Long
    Dim S As String
    Dim N As Long
    S = "RD-VBA"
    For I = 1 To Iterations
        N = InStr(S, "V")
    Next
End Sub

Public Sub BenchReplace()
    Dim I As Long
    Dim S As String
    Dim T As String
    S = "RD-VBA"
    For I = 1 To Iterations
        T = Replace(S, "-", "_")
    Next
End Sub

Public Sub BenchTrim()
    Dim I As Long
    Dim S As String
    Dim T As String
    S = "  RD-VBA  "
    For I = 1 To Iterations
        T = Trim$(S)
    Next
End Sub

Public Sub BenchUCase()
    Dim I As Long
    Dim S As String
    Dim T As String
    S = "rd-vba"
    For I = 1 To Iterations
        T = UCase$(S)
    Next
End Sub

Public Sub BenchStrComp()
    Dim I As Long
    Dim S As String
    Dim N As Long
    S = "RD-VBA"
    For I = 1 To Iterations
        N = StrComp(S, "rd-vba", vbTextCompare)
    Next
End Sub

Public Sub BenchAsc()
    Dim I As Long
    Dim S As String
    Dim N As Long
    S = "RD-VBA"
    For I = 1 To Iterations
        N = Asc(S)
    Next
End Sub

Public Sub BenchChr()
    Dim I As Long
    Dim T As String
    For I = 1 To Iterations
        T = Chr$(65 + I Mod 26)
    Next
End Sub

Public Sub BenchFormat()
    Dim I As Long
    Dim D As Double
    Dim T As String
    D = 1234.5678
    For I = 1 To Iterations
        T = Format$(D, "0.00")
    Next
End Sub

Public Sub BenchIsNumeric()
    Dim I As Long
    Dim S As String
    Dim B As Boolean
    S = "123.5"
    For I = 1 To Iterations
        B = IsNumeric(S)
    Next
End Sub

Public Sub BenchIsEmpty()
    Dim I As Long
    Dim V As Variant
    Dim B As Boolean
    For I = 1 To Iterations
        B = IsEmpty(V)
    Next
End Sub

Public Sub BenchVarType()
    Dim I As Long
    Dim V As Variant
    Dim N As Long
    V = 1.5
    For I = 1 To Iterations
        N = VarType(V)
    Next
End Sub

Public Sub BenchTypeName()
    Dim I As Long
    Dim V As Variant
    Dim T As String
    V = 1.5
    For I = 1 To Iterations
        T = TypeName(V)
    Next
End Sub

Public Sub BenchAbs()
    Dim I As Long
    Dim N As Long
    For I = 1 To Iterations
        N = Abs(-I)
    Next
End Sub

Public Sub BenchRound()
    Dim I As Long
    Dim D As Double
    Dim R As Double
    D = 1234.5678
    For I = 1 To Iterations
        R = Round(D, 2)
    Next
End Sub

Public Sub BenchSqr()
    Dim I As Long
    Dim D As Double
    For I = 1 To Iterations
        D = Sqr(I)
    Next
End Sub

Public Sub BenchInt()
    Dim I As Long
    Dim D As Double
    Dim R As Double
    D = 1234.5678
    For I = 1 To Iterations
        R = Int(D)
    Next
End Sub

Public Sub BenchDateAdd()
    Dim I As Long
    Dim T As Date
    Dim R As Date
    T = #1/1/2000#
    For I = 1 To Iterations
        R = DateAdd("d", 1, T)
    Next
End Sub

Public Sub BenchYear()
    Dim I As Long
    Dim T As Date
    Dim N As Long
    T = #1/1/2000#
    For I = 1 To Iterations
        N = Year(T)
    Next
End Sub

Public Sub BenchNow()
    Dim I As Long
    Dim T As Date
    For I = 1 To Iterations
        T = Now
    Next
End Sub

Public Sub BenchUBound()
    Dim I As Long
    Dim A(1 To 10) As Long
    Dim N As Long
    For I = 1 To Iterations
        N = UBound(A)
    Next
End Sub
