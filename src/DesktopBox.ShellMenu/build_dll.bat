@echo off
REM Build DesktopBox.ShellMenu.dll (native C++ right-click menu host).
REM Portable: auto-locates MSVC via vswhere, uses repo-relative paths.
REM Run from a normal cmd (NOT the VS developer prompt) — this script sets up the env.
setlocal enabledelayedexpansion

REM --- Locate Visual Studio via vswhere (ships with VS Installer) ---
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" set "VSWHERE=%ProgramFiles%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" (
  echo [ERROR] vswhere.exe not found. Install "Visual Studio Build Tools" ^(C++ workload^).
  exit /b 1
)
for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VSINSTALL=%%i"
if not defined VSINSTALL (
  echo [ERROR] No VS with C++ tools found. Install "Desktop development with C++".
  exit /b 1
)

REM --- Load the x64 build environment ---
call "%VSINSTALL%\VC\Auxiliary\Build\vcvars64.bat"
if errorlevel 1 (echo [ERROR] vcvars64.bat failed & exit /b 1)

REM --- Resolve repo root (parent of this script's dir) and build ---
set "SCRIPTDIR=%~dp0"
pushd "%SCRIPTDIR%\..\.."
set "REPO=%CD%"
popd

if not exist "%REPO%\publish" mkdir "%REPO%\publish"
set "INTDIR=%REPO%\publish\.native-build"
if not exist "%INTDIR%" mkdir "%INTDIR%"

cl /nologo /LD /O2 /EHsc /std:c++17 /utf-8 ^
  "%SCRIPTDIR%DesktopBox.ShellMenu.cpp" ^
  /Fo:"%INTDIR%\DesktopBox.ShellMenu.obj" ^
  /Fe:"%REPO%\publish\DesktopBox.ShellMenu.dll" ^
  /link ole32.lib shell32.lib user32.lib gdi32.lib ^
  /IMPLIB:"%INTDIR%\DesktopBox.ShellMenu.lib"

set "CL_EXIT=%ERRORLEVEL%"
echo CL_EXIT=%CL_EXIT%
if "%CL_EXIT%"=="0" if exist "%INTDIR%" rmdir /s /q "%INTDIR%"
endlocal & exit /b %CL_EXIT%
