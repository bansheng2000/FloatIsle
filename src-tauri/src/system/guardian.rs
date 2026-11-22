//! In-product self-healing watchdog.
//!
//! A second copy of this exe is spawned with `--guardian --watch-pid=<pid>`.
//! That copy runs this module's pure-Rust loop and never enters `run()`, so it
//! never creates a Tauri window, a tray icon, or the single-instance mutex.
//!
//! G1: parse the switch, spawn one hidden child, wait for the watched process.
//! G2: on exit, honour the opt-out marker, avoid doubles, and relaunch the main
//! process when nothing else is serving the island.

use std::{
    net::{TcpStream, ToSocketAddrs},
    os::windows::process::CommandExt,
    process::{Command, Stdio},
    sync::atomic::{AtomicU32, Ordering},
    time::Duration,
};

use super::CREATE_NO_WINDOW;

/// Loopback bridge port owned by the one live main process. Listening means a
/// main instance is already serving, so a guardian must not launch another.
const BRIDGE_PORT: u16 = 47821;

/// Set on a main process launched by a guardian so it does not spawn a second
/// guardian of its own — the relaunching guardian keeps watching instead.
const MANAGED_ENV: &str = "FLOATISLE_GUARDIAN_MANAGED";

/// Pid of the guardian spawned by this main process, so turning the switch off
/// can terminate it immediately (V5/G3). Zero means none is being tracked.
static SPAWNED_GUARDIAN_PID: AtomicU32 = AtomicU32::new(0);

/// Parsed guardian command line. `None` from [`parse_args`] means: run the app
/// normally; `Some` means: this process is the watchdog child.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct GuardianArgs {
    pub(crate) watch_pid: u32,
}

/// What a guardian does once the process it watches has exited.
///
/// Kept as a pure decision so every branch is unit-testable without spawning
/// real processes: a deliberate quit or an already-live island means "retire";
/// only a genuine crash with nothing listening means "relaunch".
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(crate) enum GuardianDecision {
    Relaunch,
    Retire,
}

/// Pure G2 decision. `optout` = the deliberate-exit marker is present;
/// `bridge_listening` = 47821 is served; `other_islands` = live island
/// processes other than this guardian and the watched pid.
pub(crate) fn decide_after_exit(
    optout: bool,
    bridge_listening: bool,
    other_islands: usize,
) -> GuardianDecision {
    if optout || bridge_listening || other_islands > 0 {
        GuardianDecision::Retire
    } else {
        GuardianDecision::Relaunch
    }
}

/// Reads the guardian switch from a process argument list.
///
/// Returns `None` on the normal app path; `Some` only when `--guardian` is
/// present together with a usable `--watch-pid=N` (N > 0).
pub fn parse_args(args: &[String]) -> Option<GuardianArgs> {
    if !args.iter().any(|arg| arg == "--guardian") {
        return None;
    }
    let raw = args
        .iter()
        .find_map(|arg| arg.strip_prefix("--watch-pid="))?;
    let watch_pid = raw.parse::<u32>().ok()?;
    if watch_pid == 0 {
        return None;
    }
    Some(GuardianArgs { watch_pid })
}

/// True when the loopback bridge is accepting connections, i.e. a main process
/// is already up. A short timeout keeps a wedged port from stalling the loop.
fn bridge_listening() -> bool {
    let Ok(addresses) = ("127.0.0.1", BRIDGE_PORT).to_socket_addrs() else {
        return false;
    };
    addresses
        .into_iter()
        .any(|address| TcpStream::connect_timeout(&address, Duration::from_millis(300)).is_ok())
}

/// Number of island processes still alive besides this guardian and the pid it
/// was watching. A manual reopen shows up here and stops a duplicate launch.
fn other_island_processes(watched_pid: u32) -> usize {
    let self_pid = std::process::id();
    crate::win32::guardian::running_process_ids(&["floatisle.exe"])
        .into_iter()
        .filter(|pid| *pid != self_pid && *pid != watched_pid)
        .count()
}

/// Launches a fresh main process that is already guarded, so it must not spawn
/// another guardian. Returns its pid so this guardian can keep watching it.
fn relaunch_main() -> Option<u32> {
    let exe = std::env::current_exe().ok()?;
    let child = Command::new(exe)
        .env(MANAGED_ENV, "1")
        .creation_flags(CREATE_NO_WINDOW)
        .stdin(Stdio::null())
        .stdout(Stdio::null())
        .stderr(Stdio::null())
        .spawn()
        .ok()?;
    Some(child.id())
}

/// Guardian process entry point: no Tauri, no mutex, no window, no tray.
pub fn run(mut args: GuardianArgs) {
    loop {
        // Block until the watched main process exits.
        crate::win32::guardian::wait_for_exit(args.watch_pid);
        // Give a manual reopen a moment to bind the bridge before deciding.
        std::thread::sleep(Duration::from_millis(1500));

        // G3: a guard switched off at runtime must never relaunch, even for a
        // guardian that survived a previous crash-relaunch cycle.
        if !crate::system::guardian_config::crash_guard_enabled() {
            return;
        }

        let optout = crate::system::guard_optout::deliberate_exit_flag_present();
        let decision = decide_after_exit(
            optout,
            bridge_listening(),
            other_island_processes(args.watch_pid),
        );
        match decision {
            GuardianDecision::Retire => return,
            GuardianDecision::Relaunch => {
                let Some(pid) = relaunch_main() else {
                    return;
                };
                args.watch_pid = pid; // keep watching the process we launched
            }
        }
    }
}

/// Spawns one hidden guardian for the current main process. Best effort: a
/// failure here must never stop the island from starting. A main launched by a
/// guardian is already supervised and skips this step.
pub(crate) fn spawn_for_current_process() {
    if std::env::var_os(MANAGED_ENV)
        .map(|value| !value.is_empty())
        .unwrap_or(false)
    {
        return;
    }
    // G3: the switch is off => no guardian at all (V5).
    if !super::guardian_config::crash_guard_enabled() {
        return;
    }
    let Ok(exe) = std::env::current_exe() else {
        return;
    };
    let pid = std::process::id();
    let child = Command::new(exe)
        .arg("--guardian")
        .arg(format!("--watch-pid={pid}"))
        .creation_flags(CREATE_NO_WINDOW)
        .stdin(Stdio::null())
        .stdout(Stdio::null())
        .stderr(Stdio::null())
        .spawn();
    if let Ok(child) = child {
        SPAWNED_GUARDIAN_PID.store(child.id(), Ordering::SeqCst);
    }
}

/// Ends the guardian spawned by this main process when the user turns the
/// crash guard off at runtime. Best effort; forgets the tracked pid either way.
pub(crate) fn terminate_spawned_guardian() {
    let pid = SPAWNED_GUARDIAN_PID.swap(0, Ordering::SeqCst);
    if pid != 0 {
        crate::win32::guardian::terminate(pid);
    }
}

#[cfg(test)]
mod tests {
    use super::{decide_after_exit, parse_args, GuardianDecision};

    #[test]
    fn normal_launch_has_no_guardian_args() {
        assert_eq!(parse_args(&["floatisle.exe".into()]), None);
        assert_eq!(
            parse_args(&["floatisle.exe".into(), "--watch-pid=123".into()]),
            None,
            "--watch-pid without --guardian is not a guardian request"
        );
    }

    #[test]
    fn parses_guardian_with_watch_pid() {
        let args = [
            "floatisle.exe".into(),
            "--guardian".into(),
            "--watch-pid=4242".into(),
        ];
        let parsed = parse_args(&args).expect("must parse guardian args");
        assert_eq!(parsed.watch_pid, 4242);
    }

    #[test]
    fn rejects_missing_or_invalid_watch_pid() {
        assert_eq!(
            parse_args(&["floatisle.exe".into(), "--guardian".into()]),
            None,
            "guardian with no watch pid is invalid"
        );
        assert_eq!(
            parse_args(&[
                "floatisle.exe".into(),
                "--guardian".into(),
                "--watch-pid=0".into(),
            ]),
            None,
            "pid 0 is never a valid watch target"
        );
        assert_eq!(
            parse_args(&[
                "floatisle.exe".into(),
                "--guardian".into(),
                "--watch-pid=abc".into(),
            ]),
            None,
            "non numeric pid is rejected"
        );
    }

    #[test]
    fn a_clean_crash_relaunches() {
        assert_eq!(
            decide_after_exit(false, false, 0),
            GuardianDecision::Relaunch,
            "no optout, no bridge, no other island => genuine crash"
        );
    }

    #[test]
    fn deliberate_exit_or_live_island_stops_relaunch() {
        assert_eq!(decide_after_exit(true, false, 0), GuardianDecision::Retire);
        assert_eq!(decide_after_exit(false, true, 0), GuardianDecision::Retire);
        assert_eq!(decide_after_exit(false, false, 1), GuardianDecision::Retire);
    }
}
