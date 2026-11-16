//! Reading a SQLite file through the system `winsqlite3.dll`.
//!
//! The DSH usage ledger is SQLite, and this project refuses new crates unless there is
//! no other way; Windows ships the library, so loading its nine entry points by name
//! costs no extra crate and no extra binary. Everything here is `unsafe`; callers
//! outside this module only see `Database` and integer rows.

use std::ffi::{c_char, c_int, c_void, CStr, CString};
use std::path::Path;
use std::ptr;
use std::sync::OnceLock;

use windows::core::PCWSTR;
use windows::Win32::Foundation::HMODULE;
use windows::Win32::System::LibraryLoader::{GetProcAddress, LoadLibraryW};

type Handle = *mut c_void;

type OpenV2 = unsafe extern "system" fn(*const c_char, *mut Handle, c_int, *const c_char) -> c_int;
type PrepareV2 = unsafe extern "system" fn(
    Handle,
    *const c_char,
    c_int,
    *mut *mut c_void,
    *mut *const c_char,
) -> c_int;
type BindText = unsafe extern "system" fn(*mut c_void, c_int, *const c_char, c_int, *mut c_void) -> c_int;
type Step = unsafe extern "system" fn(*mut c_void) -> c_int;
type ColumnInt64 = unsafe extern "system" fn(*mut c_void, c_int) -> i64;
type ColumnCount = unsafe extern "system" fn(*mut c_void) -> c_int;
type Finalize = unsafe extern "system" fn(*mut c_void) -> c_int;
type CloseV2 = unsafe extern "system" fn(Handle) -> c_int;
type ErrMsg = unsafe extern "system" fn(Handle) -> *const c_char;

const SQLITE_OK: c_int = 0;
const SQLITE_ROW: c_int = 100;
const SQLITE_DONE: c_int = 101;
const SQLITE_OPEN_READONLY: c_int = 0x1;
/// Only the tests ever write: they need a database to read.
#[cfg(test)]
const SQLITE_OPEN_READWRITE: c_int = 0x2;
#[cfg(test)]
const SQLITE_OPEN_CREATE: c_int = 0x4;
const SQLITE_TRANSIENT: isize = -1;

struct Api {
    open_v2: OpenV2,
    prepare_v2: PrepareV2,
    bind_text: BindText,
    step: Step,
    column_int64: ColumnInt64,
    column_count: ColumnCount,
    finalize: Finalize,
    close_v2: CloseV2,
    errmsg: ErrMsg,
}

static API: OnceLock<Option<Api>> = OnceLock::new();

fn symbol<T>(library: Handle, name: &str) -> Option<T> {
    let name = CString::new(name).ok()?;
    // SAFETY: `library` is the handle LoadLibraryW returned and stays loaded for the
    // process lifetime, and `name` is a NUL-terminated ASCII name.
    let address = unsafe { GetProcAddress(HMODULE(library), windows::core::PCSTR(name.as_ptr().cast())) }?;
    // SAFETY: the DLL is the system SQLite, so each name resolves to the documented
    // signature; `T` is only ever instantiated with those exact signatures.
    Some(unsafe { std::mem::transmute_copy::<unsafe extern "system" fn() -> isize, T>(&address) })
}

fn api() -> Option<&'static Api> {
    API.get_or_init(|| {
        let module: Vec<u16> = "winsqlite3.dll\0".encode_utf16().collect();
        // SAFETY: `module` is a NUL-terminated UTF-16 name that outlives the call.
        let library = unsafe { LoadLibraryW(PCWSTR(module.as_ptr())) }.ok()?;
        let library = library.0 as Handle;
        Some(Api {
            open_v2: symbol(library, "sqlite3_open_v2")?,
            prepare_v2: symbol(library, "sqlite3_prepare_v2")?,
            bind_text: symbol(library, "sqlite3_bind_text")?,
            step: symbol(library, "sqlite3_step")?,
            column_int64: symbol(library, "sqlite3_column_int64")?,
            column_count: symbol(library, "sqlite3_column_count")?,
            finalize: symbol(library, "sqlite3_finalize")?,
            close_v2: symbol(library, "sqlite3_close_v2")?,
            errmsg: symbol(library, "sqlite3_errmsg")?,
        })
    })
    .as_ref()
}

/// An open database handle; closing it is the destructor's job.
pub(crate) struct Database {
    handle: Handle,
    api: &'static Api,
}

impl Drop for Database {
    fn drop(&mut self) {
        // SAFETY: `handle` came from `open_v2` and is closed exactly once, here.
        unsafe {
            (self.api.close_v2)(self.handle);
        }
    }
}

impl Database {
    /// Runs one statement and returns every row as a list of integer columns.
    pub(crate) fn query_ints(
        &self,
        sql: &str,
        text_parameters: &[&str],
    ) -> Result<Vec<Vec<i64>>, String> {
        let statement = CString::new(sql).map_err(|_| "the statement carries a NUL byte".to_string())?;
        let mut handle: *mut c_void = ptr::null_mut();
        // SAFETY: `self.handle` is open, `statement` is NUL-terminated, and `handle` is
        // a valid out-parameter; the statement is finalized below on every path.
        let code = unsafe {
            (self.api.prepare_v2)(
                self.handle,
                statement.as_ptr(),
                -1,
                &mut handle,
                ptr::null_mut(),
            )
        };
        if code != SQLITE_OK || handle.is_null() {
            return Err(self.last_error(code));
        }
        let result = self.run(handle, text_parameters);
        // SAFETY: `handle` was produced by `prepare_v2` above and is finalized once.
        unsafe {
            (self.api.finalize)(handle);
        }
        result
    }

    fn run(&self, handle: *mut c_void, text_parameters: &[&str]) -> Result<Vec<Vec<i64>>, String> {
        for (index, value) in text_parameters.iter().enumerate() {
            let value = CString::new(*value).map_err(|_| "a parameter carries a NUL byte".to_string())?;
            // SAFETY: the statement is live, the index is 1-based and within the
            // statement's parameter count, and SQLITE_TRANSIENT copies the bytes.
            let code = unsafe {
                (self.api.bind_text)(
                    handle,
                    index as c_int + 1,
                    value.as_ptr(),
                    -1,
                    SQLITE_TRANSIENT as *mut c_void,
                )
            };
            if code != SQLITE_OK {
                return Err(self.last_error(code));
            }
        }

        // SAFETY: `handle` is a prepared statement; the column count is read once.
        let columns = unsafe { (self.api.column_count)(handle) }.max(0) as usize;
        let mut rows = Vec::new();
        loop {
            // SAFETY: `handle` is a prepared statement being stepped.
            let step = unsafe { (self.api.step)(handle) };
            if step == SQLITE_DONE {
                return Ok(rows);
            }
            if step != SQLITE_ROW {
                return Err(self.last_error(step));
            }
            let mut row = Vec::with_capacity(columns);
            for column in 0..columns {
                // SAFETY: SQLITE_ROW means every column below `columns` is readable.
                row.push(unsafe { (self.api.column_int64)(handle, column as c_int) });
            }
            rows.push(row);
        }
    }

    fn last_error(&self, code: c_int) -> String {
        // SAFETY: `self.handle` is open, so `errmsg` returns its message pointer.
        let message = unsafe { (self.api.errmsg)(self.handle) };
        if message.is_null() {
            return format!("sqlite error {code}");
        }
        // SAFETY: SQLite owns a NUL-terminated string behind that pointer, valid until
        // the next call on the same connection; it is copied here immediately.
        let text = unsafe { CStr::from_ptr(message) }.to_string_lossy().to_string();
        format!("sqlite error {code}: {text}")
    }
}

#[cfg(test)]
impl Database {
    /// Tests build their own database; production code only ever opens read-only.
    pub(crate) fn open_for_tests(path: &Path) -> Result<Self, String> {
        Self::open(path, SQLITE_OPEN_READWRITE | SQLITE_OPEN_CREATE)
    }

    pub(crate) fn exec(&self, sql: &str) -> Result<(), String> {
        self.query_ints(sql, &[]).map(|_| ())
    }
}

impl Database {
    pub(crate) fn open_read_only(path: &Path) -> Result<Self, String> {
        Self::open(path, SQLITE_OPEN_READONLY)
    }

    fn open(path: &Path, flags: c_int) -> Result<Self, String> {
        let api = api().ok_or_else(|| "winsqlite3.dll is not available".to_string())?;
        // SQLite takes UTF-8 paths on every platform; handing it UTF-16 stops at the
        // first NUL byte and every connection would land on the same file.
        let name = CString::new(path.as_os_str().to_string_lossy().as_bytes())
            .map_err(|_| "the database path carries a NUL byte".to_string())?;
        let mut handle: Handle = ptr::null_mut();
        // SAFETY: `name` is NUL-terminated, `handle` is a valid out-parameter, and `api`
        // holds signatures resolved from the system DLL.
        let code = unsafe { (api.open_v2)(name.as_ptr(), &mut handle, flags, ptr::null()) };
        if code != SQLITE_OK || handle.is_null() {
            let message = if handle.is_null() {
                format!("sqlite error {code}: the database could not be opened")
            } else {
                // SAFETY: a non-null handle from `open_v2` is an open connection, and
                // `errmsg` returns its NUL-terminated message.
                let text = unsafe { CStr::from_ptr((api.errmsg)(handle)) }
                    .to_string_lossy()
                    .to_string();
                // SAFETY: this connection is discarded on the error path and is closed once.
                unsafe {
                    (api.close_v2)(handle);
                }
                format!("sqlite error {code}: {text}")
            };
            return Err(message);
        }
        Ok(Self { handle, api })
    }
}

#[cfg(test)]
#[path = "sqlite_tests.rs"]
mod sqlite_tests;
