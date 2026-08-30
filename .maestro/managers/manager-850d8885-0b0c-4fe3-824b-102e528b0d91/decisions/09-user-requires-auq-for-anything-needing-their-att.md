# User requires AUQ for anything needing their attention

_Recorded 2026-08-20T23:10:47.883Z by 17dc66e4_

Standing instruction from the user, 2026-08-21:

> "If you want my attention for anything, always post as a question (AUQ), so I can't miss it if it is something important."

This overrides the default routing guidance that pure status goes to `track_note` and only genuine forks go to `ask_user_question`. For THIS user, anything the agent actually wants read — a correction, a finding, a heads-up, a recommendation — goes out as an `ask_user_question`, because the transcript and track cards are not where they look.

Prose in the transcript remains the log; it is not a delivery mechanism to this user.

Context that produced the instruction: the agent (a prior generation) asserted "nothing in the game currently prices a round of ammo" and built a question on it. The user pushed back — "I thought we have this?" — and was correct. `AmmoPool.SupplyValue` has existed all along and is exactly the rearm-draw price. The correction only reached them because they happened to read the question closely.

Second lesson recorded alongside it: re-derive an asserted premise before building a user-facing question on top of it. The false premise here was cheap to check — one grep of the engine traits directory.
