VERSION 1.0 CLASS
BEGIN
  MultiUse = -1  'True
END
Attribute VB_Name = "Counter"
' <Counter>
Option Explicit

Private mValue As Long

Public Sub Increment()
    mValue = mValue + 1
End Sub

Public Property Get Value() As Long
    Value = mValue
End Property
' </Counter>