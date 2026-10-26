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
if [ $# -gt 0 ]; then "$PROTON" run "$@" || true; else "$PROTON" run "$INSTALL/HellForge.exe" || true; fi

# "proton run" returns as soon as the launcher has started, but the launcher keeps running (and HellForge.Cli play
# returns once the session has started).
# Keep the forwarders alive until the wrapped Windows processes (session, backend, game) are gone. The native Linux
# launcher must not be included: it stays open after the game exits and would keep this wrapper waiting indefinitely.
if [ "${HELLFORGE_WAIT_FOR_PROCESSES:-1}" != 0 ]; then
  pat='HellForge\.(Session|Cli|SteamProbe)|LocalBackend|helldivers2'
  # Scope the wait to this launch's process group so another HellForge probe or session cannot hold it open.
  # The wrapper and its parents contain the Windows executable path in their arguments, so ignore that ancestry too.
  session_pgid="$(ps -o pgid= -p "$$" | tr -d '[:space:]')"
  [[ "$session_pgid" =~ ^[0-9]+$ ]] || { echo "Could not determine the session process group." >&2; exit 1; }
  wrapper_pids=" $$"
  parent=$PPID
  while [[ "$parent" =~ ^[0-9]+$ ]] && [ "$parent" -gt 1 ]; do
    wrapper_pids+=" $parent"
    parent="$(ps -o ppid= -p "$parent" 2>/dev/null | tr -d '[:space:]')"
  done
  wrapped_process_running() {
    local pid
    while IFS= read -r pid; do
      case "$wrapper_pids" in *" $pid "*) continue ;; esac
      return 0
    done < <(pgrep -u "$(id -u)" -g "$session_pgid" -f "$pat" || true)
    return 1
  }
  for _ in $(seq 1 30); do wrapped_process_running && break; sleep 1; done
  while wrapped_process_running; do sleep 2; done
fi
