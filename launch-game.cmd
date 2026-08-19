@powershell -NoProfile -ExecutionPolicy Bypass -File make.ps1 %* all
@if errorlevel 1 goto buildfailed

@echo off
setlocal EnableDelayedExpansion
title OpenRA

FOR /F "tokens=1,2 delims==" %%A IN (mod.config) DO (set %%A=%%B)
if exist user.config (FOR /F "tokens=1,2 delims==" %%A IN (user.config) DO (set %%A=%%B))
set TEMPLATE_LAUNCHER=%0
set MOD_SEARCH_PATHS=%~dp0mods,./mods

if "!MOD_ID!" == "" goto badconfig
if "!ENGINE_VERSION!" == "" goto badconfig
if "!ENGINE_DIRECTORY!" == "" goto badconfig

set TEMPLATE_DIR=%CD%
if not exist %ENGINE_DIRECTORY%\bin\OpenRA.exe goto noengine
>nul find %ENGINE_VERSION% %ENGINE_DIRECTORY%\VERSION || goto noengine
cd %ENGINE_DIRECTORY%

rem Force fullscreen by default; later args (AUTOTEST/DEMO Graphics.Mode=Windowed
rem or manual user override) still win via last-wins arg parsing.
bin\OpenRA.exe Game.Mod=%MOD_ID% Engine.EngineDir=".." Engine.LaunchPath="%TEMPLATE_LAUNCHER%" Engine.ModSearchPaths="%MOD_SEARCH_PATHS%" Graphics.Mode=PseudoFullscreen  "%*"
set ERROR=%errorlevel%
cd %TEMPLATE_DIR%

if %ERROR% neq 0 goto crashdialog
exit /b

:buildfailed
@rem Reached by the line-2 goto, which jumps over this script's `@echo off`, so set it here
@rem or every line below is printed twice.
@echo off
echo.
echo ----------------------------------------
echo Build failed - not launching.
echo The build messages above are the real error. Launching now would run the
echo game on stale binaries from an older build, which reports itself as an
echo unrelated "Cannot locate type" trait error and hides the actual cause.
echo ----------------------------------------
pause
exit /b 1

:noengine
echo Required engine files not found.
echo Run `make all` in the mod directory to fetch and build the required files, then try again.
pause
exit /b

:badconfig
echo Required mod.config variables are missing.
echo Ensure that MOD_ID ENGINE_VERSION and ENGINE_DIRECTORY are
echo defined in your mod.config (or user.config) and try again.
pause
exit /b

:crashdialog
echo ----------------------------------------
echo OpenRA has encountered a fatal error.
echo   * Log Files are available in Documents\OpenRA\Logs
echo   * FAQ is available at https://github.com/OpenRA/OpenRA/wiki/FAQ
echo ----------------------------------------
pause
