Option Explicit

Dim shell
Dim fso
Dim baseDir
Dim command

Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")

baseDir = fso.GetParentFolderName(WScript.ScriptFullName)
command = """" & baseDir & "\SenSÉ.exe" & """"

shell.Run command, 0, False
