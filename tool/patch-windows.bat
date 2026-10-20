@echo off
chcp 65001 >nul
setlocal

REM ===========================================================================
REM  PCL Restore - one-shot IL patch for Plain Craft Launcher 2 (Windows)
REM
REM  usage:
REM    patch-windows.bat <input.exe> [output.exe]
REM    or simply DRAG the EXE onto this .bat file in Explorer.
REM
REM  Requires the .NET 8 SDK to build the tool on first run. The patcher
REM  itself then also runs on .NET 8. No prebuilt binary is shipped.
REM
REM  This file is deliberately PURE ASCII. Two Windows quirks are worked
REM  around here:
REM    1. A UTF-8 .bat containing CJK can be mis-parsed by cmd.exe, so all
REM       localized text lives in separate UTF-8 files.
REM    2. The built-in `type` decodes a file in small byte chunks and cuts
REM       multi-byte characters that straddle a chunk boundary (they show up
REM       as garbage), so CJK text is printed with PowerShell instead, which
REM       writes one complete line per call.
REM ===========================================================================

set "TOOLDIR=%~dp0"

REM ---- startup notice: the two statements + short license pointer -----------
if not exist "%TOOLDIR%notice.txt" echo [PCL Restore] Using this script means you agree to the PCL license.
call :show "%TOOLDIR%notice.txt"
echo.

set "VIEWLIC="
set /p "VIEWLIC=Press 0 to view the full license, or Enter to continue: "
if not "%VIEWLIC%"=="0" goto :after_license
echo.
call :show "%TOOLDIR%..\LICENCE"
echo.
pause
:after_license
echo.

REM ---- arguments -----------------------------------------------------------
REM Explorer passes a dropped file as %1; a 2nd dropped file arrives as %2.
REM Never silently treat a 2nd existing file as the output path.

if "%~1"=="" goto :usage

set "IN=%~1"
set "OUT=%~2"
set "OUTEXPLICIT=0"

if "%OUT%"=="" goto :use_default

set "OUTEXPLICIT=1"
if not "%~3"=="" goto :multi
if not exist "%OUT%" goto :after_args
if /i "%OUT%"=="%IN%" goto :after_args
echo.
echo [WARN] output file already exists: %OUT%
set /p "OW=Overwrite it? [y/N]: "
if /i not "%OW%"=="y" goto :cancelled
goto :after_args

:use_default
REM Drop the result next to the input file, named "<original>.restored.exe".
set "OUT=%~dpn1.restored.exe"
goto :after_args

:cancelled
echo cancelled - nothing was written.
goto :hold

:after_args
REM ---- locate or build the patcher -----------------------------------------
set "DLL=%TOOLDIR%bin\Release\net8.0\PclRestore.dll"
if not exist "%DLL%" call :build_tool
if not exist "%DLL%" goto :done_fail

echo.
echo   input : %IN%
echo   output: %OUT%
echo.
call :show "%TOOLDIR%msg\patch.txt"
echo.
dotnet "%DLL%" "%IN%" "%OUT%"
if errorlevel 1 goto :done_fail
goto :done_ok

REM ---------------------------------------------------------------------------
:usage
echo usage: patch-windows.bat ^<input.exe^> [output.exe]
echo   or drag the EXE onto this .bat in Explorer.
exit /b 1

:multi
echo.
echo [ERROR] more than one file was passed or dropped.
echo         Please drag only ONE exe at a time, or run:
echo             patch-windows.bat "input.exe" "output.exe"
goto :hold

:done_ok
echo.
call :show "%TOOLDIR%msg\done.txt"
echo   %OUT%
goto :hold

:done_fail
echo.
call :show "%TOOLDIR%msg\fail.txt"
goto :hold

:hold
REM Keep the window open when it was opened by a drag-and-drop (no explicit
REM output argument), otherwise the result would flash by and disappear.
if "%OUTEXPLICIT%"=="0" pause
exit /b 0

REM ---------------------------------------------------------------------------
:build_tool
where dotnet >nul 2>&1
if errorlevel 1 (
    echo [ERROR] .NET SDK 8+ not found.
    echo   Install it from: https://dotnet.microsoft.com/download/dotnet/8.0
    exit /b 1
)
echo.
call :show "%TOOLDIR%msg\build.txt"
echo.
pushd "%TOOLDIR%"
dotnet build -c Release "PclRestore.csproj"
set "RC=%ERRORLEVEL%"
popd
if not %RC%==0 (
    echo [ERROR] build failed.
    exit /b 1
)
exit /b 0

REM ---------------------------------------------------------------------------
REM :show prints a UTF-8 text file. PowerShell emits one complete line per
REM write, which avoids `type`'s chunk-splitting of multi-byte characters.
:show
if not exist "%~1" exit /b 0
where powershell >nul 2>&1
if errorlevel 1 goto :show_type
powershell -NoProfile -Command "Get-Content -LiteralPath '%~1' -Encoding UTF8"
if errorlevel 1 goto :show_type
exit /b 0
:show_type
type "%~1"
exit /b 0
