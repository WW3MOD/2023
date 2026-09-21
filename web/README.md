# `web/` — files the running game fetches

These two files are served straight out of this repository over
`https://raw.githubusercontent.com/WW3MOD/2023/main/web/<file>`, which is why they live
at a stable path and must stay committed on `main`. Nothing here is packaged into the
mod; the game downloads it at runtime. `mods/ww3mod/mod.yaml`'s `WebServices:` block
holds the two URLs.

| File | Read by | What happens if it is wrong |
|---|---|---|
| `latest.txt` | `WebServices.LatestVersionUrl` → `ModVersion.Compare` | Anything that is not a version string parses as Unknown, and the update notice stays hidden. It never nags wrongly; it just stops working. |
| `news.yaml` | `WebServices.GameNews` → `MainMenuLogic.ParseNews` | A parse failure leaves the last cached copy on screen with a "Failed to parse news" status. The format rules are in the file's own header comment. |

## Release step

**After tagging a release, bump `latest.txt` to the new tag and commit it to `main`.**

`latest.txt` holds one line — the tag exactly as `git tag` spells it, leading `v` included
(`v0.1.2`). It is compared against the `Version:` that `packaging/functions.sh`'s
`set_mod_version` stamps into `mods/ww3mod/mod.yaml` at package time, which is also the raw
tag, so the two sides only line up if this file carries the tag verbatim. Until it is bumped,
everyone on the previous release is told they are current.

Unstamped development builds carry the checked-in `Version:` (`release-20230225`), which is
not a version string at all — `ModVersion.Compare` returns Unknown for those, so working from
source never shows the notice however far behind `latest.txt` gets.
