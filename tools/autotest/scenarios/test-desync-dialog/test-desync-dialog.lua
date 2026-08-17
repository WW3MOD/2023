-- AUTO TEST: Capture the out-of-sync dialog.
--
-- A desync ends the match by latching the world permanently unresumable, and until now the only
-- output was one system chat line written into the chat panel that had just been disabled. This
-- captures the dialog that replaces it, whose job is to name the sync report file so a stranger can
-- report a desync well enough for it to be diagnosed.
--
-- MUST be run as:  run-test.sh --sync-reports test-desync-dialog
-- The flag has to come BEFORE the test name: run-test.sh's parse loop breaks on the first non-flag
-- argument, so a trailing --sync-reports is silently dropped. A single-client test does not
-- otherwise arm sync reporting (there is no second peer to diff against), so no report is written
-- for the frame and the dialog correctly falls back to its "nothing useful to send" wording - which
-- is not the variant worth looking at. That mistake has already cost one run.
--
-- Test.ForceDesyncAndCapture forces the real path, waits for the dialog, captures, and passes:
-- the desync stops world ticks, so Trigger.AfterDelay cannot be used to sequence any of it.

WorldLoaded = function()
	TestHarness.FocusBetween(Paladin, Target)

	Trigger.AfterDelay(25, function()
		-- The verdict is decided inside the binding, on state: it fails unless the world actually
		-- latched out of sync, a report was written, the dialog is the TOPMOST window, and one of
		-- its lines contains the report filename. So the screenshot is only ever asked one thing -
		-- whether the text fits - and cannot be mistaken for evidence that the dialog appeared.
		Test.ForceDesyncAndCapture("01-desync-dialog",
			"expects: every line of the Out of Sync dialog inside the panel and unclipped, " ..
			"folder and filename on their own lines, Quit to Menu / Stay buttons both visible")
	end)
end
