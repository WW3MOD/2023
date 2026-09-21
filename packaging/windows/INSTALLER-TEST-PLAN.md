# Windows installer — release test plan

**Run this against a real, freshly-built installer before every release.** It
exists because of the 2026-09-08 incident: a user accepted their Desktop as
`$INSTDIR`, and the three behaviours that followed from that one accepted
default were (a) a 25-minute uncancellable size scan of 275,720 files / 71 GB,
(b) an installer that unpacked the game loose over their Desktop, and (c) an
uninstaller that would have deleted every unrelated `.exe`, `.dll` and `.ico`
there — `Firefox.exe` among them — plus any folder named `mods`, `maps`,
`glsl`, `lua` or `Support`.

Every guard below is **unexercised code until a human runs it**. The fixes were
verified by reading and by a `makensis` compile; nothing in this file has been
executed by the author.

## Before you start

- **Use a Windows VM with a snapshot, or a machine you are willing to lose.**
  Sections 5–7 deliberately point a destructive uninstaller at directories
  containing files that must survive. If a guard has regressed, the test *is*
  the incident.
- Build both installers the normal way: `./packaging/windows/buildpackage.sh
  <tag> <outputdir>`. Keep the matching `*-winportable.zip` from the same run —
  section 6 compares against it.
- The installer requires elevation (`RequestExecutionLevel admin`). Run as a
  user who can elevate.
- Registry paths below use `OpenRAWW3MOD` (`mod.config`'s
  `PACKAGING_WINDOWS_REGISTRY_KEY`). **The x64 installer does `SetRegView 64`
  and the x86 one does not**, so use `/reg:64` when testing the x64 build and
  `/reg:32` for x86 — reading the wrong view shows an empty key and looks like
  a pass.
- Take a `reg export HKLM\Software\OpenRAWW3MOD before.reg /reg:64` first so
  you can restore between runs.

---

## 1. Build-side: the manifest exists and is the right size

1. Run `./packaging/windows/buildpackage.sh <tag> <outputdir>` to completion.
2. Before the script's cleanup removes it — or by re-running just the generator
   by hand against a kept `packaging/windows/build/` tree — open
   `packaging/windows/uninstall-manifest-x64.nsh`.
3. **Expect:** a header comment reading `; N files, M directories, B bytes.`,
   then `N` `Delete "$INSTDIR\..."` lines, then `M` `RMDir "$INSTDIR\..."`
   lines with the deepest paths first.
4. **Expect:** `N` is in the low thousands (the 2026-09-08 manual cleanup
   removed 2,736 files) and matches the number of entries in the
   `*-winportable.zip` from the same build.
5. **Expect:** grep the manifest for `\*` — there must be **zero** matches. Any
   wildcard is a fail.
6. **Expect:** grep for `Support` and for `RMDir /r` — both must be **zero**
   matches.

**Fails if:** the manifest is missing, empty, contains a wildcard, or its file
count is wildly below the zip's.

## 2. Fresh install to the default location

1. On a machine with no prior install, delete `HKLM\Software\OpenRAWW3MOD` if
   present.
2. Run the installer. On the directory page, **expect** the default to be
   `C:\Program Files\OpenRA WW3MOD`.
3. **Expect:** the explanatory paragraph naming Desktop / Documents / profile /
   drive root as not accepted is visible on that page.
4. Accept the default and install.
5. **Expect:** the progress bar reaches the end without a multi-minute stall
   near "Estimated install size" — the old `${GetSize}` scan is gone.
6. Open *Settings → Apps → Installed apps* (or `appwiz.cpl`). **Expect:** a
   WW3MOD entry with a **non-zero, plausible size** (a few GB, matching the
   build). A blank or `0.00 KB` size means `-DINSTALL_SIZE_KB` did not reach
   the compiler.
7. **Expect:** `reg query HKLM\Software\OpenRAWW3MOD /v InstallDir /reg:64`
   returns the path you installed to.

## 3. The directory page refuses the dangerous paths

For **each** of the paths below, type it into the directory field on the
directory page (or pick it with Browse):

| # | Path | |
|---|---|---|
| a | `C:\Users\<you>\Desktop` | the incident path |
| b | `C:\Users\<you>\Documents` | |
| c | `C:\Users\<you>` | the profile root |
| d | `C:\` | a drive root |
| e | `C:\Users\<you>\Desktop\` | same as (a) with a trailing backslash — the normalisation path |
| f | `C:\Windows` | |
| g | `C:\Program Files` | the *parent*, not our subfolder |

1. **Expect for every row:** the **Install/Next button greys out** while that
   path is in the field, and cannot be clicked.
2. **Expect:** editing the path back to something valid re-enables the button
   immediately, with no message box and no lag.
3. If your Desktop is redirected into OneDrive, repeat (a) with **both**
   `C:\Users\<you>\OneDrive\Desktop` and `C:\Users\<you>\Desktop`. Both must be
   rejected.
4. **Also expect:** typing a *subfolder* of the Desktop, e.g.
   `C:\Users\<you>\Desktop\ww3test`, is **accepted** — the guard rejects the
   folders themselves, not everything beneath them. That case is handled by
   section 4 instead.

**Fails if:** any row leaves the button clickable.

## 4. Install into a non-empty directory (the subfolder append)

1. Create `C:\Users\<you>\Desktop\ww3test`.
2. Put three files in it you will check for afterwards — e.g. copy any
   `notepad.exe` in as `keepme.exe`, plus `keepme.txt` and `keepme.ico`.
   **Record their sizes and hashes:** `Get-FileHash C:\Users\<you>\Desktop\ww3test\keepme.*`.
3. Run the installer and set the directory to `C:\Users\<you>\Desktop\ww3test`.
4. **Expect:** the button stays enabled (it is not one of the refused folders).
5. Click Install/Next. **Expect:** a message box saying the folder already
   contains other files and naming the new target
   `C:\Users\<you>\Desktop\ww3test\OpenRA WW3MOD`.
6. Complete the install. **Expect:** the game files are in
   `...\ww3test\OpenRA WW3MOD\`, and `...\ww3test\` itself contains only your
   three `keepme.*` files plus the new subfolder.
7. **Expect:** `reg query HKLM\Software\OpenRAWW3MOD /v InstallDir /reg:64`
   shows the **subfolder**, not `ww3test`.
8. Repeat steps 3–5 but click **Back** on the Start Menu page and then Next
   again. **Expect:** the target does not grow a second `\OpenRA WW3MOD`
   component per pass — the appended path does not yet exist, so the append
   does not re-fire.

**Fails if:** files land loose in `ww3test\`, or the path accumulates repeated
subfolder components.

## 5. Poisoned `HKLM InstallDir` (the `.onInit` path)

`.onInit` reads the previous install directory back out of the registry to
pre-fill the default, so one bad install would otherwise poison the next one's
default forever.

1. Uninstall any existing install, then hand-write the poison:
   ```
   reg add HKLM\Software\OpenRAWW3MOD /v InstallDir /t REG_SZ ^
       /d "C:\Users\<you>\Desktop" /f /reg:64
   ```
2. **Verify it took:** `reg query HKLM\Software\OpenRAWW3MOD /v InstallDir /reg:64`.
3. Run the installer and go to the directory page **without typing anything**.
4. **Expect:** the pre-filled default is `C:\Program Files\OpenRA WW3MOD`, *not*
   the Desktop. The poisoned value was rejected and the built-in default
   substituted.
5. **Expect:** the Install button is enabled (the substituted default is valid),
   so a user who just clicks through installs somewhere safe.
6. Repeat with a poison of `C:\` and of `C:\Users\<you>` — same expectation.
7. Repeat with a **valid** poison, e.g. `D:\Games\WW3MOD`. **Expect:** that one
   *is* honoured as the default — the guard must not throw away a legitimate
   remembered path.

**Fails if:** the Desktop appears pre-filled in the directory field.

## 6. Uninstall from a directory holding an unrelated `.exe` — the Firefox case

**This is the test item 10 exists for. Do not skip it, and do not run it
outside a snapshot.**

1. Install normally to `C:\Users\<you>\Desktop\ww3test\OpenRA WW3MOD` (i.e. the
   state section 4 leaves behind), or to any scratch folder.
2. Now place unrelated files **inside the install directory itself**, beside the
   game's own:
   - `keepme.exe` — copy of any real executable
   - `keepme.dll` — copy of any real DLL
   - `keepme.ico` — any icon
   - `keepme.txt`
   - `mods\keepme.txt` — inside a directory the installer *did* create
   - `Support\settings.yaml` — the portable user-data directory
   - `maps\mymap.oramap` — any file
   3 of these are the exact extensions the old uninstaller wildcarded; the last
   two are the directories it did `RMDir /r` on.
3. **Record hashes of all seven** with
   `Get-ChildItem -Recurse | Get-FileHash | Export-Csv before.csv`.
4. Uninstall — via Add/Remove Programs, or by running `uninstaller.exe` in the
   install directory.
5. **Expect, and check every one:**
   - `keepme.exe`, `keepme.dll`, `keepme.ico`, `keepme.txt` **still exist**,
     unchanged by hash. *(Under the old script all four but the `.txt` were
     gone.)*
   - `Support\settings.yaml` **still exists**. *(Old: gone, with the whole
     folder.)*
   - `maps\mymap.oramap` **still exists**. *(Old: gone, with the whole folder.)*
   - `mods\keepme.txt` **still exists**, and `mods\` therefore still exists —
     `RMDir` without `/r` cannot remove a non-empty directory. Everything else
     under `mods\` is gone.
   - The install directory itself **still exists**, because your files are in
     it.
6. **Expect the other direction too — it removed what it installed.** Take the
   `*-winportable.zip` from the same build and check that nothing it contains
   is still present:
   ```powershell
   $zip = [IO.Compression.ZipFile]::OpenRead("WW3MOD-<tag>-x64-winportable.zip")
   $zip.Entries | Where-Object { $_.Name } | ForEach-Object {
       $p = Join-Path $InstallDir $_.FullName.Replace('/','\')
       if (Test-Path $p) { "LEFT BEHIND: $p" }
   }
   ```
   **Expect:** no output. Any line is a file the uninstaller failed to remove.
7. **Expect:** `HKLM\Software\OpenRAWW3MOD` and the
   `...\CurrentVersion\Uninstall\OpenRAWW3MOD` key are both gone
   (`reg query` returns "unable to find").
8. **Expect:** the Start Menu shortcut and the Desktop shortcut are gone.

**Fails if:** any `keepme.*`, `Support\*` or `maps\*` file is missing. That is
the original bug, alive.

## 7. Uninstall from a clean install directory

1. Install to the default `C:\Program Files\OpenRA WW3MOD` and add nothing.
2. Uninstall.
3. **Expect:** the install directory itself is **gone** — with nothing left in
   it, the final non-recursive `RMDir "$INSTDIR"` succeeds.
4. **Expect:** no empty `mods\`, `lua\` or `glsl\` skeleton left behind.

## 8. Reinstall over an existing install

1. With a working install at `C:\Program Files\OpenRA WW3MOD`, run the same
   installer again.
2. **Expect:** the directory page pre-fills that path, and clicking Install
   shows **no** "folder already contains other files" message — reinstalling
   over yourself must stay in place, not create
   `...\OpenRA WW3MOD\OpenRA WW3MOD`.
3. **Expect:** the install completes and the game still launches.

## 9. Both architectures

Repeat sections 2, 4 and 6 for the **x86** installer. It compiles with
`-DUSE_PROGRAMFILES32=true`, which changes the default path *and* leaves the
registry in the 32-bit view — use `/reg:32` for every `reg query` above.

---

## Sign-off

A release is clear when every **Expect** above held. Record the tag, the two
installer SHA-256s, the Windows build tested on, and any row that was skipped
and why.
