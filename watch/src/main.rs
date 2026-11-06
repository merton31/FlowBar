// Сторож FlowBar: ждёт завершения панели (даже аварийного или через «Снять задачу»)
// и возвращает панель задач Windows в исходное состояние.
// Запуск: flowbar-watch.exe <pid FlowBar> [исходное состояние ABM_GETSTATE] [была ли видна: 1/0]
// Готовность подтверждает событием FlowBar_watch_ready_<pid>: FlowBar не скрывает панель Windows, пока его не дождётся.
// Без окна и без консоли; ~2 МБ памяти вместо ~18 МБ у сторожа на .NET.
#![windows_subsystem = "windows"]

use std::ffi::c_void;
use std::ptr::null_mut;

type Handle = *mut c_void;
type Hwnd = *mut c_void;

const SYNCHRONIZE: u32 = 0x0010_0000;
const EVENT_MODIFY_STATE: u32 = 0x0002;
const INFINITE: u32 = 0xFFFF_FFFF;
const SW_SHOW: i32 = 5;
const ABM_GETSTATE: u32 = 4;
const ABM_SETSTATE: u32 = 10;

#[repr(C)]
struct AppBarData {
    cb_size: u32,
    hwnd: Hwnd,
    callback: u32,
    edge: u32,
    rc: [i32; 4],
    lparam: isize,
}

#[link(name = "kernel32")]
extern "system" {
    fn OpenProcess(access: u32, inherit: i32, pid: u32) -> Handle;
    fn WaitForSingleObject(h: Handle, ms: u32) -> u32;
    fn CloseHandle(h: Handle) -> i32;
    fn OpenEventW(access: u32, inherit: i32, name: *const u16) -> Handle;
    fn SetEvent(h: Handle) -> i32;
}

#[link(name = "user32")]
extern "system" {
    fn FindWindowW(class: *const u16, title: *const u16) -> Hwnd;
    fn FindWindowExW(parent: Hwnd, after: Hwnd, class: *const u16, title: *const u16) -> Hwnd;
    fn IsWindowVisible(h: Hwnd) -> i32;
    fn ShowWindow(h: Hwnd, cmd: i32) -> i32;
}

#[link(name = "shell32")]
extern "system" {
    fn SHAppBarMessage(msg: u32, data: *mut AppBarData) -> usize;
}

fn wide(s: &str) -> Vec<u16> {
    s.encode_utf16().chain(std::iter::once(0)).collect()
}

/// Показать основную и дополнительные (на других мониторах) панели задач Windows.
fn show_taskbars() {
    unsafe {
        let main = FindWindowW(wide("Shell_TrayWnd").as_ptr(), null_mut());
        if !main.is_null() && IsWindowVisible(main) == 0 {
            ShowWindow(main, SW_SHOW);
        }
        let class = wide("Shell_SecondaryTrayWnd");
        let mut h = FindWindowExW(null_mut(), null_mut(), class.as_ptr(), null_mut());
        while !h.is_null() {
            if IsWindowVisible(h) == 0 {
                ShowWindow(h, SW_SHOW);
            }
            h = FindWindowExW(null_mut(), h, class.as_ptr(), null_mut());
        }
    }
}

/// Вернуть исходное автоскрытие панели Windows (FlowBar включает его, чтобы скрытая панель не держала свою полосу).
fn restore_state(state: isize) {
    unsafe {
        let mut d = AppBarData { cb_size: std::mem::size_of::<AppBarData>() as u32, hwnd: FindWindowW(wide("Shell_TrayWnd").as_ptr(), null_mut()), callback: 0, edge: 0, rc: [0; 4], lparam: 0 };
        if SHAppBarMessage(ABM_GETSTATE, &mut d) as isize != state {
            d.lparam = state;
            SHAppBarMessage(ABM_SETSTATE, &mut d);
        }
    }
}

fn main() {
    let args: Vec<String> = std::env::args().collect();
    let pid: u32 = match args.get(1).and_then(|a| a.parse().ok()) {
        Some(p) => p,
        None => return, // без pid делать нечего
    };
    let state: isize = args.get(2).and_then(|a| a.parse().ok()).unwrap_or(-1);
    let was_visible = args.get(3).map(|a| a != "0").unwrap_or(true);
    unsafe {
        let h = OpenProcess(SYNCHRONIZE, 0, pid);
        if !h.is_null() {
            // готов: процесс FlowBar открыт, его завершение не пропустим
            let ev = OpenEventW(EVENT_MODIFY_STATE, 0, wide(&format!("FlowBar_watch_ready_{}", pid)).as_ptr());
            if !ev.is_null() {
                SetEvent(ev);
                CloseHandle(ev);
            }
            WaitForSingleObject(h, INFINITE);
            CloseHandle(h);
        }
        // процесс уже завершён или недоступен — всё равно возвращаем панель
    }
    if state >= 0 {
        restore_state(state);
    }
    if was_visible {
        show_taskbars();
    }
    if let Ok(appdata) = std::env::var("APPDATA") {
        let _ = std::fs::remove_file(std::path::Path::new(&appdata).join("FlowBar").join("taskbar.txt"));
    }
}
