@echo off
setlocal EnableDelayedExpansion

FOR /F "tokens=1,2 delims==" %%A IN (mod.config) DO (set %%A=%%B)
if exist user.config (FOR /F "tokens=1,2 delims==" %%A IN (user.config) DO (set %%A=%%B))
set MOD_SEARCH_PATHS=%~dp0mods,./mods
set ENGINE_DIR=..
if "!MOD_ID!" == "" goto badconfig
if "!ENGINE_VERSION!" == "" goto badconfig
if "!ENGINE_DIRECTORY!" == "" goto badconfig

title OpenRA.Utility.exe %MOD_ID%

set TEMPLATE_DIR=%CD%
if not exist %ENGINE_DIRECTORY%\bin\OpenRA.exe goto noengine
@REM PITFALL (2026-08-30): call find.exe by ABSOLUTE PATH. With Git-for-Windows on
@REM PATH, bare `find` resolves to C:\Program Files\Git\usr\bin\find.exe -- GNU find,
@REM whose syntax is unrelated -- so this check errored, fell through to :noengine,
@REM and sat on `pause` FOREVER at ~0%% CPU. It looks exactly like a slow run, not a
@REM failure: an agent waited ten minutes believing it was computing shadows.
>nul "%SystemRoot%\System32\find.exe" %ENGINE_VERSION% %ENGINE_DIRECTORY%\VERSION || goto noengine
cd %ENGINE_DIRECTORY%

set argC=0
for %%x in (%*) do set /A argC+=1

if %argC% == 0 goto choosemod

if %argC% == 1 (
    set MOD_ID=%1
    goto loop
)

if %argC% GEQ 2 (
    @REM This option is for use by other scripts so we don't want any extra output here - before or after.
    @REM
    @REM NOT EQUIVALENT TO utility.sh -- KNOWN, UNFIXED (2026-08-30). utility.sh:62 injects
    @REM the mod id for you (`... OpenRA.Utility.dll "${LAUNCH_MOD}" "$@"`); this line passes
    @REM %* verbatim, so the caller MUST type it. `.\utility.cmd --regen-shadows <path>` is
    @REM therefore NOT the Windows form of `./utility.sh --regen-shadows <path>` -- you want
    @REM `.\utility.cmd ww3mod --regen-shadows <path>`.
    @REM
    @REM Deliberately not "fixed" by injecting %MOD_ID% here: existing script callers already
    @REM pass it, and they would then get it twice. A correct fix needs a first-arg test and
    @REM must be verified ON WINDOWS, which the manager who found this could not do.
    call bin\OpenRA.Utility.exe %*
    EXIT /B 0
)

:choosemod
echo ----------------------------------------
echo.
call bin\OpenRA.Utility.exe
echo Enter --exit to exit
set /P mod="Please enter a modname: OpenRA.Utility.exe "
if /I "%mod%" EQU "--exit" (exit /b)
set MOD_ID=%mod%
echo.

:loop
echo.
echo ----------------------------------------
echo.
echo Enter a utility command or --exit to exit.
echo Press enter to view a list of valid utility commands.
echo.

set /P command="Please enter a command: OpenRA.Utility.exe %MOD_ID% "
if /I "%command%" EQU "--exit" (cd %TEMPLATE_DIR% & exit /b)
echo.
echo ----------------------------------------
echo.
echo Starting OpenRA.Utility.exe %MOD_ID% %command%
call bin\OpenRA.Utility.exe %MOD_ID% %command%
goto loop

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
