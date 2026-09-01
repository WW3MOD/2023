#!/bin/sh
# WW3MOD autotest — declared expected status for a scenario.
#
# THE PROBLEM. run-batch.sh --all globs every test-* folder and includes any scenario
# that contains an assertion; its only exclusion catches scenarios with NO verdict call
# at all. So a scenario that produces a verdict and *legitimately* fails — a by-merit
# negative, a "we measured it and it is within tolerance" result, a layer of a bug that
# is knowingly unfixed — turns the batch permanently red. run-batch.sh's own comment
# says what that costs: it "is how a red batch stops meaning anything."
#
# THE SHAPE OF THE FIX, and why it is an expected status rather than an opt-out marker.
# An opt-out ("skip this one in --all") hides the scenario, and a hidden scenario stops
# reporting: if it later starts CRASHING nobody hears. A declared status keeps the run
# and grades it, which buys the asymmetry that `mods/ww3mod/lint-baseline.txt` already
# implements deliberately and for the same reason — read that file's header, it is the
# best statement of the principle in this repo:
#
#   * The declared outcome occurring is GREEN. It is the recorded floor.
#   * The declared outcome NO LONGER occurring is RED, loudly. A scenario that was
#     declared `fail` and now passes has had its premise change under it; the
#     declaration is stale and must be removed in the same commit as whatever fixed it.
#     Lowering the floor is a deliberate, reviewable act — exactly as with the lint
#     baseline, where the prune can only ever REMOVE lines.
#   * Any OTHER outcome is RED. Declaring `fail` does not buy silence for a crash.
#
# So this can never be used to make a red run green by fiat: the only thing it silences
# is the one specific outcome the author wrote down and justified, and it fails the
# moment reality stops matching the note.
#
# KNOWN GAP, and it is the one that matters most when you declare `fail` — a WATCHDOG
# TIMEOUT is graded GREEN by a `fail` declaration. The bullet above is true for a crash
# (exit 3 -> ERR -> RED) but NOT for a hang. run-test.sh forks the outcome name at :932
# (`TIMEOUT-FAIL` vs `FAIL`) and deliberately gives both `exit 1`, saying so at :791-795;
# its synthesized timeout record at :798-800 is `"status":"fail"`, schema-identical to a
# real assertion failure. run-batch.sh derives its outcome from the exit code alone
# (:213-218), so what reaches expected_status_grade is plain `FAIL` either way. A scenario
# declared `fail` therefore reports OK(fail) when the game hung or never loaded its rules
# -- i.e. it absorbs "the run did not happen", the one outcome a by-merit declaration must
# never absorb.
#
# So: a `fail` declaration is only honest while the scenario still reaches a verdict under
# its own power, and NOTHING HERE CHECKS THAT. Until it does, re-read the run banner (which
# does print TIMEOUT-FAIL) rather than trusting an OK(fail) in the tally. The fix needs the
# run dir plumbed out of run-test.sh -- RUN_ID/RESULT_FILE are generated there (:515, :522)
# and never handed back, so run-batch cannot inspect the verdict file today. Full write-up,
# including the per-scenario marker scheme that already solves this one level down
# (`00-script-loaded` / `99-verdict-reached`), in WORKSPACE/DISCOVERIES.md 2026-09-01.
#
# DECLARING IT. Put a file named `expected-status` in the scenario folder:
#
#   tools/autotest/scenarios/test-<name>/expected-status
#   ------------------------------------------------------
#   fail
#   The negative arm is by merit: the drone genuinely does not prefer the lost-track
#   contact yet. Remove this file when the preference lands and the run goes green.
#
# First non-comment line is the status (`fail` or `skip`). Everything after it is the
# reason and is REQUIRED — an entry with no reason is a configuration error and fails
# the batch, on the same argument as lint-baseline's "[accepted] needs a comment above
# it saying why". `pass` is rejected: that is the default and declaring it says nothing.
# Lines starting with `#` are comments.
#
# USAGE
#   expected_status_read  <scenario-dir>          -> echoes "" | "fail" | "skip" | "ERROR:msg"
#   expected_status_grade <declared> <actual>     -> echoes GREEN|RED|STOPPED|CONFIG
#   ./expected-status.sh --selftest               -> proves the decision table, no launch

# Echo the declared status for a scenario dir, or nothing if undeclared.
# An unreadable/ill-formed declaration echoes "ERROR:<msg>" so the caller can fail loudly
# rather than silently treating it as undeclared -- a typo'd status must never read as
# "no declaration" and quietly restore the permanent red this exists to remove.
expected_status_read() {
	_esr_file="$1/expected-status"
	[ -f "${_esr_file}" ] || return 0

	_esr_status=""
	_esr_reason=""
	while IFS= read -r _esr_line || [ -n "${_esr_line}" ]; do
		case "${_esr_line}" in
			'#'*) continue ;;
		esac
		# Trim leading/trailing whitespace without invoking sed per line.
		_esr_line="${_esr_line#"${_esr_line%%[![:space:]]*}"}"
		_esr_line="${_esr_line%"${_esr_line##*[![:space:]]}"}"
		[ -n "${_esr_line}" ] || continue
		if [ -z "${_esr_status}" ]; then
			_esr_status="${_esr_line}"
		else
			_esr_reason="${_esr_line}"
			break
		fi
	done < "${_esr_file}"

	case "${_esr_status}" in
		fail|skip) : ;;
		pass) echo "ERROR:'pass' is the default and declares nothing; delete the file"; return 0 ;;
		"")   echo "ERROR:expected-status file is empty"; return 0 ;;
		*)    echo "ERROR:unknown status '${_esr_status}' (expected 'fail' or 'skip')"; return 0 ;;
	esac

	if [ -z "${_esr_reason}" ]; then
		echo "ERROR:'${_esr_status}' declared with no reason; add one below the status line"
		return 0
	fi

	echo "${_esr_status}"
}

# Grade an actual verdict against a declaration.
#   GREEN   counts as a pass for the batch's exit code
#   STOPPED the declared outcome no longer occurs -- the declaration is stale (RED)
#   RED     an outcome nobody declared
#   CONFIG  the declaration itself is malformed (RED)
expected_status_grade() {
	_esg_declared="$1"
	_esg_actual="$2"

	case "${_esg_declared}" in
		ERROR:*) echo "CONFIG"; return 0 ;;
	esac

	if [ -z "${_esg_declared}" ]; then
		[ "${_esg_actual}" = "PASS" ] && echo "GREEN" || echo "RED"
		return 0
	fi

	# Uppercase the declaration for comparison without relying on `tr` locale behaviour.
	case "${_esg_declared}" in
		fail) _esg_want="FAIL" ;;
		skip) _esg_want="SKIP" ;;
		*)    echo "CONFIG"; return 0 ;;
	esac

	if [ "${_esg_actual}" = "${_esg_want}" ]; then
		echo "GREEN"
	elif [ "${_esg_actual}" = "PASS" ]; then
		echo "STOPPED"
	else
		echo "RED"
	fi
}

_expected_status_selftest() {
	_t_fail=0
	_check() {
		_got=$(expected_status_grade "$2" "$3")
		if [ "${_got}" = "$4" ]; then
			printf '  ok    %-34s declared=%-16s actual=%-5s -> %s\n' "$1" "'$2'" "$3" "${_got}"
		else
			printf '  FAIL  %-34s declared=%-16s actual=%-5s -> %s (want %s)\n' \
				"$1" "'$2'" "$3" "${_got}" "$4"
			_t_fail=$((_t_fail + 1))
		fi
	}

	echo "expected-status selftest — decision table"
	# Undeclared: unchanged behaviour, only PASS is green.
	_check "undeclared, passes"            ""      PASS GREEN
	_check "undeclared, fails"             ""      FAIL RED
	_check "undeclared, skips"             ""      SKIP RED
	_check "undeclared, errors"            ""      ERR  RED
	# Declared fail: the by-merit negative.
	_check "declared fail, fails"          "fail"  FAIL GREEN
	_check "declared fail, STOPS failing"  "fail"  PASS STOPPED
	_check "declared fail, skips instead"  "fail"  SKIP RED
	_check "declared fail, crashes"        "fail"  ERR  RED
	# Declared skip.
	_check "declared skip, skips"          "skip"  SKIP GREEN
	_check "declared skip, STOPS skipping" "skip"  PASS STOPPED
	_check "declared skip, fails instead"  "skip"  FAIL RED
	# Malformed declarations are never silently ignored.
	_check "malformed declaration"         "ERROR:bad" FAIL CONFIG
	_check "malformed, would-be green"     "ERROR:bad" PASS CONFIG

	echo
	echo "file parsing"
	_tmp="${TMPDIR:-/tmp}/expstatus-selftest.$$"
	mkdir -p "${_tmp}/none" "${_tmp}/good" "${_tmp}/noreason" "${_tmp}/bogus" "${_tmp}/empty" "${_tmp}/pass"
	printf 'fail\nby-merit negative, see CONTROL-ARM.md\n'   > "${_tmp}/good/expected-status"
	printf '# a comment\n\nfail\n'                            > "${_tmp}/noreason/expected-status"
	printf 'flaky\nbecause reasons\n'                         > "${_tmp}/bogus/expected-status"
	printf '\n'                                               > "${_tmp}/empty/expected-status"
	printf 'pass\nwhy not\n'                                  > "${_tmp}/pass/expected-status"

	_checkfile() {
		_got=$(expected_status_read "$2")
		case "${_got}" in
			$3) printf '  ok    %-34s -> %s\n' "$1" "${_got:-<undeclared>}" ;;
			*)  printf '  FAIL  %-34s -> %s (want %s)\n' "$1" "${_got:-<undeclared>}" "$3"
			    _t_fail=$((_t_fail + 1)) ;;
		esac
	}
	_checkfile "no file"                  "${_tmp}/none"     ""
	_checkfile "status + reason"          "${_tmp}/good"     "fail"
	_checkfile "status, no reason"        "${_tmp}/noreason" "ERROR:*"
	_checkfile "unknown status"           "${_tmp}/bogus"    "ERROR:*"
	_checkfile "empty file"               "${_tmp}/empty"    "ERROR:*"
	_checkfile "declares pass"            "${_tmp}/pass"     "ERROR:*"
	rm -rf "${_tmp}"

	echo
	if [ ${_t_fail} -eq 0 ]; then
		echo "expected-status selftest: ok"
		return 0
	fi
	echo "expected-status selftest: ${_t_fail} FAILURE(S)"
	return 1
}

case "$1" in
	--selftest) _expected_status_selftest ;;
esac
