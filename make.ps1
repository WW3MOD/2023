####### The starting point for the script is the bottom #######

# The SDK keeps MSBuild worker nodes alive for ~15 minutes after every build. On a dev box that
# builds often they are respawned faster than they retire: seven nodes at ~108 MB each were measured
# idle, with no build running, alongside a 653 MB Roslyn compiler server (that one is separate — it
# answers to -p:UseSharedCompilation, which we deliberately leave on because it is where the
# incremental-build speed actually comes from). Reclaim on demand with `dotnet build-server shutdown`.
$env:MSBUILDDISABLENODEREUSE = "1"

###############################################################
########################## FUNCTIONS ##########################
###############################################################
function All-Command
{
	If (!(Test-Path "*.sln"))
	{
		Write-Host "No custom solution file found. Aborting." -ForegroundColor Red
		return
	}

	if ((CheckForDotnet) -eq 1)
	{
		return
	}

	Write-Host "Building $modID in" $configuration "configuration..." -ForegroundColor Cyan
	dotnet build -c $configuration --nologo -p:TargetPlatform=win-x64

	if ($lastexitcode -ne 0)
	{
		# Must exit, not just report. launch-game.cmd invokes this script and then launches
		# unconditionally; with no non-zero exit it starts the game on whatever stale binaries
		# are still in engine/bin, so the player sees a "Cannot locate type" trait error from
		# the new YAML instead of the build failure that actually happened.
		Write-Host "Build failed." -ForegroundColor Red
		exit $lastexitcode
	}

	Write-Host "Build succeeded." -ForegroundColor Green
}

function Clean-Command
{
	If (!(Test-Path "*.sln"))
	{
		Write-Host "No custom solution file found - nothing to clean. Aborting." -ForegroundColor Red
		return
	}

	if ((CheckForDotnet) -eq 1)
	{
		return
	}

	Write-Host "Cleaning $modID..." -ForegroundColor Cyan

	dotnet clean /nologo
	Remove-Item ./*/obj -Recurse -ErrorAction Ignore
	Remove-Item env:ENGINE_DIRECTORY/bin -Recurse -ErrorAction Ignore
	Remove-Item env:ENGINE_DIRECTORY/*/obj -Recurse -ErrorAction Ignore

	Write-Host "Clean complete." -ForegroundColor Green
}

function Version-Command
{
	if ($command.Length -gt 1)
	{
		$version = $command[1]
	}
	elseif (Get-Command 'git' -ErrorAction SilentlyContinue)
	{
		$gitRepo = git rev-parse --is-inside-work-tree
		if ($gitRepo)
		{
			$version = git name-rev --name-only --tags --no-undefined HEAD 2>$null
			if ($version -eq $null)
			{
				$version = "git-" + (git rev-parse --short HEAD)
			}
		}
		else
		{
			Write-Host "Not a git repository. The version will remain unchanged." -ForegroundColor Red
		}
	}
	else
	{
		Write-Host "Unable to locate Git. The version will remain unchanged." -ForegroundColor Red
	}

	if ($version -ne $null)
	{
		$mod = "mods/" + $modID + "/mod.yaml"
		$replacement = (gc $mod) -Replace "Version:.*", ("Version: {0}" -f $version)
		sc $mod $replacement

		$prefix = $(gc $mod) | Where { $_.ToString().EndsWith(": User") }
		if ($prefix -and $prefix.LastIndexOf("/") -ne -1)
		{
			$prefix = $prefix.Substring(0, $prefix.LastIndexOf("/"))
		}
		$replacement = (gc $mod) -Replace ".*: User", ("{0}/{1}: User" -f $prefix, $version)
		sc $mod $replacement

		Write-Host ("Version strings set to '{0}'." -f $version)
	}
}

# Static map-connectivity guard. Mirrors the Makefile's `nav-guard` target. Needs no
# build and no engine, so it is also runnable on its own as `make.ps1 nav-guard`.
function NavGuard-Command
{
	$python = (Get-Command 'python' -ErrorAction SilentlyContinue)
	if ($python -eq $null)
	{
		$python = (Get-Command 'python3' -ErrorAction SilentlyContinue)
	}

	if ($python -eq $null)
	{
		Write-Host "nav-guard needs python on PATH; skipping." -ForegroundColor Yellow
		return
	}

	Write-Host "Checking map connectivity (nav-guard)..." -ForegroundColor Cyan
	& $python.Source "tools/nav-guard/selftest.py"
	if ($lastexitcode -ne 0)
	{
		exit $lastexitcode
	}

	& $python.Source "tools/nav-guard/nav_guard.py" check
	if ($lastexitcode -ne 0)
	{
		exit $lastexitcode
	}
}

function Test-Command
{
	NavGuard-Command

	if ((CheckForUtility) -eq 1)
	{
		return
	}

	Write-Host "Testing $modID mod MiniYAML..." -ForegroundColor Cyan
	InvokeCommand "$utilityPath $modID --check-yaml"
}

function Check-Command
{
	If (!(Test-Path "*.sln"))
	{
		Write-Host "No custom solution file found. Skipping static code checks." -ForegroundColor Cyan
		return
	}

	Write-Host "Compiling $modID in Debug configuration..." -ForegroundColor Cyan

	# Enabling EnforceCodeStyleInBuild and GenerateDocumentationFile as a workaround for some code style rules (in particular IDE0005) being bugged and not reporting warnings/errors otherwise.
	dotnet clean -c Debug --nologo --verbosity minimal
	dotnet build -c Debug --nologo -warnaserror -p:TargetPlatform=win-x64 -p:EnforceCodeStyleInBuild=true -p:GenerateDocumentationFile=true
	if ($lastexitcode -ne 0)
	{
		Write-Host "Build failed." -ForegroundColor Red

		# Must exit here, not just report: the utility calls below are the last commands the
		# script runs, so they would overwrite $LASTEXITCODE with their own 0 and hand CI a
		# green tick for a failed build. The Makefile aborts at the same point.
		exit $lastexitcode
	}

	# WW3MOD.sln above is only OpenRA.Game + OpenRA.Mods.Common, and OpenRA.Test is excluded from
	# engine\OpenRA.sln upstream (ActiveCfg, no Build.0), so the test project reaches no gate unless
	# it is named. Mirrors the same line in the Makefile's check target.
	dotnet build engine/OpenRA.Test/OpenRA.Test.csproj -c Debug --nologo -warnaserror -p:TargetPlatform=win-x64 -p:EnforceCodeStyleInBuild=true -p:GenerateDocumentationFile=true
	if ($lastexitcode -ne 0)
	{
		Write-Host "Test project build failed." -ForegroundColor Red
		exit $lastexitcode
	}

	if ((CheckForUtility) -eq 0)
	{
		Write-Host "Checking $modID for explicit interface violations..." -ForegroundColor Cyan
		InvokeCommand "$utilityPath $modID --check-explicit-interfaces"

		Write-Host "Checking $modID for incorrect conditional trait interface overrides..." -ForegroundColor Cyan
		InvokeCommand "$utilityPath $modID --check-conditional-trait-interface-overrides"
	}
}

function Check-Scripts-Command
{
	if ((Get-Command "luac.exe" -ErrorAction SilentlyContinue) -ne $null)
	{
		Write-Host "Testing Lua scripts..." -ForegroundColor Cyan
		foreach ($script in ls "mods/*/maps/*/*.lua")
		{
			luac -p $script
		}
		Write-Host "Check completed!" -ForegroundColor Green
	}
	else
	{
		Write-Host "luac.exe could not be found. Please install Lua." -ForegroundColor Red
	}
}

function CheckForUtility
{
	if (Test-Path $utilityPath)
	{
		return 0
	}

	Write-Host "OpenRA.Utility.exe could not be found. Build the project first using the `"all`" command." -ForegroundColor Red
	return 1
}

function CheckForDotnet
{
	if ((Get-Command "dotnet" -ErrorAction SilentlyContinue) -eq $null)
	{
		Write-Host "The 'dotnet' tool is required to compile OpenRA. Please install the .NET 6.0 SDK and try again. https://dotnet.microsoft.com/download/dotnet/6.0" -ForegroundColor Red
		return 1
	}

	return 0
}

# The `dotnet` muxer being on PATH is not the same thing as the SDK global.json pins being
# installed. The pin uses rollForward=latestFeature, which cannot cross a major version, so a
# machine whose only SDK is newer (8.x, 10.x) passes CheckForDotnet and still cannot build a
# single project. Ask the muxer to resolve the pin rather than reimplementing the rollForward
# rules here: `dotnet --version` honours global.json and fails with exactly the error a build
# would hit, so this check cannot drift away from what dotnet actually does.
function CheckForDotnetSdk
{
	$null = & dotnet --version 2>&1
	if ($lastexitcode -eq 0)
	{
		return 0
	}

	$pin = $null
	if (Test-Path "global.json")
	{
		$pin = (Get-Content "global.json" -Raw | ConvertFrom-Json).sdk.version
	}

	if ($pin -match '^(\d+)\.(\d+)\.(\d)')
	{
		$band = "{0}.{1}.{2}xx" -f $matches[1], $matches[2], $matches[3]
		$major = $matches[1]
	}
	else
	{
		$band = "the version named in global.json"
		$major = "6"
	}

	Write-Host "No .NET SDK matching global.json is installed; it requires a $band SDK." -ForegroundColor Red
	Write-Host "A newer SDK is not a substitute: rollForward=latestFeature cannot cross a major version." -ForegroundColor Red
	Write-Host "Installed SDKs:" -ForegroundColor Red
	# Out-Host, not a bare call: this function's contract is to return an int, and anything a
	# native command leaves on the success stream would be returned alongside it.
	& dotnet --list-sdks | Out-Host
	Write-Host "Fix:  winget install Microsoft.DotNet.SDK.$major" -ForegroundColor Yellow
	Write-Host "      or https://dotnet.microsoft.com/download/dotnet/$major.0" -ForegroundColor Yellow
	Write-Host "Installing side-by-side is safe; it does not disturb your existing SDKs." -ForegroundColor Yellow
	return 1
}

function WaitForInput
{
	Write-Host "Press enter to continue."
	while ($true)
	{
		if ([System.Console]::KeyAvailable)
		{
			exit
		}
		Start-Sleep -Milliseconds 50
	}
}

function ReadConfigLine($line, $name)
{
	$prefix = $name + '='
	if ($line.StartsWith($prefix))
	{
		[Environment]::SetEnvironmentVariable($name, $line.Replace($prefix, '').Replace('"', ''))
	}
}

function ParseConfigFile($fileName)
{
	$names = @("MOD_ID", "ENGINE_VERSION", "AUTOMATIC_ENGINE_MANAGEMENT", "AUTOMATIC_ENGINE_SOURCE",
		"AUTOMATIC_ENGINE_EXTRACT_DIRECTORY", "AUTOMATIC_ENGINE_TEMP_ARCHIVE_NAME", "ENGINE_DIRECTORY")

	$reader = [System.IO.File]::OpenText($fileName)
	while($null -ne ($line = $reader.ReadLine()))
	{
		foreach ($name in $names)
		{
			ReadConfigLine $line $name
		}
	}
	$reader.Close()

	$missing = @()
	foreach ($name in $names)
	{
		if (!([System.Environment]::GetEnvironmentVariable($name)))
		{
			$missing += $name
		}
	}

	if ($missing)
	{
		Write-Host "Required mod.config variables are missing:"
		foreach ($m in $missing)
		{
			Write-Host "   $m"
		}
		Write-Host "Repair your mod.config (or user.config) and try again."
		WaitForInput
		exit
	}
}

function InvokeCommand
{
	param($expression)
	# $? is the return value of the called expression
	# Invoke-Expression itself will always succeed, even if the invoked expression fails
	# So temporarily store the return value in $success
	$expression += '; $success = $?'
	Invoke-Expression $expression
	if ($success -eq $False)
	{
		exit 1
	}
}

###############################################################
############################ Main #############################
###############################################################
if ($PSVersionTable.PSVersion.Major -clt 3)
{
    Write-Host "The makefile requires PowerShell version 3 or higher." -ForegroundColor Red
    Write-Host "Please download and install the latest Windows Management Framework version from Microsoft." -ForegroundColor Red
    WaitForInput
}

if ($args.Length -eq 0)
{
	Write-Host "Command list:"
	Write-Host ""
	Write-Host "  all (a)            - Builds the game, its development tools and the mod dlls."
	Write-Host "  version (v)        - Sets the version strings for all mods to the latest"
	Write-Host "                                       version for the current Git branch."
	Write-Host "  clean (c)          - Removes all built and copied files from the mods and"
	Write-Host "                                                    the engine directories."
	Write-Host "  test (t)           - Tests the mod's MiniYAML for errors, and map connectivity."
	Write-Host "  nav-guard (n)      - Checks no map lost reachable ground. No build required."
	Write-Host "  check (e)          - Checks .cs files for StyleCop violations."
	Write-Host "  check-scripts(s)   - Checks .lua files for syntax errors."
	Write-Host ""
	$command = (Read-Host "Enter command").Split(' ', 2)
}
else
{
	$command = $args
}

# Set the working directory for our IO methods
$templateDir = $pwd.Path
[System.IO.Directory]::SetCurrentDirectory($templateDir)

# Load the environment variables from the config file
# and get the mod ID from the local environment variable
ParseConfigFile "mod.config"

if (Test-Path "user.config")
{
	ParseConfigFile "user.config"
}

$modID = $env:MOD_ID

$env:MOD_SEARCH_PATHS = "./mods,$env:ENGINE_DIRECTORY/mods"
$env:ENGINE_DIR = ".." # Set to potentially be used by the Utility and different than $env:ENGINE_DIRECTORY, which is for the script.

# Fetch the engine if required
if ($command -eq "all" -or $command -eq "clean" -or $command -eq "check")
{
	# Pre-flight before anything compiles. An unsatisfiable global.json pin fails every
	# project, so catching it here reports the one real cause instead of burying it under a
	# per-project wall of identical muxer errors from the engine sub-build below.
	if ((CheckForDotnet) -eq 1 -or (CheckForDotnetSdk) -eq 1)
	{
		exit 1
	}

	$versionFile = $env:ENGINE_DIRECTORY + "/VERSION"
	$currentEngine = ""
	if (Test-Path $versionFile)
	{
		$reader = [System.IO.File]::OpenText($versionFile)
		$currentEngine = $reader.ReadLine()
		$reader.Close()
	}

	if ($currentEngine -ne "" -and $currentEngine -eq $env:ENGINE_VERSION)
	{
		cd $env:ENGINE_DIRECTORY
		Invoke-Expression ".\make.cmd $command"
		$engineExitCode = $lastexitcode
		Write-Host ""
		cd $templateDir

		# A failed engine build stops us for every command, not just the `check` CI gate. It
		# used to be scoped to `check` so that `all` could keep a softer "you may still be able
		# to run the game" posture -- but `all` is what launch-game.cmd runs, and that posture
		# is precisely what launched the game on stale binaries.
		if ($engineExitCode -ne 0)
		{
			exit $engineExitCode
		}
	}
	elseif ($env:AUTOMATIC_ENGINE_MANAGEMENT -ne "True")
	{
		Write-Host "Automatic engine management is disabled."
		Write-Host "Please manually update the engine to version $env:ENGINE_VERSION."
		WaitForInput
	}
	else
	{
		Write-Host "OpenRA engine version $env:ENGINE_VERSION is required."

		if (Test-Path $env:ENGINE_DIRECTORY)
		{
			if ($currentEngine -ne "")
			{
				Write-Host "Deleting engine version $currentEngine."
			}
			else
			{
				Write-Host "Deleting existing engine (unknown version)."
			}

			rm $env:ENGINE_DIRECTORY -r
		}

		Write-Host "Downloading engine..."

		if (Test-Path $env:AUTOMATIC_ENGINE_EXTRACT_DIRECTORY)
		{
			rm $env:AUTOMATIC_ENGINE_EXTRACT_DIRECTORY -r
		}

		$url = $env:AUTOMATIC_ENGINE_SOURCE
		$url = $url.Replace("$", "").Replace("{ENGINE_VERSION}", $env:ENGINE_VERSION)

		mkdir $env:AUTOMATIC_ENGINE_EXTRACT_DIRECTORY > $null
		$dlPath = Join-Path $pwd (Split-Path -leaf $env:AUTOMATIC_ENGINE_EXTRACT_DIRECTORY)
		$dlPath = Join-Path $dlPath (Split-Path -leaf $env:AUTOMATIC_ENGINE_TEMP_ARCHIVE_NAME)

		$client = new-object System.Net.WebClient
		[Net.ServicePointManager]::SecurityProtocol = 'Tls12'
		$client.DownloadFile($url, $dlPath)

		Add-Type -assembly "system.io.compression.filesystem"
		[io.compression.zipfile]::ExtractToDirectory($dlPath, $env:AUTOMATIC_ENGINE_EXTRACT_DIRECTORY)
		rm $dlPath

		$extractedDir = Get-ChildItem $env:AUTOMATIC_ENGINE_EXTRACT_DIRECTORY -Recurse | ?{ $_.PSIsContainer } | Select-Object -First 1
		Move-Item $extractedDir.FullName -Destination $templateDir
		Rename-Item $extractedDir.Name (Split-Path -leaf $env:ENGINE_DIRECTORY)

		rm $env:AUTOMATIC_ENGINE_EXTRACT_DIRECTORY -r

		cd $env:ENGINE_DIRECTORY
		Invoke-Expression ".\make.cmd version $env:ENGINE_VERSION"
		Invoke-Expression ".\make.cmd $command"
		Write-Host ""
		cd $templateDir
	}
}

$utilityPath = $env:ENGINE_DIRECTORY + "/bin/OpenRA.Utility.exe"

$configuration = "Release"
if ($args.Contains("CONFIGURATION=Debug"))
{
	$configuration = "Debug"
}

$execute = $command
if ($command.Length -gt 1)
{
	$execute = $command[0]
}

switch ($execute)
{
	"all" { All-Command }
	"a" { All-Command }
	"version" { Version-Command }
	"v" { Version-Command }
	"clean" { Clean-Command }
	"c" { Clean-Command }
	"test" { Test-Command }
	"t" { Test-Command }
	"nav-guard" { NavGuard-Command }
	"n" { NavGuard-Command }
	"check" { Check-Command }
	"e" { Check-Command }
	"check-scripts" { Check-Scripts-Command }
	"s" { Check-Scripts-Command }
	Default { Write-Host ("Invalid command '{0}'" -f $command) }
}

# In case the script was called without any parameters we keep the window open
if ($args.Length -eq 0)
{
	WaitForInput
}
