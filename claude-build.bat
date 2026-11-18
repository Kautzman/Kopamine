@echo off
rem Builds Dopamine.sln and writes the results to build.log / build-errors.log.
rem build-done.txt is written last and contains MSBuild's exit code.
setlocal
cd /d "%~dp0"
del /q build.log build-errors.log build-done.txt 2>nul

set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" (
    echo vswhere.exe not found - is Visual Studio installed? > build-errors.log
    echo 1 > build-done.txt
    exit /b 1
)

for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -prerelease -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`) do set "MSBUILD=%%i"

if not defined MSBUILD (
    echo MSBuild.exe not found via vswhere. > build-errors.log
    echo 1 > build-done.txt
    exit /b 1
)

echo Building with "%MSBUILD%" ...
"%MSBUILD%" Dopamine.sln /restore /p:RestorePackagesConfig=true /p:Configuration=Debug /m /nologo /v:minimal ^
    /fl "/flp:logfile=build.log;verbosity=normal" ^
    /fl1 "/flp1:logfile=build-errors.log;errorsonly"

echo %ERRORLEVEL% > build-done.txt
echo.
echo Build finished with exit code %ERRORLEVEL%. You can close this window.
timeout /t 5 >nul
