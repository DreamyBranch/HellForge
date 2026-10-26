# HellForge on Linux (Proton)

For players on Linux: the launcher and setup wizard run natively, while the Windows session runner, backend, and game
run through Proton inside the offline network namespace. Linux support is experimental.

Linux support is the work of DreamyBranch and joshua-software-dev, who worked it out and shared it in
[issue 1](https://github.com/SkyeShade/HellForge/issues/1). Thank you both. The setup steps below, and the dependency
check and Steam folder search in `hellforge-linux.sh`, follow DreamyBranch's guided install script in
[issue 11](https://github.com/SkyeShade/HellForge/issues/11). The scripts are in `linux/` (in an installation:
`app/linux/`).

## What you need

- Steam for Linux, running and signed in to an account that owns HELLDIVERS 2, with at least one Proton installed.
  HELLDIVERS 2 itself does not need to be installed: setup on Linux gets the supported build through Steam's console.
- About 50 GB of free space while setting up: Steam's download of the supported build and HellForge's copy of the game
  are about 24 GB each. Setup deletes the download after copying unless you untick that.
- `HellForge-Linux.zip` from the HellForge release package. It contains the app but no game files.
- These programs: `socat`, `ip` and `ss` (from iproute2), `unshare` (from util-linux) and `pgrep` (from procps).
  `hellforge-linux.sh` checks for them and prints the install command for your package manager:

  | Package manager | Common distributions | Command |
  |---|---|---|
  | `pacman` | Arch, CachyOS, Manjaro | `sudo pacman -S --needed iproute2 socat util-linux procps-ng` |
  | `apt-get` | Ubuntu, Debian, Mint, Pop!_OS | `sudo apt install iproute2 socat util-linux procps` |
  | `dnf` | Fedora, Nobara | `sudo dnf install iproute socat util-linux procps-ng` |
  | `zypper` | openSUSE | `sudo zypper install iproute2 socat util-linux procps` |

  On other systems, install the same packages with your package manager.

## Building the Linux package without a Windows PC

The engine's session runner and backend are Windows NativeAOT programs, so the complete package must be built on a
Windows runner. You can prepare and trigger that build from Linux; the runner uses only the repository and a small
text-only manifest, not your game files.

From the repository root, point `steam_api` at the supported game's `bin/steam_api64.dll` in your Steam library and
generate the manifest:

```fish
set steam_api "$HOME/.local/share/Steam/steamapps/common/HELLDIVERS 2/bin/steam_api64.dll"
dotnet run --project src/HellForge.SdkShimGen -- manifest "$steam_api" steam-export-manifest.json
```

Use the actual path if Steam or the game is installed elsewhere. The command writes only export names, ordinals, data
export flags, and the source DLL's SHA-256. **Commit and push `steam-export-manifest.json` only; never add or upload
`steam_api64.dll`.**

On GitHub, open **Actions → Build Linux package → Run workflow** on the branch containing that JSON. The Windows runner
checks the manifest's recorded source hash against the supported-build pin, builds the engine and package, and publishes an
artifact containing `HellForge-Linux.zip`, the engine package, and the Windows setup executable. Download
`HellForge-Linux.zip` from that workflow run's Artifacts section, then follow [Setting it up](#setting-it-up).
The run will stop with a clear error if the manifest is missing or does not match the supported build.

## Setting it up

The commands use `~/.local/share/hellforge-prefix` as HellForge's own Wine prefix and `~/.local/share/Steam` as the
Steam folder. Any empty folder works as the prefix. If your Steam is elsewhere (`~/.steam/debian-installation` on some
Debian and Ubuntu installs), use that folder.

1. Install the programs from [What you need](#what-you-need).
2. Extract the Linux package into the prefix's C: drive. The first launcher start creates `hellforge-install.json` and
   the same installation folders as Windows setup:

   ```fish
   set install "$HOME/.local/share/hellforge-prefix/pfx/drive_c/users/steamuser/Documents/HellForge"
   mkdir -p "$install"
   unzip "$HOME/Downloads/HellForge-Linux.zip" -d "$install"
   chmod +x "$install/app/launcher-linux/HellForge.Launcher"
   ```

   Keep the installation inside the prefix's C: drive (`pfx/drive_c`). Wine shows your Linux folders as its Z: drive,
   and HellForge refuses that drive as a linked path ("Linked/reparse paths are not supported: Z:\").
3. On Ubuntu 24.04 and later, and other systems where `/proc/sys/kernel/apparmor_restrict_unprivileged_userns` is 1,
   only allowed programs may make the namespace. Run this once, from your own account:

   ```fish
   sudo bash ~/.local/share/hellforge-prefix/pfx/drive_c/users/steamuser/Documents/HellForge/app/linux/allow-namespace.sh
   ```

   It installs a copy of `unshare` that only you (and root) can run, and an AppArmor profile that lets that copy make
   namespaces, as Ubuntu does for Steam. For you this lifts Ubuntu's restriction, for any namespace, not only
   HellForge's; other accounts keep it. `--remove` undoes it. Run it again after a util-linux update.
   `hellforge-linux.sh` tells you when it is needed.
4. Start Steam and sign in. The native setup wizard needs Steam's console to download the supported build if it is not
   already installed.
5. Start the native launcher (the first start creates the marker and layout):

   ```fish
   "$install/app/launcher-linux/HellForge.Launcher"
   ```

   It reads the installation natively, finds Steam's Linux folders and a Proton install. To select another Proton, set
   `PROTON` to its `proton` script before starting the launcher.
6. In HellForge, press Start setup. If the supported build is not installed, setup shows three `download_depot`
   commands ([troubleshooting](troubleshooting.md#setup-cannot-find-the-supported-build) describes them). Open Steam's
   console with the button in the wizard (or `xdg-open steam://open/console` in a terminal), paste each command and
   press Enter. The wizard reads completed depots directly from Steam's content folder; **do not move them**. When all
   three downloads are complete, press Check again / Start setup. The native wizard copies the game and verifies every
   file against Steam's hashes, runs the ownership probe through Proton, and installs the namespace marker only after
   the wrapper's offline self-test passes.

When you press PLAY, the native launcher starts `app/linux/hellforge-linux.sh` with the existing
`app/session/HellForge.Session.exe play ...` command. The wrapper still starts the session runner through Proton inside
the network namespace, with Steam bridged and the live war fetched before entering it. The session runner's own
network-probe refusal remains in force. The native launcher stays outside the namespace; it reads and writes only
HellForge's local files. Do not start a session runner directly through Proton or run it without the wrapper.

`hellforge-linux.sh` reads these settings from the environment. Each one has a default it finds by itself:

| Variable | Default |
|---|---|
| `PROTON` | a Proton the Steam client has (`steamapps/common/Proton*` or `compatibilitytools.d/*`, the last by version sort, which puts Proton - Experimental after the numbered versions) |
| `INSTALL` | the installation the script is in |
| `STEAM_COMPAT_CLIENT_INSTALL_PATH` | the first of `~/.local/share/Steam`, `~/.steam/debian-installation`, `~/.steam/steam` and Flatpak's and Snap's Steam folders that has a `steamapps` folder |
| `STEAM_COMPAT_DATA_PATH` | the prefix the installation is in (the part of its path before `/pfx/drive_c/`); for an installation outside a prefix, HELLDIVERS 2's prefix, `steamapps/compatdata/553850` |

It starts the bridge to Steam itself and stops it when the wrapped program closes. A bridge already running
(`hf-bridge-host.sh`) is used as it is.

Another launcher (a mod manager, [external-launchers.md](external-launchers.md)) starts sessions through the same
script. It passes the Windows program to run in place of HellForge.exe, for example
`hellforge-linux.sh "$INSTALL/app/cli/HellForge.Cli.exe" play --wait yes`, and keeps the Steam bridge, the war fetch and
the namespace.

### From an installation made on Windows

An installation set up on Windows already has the game copy, so it needs no download. Copy the whole installation folder
into a prefix's C: drive, for example to `~/.local/share/hellforge-prefix/pfx/drive_c/HellForge` (the prefix directory
is created by Proton). Then follow steps 3 and 5 with that
installation's `app/launcher-linux` folder.

Sharing HELLDIVERS 2's prefix (553850) works too, but then nothing else may run in that prefix while HellForge runs. A
Windows program of the prefix running outside would share its network with HellForge's
([one prefix, one network](#one-prefix-one-network)), and the real game, started while HellForge runs, would have no
network.

### Steam's download of the supported build

The Linux Steam client puts what `download_depot` downloads in `<Steam>/steamapps/content/app_553850/`, or in the same
path under `<Steam>/ubuntu12_32/`, `<Steam>/ubuntu12_64/` or `<Steam>/steamrt64/`, because it downloads relative to the
folder its program runs from. The native setup reads these locations directly and does not move the depot folders:

```text
<Steam>/steamapps/content/app_553850/depot_553851/
<Steam>/steamapps/content/app_553850/depot_553853/
<Steam>/steamapps/content/app_553850/depot_553854/
```

The wizard waits for the required depot files, then copies only the supported build's files and checks their Steam
hashes as it copies. The download can remain in Steam's folder or be deleted using the setup option.

## Updating

Close the native launcher, then replace only the install's `app` folder from the newer `HellForge-Linux.zip`; keep
`game`, `userdata`, `profiles`, `logs` and `mods`. Restore the launcher's executable bit with
`chmod +x "$install/app/launcher-linux/HellForge.Launcher"` and restart it.

## Removing HellForge

HellForge's Uninstall button does not work on Linux; remove it by hand:

1. If you ran `allow-namespace.sh`, undo it first, while the script is still there:
   `sudo bash <installation>/app/linux/allow-namespace.sh --remove`.
2. Delete the prefix, if it is HellForge's own: `rm -rf ~/.local/share/hellforge-prefix`. That deletes the
   installation with the game copy, your HellForge profiles and progress, and every mod in its mods folder, your own
   too. Copy anything you want to keep first. In a shared prefix, delete only the installation folder.
3. If Steam's download of the supported build is still in Steam's folder, delete its `app_553850` folder there if you
   no longer need it.

## How it keeps the game offline

On Windows the copied game is kept off the internet by a network block in the Windows Filtering Platform. Wine has
none, so on Linux the session runner, backend and game run inside a network namespace: a private network with no route
out. `linux/hellforge-offline.sh` makes it without root, with:

- loopback, for the local backend and the bridge to Steam;
- a virtual network adapter (`veth`), so Wine and the game see an ordinary network;
- "prohibit" default routes for IPv4 and IPv6, so every outside connection is refused at once. That is the same refusal
  the Windows block gives, which the session's network probe checks before every session;
- a self-test that refuses to start if a connection to 1.1.1.1 gets through.

The original Wine launcher, the session runner, the local backend and the game all run inside it. The native launcher
starts each session through the wrapper; it checks that the wrapper and network-block marker exist. The wrapper enforces
the namespace and its self-test, and the session runner checks the isolation before starting the game
(`src/HellForge.Core/LinuxNetworkBlock.cs`), including for installations copied from Windows.

The game still needs the Steam client, which runs outside with its normal network. `linux/hf-bridge-host.sh` exposes
Steam's local ports as files (unix sockets, which cross into the namespace), and `linux/hf-inner.sh` turns them back into
the same local ports inside. The bridges use `nodelay`: without it the game's many small calls to Steam were held back,
and the frame rate dropped badly.

The namespace is a network block, not a sandbox. It holds back the game's own connections, but programs the game asks to
act for it outside still can: the Steam client through the bridge, the desktop session's services through their
sockets, and, on systems that resolve names through systemd-resolved, name lookups. The Windows block is the same kind of
block.

The galactic war is fetched just before the namespace is entered (`HellForge.LocalBackend.exe fetch-live-war`), and the
session serves that copy. With "Follow the live galactic war", the war is the one from launch time and moves on at the
next launch.

## One prefix, one network

Wine opens a Windows program's sockets in the prefix's `wineserver`, so a program gets the wineserver's network, not its
own. A wineserver of HellForge's prefix still running outside the namespace would give the programs started inside the
internet. `hellforge-offline.sh` therefore waits up to 20 seconds for one to end (it does, a few seconds after its last
program) and refuses to start while one runs. Should one start in the moment between that check and the namespace, the
session's network probe (in the session runner, before the game starts) refuses the session. A prefix of HellForge's own
avoids all of this.

## Problems players have reported

These come from the testers in [issue 1](https://github.com/SkyeShade/HellForge/issues/1). None of them is fixed yet.

- The game closes when you pick a mission on the ship's map. DreamyBranch found it happens after hovering over the
  mission's card, and that clicking a mission quickly, without hovering, gets you into the Hellpod. For other players
  it closes as soon as the ship starts to move, and one player saw the unmodified game close a few minutes into missions
  under Proton too. The cause is not known.
- HellForge stops at start with a connection error although Steam is running and signed in. Two testers saw this after
  a Vulkan overlay such as MangoHud, or a Proton build that changes the overlays, had changed the prefix. Starting
  HellForge once with another Proton (`PROTON=...`), then with the usual one again, cleared it.
- Co-op between a Linux player and Windows players did not connect in joshua-software-dev's tests.

## Tested so far

In an Ubuntu 26.04 VM with plain Wine 10 (no Steam, no GPU; 2026-10-21):

- the namespace as a normal user, with `allow-namespace.sh` where AppArmor restricts it;
- Windows programs inside refused with WSAEACCES;
- the war fetched outside and served inside without a request to the mirror;
- the session runner's network probe;
- the refusal while a wineserver of the prefix runs outside;
- the Steam bridge (against a stand-in for Steam), started and stopped by `hellforge-linux.sh`.

The native launcher UI was started successfully on CachyOS. The game itself, Proton and the Steam client were tested by
the testers in issue 1. This environment has no game files or live Steam client, so native setup, the Proton ownership
probe, and a real game session have not been tested here.

## Not known yet

- Native setup's direct depot reads, engine-package extraction, and the Proton ownership probe still need a full test on
  a Linux machine with Steam and the game depot download.
- Co-op on Linux. Friends' games are reached through Steam's networking, which runs in the Steam client outside the
  namespace, but the one test so far did not connect (see [above](#problems-players-have-reported)).
- Steam as a Flatpak or Snap (its own `/tmp` and paths), and the Steam Deck (no `socat` by default), are untested.
- The Steam ownership check under Proton. Native setup and every session run it, as on Windows. The probe must confirm
  the game on the signed-in Steam account, or setup and the session stop. It has not yet been run on a Linux machine,
  so if setup or a session stops at "The ownership check did not pass", please report it with the logs.

## Helping with Linux

Linux support grows from what Linux players try. If you get further than this page, find a fix, or test on a
distribution or Steam setup listed above as untested, open an issue with your logs or send a pull request: the scripts
are in [`linux/`](../linux) and the Wine checks in `src/HellForge.Core/LinuxNetworkBlock.cs`. See
[CONTRIBUTING.md](../CONTRIBUTING.md) for how to build and test.

If a session refuses to start, see [troubleshooting](troubleshooting.md). For every other page, see the
[docs map](README.md).
