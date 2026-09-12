@echo off
REM =============================================================================
REM LDv2rayN build + publish + launch script
REM -----------------------------------------------------------------------------
REM Same as build_publish.bat, but at the end it launches LDv2rayN.exe from the
REM canonical Release output directory so you don't have to double-click it.
REM
REM Usage:
REM   build_publish_and_launch.bat              build + publish + launch
REM   build_publish_and_launch.bat clean        also dotnet clean first
REM   build_publish_and_launch.bat --no-launch  skip the final start
REM   build_publish_and_launch.bat --run-tests  run ServiceLib.Tests after build
REM   build_publish_and_launch.bat --run-tests --no-launch
REM
REM --run-tests runs the ServiceLib.Tests project (TUnit). Test failures do
REM not stop the build or the launch unless you also pass --fail-fast-tests.
REM =============================================================================
setlocal EnableExtensions EnableDelayedExpansion

REM -- Paths ------------------------------------------------------------------
set "ROOT=%~dp0"
set "PROJ=%ROOT%v2rayN\v2rayN.csproj"
set "OUT=%ROOT%v2rayN\bin\Release\net10.0-windows10.0.19041.0"
set "EXE=%OUT%\LDv2rayN.exe"
set "WINX64=%OUT%\win-x64"

REM -- Parse args -------------------------------------------------------------
set "DO_LAUNCH=1"
set "DO_TESTS=0"
set "FAIL_FAST_TESTS=0"
for %%A in (%*) do (
    if /i "%%A"=="--no-launch" set "DO_LAUNCH=0"
    if /i "%%A"=="--run-tests" set "DO_TESTS=1"
    if /i "%%A"=="--fail-fast-tests" set "FAIL_FAST_TESTS=1"
)

echo.
echo ============================================
echo  LDv2rayN build + publish + launch
echo ============================================
echo  project : %PROJ%
echo  output  : %OUT%
echo  launch  : %DO_LAUNCH%
echo  tests   : %DO_TESTS%
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

set "TESTS_PROJ=%ROOT%ServiceLib.Tests\ServiceLib.Tests.csproj"
if "%DO_TESTS%"=="1" if not exist "%TESTS_PROJ%" (
 echo ERROR: tests project not found: %TESTS_PROJ%
 goto :fail
)

REM -- Kill any running instance so file locks don't fail publish -------------
echo Killing running LDv2rayN.exe / xray.exe / sing-box.exe ...
taskkill /F /IM LDv2rayN.exe >nul 2>nul
taskkill /F /IM xray.exe >nul 2>nul
taskkill /F /IM sing-box.exe >nul 2>nul
REM Give processes a moment to release file locks
powershell -NoProfile -Command "Start-Sleep -Seconds 2" >nul 2>nul

REM -- Optional clean ---------------------------------------------------------
for %%A in (%*) do (
    if /i "%%A"=="clean" (
        echo Cleaning previous build outputs ...
        dotnet clean "%PROJ%" --nologo -v minimal
        if errorlevel 1 goto :fail
    )
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
if not exist "%EXE%" (
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
echo  BUILD + PUBLISH SUCCESS
echo ============================================
dir "%OUT%\LDv2rayN.exe" "%OUT%\LDv2rayN.dll" "%OUT%\ServiceLib.dll" | findstr "LDv2rayN.exe LDv2rayN.dll ServiceLib.dll"
echo ============================================
echo.

REM -- Optional unit tests ----------------------------------------------------
if "%DO_TESTS%"=="1" (
    echo ============================================
    echo  RUNNING TESTS: %TESTS_PROJ%
    echo ============================================
    dotnet test "%TESTS_PROJ%" -c Debug --nologo -v minimal
    set "TEST_RC=!ERRORLEVEL!"
    if !TEST_RC! NEQ 0 (
        echo.
        echo Test run finished with exit code !TEST_RC!.
        if "!FAIL_FAST_TESTS!"=="1" (
            echo --fail-fast-tests: aborting before launch.
            goto :fail_tests
        ) else (
            echo Continuing without --fail-fast-tests.
        )
    ) else (
        echo Tests passed.
    )
    echo ============================================
    echo.
)

REM -- Launch -----------------------------------------------------------------
if "%DO_LAUNCH%"=="1" (
    echo Launching %EXE% ...
    start "" "%EXE%"
    echo Done. LDv2rayN.exe has been launched.
) else (
    echo Launch skipped (--no-launch).
    echo To run manually: start "%EXE%"
)

echo.
set /p _=Press any key to close...
exit /b 0

:fail_tests
echo.
echo ============================================
echo  TESTS FAILED (fail-fast)
echo ============================================
set /p _=Press any key to close...
exit /b 1

:fail
echo.
echo ============================================
echo  FAILED
echo ============================================
set /p _=Press any key to close...
exit /b 1
