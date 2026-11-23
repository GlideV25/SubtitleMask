@echo off
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo csc.exe not found.
  exit /b 1
)
"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 ^
  /win32manifest:"%~dp0app.manifest" /win32icon:"%~dp0icon.ico" ^
  /out:"%~dp0..\SubtitleMask.exe" ^
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  "%~dp0SubtitleMask.cs"
if errorlevel 1 (
  echo BUILD FAILED
  exit /b 1
)
echo BUILD OK: %~dp0..\SubtitleMask.exe
