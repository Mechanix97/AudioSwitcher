@echo off
cd /d "%~dp0"
"%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /optimize+ /codepage:65001 /out:AudioSwitcher.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll AudioSwitcher.cs
