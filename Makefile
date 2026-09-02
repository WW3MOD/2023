############################# INSTRUCTIONS #############################
#
# to compile, run:
#   make
#
# to compile using system libraries for native dependencies, run:
#   make TARGETPLATFORM=unix-generic
#
# to remove the files created by compiling, run:
#   make clean
#
# to set the mods version, run:
#   make version [VERSION="custom-version"]
#
# to check lua scripts for syntax errors, run:
#   make check-scripts
#
# to check the engine and your mod dlls for StyleCop violations, run:
#   make check
#
# to check your mod yaml for errors, run:
#   make test
#
# to check that no map has lost reachable ground (no build required), run:
#   make nav-guard
#
# to check that scenario Lua only names real engine bindings (no build required), run:
#   make lua-gate
#
# the following are internal sdk helpers that are not intended to be run directly:
#   make check-variables
#   make check-sdk-scripts
#   make check-packaging-scripts

.PHONY: check-sdk-scripts check-packaging-scripts check-variables check-dotnet-sdk engine all clean version check-scripts check test nav-guard lua-gate
.DEFAULT_GOAL := all

PYTHON = $(shell command -v python3 2> /dev/null)
ifeq ($(PYTHON),)
PYTHON = $(shell command -v python 2> /dev/null)
endif
ifeq ($(PYTHON),)
$(error "The OpenRA mod SDK requires python.")
endif

VERSION = $(shell git name-rev --name-only --tags --no-undefined HEAD 2>/dev/null || echo git-`git rev-parse --short HEAD`)
MOD_ID = $(shell cat user.config mod.config 2> /dev/null | awk -F= '/MOD_ID/ { print $$2; exit }')
ENGINE_DIRECTORY = $(shell cat user.config mod.config 2> /dev/null | awk -F= '/ENGINE_DIRECTORY/ { print $$2; exit }')
MOD_SEARCH_PATHS = "$(shell $(PYTHON) -c "import os; print(os.path.realpath('.'))")/mods,./mods"

SDK_PIN = $(shell awk -F'"' '/"version"/ { print $$4; exit }' global.json 2>/dev/null)
SDK_BAND = $(shell echo "$(SDK_PIN)" | sed -E 's/^([0-9]+\.[0-9]+\.[0-9]).*/\1xx/')

MANIFEST_PATH = "mods/$(MOD_ID)/mod.yaml"
HAS_LUAC = $(shell command -v luac 2> /dev/null)
LUA_FILES = $(shell find mods/*/maps/* -iname '*.lua' 2> /dev/null)
MOD_SOLUTION_FILES = $(shell find . -maxdepth 1 -iname '*.sln' 2> /dev/null)

DOTNET = dotnet

# The SDK keeps MSBuild worker nodes alive for ~15 minutes after every build. On a dev box that
# builds often they are respawned faster than they retire: seven nodes at ~108 MB each were
# measured idle, with no build running, alongside a 653 MB Roslyn compiler server (that one is
# separate — it answers to -p:UseSharedCompilation, which we deliberately leave on because it is
# where the incremental-build speed actually comes from). Reclaim on demand with
# `dotnet build-server shutdown`.
export MSBUILDDISABLENODEREUSE = 1

# RUNTIME=mono was removed: the SDK builds net6 only. Refuse the flag rather than ignoring it,
# so a stale invocation fails loudly instead of quietly producing a net6 build that the caller
# believes is a mono one. Only the literal "mono" is rejected, so an unrelated exported RUNTIME
# in the environment cannot break the build.
ifeq ($(RUNTIME), mono)
$(error RUNTIME=mono is no longer supported; this SDK builds net6 only. Drop RUNTIME=mono.)
endif

CONFIGURATION ?= Release
DOTNET_RID = $(shell ${DOTNET} --info | grep RID: | cut -w -f3)
ARCH_X64 = $(shell echo ${DOTNET_RID} | grep x64)

ifndef TARGETPLATFORM
UNAME_S := $(shell uname -s)
UNAME_M := $(shell uname -m)
ifeq ($(UNAME_S),Darwin)
ifeq ($(ARCH_X64),)
TARGETPLATFORM = osx-arm64
else
TARGETPLATFORM = osx-x64
endif
else
ifeq ($(UNAME_M),x86_64)
TARGETPLATFORM = linux-x64
else
ifeq ($(UNAME_M),aarch64)
TARGETPLATFORM = linux-arm64
else
TARGETPLATFORM = unix-generic
endif
endif
endif
endif

check-sdk-scripts:
	@awk '/\r$$/ { exit(1); }' mod.config || (printf "Invalid mod.config format: file must be saved using unix-style (CR, not CRLF) line endings.\n"; exit 1)
	@if [ ! -x "fetch-engine.sh" ] || [ ! -x "launch-dedicated.sh" ] || [ ! -x "launch-game.sh" ] || [ ! -x "utility.sh" ]; then \
		echo "Required SDK scripts are not executable:"; \
		if [ ! -x "fetch-engine.sh" ]; then \
			echo "   fetch-engine.sh"; \
		fi; \
		if [ ! -x "launch-dedicated.sh" ]; then \
			echo "   launch-dedicated.sh"; \
		fi; \
		if [ ! -x "launch-game.sh" ]; then \
			echo "   launch-game.sh"; \
		fi; \
		if [ ! -x "utility.sh" ]; then \
			echo "   utility.sh"; \
		fi; \
		echo "Repair their permissions and try again."; \
		echo "If you are using git you can repair these permissions by running"; \
		echo "   git update-index --chmod=+x *.sh"; \
		echo "and commiting the changed files to your repository."; \
		exit 1; \
	fi

check-packaging-scripts:
	@if [ ! -x "packaging/package-all.sh" ] || [ ! -x "packaging/linux/buildpackage.sh" ] || [ ! -x "packaging/macos/buildpackage.sh" ] || [ ! -x "packaging/windows/buildpackage.sh" ]; then \
		echo "Required SDK scripts are not executable:"; \
		if [ ! -x "packaging/package-all.sh" ]; then \
			echo "   packaging/package-all.sh"; \
		fi; \
		if [ ! -x "packaging/linux/buildpackage.sh" ]; then \
			echo "   packaging/linux/buildpackage.sh"; \
		fi; \
		if [ ! -x "packaging/macos/buildpackage.sh" ]; then \
			echo "   packaging/macos/buildpackage.sh"; \
		fi; \
		if [ ! -x "packaging/windows/buildpackage.sh" ]; then \
			echo "   packaging/windows/buildpackage.sh"; \
		fi; \
		echo "Repair their permissions and try again."; \
		echo "If you are using git you can repair these permissions by running"; \
		echo "   git update-index --chmod=+x *.sh"; \
		echo "in the directories containing the affected files"; \
		echo "and commiting the changed files to your repository."; \
		exit 1; \
	fi

check-variables:
	@if [ -z "$(MOD_ID)" ] || [ -z "$(ENGINE_DIRECTORY)" ]; then \
		echo "Required mod.config variables are missing:"; \
		if [ -z "$(MOD_ID)" ]; then \
			echo "   MOD_ID"; \
		fi; \
		if [ -z "$(ENGINE_DIRECTORY)" ]; then \
			echo "   ENGINE_DIRECTORY"; \
		fi; \
		echo "Repair your mod.config (or user.config) and try again."; \
		exit 1; \
	fi

# The `dotnet` muxer being on PATH is not the same thing as the SDK global.json pins being
# installed: rollForward=latestFeature cannot cross a major version, so a machine whose only SDK
# is newer resolves `dotnet` fine and still cannot build a single project. Ask the muxer to
# resolve the pin rather than reimplementing the rollForward rules here -- `dotnet --version`
# honours global.json and fails with exactly the error a build would hit, so this cannot drift
# away from what dotnet actually does. Runs before anything compiles so the one real cause is
# not buried under a per-project wall of identical muxer errors.
check-dotnet-sdk:
	@$(DOTNET) --version >/dev/null 2>&1 || ( \
		echo "No .NET SDK matching global.json is installed; it requires a $(SDK_BAND) SDK."; \
		echo "A newer SDK is not a substitute: rollForward=latestFeature cannot cross a major version."; \
		echo "Installed SDKs:"; \
		$(DOTNET) --list-sdks; \
		echo "Install from https://dotnet.microsoft.com/download/dotnet/ (side-by-side is safe)."; \
		exit 1)

engine: check-variables check-sdk-scripts
	@./fetch-engine.sh || (printf "Unable to continue without engine files\n"; exit 1)
	@cd $(ENGINE_DIRECTORY) && make TARGETPLATFORM=$(TARGETPLATFORM) all

all: check-dotnet-sdk engine
# NOT `find -exec`: find exits 0 whatever the command it ran returned, so a failed mod-solution
# build reported success here and every consumer downstream believed it -- including the
# launchers, which then started the game on stale binaries.
	@set -e; for sln in $(MOD_SOLUTION_FILES); do $(DOTNET) build "$$sln" -c ${CONFIGURATION} -p:TargetPlatform=$(TARGETPLATFORM); done

clean: engine
ifneq ("$(MOD_SOLUTION_FILES)","")
# Same reason as `all` above: `find -exec` exits 0 whatever the command returned.
	@set -e; for sln in $(MOD_SOLUTION_FILES); do $(DOTNET) clean "$$sln"; done
endif
	@cd $(ENGINE_DIRECTORY) && make clean

version: check-variables
	@sh -c '. $(ENGINE_DIRECTORY)/packaging/functions.sh; set_mod_version $(VERSION) $(MANIFEST_PATH)'
	@printf "Version changed to $(VERSION).\n"

check-scripts: check-variables
ifeq ("$(HAS_LUAC)","")
	@printf "'luac' not found.\n" && exit 1
endif
	@echo
	@echo "Checking for Lua syntax errors..."
ifneq ("$(LUA_FILES)","")
	@luac -p $(LUA_FILES)
endif

check: engine
ifneq ("$(MOD_SOLUTION_FILES)","")
	@echo "Compiling in Debug mode..."
# Enabling EnforceCodeStyleInBuild and GenerateDocumentationFile as a workaround for some code style rules (in particular IDE0005) being bugged and not reporting warnings/errors otherwise.
	@$(DOTNET) build -c Debug -nologo -warnaserror -p:TargetPlatform=$(TARGETPLATFORM) -p:EnforceCodeStyleInBuild=true -p:GenerateDocumentationFile=true
# The line above builds WW3MOD.sln, which is only OpenRA.Game + OpenRA.Mods.Common -- 2 of the
# engine's 10 projects. The two lines below take this target to 10 of 10, and Windows with it. The
# asymmetry was never in the solution files: make.ps1 routes `check` through the ENGINE's check
# target (engine/make.ps1:113-137, a Debug -warnaserror build of engine/OpenRA.sln), whereas
# `check: engine` here routes through the engine's *all* target, which is Release -- and
# engine/Directory.Build.props:51-56 strips every analyzer from Release builds. So this line is not
# new coverage in the project-wide sense; it is Linux/macOS catching up to Windows.
	@$(DOTNET) build $(ENGINE_DIRECTORY)/OpenRA.sln -c Debug -nologo -warnaserror -p:TargetPlatform=$(TARGETPLATFORM) -p:EnforceCodeStyleInBuild=true -p:GenerateDocumentationFile=true
# OpenRA.Test needs naming separately even so: upstream gives it an ActiveCfg but no Build.0 in
# engine/OpenRA.sln, so a solution build skips it. OpenRA.WindowsLauncher was missing Build.0 the
# same way; it now has a Debug one, which is what puts it in the line above on every platform. Do
# NOT give it a Release Build.0: OutputPath is engine/bin for every project
# (engine/Directory.Build.props:11) and packaging copies bin/*.dll by wildcard
# (engine/packaging/functions.sh:67), so a Release build would ship the Windows launcher DLL inside
# the Linux and macOS packages.
	@$(DOTNET) build engine/OpenRA.Test/OpenRA.Test.csproj -c Debug -nologo -warnaserror -p:TargetPlatform=$(TARGETPLATFORM) -p:EnforceCodeStyleInBuild=true -p:GenerateDocumentationFile=true
endif
	@echo "Checking for explicit interface violations..."
	@./utility.sh --check-explicit-interfaces
	@echo "Checking for incorrect conditional trait interface overrides..."
	@./utility.sh --check-conditional-trait-interface-overrides

# Static map-connectivity guard. Needs no build and no engine, so it is its own target
# as well as a prerequisite of `test` -- `make nav-guard` is the fast inner-loop form.
nav-guard:
	@echo "Checking map connectivity (nav-guard)..."
	@$(PYTHON) tools/nav-guard/selftest.py
	@$(PYTHON) tools/nav-guard/nav_guard.py check

# Static check that autotest scenario Lua only names bindings the engine registers.
# Same shape as nav-guard: no build, no engine, no launch. Warnings (exit 1) are
# printed but do not fail the target; only undefined references (exit 2) do. Use
# `lua_gate.py check --strict` to make warnings fatal too.
lua-gate:
	@echo "Checking scenario Lua against the engine's script bindings (lua-gate)..."
	@$(PYTHON) tools/lua-gate/lua_gate.py selftest
	@$(PYTHON) tools/lua-gate/lua_gate.py check || [ $$? -eq 1 ]

test: all nav-guard lua-gate
	@echo "Testing $(MOD_ID) mod MiniYAML..."
	@./utility.sh --check-yaml
