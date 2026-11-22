//! Win32 primitives for the in-product guardian (G1/G2).
//!
//! Unsafe calls stay in this module; the rest of the crate only sees the safe
//! wrappers.

use std::mem::size_of;

use windows::Win32::{
    Foundation::WAIT_OBJECT_0,
    System::{
        Diagnostics::ToolHelp::{
            CreateToolhelp32Snapshot, Process32FirstW, Process32NextW, PROCESSENTRY32W,
            TH32CS_SNAPPROCESS,
        },
        Threading::{
            OpenProcess, TerminateProcess, WaitForSingleObject, PROCESS_SYNCHRONIZE,
            PROCESS_TERMINATE,
        },
    },
};

use super::process::ProcessHandle;

/// Blocks until the process with `pid` exits. Returns immediately when the
/// process no longer exists or cannot be opened (nothing to wait for).
pub(crate) fn wait_for_exit(pid: u32) {
    // SAFETY: OpenProcess takes no pointers; the returned handle is owned by
    // ProcessHandle, which closes it on drop. SYNCHRONIZE is enough to wait.
    let Ok(handle) = (unsafe { OpenProcess(PROCESS_SYNCHRONIZE, false, pid) }) else {
        return;
    };
    let handle = ProcessHandle(handle);
    // SAFETY: handle is a live process handle with SYNCHRONIZE access; the wait
    // takes no borrowed pointers and blocks until the process signals exit.
    let _ = unsafe { WaitForSingleObject(handle.0, windows::Win32::System::Threading::INFINITE) }
        == WAIT_OBJECT_0;
}

/// Terminates the process with `pid`. Best effort: a missing process or a
/// failed open is simply ignored. Used when the user turns the crash guard off
/// and the guardian this main process spawned must stop at once.
pub(crate) fn terminate(pid: u32) {
    // SAFETY: OpenProcess takes no pointers; a returned handle is owned by
    // ProcessHandle, which closes it. PROCESS_TERMINATE permits TerminateProcess.
    let Ok(handle) = (unsafe { OpenProcess(PROCESS_TERMINATE, false, pid) }) else {
        return;
    };
    let handle = ProcessHandle(handle);
    // SAFETY: handle is a live process handle opened with terminate access;
    // TerminateProcess takes no borrowed pointers.
    let _ = unsafe { TerminateProcess(handle.0, 0) };
}

/// Ids of every running process whose executable name is one of `file_names`
/// (case-insensitive). Used by the guardian to detect that the island was
/// reopened manually before it tries to relaunch one itself.
pub(crate) fn running_process_ids(file_names: &[&str]) -> Vec<u32> {
    // SAFETY: a process snapshot takes no pointers; the returned HANDLE is
    // closed below via CloseHandle (which also closes snapshot handles).
    let Ok(raw) = (unsafe { CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0) }) else {
        return Vec::new();
    };
    let snapshot = ProcessHandle(raw);
    let mut entry = PROCESSENTRY32W {
        dwSize: size_of::<PROCESSENTRY32W>() as u32,
        ..Default::default()
    };
    let mut ids = Vec::new();
    // SAFETY: the snapshot handle is live and `entry` has dwSize set.
    let mut more = unsafe { Process32FirstW(snapshot.0, &mut entry) }.is_ok();
    while more {
        let length = entry
            .szExeFile
            .iter()
            .position(|unit| *unit == 0)
            .unwrap_or(entry.szExeFile.len());
        let name = String::from_utf16_lossy(&entry.szExeFile[..length]);
        if file_names
            .iter()
            .any(|wanted| wanted.eq_ignore_ascii_case(&name))
        {
            ids.push(entry.th32ProcessID);
        }
        // SAFETY: as for Process32FirstW.
        more = unsafe { Process32NextW(snapshot.0, &mut entry) }.is_ok();
    }
    ids
}
