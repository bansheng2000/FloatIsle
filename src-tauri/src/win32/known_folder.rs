//! The user's known folders (Downloads, Documents), which can be moved away from
//! the profile folder, so they are asked from Windows rather than assumed.

use std::path::PathBuf;
use windows::Win32::{
    System::Com::CoTaskMemFree,
    UI::Shell::{FOLDERID_Documents, FOLDERID_Downloads, SHGetKnownFolderPath, KF_FLAG_DEFAULT},
};

/// The current user's Downloads folder; `None` if Windows cannot say.
pub(crate) fn downloads() -> Option<PathBuf> {
    // SAFETY: FOLDERID_Downloads is a valid known-folder id, no token is passed
    // (current user), and the returned buffer is freed below with CoTaskMemFree,
    // as SHGetKnownFolderPath requires, whether or not it reads as a string.
    let path = unsafe { SHGetKnownFolderPath(&FOLDERID_Downloads, KF_FLAG_DEFAULT, None) }.ok()?;
    // SAFETY: on success `path` points to a NUL-terminated UTF-16 string.
    let text = unsafe { path.to_string() };
    // SAFETY: `path` came from SHGetKnownFolderPath and is freed exactly once.
    unsafe { CoTaskMemFree(Some(path.0.cast())) };
    text.ok().filter(|text| !text.is_empty()).map(PathBuf::from)
}

/// The current user's Documents folder; `None` if Windows cannot say. It is not
/// always `<profile>\Documents`: the user can move it to another drive, and this
/// machine has it at `D:\文档`, so joining `Documents` by hand lands on the wrong
/// disk.
pub(crate) fn documents() -> Option<PathBuf> {
    // SAFETY: FOLDERID_Documents is a valid known-folder id, no token is passed
    // (current user), and the buffer is freed below exactly once.
    let path = unsafe { SHGetKnownFolderPath(&FOLDERID_Documents, KF_FLAG_DEFAULT, None) }.ok()?;
    // SAFETY: on success `path` points to a NUL-terminated UTF-16 string.
    let text = unsafe { path.to_string() };
    // SAFETY: `path` came from SHGetKnownFolderPath and is freed exactly once.
    unsafe { CoTaskMemFree(Some(path.0.cast())) };
    text.ok().filter(|text| !text.is_empty()).map(PathBuf::from)
}

#[cfg(test)]
mod tests {
    use super::{documents, downloads};

    #[test]
    fn the_downloads_folder_is_an_absolute_path() {
        let path = downloads().expect("Downloads");
        assert!(path.is_absolute(), "{path:?}");
    }

    #[test]
    fn the_documents_folder_is_an_absolute_path() {
        let path = documents().expect("Documents");
        assert!(path.is_absolute(), "{path:?}");
    }
}
