@echo off
setlocal

set "PROJECT_DIR=%~dp0godot\HunterHackslashGodot"
set "GODOT_EXE=D:\tools\godot-4.6.2-stable-mono\Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64.exe"

if not exist "%GODOT_EXE%" (
  echo Godot .NET 4.6.2 executable was not found.
  echo Expected: %GODOT_EXE%
  echo Download the .NET version from https://godotengine.org/download/windows/
  pause
  exit /b 1
)

start "" "%GODOT_EXE%" --path "%PROJECT_DIR%"
