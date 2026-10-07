@echo off
setlocal
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /platform:anycpu /out:"%~dp0..\WOTS4-Launcher.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Core.dll "%~dp0Launcher.cs"
exit /b %errorlevel%
