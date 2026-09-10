@echo off
REM =============================================================================
REM LDv2rayN build + publish script
REM -----------------------------------------------------------------------------
REM Double-click this to build the source tree and refresh the single canonical
REM Release output directory that the user double-clicks:
REM
REM   v2rayN\bin\Release\net10.0-windows10.0.19041.0\
REM
REM Two pitfalls this script avoids:
REM   1) Directory.Build.props sets PublishSingleFile=true -> EXE becomes 180MB
REM      if we don't pass -p:PublishSingleFile=false.
REM   2) dotnet publish without -o creates a win-x64\ subfolder alongside the
REM      default output. We always pass -o and rm -rf the accidental folder.
REM
REM Usage:  build_publish.bat          (build Debug + publish Release)
REM         build_publish.bat clean    (also dotnet clean first)
REM =============================================================================
setlocal EnableExtensions EnableDelayedExpansion

REM -- Paths ------------------------------------------------------------------
set "ROOT=%~dp0"
set "PROJ=%ROOT%v2rayN\v2rayN.csproj"
set "OUT=%ROOT%v2rayN\bin\Release\net10.0-windows10.0.19041.0"
set "WINX64=%OUT%\win-x64"

echo.
echo ============================================
echo  LDv2rayN build + publish
echo ============================================
echo  project : %PROJ%
echo  output  : %OUT%
echo ============================================
echo.

REM -- Prereq -----------------------------------------------------------------
where dotnet >nul 2>nul
if errorlevel 1 (
    echo ERROR: dotnet not found on PATH. Install .NET 10 SDK first.
    goto :fail
)

if not exist "%PROJ%" (
    echo ERROR: project file not found: %PROJ%
    goto :fail
)

REM -- Kill any running instance so file locks don't fail publish -------------
echo Killing running LDv2rayN.exe / xray.exe / sing-box.exe ...
taskkill /F /IM LDv2rayN.exe >nul 2>nul
taskkill /F /IM xray.exe     >nul 2>nul
taskkill /F /IM sing-box.exe >nul 2>nul
REM Give processes a moment to release file locks
powershell -NoProfile -Command "Start-Sleep -Seconds 2" >nul 2>nul

REM -- Optional clean ---------------------------------------------------------
if /i "%~1"=="clean" (
    echo Cleaning previous build outputs ...
    dotnet clean "%PROJ%" --nologo -v minimal
    if errorlevel 1 goto :fail
)

REM -- Build (Debug) ----------------------------------------------------------
echo.
echo [1/3] dotnet build (Debug, catches compile errors fast) ...
dotnet build "%PROJ%" -c Debug --nologo -v minimal
if errorlevel 1 goto :fail

REM -- Publish (Release, single-file=false, explicit output) -------------------
echo.
echo [2/3] dotnet publish (Release, PublishSingleFile=false, -o default dir) ...
dotnet publish "%PROJ%" -c Release ^
    -p:PublishSingleFile=false ^
    -o "%OUT%" ^
    --nologo -v minimal
if errorlevel 1 goto :fail

REM -- Nuke the accidental win-x64 subfolder (dotnet creates it sometimes) ----
if exist "%WINX64%" (
    echo Removing accidental win-x64 subfolder ...
    rmdir /s /q "%WINX64%"
)

REM -- Verify -----------------------------------------------------------------
echo.
echo [3/3] verifying output ...
if not exist "%OUT%\LDv2rayN.exe" (
    echo ERROR: LDv2rayN.exe missing from %OUT%
    goto :fail
)
if not exist "%OUT%\LDv2rayN.dll" (
    echo ERROR: LDv2rayN.dll missing from %OUT%
    goto :fail
)
if exist "%WINX64%" (
    echo ERROR: win-x64 subfolder still present after cleanup
    goto :fail
)

REM -- Report file sizes ------------------------------------------------------
echo.
echo ============================================
echo  SUCCESS
echo ============================================
echo  Output files:
dir /b "%OUT%\LDv2rayN.exe" "%OUT%\LDv2rayN.dll" "%OUT%\ServiceLib.dll" | findstr /v "^$"
echo.
echo  Sizes:
dir "%OUT%\LDv2rayN.exe" "%OUT%\LDv2rayN.dll" "%OUT%\ServiceLib.dll" | findstr "LDv2rayN.exe LDv2rayN.dll ServiceLib.dll"
echo ============================================
echo  Open: %OUT%\LDv2rayN.exe
echo ============================================
echo.

REM Interactive pause when double-clicked; skip when piped/redirected.
REM `choice` returns 255 if stdin is closed, which lets the script finish.
set /p _=Press any key to close... 
exit /b 0

:fail
echo.
echo ============================================
echo  FAILED
echo ============================================
set /p _=Press any key to close... 
exit /b 1
