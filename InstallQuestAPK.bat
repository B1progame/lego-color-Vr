@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "PACKAGE_ID=com.local.questlegocolorfinder"
set "BUILD_DIR=%SCRIPT_DIR%BUILD"
set "APK_PATH="
set "ADB_EXE=C:\Users\bslid.BENJI-PC\Downloads\platform-tools-latest-windows\platform-tools\adb.exe"

if not exist "%BUILD_DIR%" (
    echo BUILD folder not found: "%BUILD_DIR%"
    echo.
    pause
    exit /b 1
)

if exist "%BUILD_DIR%\QuestColorFinder.apk" (
    set "APK_PATH=%BUILD_DIR%\QuestColorFinder.apk"
)

if not defined APK_PATH (
    for %%F in ("%BUILD_DIR%\*.apk") do (
        set "APK_PATH=%%~fF"
        goto :FoundApk
    )
)

:FoundApk

if not defined APK_PATH (
    echo No APK found in "%BUILD_DIR%"
    echo.
    echo Put your built APK in the BUILD folder, for example:
    echo   BUILD\QuestColorFinder.apk
    echo.
    pause
    exit /b 1
)

if not exist "%ADB_EXE%" (
    where adb >nul 2>nul
    if errorlevel 1 (
        echo adb was not found in PATH and configured ADB_EXE does not exist:
        echo   "%ADB_EXE%"
        echo Install Android platform-tools or update ADB_EXE in InstallQuestAPK.bat
        echo.
        pause
        exit /b 1
    )
    set "ADB_EXE=adb"
)

if not exist "%ADB_EXE%" if /i not "%ADB_EXE%"=="adb" (
    echo Configured ADB_EXE does not exist:
    echo   "%ADB_EXE%"
    echo.
    pause
    exit /b 1
)

echo Checking connected devices...
"%ADB_EXE%" devices
echo.
echo Put on the Quest headset and accept the USB debugging prompt if needed.
echo.

"%ADB_EXE%" get-state >nul 2>nul
if errorlevel 1 (
    echo No authorized Android device detected.
    echo.
    echo Steps:
    echo  1. Connect Quest via USB
    echo  2. Put on headset
    echo  3. Allow USB debugging prompt
    echo  4. Run this script again
    echo.
    pause
    exit /b 1
)

echo Using APK:
echo   "%APK_PATH%"
echo.

echo Installing APK...
"%ADB_EXE%" install -r "%APK_PATH%"
if errorlevel 1 (
    echo.
    echo Install failed.
    echo If the app is already installed with a conflicting signature, uninstall first:
    echo   "%ADB_EXE%" uninstall %PACKAGE_ID%
    echo Then run this script again.
    echo.
    pause
    exit /b 1
)

echo.
echo APK installed successfully.
echo Launching app...
"%ADB_EXE%" shell monkey -p %PACKAGE_ID% -c android.intent.category.LAUNCHER 1 >nul 2>nul
if errorlevel 1 (
    echo Launch command failed (install likely still succeeded).
    echo Open the app manually on the headset from Unknown Sources.
)

echo.
echo Done.
pause
endlocal
