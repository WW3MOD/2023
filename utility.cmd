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

@REM EQUIVALENT TO utility.sh AS OF 2026-09-01 -- keep it that way. utility.sh:62 ends
@REM `... OpenRA.Utility.dll "${LAUNCH_MOD}" "$@"`, so the mod id is injected and your
@REM arguments follow it verbatim. The line below does the same, which makes
@REM `.\utility.cmd --regen-shadows PATH` exactly the Windows form of
@REM `./utility.sh --regen-shadows PATH`. Every doc that gives one form now gives both.
@REM
@REM PITFALL, and the reason PATH is spelled out above instead of in angle brackets: cmd.exe
@REM parses redirection operators on @REM lines too, so an angle bracket in a comment is a
@REM live redirect, not documentation. The comment this block replaced carried a literal
@REM bracketed path on the argument-passing branch. Keep this whole file free of the
@REM characters for redirect, pipe and escape inside comments.
@REM
@REM DO NOT type the mod id yourself. `.\utility.cmd ww3mod --check-yaml` now passes it
@REM twice and the utility rejects it -- same as `./utility.sh ww3mod --check-yaml` always
@REM has. That symmetry is the point; a first-arg test that quietly swallowed a second
@REM `ww3mod` would leave the two scripts disagreeing again, which is what put the
@REM previous KNOWN, UNFIXED note here in the first place.
@REM
@REM What this replaced: an arg COUNT test, where exactly one argument was read as a mod
@REM id. `.\utility.cmd --regen-shadows` therefore tried to LOAD A MOD called
@REM `--regen-shadows` and dropped into the interactive prompt -- worse than a no-op,
@REM because it fails looking like it ran. Run with no arguments for that prompt, which
@REM is also where you pick a different mod.
if "%~1" == "" goto choosemod

@REM This path is for use by other scripts so we don't want any extra output here - before or after.
@REM %MOD_ID% carries its quotes over from mod.config, whose line reads MOD_ID="ww3mod".
@REM Passing it still quoted is correct -- the argument parser strips them -- and it is
@REM exactly what the interactive path at the bottom of this file already does.
call bin\OpenRA.Utility.exe %MOD_ID% %*
EXIT /B 0

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
