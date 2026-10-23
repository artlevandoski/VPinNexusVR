@echo off
rem Builds a self-contained release (no .NET install needed) and zips it into .\release for a GitHub release.
setlocal
set ROOT=%~dp0
set PROJECT=%ROOT%VRPLauncher.csproj

for /f "usebackq delims=" %%v in (`dotnet msbuild "%PROJECT%" -nologo -getProperty:Version`) do set VERSION=%%v
if "%VERSION%"=="" (echo Could not read the version from VRPLauncher.csproj & exit /b 1)

set NAME=VPinNexusVR-%VERSION%
set STAGE=%ROOT%release\%NAME%
set ZIP=%ROOT%release\%NAME%-win-x64.zip

echo Building %NAME% ...
if exist "%STAGE%" rmdir /s /q "%STAGE%"
if exist "%ZIP%" del "%ZIP%"

dotnet publish "%PROJECT%" -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:DebugType=none -o "%STAGE%" || exit /b 1

copy /y "%ROOT%Packaging\README.txt" "%STAGE%\" >nul
copy /y "%ROOT%LICENSE" "%STAGE%\LICENSE.txt" >nul
copy /y "%ROOT%Assets\Fonts\OFL-*.txt" "%STAGE%\" >nul

powershell -NoProfile -Command "Compress-Archive -Path '%STAGE%\*' -DestinationPath '%ZIP%' -Force" || exit /b 1

echo.
echo Done: %ZIP%
dir /b "%STAGE%"
