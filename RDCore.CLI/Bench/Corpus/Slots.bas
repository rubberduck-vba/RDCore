Attribute VB_Name = "Slots"
Option Explicit
' What stage 1 of the IR lowering plan moves into frame slots: an activation's parameters and locals, a module's variables, and the
' payloads of Currency and Boolean values.

Private Const Iterations As Long = 100000

Private Total As Long

' six typed parameters and four locals, for each of which an activation reserves storage.
Private Function Mix(ByVal A As Long, ByVal B As Long, ByVal C As Double, ByVal D As Integer, ByVal E As Boolean, ByVal F As Currency) As Long
    Dim W As Long
    Dim X As Double
    Dim Y As Integer
    Dim Z As Currency
    W = A + B
    X = C
    Y = D
    Z = F
    Mix = W
End Function

Public Sub BenchWideActivations()
    Dim I As Long
    Dim S As Long
    For I = 1 To Iterations
        S = Mix(I, 1, 2.5, 3, True, 4)
    Next
End Sub

' a variable of the module, read and written each time round.
Public Sub BenchModuleVariableLoop()
    Dim I As Long
    Total = 0
    For I = 1 To Iterations
        Total = Total + 1
    Next
End Sub

' Currency's payload is a scaled integer, which a value of it divides on every read today.
Public Sub BenchCurrencySum()
    Dim I As Long
    Dim S As Currency
    For I = 1 To Iterations
        S = S + 1.25@
    Next
End Sub

' a Boolean flag flipped and tested each time round.
Public Sub BenchBooleanFlag()
    Dim I As Long
    Dim B As Boolean
    Dim N As Long
    For I = 1 To Iterations
        B = Not B
        If B Then N = N + 1
    Next
End Sub
