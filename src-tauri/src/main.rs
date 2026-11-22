// Prevents additional console window on Windows in release, DO NOT REMOVE!!
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

fn main() {
    // A guardian child must branch before any Tauri setup: entering `run()` would
    // create a window/tray and collide with the single-instance mutex.
    let args: Vec<String> = std::env::args().collect();
    if let Some(guardian) = floatisle_lib::guardian_args(&args) {
        floatisle_lib::run_guardian(guardian);
        return;
    }
    floatisle_lib::run()
}
