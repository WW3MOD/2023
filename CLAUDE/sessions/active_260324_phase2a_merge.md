# Session: Phase 2a Upstream Merge — Platforms.Default, glsl, Tests
**Date:** 2026-03-24
**Branch:** upstream-merge-2025
**Working dir:** C:/Users/fredr/Desktop/BACKUP/WW3MOD
**Status:** in-progress

## Task
Apply upstream-only changes for Phase 2a: 17 Platforms.Default files, 10 glsl shaders, 17 test files.
These are files we haven't modified, so we take them directly from release-20250330.

## Intended Files
- engine/OpenRA.Platforms.Default/ (17 files)
- engine/glsl/ (10 files)
- engine/OpenRA.Test/ (17 files)

## Process
1. Extract each file from upstream tag: `git show release-20250330:<path>`
2. Write to engine/<path>
3. Build and verify
4. Commit
