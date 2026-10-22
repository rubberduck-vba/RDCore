VERSION 1.0 CLASS
BEGIN
  MultiUse = -1  'True
END
Attribute VB_Name = "InterfaceCounter"
Option Explicit
Implements ICounter

Private mValue As Long

Private Sub ICounter_Increment()
    mValue = mValue + 1
End Sub

Private Property Get ICounter_Value() As Long
    ICounter_Value = mValue
End Property
