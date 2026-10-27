@echo off
rem Builds SoundFlip.exe with the C# compiler that ships with Windows (.NET Framework 4.x)
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /codepage:65001 /out:"%~dp0SoundFlip.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll "%~dp0SoundFlip.cs"
