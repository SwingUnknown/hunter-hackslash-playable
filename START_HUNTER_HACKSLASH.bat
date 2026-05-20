@echo off
setlocal
set "ROOT_DIR=%~dp0"
set "GAME_DIR=%ROOT_DIR%native\dist"
set "GAME_EXE=%GAME_DIR%\HunterHackslashNative.exe"
set "PROJECT=%ROOT_DIR%native\HunterHackslashNative\HunterHackslashNative.csproj"

if exist "%GAME_EXE%" (
  cd /d "%GAME_DIR%"
  start "" "%GAME_EXE%"
  endlocal
  exit /b 0
)

where dotnet >nul 2>nul
if errorlevel 1 (
  echo .NET 9 SDK or runtime is required to run from source.
  echo Download: https://dotnet.microsoft.com/download
  pause
  exit /b 1
)

cd /d "%ROOT_DIR%"
dotnet run --project "%PROJECT%" -c Release
endlocal
