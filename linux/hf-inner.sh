#!/usr/bin/env bash
# From DreamyBranch's LinuxSupport (SkyeShade/HellForge issue 1), with joshua-software-dev; docs/linux.md.
# Run INSIDE the offline wrapper (hellforge-linux.sh does: hellforge-offline.sh hf-inner.sh).
# For every socket made by hf-bridge-host.sh it opens the same loopback TCP port inside the
# namespace and forwards it to Steam. Then it starts HellForge with Proton: HellForge.exe, or the Windows program and
# arguments it was given (another launcher's HellForge.Cli play, docs/external-launchers.md).
# Needs PROTON, INSTALL, STEAM_COMPAT_DATA_PATH and STEAM_COMPAT_CLIENT_INSTALL_PATH in the environment.
set -euo pipefail
command -v socat >/dev/null || { echo "need socat" >&2; exit 1; }
: "${PROTON:?PROTON is not set}" "${INSTALL:?INSTALL is not set}"
dir="${XDG_RUNTIME_DIR:-/run/user/$(id -u)}"

n=0
for f in "$dir"/hf-steam-*.sock; do
  [ -S "$f" ] || continue
  p=${f##*hf-steam-}; p=${p%.sock}
  socat TCP-LISTEN:"$p",bind=127.0.0.1,fork,reuseaddr,nodelay UNIX-CONNECT:"$f" &
  echo "forwarding 127.0.0.1:$p -> Steam"
  n=$((n+1))
done
[ "$n" -gt 0 ] || { echo "no bridge sockets found; start hf-bridge-host.sh first" >&2; exit 1; }
trap 'kill $(jobs -p) 2>/dev/null || true' EXIT
sleep 1
if [ -n "${HELLFORGE_WORKING_DIRECTORY:-}" ]; then cd "$HELLFORGE_WORKING_DIRECTORY"; fi
session_run=0
for argument in "$@"; do
  case "${argument,,}" in *hellforge.session.exe|*hellforge.cli.exe) session_run=1 ;; esac
done
session_file="$INSTALL/logs/play-session.json"
previous_run=""
if [ "$session_run" = 1 ]; then
  previous_run="$(sed -n 's/.*"runId"[[:space:]]*:[[:space:]]*"\([[:xdigit:]-]*\)".*/\1/p' "$session_file" 2>/dev/null | head -n 1)"
fi
if [ $# -gt 0 ]; then "$PROTON" run "$@" || true; else "$PROTON" run "$INSTALL/HellForge.exe" || true; fi

# "proton run" returns as soon as the launcher has started, but the launcher keeps running (and HellForge.Cli play
# returns once the session has started).
# A HellForge session writes completedUtc only after its game job and backend are stopped and cleanup is done. Use that
# per-run record rather than process-name scans: bash wrapper command lines contain the Windows exe argument, and
# independent Proton probes can otherwise keep an unrelated session's bridge alive forever.
if [ "${HELLFORGE_WAIT_FOR_PROCESSES:-1}" != 0 ]; then
  if [ "$session_run" = 1 ]; then
    run_id=""
    for _ in $(seq 1 60); do
      run_id="$(sed -n 's/.*"runId"[[:space:]]*:[[:space:]]*"\([[:xdigit:]-]*\)".*/\1/p' "$session_file" 2>/dev/null | head -n 1)"
      if [[ "$run_id" =~ ^[[:xdigit:]]{32}$ ]] && [ "$run_id" != "$previous_run" ]; then break; fi
      run_id=""
      sleep 1
    done
    if [ -z "$run_id" ]; then
      echo "The session runner did not publish a new run record; cannot confirm session cleanup." >&2
      exit 1
    fi
    result="$INSTALL/logs/sessions/$run_id/result.json"
    while [ ! -f "$result" ] || ! grep -q '"completedUtc"[[:space:]]*:' "$result"; do sleep 2; done
  else
    pat='HellForge\.(Session|Cli|SteamProbe)|LocalBackend|helldivers2'
    for _ in $(seq 1 30); do pgrep -u "$(id -u)" -f "$pat" >/dev/null && break; sleep 1; done
    while pgrep -u "$(id -u)" -f "$pat" >/dev/null; do sleep 2; done
  fi
fi
