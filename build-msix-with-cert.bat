@echo off
setlocal EnableExtensions

set "SCRIPT_DIR=%~dp0"
set "PS_SCRIPT=%SCRIPT_DIR%build-package.ps1"

rem ===== Default parameters (edit these) =====
set "DEFAULT_CERT_FILE=src\OpenHab.Windows.Package\OpenHab.Windows.Package_TemporaryKey.pfx"
set "DEFAULT_CERT_PASSWORD="
set "DEFAULT_CONFIGURATION=Release"
set "DEFAULT_PLATFORM=x64"
set "DEFAULT_EXPORT_CERT="

if not exist "%PS_SCRIPT%" (
  echo ERROR: Could not find build-package.ps1 next to this batch file.
  exit /b 1
)

set "CERT_FILE=%~1"
set "CERT_PASSWORD=%~2"
set "CONFIGURATION=%~3"
set "PLATFORM=%~4"
set "EXPORT_CERT=%~5"

if "%CERT_FILE%"=="" set "CERT_FILE=%DEFAULT_CERT_FILE%"
if "%CERT_PASSWORD%"=="" set "CERT_PASSWORD=%DEFAULT_CERT_PASSWORD%"
if "%CONFIGURATION%"=="" set "CONFIGURATION=%DEFAULT_CONFIGURATION%"
if "%PLATFORM%"=="" set "PLATFORM=%DEFAULT_PLATFORM%"
if "%EXPORT_CERT%"=="" set "EXPORT_CERT=%DEFAULT_EXPORT_CERT%"

if "%CERT_FILE%"=="" goto :usage

if /I not "%CONFIGURATION%"=="Debug" if /I not "%CONFIGURATION%"=="Release" (
  echo ERROR: Configuration must be Debug or Release.
  exit /b 1
)

if not exist "%CERT_FILE%" (
  echo ERROR: Certificate file not found: %CERT_FILE%
  exit /b 1
)

set "EXPORT_SWITCH="
if /I "%EXPORT_CERT%"=="export-cert" set "EXPORT_SWITCH=-ExportCertificate"

echo Building signed MSIX...
echo Certificate: %CERT_FILE%
echo Configuration: %CONFIGURATION%
echo Platform: %PLATFORM%

powershell -NoProfile -ExecutionPolicy Bypass -File "%PS_SCRIPT%" ^
  -Solution "%SCRIPT_DIR%OpenHab.Windows.sln" ^
  -Configuration "%CONFIGURATION%" ^
  -Platform "%PLATFORM%" ^
  -PackageCertificateKeyFile "%CERT_FILE%" ^
  -PackageCertificatePassword "%CERT_PASSWORD%" ^
  %EXPORT_SWITCH%

set "EXIT_CODE=%ERRORLEVEL%"
if not "%EXIT_CODE%"=="0" (
  echo Build failed with exit code %EXIT_CODE%.
  exit /b %EXIT_CODE%
)

echo Build completed successfully.
exit /b 0

:usage
echo Usage:
echo   %~nx0 ^<path-to-pfx^> ^<pfx-password^> [Configuration] [Platform] [export-cert]
echo.
echo Current defaults:
echo   cert file: %DEFAULT_CERT_FILE%
echo   configuration: %DEFAULT_CONFIGURATION%
echo   platform: %DEFAULT_PLATFORM%
echo   export cert: %DEFAULT_EXPORT_CERT%
echo.
echo Examples:
echo   %~nx0 certs\release-signing.pfx MySecretPassword
echo   %~nx0 certs\release-signing.pfx MySecretPassword Release x64 export-cert
echo.
echo Notes:
echo   - Configuration defaults to Release.
echo   - Platform defaults to x64.
echo   - Use export-cert to write a .cer beside the generated .msixbundle.
exit /b 1

pause
