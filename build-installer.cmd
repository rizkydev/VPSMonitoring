@echo off
REM =====================================================
REM Build Installer untuk VPS Monitoring Desktop
REM Jalankan dari project root: build-installer.cmd
REM =====================================================
setlocal

REM Konfigurasi
set PROJECT_NAME=VPS Monitor Desktop App
set TARGET_FRAMEWORK=net10.0-windows10.0.19041.0
set RUNTIME=win-x64
set CONFIG=Release
set ISCC="C:\Program Files (x86)\Inno Setup 6\ISCC.exe"

REM Path absolut ke project (assume script di project root)
set PROJECT_DIR=%~dp0
set PUBLISH_DIR=%PROJECT_DIR%bin\%CONFIG%\%TARGET_FRAMEWORK%\%RUNTIME%\publish
set INSTALLER_OUTPUT=%PROJECT_DIR%installer-output
set INSTALLER_SCRIPT=%PROJECT_DIR%installer.iss

echo.
echo ============================================================
echo   Build %PROJECT_NAME%
echo   Target: %TARGET_FRAMEWORK% ^| Runtime: %RUNTIME% ^| Config: %CONFIG%
echo ============================================================
echo.

REM Step 0: Kill any running instance + WebView2 processes
echo [0/4] Membersihkan proses yang masih jalan...
taskkill /F /IM "VPS Monitor Desktop App.exe" /T 2>nul
taskkill /F /IM msedgewebview2.exe /T 2>nul
echo.

REM Step 1: Publish
echo [1/4] Publish app...
dotnet publish "%PROJECT_DIR%%PROJECT_NAME%.csproj" ^
    -f %TARGET_FRAMEWORK% ^
    -c %CONFIG% ^
    -r %RUNTIME% ^
    --self-contained false ^
    -p:PublishSingleFile=false ^
    -p:WindowsOnly=true

if %ERRORLEVEL% neq 0 (
    echo.
    echo *** PUBLISH GAGAL. Cek error di atas. ***
    exit /b 1
)
echo Publish OK.
echo.

REM Step 2: Compile installer
echo [2/4] Compile installer dengan Inno Setup...
if not exist "%ISCC%" (
    echo *** INNO SETUP TIDAK DITEMUKAN di "%ISCC%" ***
    echo Install dengan: choco install innosetup -y
    exit /b 1
)
"%ISCC%" "%INSTALLER_SCRIPT%"

if %ERRORLEVEL% neq 0 (
    echo.
    echo *** COMPILE GAGAL. Cek error di atas. ***
    exit /b 1
)
echo Compile OK.
echo.

REM Step 3: Verify output
echo [3/4] Verify installer...
if exist "%INSTALLER_OUTPUT%\VPSMonitoringDesktop-Setup-1.0.0.exe" (
    echo.
    echo ============================================================
    echo   BUILD SUKSES
    echo ============================================================
    echo.
    echo   Installer: %INSTALLER_OUTPUT%\VPSMonitoringDesktop-Setup-1.0.0.exe
    echo.
    for %%F in ("%INSTALLER_OUTPUT%\VPSMonitoringDesktop-Setup-1.0.0.exe") do (
        echo   Ukuran: %%~zF bytes (~%PROJECT_NAME% KB)
    )
    echo.
) else (
    echo *** Installer tidak ditemukan di expected location ***
    exit /b 1
)

REM Step 4: Optional - open folder
echo [4/4] Selesai.
echo.
set /p OPEN="Buka folder installer? (y/n): "
if /i "%OPEN%"=="y" (
    explorer "%INSTALLER_OUTPUT%"
)
echo.
echo Bye!
endlocal
