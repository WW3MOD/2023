#!/bin/sh
# Extract the two new functions from run-test.sh and exercise them against
# synthetic logs. Proves the fail-fast can actually fire before a real slot is
# spent on it -- the failure mode of a detector is silence, which is
# indistinguishable from "the thing never happened".
set -e

SRC=tools/autotest/run-test.sh
TMP=$(mktemp -d)
trap 'rm -rf "${TMP}"' EXIT

sed -n '/^find_engine_log() {/,/^}/p'        "${SRC}" >  "${TMP}/fns.sh"
sed -n '/^check_launch_failure() {/,/^}/p'   "${SRC}" >> "${TMP}/fns.sh"

REPO_ROOT=$(pwd)
export REPO_ROOT
. "${TMP}/fns.sh"

LOGS="${TMP}/OpenRA/Logs"
mkdir -p "${LOGS}"
APPDATA="${TMP}"
export APPDATA

FAILED=0
ok()   { echo "  ok    $1"; }
bad()  { echo "  FAIL  $1  $2"; FAILED=1; }

reset_logs() {
	rm -f "${LOGS}"/server.log* "${LOGS}"/client.log*
	printf 'Initial map: be8e78bae179ab7b0703fc3b1b7e1cc40385fe10\n' > "${LOGS}/server.log"
	printf 'Game started\n' > "${LOGS}/client.log"
}

# ---- 1. clean logs must NOT trip -------------------------------------------
reset_logs
LAUNCH_FAIL_SRC=""; LAUNCH_FAIL_DETAIL=""
if check_launch_failure; then
	bad "clean logs do not trip" "tripped on ${LAUNCH_FAIL_SRC}"
else
	ok "clean logs do not trip"
fi

# ---- 2. the real server refusal, verbatim from the failed baseline run -----
reset_logs
cat >> "${LOGS}/server.log" <<'EOF'
Client 0: Accepted connection from 127.0.0.1:58366.
Dropping connection 127.0.0.1:58366 because an error occurred:
System.ArgumentException: An item with the same key has already been added. Key: explored
   at System.Linq.Enumerable.ToDictionary[TSource,TKey,TElement](IEnumerable`1 source)
   at OpenRA.Mods.Common.Server.LobbySettingsNotification.ClientJoined(Server server, Connection conn)
EOF
LAUNCH_FAIL_SRC=""; LAUNCH_FAIL_DETAIL=""
if check_launch_failure; then
	ok "the real 'Dropping connection' refusal trips"
	case "${LAUNCH_FAIL_DETAIL}" in
		*"Key: explored"*) ok "the captured detail carries the exception, not just the refusal" ;;
		*) bad "detail carries the exception" "got: ${LAUNCH_FAIL_DETAIL}" ;;
	esac
	case "${LAUNCH_FAIL_SRC}" in
		*server.log) ok "source is reported as server.log" ;;
		*) bad "source is server.log" "got ${LAUNCH_FAIL_SRC}" ;;
	esac
else
	bad "the real refusal trips" "did not trip"
fi

# ---- 3. client-side symptom alone still trips ------------------------------
reset_logs
printf 'Connection to 127.0.0.1:58365 failed: Attempted to read past the end of the stream.\n' \
	>> "${LOGS}/client.log"
LAUNCH_FAIL_SRC=""; LAUNCH_FAIL_DETAIL=""
if check_launch_failure; then
	case "${LAUNCH_FAIL_SRC}" in
		*client.log) ok "client.log 'Connection to ... failed' trips on its own" ;;
		*) bad "client.log trips on its own" "source ${LAUNCH_FAIL_SRC}" ;;
	esac
else
	bad "client.log trips on its own" "did not trip"
fi

# ---- 4. rotation: the NEWEST server.log* is the one read -------------------
# Log.AddChannel falls through to server.log.1 when the bare name is held open
# by a living instance, so OUR log can be the rotated one -- and it is the newer
# of the two, because it was created later.
reset_logs
cp "${LOGS}/server.log" "${LOGS}/server.log.1"
cat >> "${LOGS}/server.log.1" <<'EOF'
Dropping connection 127.0.0.1:59999 because an error occurred:
System.ArgumentException: An item with the same key has already been added. Key: explored
EOF
touch -t 200001010000 "${LOGS}/server.log"
LAUNCH_FAIL_SRC=""; LAUNCH_FAIL_DETAIL=""
if check_launch_failure; then
	case "${LAUNCH_FAIL_SRC}" in
		*server.log.1) ok "the newest rotated log wins over a stale bare name" ;;
		*) bad "newest rotated log wins" "read ${LAUNCH_FAIL_SRC}" ;;
	esac
else
	bad "newest rotated log wins" "did not trip"
fi

# ---- 5. a stale hit in an OLD log must not mask a clean current one --------
# The inverse of 4: bare name is current and clean, rotated one is old and dirty.
reset_logs
cp "${LOGS}/server.log" "${LOGS}/server.log.1"
printf 'Dropping connection 127.0.0.1:1 because an error occurred:\n' >> "${LOGS}/server.log.1"
touch -t 200001010000 "${LOGS}/server.log.1"
LAUNCH_FAIL_SRC=""; LAUNCH_FAIL_DETAIL=""
if check_launch_failure; then
	bad "a stale rotated hit does not trip" "tripped on ${LAUNCH_FAIL_SRC}"
else
	ok "a stale rotated hit does not trip a clean current run"
fi

echo
if [ "${FAILED}" = "1" ]; then
	echo "FAILED"
	exit 1
fi
echo "OK -- the launch-failure detector fires on the real signature and stays quiet otherwise"
