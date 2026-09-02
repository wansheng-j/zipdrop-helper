@echo off
rem Build ZipDrop.exe with the .NET Framework C# compiler that ships with Windows.
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
    echo csc.exe not found. Please make sure you are on Windows 10/11.
    exit /b 1
)

"%CSC%" /nologo /target:winexe /out:"%~dp0ZipDrop.exe" ^
  /reference:"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\System.dll" ^
  /reference:"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\System.Core.dll" ^
  /reference:"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationFramework.dll" ^
  /reference:"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationCore.dll" ^
  /reference:"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\WPF\WindowsBase.dll" ^
  /reference:"%WINDIR%\Microsoft.NET\assembly\GAC_MSIL\System.Xaml\v4.0_4.0.0.0__b77a5c561934e089\System.Xaml.dll" ^
  /reference:"%WINDIR%\Microsoft.NET\assembly\GAC_MSIL\System.IO.Compression\v4.0_4.0.0.0__b77a5c561934e089\System.IO.Compression.dll" ^
  "%~dp0ZipDrop.cs"

if %errorlevel%==0 (
    echo Build OK: %~dp0ZipDrop.exe
) else (
    echo Build failed. See errors above.
)
endlocal
