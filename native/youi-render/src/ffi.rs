//! Versioned C ABI. Never retain caller buffers; platform handles are borrowed.
//! Renderer handles are checked registry IDs.
//! Calls are serialized, and errors/panics are contained at this boundary.
use crate::{Command, FrameStats, Renderer, ResourceBudget, ResourceUsage, TextCell};
use std::{
    cell::RefCell,
    collections::HashMap,
    sync::{Mutex, OnceLock},
};
thread_local! {static ERROR:RefCell<String>=const{RefCell::new(String::new())};}
static ENGINES: OnceLock<Mutex<HashMap<u64, Renderer>>> = OnceLock::new();
static NEXT: std::sync::atomic::AtomicU64 = std::sync::atomic::AtomicU64::new(1);
fn guard(f: impl FnOnce() -> Result<(), String>) -> i32 {
    match std::panic::catch_unwind(std::panic::AssertUnwindSafe(f)) {
        Ok(Ok(())) => 0,
        Ok(Err(e)) => {
            ERROR.with(|s| *s.borrow_mut() = e);
            -1
        }
        Err(_) => {
            ERROR.with(|s| *s.borrow_mut() = "native panic contained at ABI boundary".into());
            -2
        }
    }
}
fn with_engine(id: u64, f: impl FnOnce(&mut Renderer) -> Result<(), String>) -> Result<(), String> {
    let mut engines = ENGINES
        .get_or_init(Default::default)
        .lock()
        .map_err(|_| "renderer registry poisoned")?;
    f(engines
        .get_mut(&id)
        .ok_or("invalid or disposed renderer handle")?)
}
fn register(renderer: Renderer) -> Result<u64, String> {
    let id = NEXT.fetch_add(1, std::sync::atomic::Ordering::Relaxed);
    ENGINES
        .get_or_init(Default::default)
        .lock()
        .map_err(|_| "renderer registry poisoned")?
        .insert(id, renderer);
    Ok(id)
}
fn checked_pointer<T>(ptr: *const T, name: &str) -> Result<(), String> {
    if ptr.is_null() || !(ptr as usize).is_multiple_of(std::mem::align_of::<T>()) {
        return Err(format!("null or misaligned {name} pointer"));
    }
    Ok(())
}
unsafe fn bytes<'a>(ptr: *const u8, len: u32) -> Result<&'a [u8], String> {
    if len == 0 {
        return Ok(&[]);
    }
    if ptr.is_null() || len > 16 * 1024 * 1024 {
        return Err("invalid byte buffer".into());
    }
    Ok(std::slice::from_raw_parts(ptr, len as usize))
}
#[no_mangle]
pub extern "C" fn youi_abi_version() -> u32 {
    0x0001_0000
}
#[no_mangle]
pub extern "C" fn youi_command_size() -> u32 {
    std::mem::size_of::<Command>() as u32
}
#[no_mangle]
pub extern "C" fn youi_resource_budget_size() -> u32 {
    std::mem::size_of::<ResourceBudget>() as u32
}
#[no_mangle]
pub extern "C" fn youi_resource_usage_size() -> u32 {
    std::mem::size_of::<ResourceUsage>() as u32
}
/// Additive v1 geometry ABI. Older v1 libraries may not export this symbol.
#[no_mangle]
pub extern "C" fn youi_text_cell_size() -> u32 {
    std::mem::size_of::<TextCell>() as u32
}
/// # Safety
/// out must be writable; a nonzero window handle must outlive the renderer.
#[no_mangle]
pub unsafe extern "C" fn youi_create(w: u32, h: u32, hwnd: isize, out: *mut u64) -> i32 {
    youi_create_with_budget(w, h, hwnd, &ResourceBudget::default(), out)
}
/// Creates a renderer using explicit retained-resource ceilings, in bytes.
/// # Safety
/// budget must be readable and aligned; out must be writable and aligned.
/// A nonzero window handle must outlive the renderer. No caller memory is retained.
#[no_mangle]
pub unsafe extern "C" fn youi_create_with_budget(
    w: u32,
    h: u32,
    hwnd: isize,
    budget: *const ResourceBudget,
    out: *mut u64,
) -> i32 {
    guard(|| {
        checked_pointer(out, "result")?;
        *out = 0;
        checked_pointer(budget, "resource budget")?;
        let renderer = Renderer::new_with_budget(w, h, hwnd, *budget)?;
        *out = register(renderer)?;
        Ok(())
    })
}
/// Additive v1 Linux Xlib surface API. Older v1 libraries may lack this symbol.
/// This does not reinterpret the existing HWND/offscreen API.
/// # Safety
/// `out` must be writable and aligned. `display` must be a live Xlib Display
/// pointer and `window` a live XID on its valid `screen`. Destroy the renderer
/// before the window or display. Calls must obey the host's Xlib thread rules.
#[no_mangle]
pub unsafe extern "C" fn youi_create_xlib(
    w: u32,
    h: u32,
    display: *mut std::ffi::c_void,
    window: usize,
    screen: i32,
    out: *mut u64,
) -> i32 {
    youi_create_xlib_with_budget(
        w,
        h,
        display,
        window,
        screen,
        &ResourceBudget::default(),
        out,
    )
}
/// Creates a Linux Xlib renderer using explicit retained-resource ceilings.
/// Swapchain allocations are excluded from the budget, as for HWND surfaces.
/// # Safety
/// The borrowed display/window and threading contract of `youi_create_xlib`
/// applies. `budget` must be readable and aligned; `out` writable and aligned.
/// The budget is copied. A valid `out` is zeroed before any fallible work.
#[no_mangle]
pub unsafe extern "C" fn youi_create_xlib_with_budget(
    w: u32,
    h: u32,
    display: *mut std::ffi::c_void,
    window: usize,
    screen: i32,
    budget: *const ResourceBudget,
    out: *mut u64,
) -> i32 {
    guard(|| {
        checked_pointer(out, "result")?;
        *out = 0;
        checked_pointer(budget, "resource budget")?;
        let renderer = Renderer::new_xlib_with_budget(w, h, display, window, screen, *budget)?;
        *out = register(renderer)?;
        Ok(())
    })
}
/// # Safety
/// out must reference a writable, aligned ResourceUsage. It is unchanged on error.
#[no_mangle]
pub unsafe extern "C" fn youi_resource_usage(id: u64, out: *mut ResourceUsage) -> i32 {
    guard(|| {
        checked_pointer(out, "resource usage output")?;
        with_engine(id, |e| {
            *out = e.resource_usage();
            Ok(())
        })
    })
}
/// # Safety
/// out must reference a writable, aligned ResourceBudget. It is unchanged on error.
#[no_mangle]
pub unsafe extern "C" fn youi_resource_budget(id: u64, out: *mut ResourceBudget) -> i32 {
    guard(|| {
        checked_pointer(out, "resource budget output")?;
        with_engine(id, |e| {
            *out = e.resource_budget();
            Ok(())
        })
    })
}
#[no_mangle]
pub extern "C" fn youi_destroy(id: u64) -> i32 {
    guard(|| {
        ENGINES
            .get_or_init(Default::default)
            .lock()
            .map_err(|_| "renderer registry poisoned")?
            .remove(&id)
            .ok_or("invalid renderer handle")?;
        Ok(())
    })
}
#[no_mangle]
pub extern "C" fn youi_resize(id: u64, w: u32, h: u32) -> i32 {
    guard(|| with_engine(id, |e| e.resize(w, h)))
}
/// # Safety
/// data must reference length readable RGBA bytes; image must be writable.
#[no_mangle]
pub unsafe extern "C" fn youi_upload_image(
    id: u64,
    w: u32,
    h: u32,
    data: *const u8,
    length: u32,
    image: *mut u32,
) -> i32 {
    guard(|| {
        if image.is_null() {
            return Err("null image output".into());
        }
        let rgba = bytes(data, length)?;
        with_engine(id, |e| {
            *image = e.upload_image(w, h, rgba)?;
            Ok(())
        })
    })
}
#[no_mangle]
pub extern "C" fn youi_release_image(id: u64, image: u32) -> i32 {
    guard(|| with_engine(id, |e| e.release_image(image)))
}
/// # Safety
/// Pointers must reference readable arrays of the given lengths; stats is writable.
#[no_mangle]
pub unsafe extern "C" fn youi_render(
    id: u64,
    commands: *const Command,
    count: u32,
    text: *const u8,
    len: u32,
    stats: *mut FrameStats,
) -> i32 {
    guard(|| {
        if count > 1_000_000
            || (count != 0
                && (commands.is_null()
                    || !(commands as usize).is_multiple_of(std::mem::align_of::<Command>())))
            || stats.is_null()
        {
            return Err("invalid frame pointers/count".into());
        }
        let commands = if count == 0 {
            &[]
        } else {
            std::slice::from_raw_parts(commands, count as usize)
        };
        let text = bytes(text, len)?;
        with_engine(id, |e| {
            *stats = e.render(commands, text)?;
            Ok(())
        })
    })
}
/// # Safety
/// text must reference len bytes. width/height must be writable f32 pointers.
#[no_mangle]
pub unsafe extern "C" fn youi_measure(
    id: u64,
    text: *const u8,
    len: u32,
    size: f32,
    max_width: f32,
    bold: u32,
    width: *mut f32,
    height: *mut f32,
) -> i32 {
    guard(|| {
        if width.is_null() || height.is_null() {
            return Err("null measurement output".into());
        }
        let text = std::str::from_utf8(bytes(text, len)?).map_err(|e| e.to_string())?;
        with_engine(id, |e| {
            let (w, h) = e.measure(text, size, max_width, bold != 0)?;
            *width = w;
            *height = h;
            Ok(())
        })
    })
}
/// Copies grapheme advance cells from the renderer's cached paint layout.
/// flags bit 0 selects bold, bit 1 disables wrapping; other bits are invalid.
///
/// `capacity` and `count` are cell counts, not bytes. A zero-capacity call is a
/// successful sizing query and ignores `cells`. On a nonzero insufficient
/// capacity, returns -1, reports the required count, and writes no cells. On any
/// other error both outputs are unchanged. No caller memory is retained.
/// # Safety
/// text must reference len readable UTF-8 bytes (NULL is allowed for len zero).
/// count must be writable and naturally aligned. For nonzero capacity, cells
/// must be writable and naturally aligned for capacity TextCells. Outputs must
/// not overlap each other or the text buffer. As with other ABI functions, the
/// caller is responsible for backing allocation validity and lifetime.
#[no_mangle]
pub unsafe extern "C" fn youi_text_cells(
    id: u64,
    text: *const u8,
    len: u32,
    size: f32,
    max_width: f32,
    flags: u32,
    cells: *mut TextCell,
    capacity: u32,
    count: *mut u32,
) -> i32 {
    guard(|| {
        checked_pointer(count, "text cell count")?;
        if capacity != 0 {
            checked_pointer(cells, "text cell output")?;
        }
        if flags & !3 != 0 {
            return Err("invalid text geometry flags".into());
        }
        let text = std::str::from_utf8(bytes(text, len)?).map_err(|e| e.to_string())?;
        with_engine(id, |e| {
            let layout = e.text_cells(text, size, max_width, flags & 1 != 0, flags & 2 == 0)?;
            copy_text_cells(&layout, cells, capacity, count)
        })
    })
}
unsafe fn copy_text_cells(
    layout: &[TextCell],
    cells: *mut TextCell,
    capacity: u32,
    count: *mut u32,
) -> Result<(), String> {
    let required = u32::try_from(layout.len()).map_err(|_| "text cell count overflow")?;
    *count = required;
    if capacity == 0 {
        return Ok(());
    }
    if capacity < required {
        return Err("insufficient text cell capacity".into());
    }
    std::ptr::copy_nonoverlapping(layout.as_ptr(), cells, layout.len());
    Ok(())
}
/// # Safety
/// path must reference len bytes of UTF-8.
#[no_mangle]
pub unsafe extern "C" fn youi_save_png(id: u64, path: *const u8, len: u32) -> i32 {
    guard(|| {
        let path = std::str::from_utf8(bytes(path, len)?).map_err(|e| e.to_string())?;
        with_engine(id, |e| e.save_png(path))
    })
}
/// # Safety
/// output must reference capacity writable bytes. Returns total UTF-8 length.
#[no_mangle]
pub unsafe extern "C" fn youi_last_error(output: *mut u8, capacity: u32) -> u32 {
    ERROR.with(|s| {
        let s = s.borrow();
        if !output.is_null() {
            std::ptr::copy_nonoverlapping(s.as_ptr(), output, (capacity as usize).min(s.len()));
        }
        s.len() as u32
    })
}
/// # Safety
/// output must reference capacity writable bytes; written must be writable.
#[no_mangle]
pub unsafe extern "C" fn youi_adapter_name(
    id: u64,
    output: *mut u8,
    capacity: u32,
    written: *mut u32,
) -> i32 {
    guard(|| {
        if output.is_null() || written.is_null() {
            return Err("null adapter output".into());
        }
        with_engine(id, |e| {
            let n = e.adapter_name.len().min(capacity as usize);
            std::ptr::copy_nonoverlapping(e.adapter_name.as_ptr(), output, n);
            *written = n as u32;
            Ok(())
        })
    })
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn resource_struct_layout_is_stable() {
        assert_eq!(youi_abi_version(), 0x0001_0000);
        assert_eq!(youi_resource_budget_size(), 16);
        assert_eq!(youi_resource_usage_size(), 40);
        assert_eq!(std::mem::offset_of!(ResourceBudget, max_texture_bytes), 0);
        assert_eq!(
            std::mem::offset_of!(ResourceBudget, max_instance_buffer_bytes),
            8
        );
        assert_eq!(std::mem::offset_of!(ResourceUsage, atlas_bytes), 0);
        assert_eq!(std::mem::offset_of!(ResourceUsage, output_bytes), 8);
        assert_eq!(std::mem::offset_of!(ResourceUsage, layer_bytes), 16);
        assert_eq!(
            std::mem::offset_of!(ResourceUsage, instance_buffer_bytes),
            24
        );
        assert_eq!(std::mem::offset_of!(ResourceUsage, layer_count), 32);
        assert_eq!(std::mem::offset_of!(ResourceUsage, reserved), 36);
    }
    #[test]
    fn resource_outputs_are_unchanged_for_invalid_handles() {
        let mut budget = ResourceBudget::default();
        let expected_budget = budget;
        let mut usage = ResourceUsage {
            atlas_bytes: 1,
            output_bytes: 2,
            layer_bytes: 3,
            instance_buffer_bytes: 4,
            layer_count: 5,
            reserved: 6,
        };
        let expected_usage = usage;
        unsafe {
            assert_eq!(youi_resource_budget(u64::MAX, &mut budget), -1);
            assert_eq!(youi_resource_usage(u64::MAX, &mut usage), -1);
        }
        assert_eq!(budget, expected_budget);
        assert_eq!(usage, expected_usage);
    }
    #[test]
    fn resource_pointers_are_checked_before_dereferencing() {
        let budget = ResourceBudget::default();
        let mut handle = u64::MAX;
        let mut storage = [0_u64; 8];
        let misaligned = unsafe { storage.as_mut_ptr().cast::<u8>().add(1) };
        unsafe {
            assert_eq!(
                youi_create_with_budget(10, 10, 0, std::ptr::null(), &mut handle),
                -1
            );
            assert_eq!(handle, 0);
            handle = u64::MAX;
            assert_eq!(
                youi_create_with_budget(10, 10, 0, misaligned.cast(), &mut handle),
                -1
            );
            assert_eq!(handle, 0);
            assert_eq!(
                youi_create_with_budget(10, 10, 0, &budget, std::ptr::null_mut()),
                -1
            );
            assert_eq!(
                youi_create_with_budget(10, 10, 0, &budget, misaligned.cast()),
                -1
            );
            assert_eq!(youi_create(10, 10, 0, misaligned.cast()), -1);
            assert_eq!(youi_resource_budget(0, std::ptr::null_mut()), -1);
            assert_eq!(youi_resource_budget(0, misaligned.cast()), -1);
            assert_eq!(youi_resource_usage(0, std::ptr::null_mut()), -1);
            assert_eq!(youi_resource_usage(0, misaligned.cast()), -1);
        }
        assert_eq!(storage, [0; 8]);
    }
    #[test]
    fn invalid_budget_resets_handle_without_creating_a_gpu_device() {
        let mut handle = u64::MAX;
        let budget = ResourceBudget {
            max_texture_bytes: 0,
            max_instance_buffer_bytes: 0,
        };
        unsafe {
            assert_eq!(youi_create_with_budget(10, 10, 0, &budget, &mut handle), -1);
        }
        assert_eq!(handle, 0);
    }
    #[test]
    fn xlib_rejects_null_handles_and_negative_screen_before_gpu() {
        let display = std::ptr::NonNull::<u8>::dangling().as_ptr().cast();
        for (display, window, screen) in [
            (std::ptr::null_mut(), 1, 0),
            (display, 0, 0),
            (display, 1, -1),
        ] {
            let mut handle = u64::MAX;
            unsafe {
                assert_eq!(
                    youi_create_xlib(10, 10, display, window, screen, &mut handle),
                    -1
                );
            }
            assert_eq!(handle, 0);
        }
    }
    #[test]
    fn xlib_checks_budget_and_output_pointers_before_dereferencing() {
        let display = std::ptr::NonNull::<u8>::dangling().as_ptr().cast();
        let budget = ResourceBudget::default();
        let mut handle = u64::MAX;
        let mut storage = [0_u64; 8];
        let misaligned = unsafe { storage.as_mut_ptr().cast::<u8>().add(1) };
        unsafe {
            for bad_budget in [std::ptr::null(), misaligned.cast::<ResourceBudget>()] {
                assert_eq!(
                    youi_create_xlib_with_budget(10, 10, display, 1, 0, bad_budget, &mut handle),
                    -1
                );
                assert_eq!(handle, 0);
                handle = u64::MAX;
            }
            for bad_out in [std::ptr::null_mut(), misaligned.cast::<u64>()] {
                assert_eq!(youi_create_xlib(10, 10, display, 1, 0, bad_out), -1);
                assert_eq!(
                    youi_create_xlib_with_budget(10, 10, display, 1, 0, &budget, bad_out),
                    -1
                );
            }
        }
        assert_eq!(storage, [0; 8]);
    }
    #[cfg(target_os = "linux")]
    #[test]
    fn xlib_rejects_dimensions_and_insufficient_budgets_before_gpu() {
        let display = std::ptr::NonNull::<u8>::dangling().as_ptr().cast();
        let default = ResourceBudget::default();
        for (w, h, budget, expected) in [
            (0, 10, default, "target size"),
            (10, 8193, default, "target size"),
            (
                10,
                10,
                ResourceBudget {
                    max_texture_bytes: 0,
                    ..default
                },
                "texture payload budget",
            ),
            (
                10,
                10,
                ResourceBudget {
                    max_instance_buffer_bytes: 0,
                    ..default
                },
                "instance buffer payload budget",
            ),
        ] {
            let mut handle = u64::MAX;
            unsafe {
                assert_eq!(
                    youi_create_xlib_with_budget(w, h, display, 1, 0, &budget, &mut handle),
                    -1
                );
            }
            assert_eq!(handle, 0);
            ERROR.with(|error| assert!(error.borrow().contains(expected), "{}", error.borrow()));
        }
    }
    #[cfg(not(target_os = "linux"))]
    #[test]
    fn xlib_rejects_unsupported_platform_before_gpu() {
        let display = std::ptr::NonNull::<u8>::dangling().as_ptr().cast();
        let mut handle = u64::MAX;
        unsafe {
            assert_eq!(youi_create_xlib(10, 10, display, 1, 0, &mut handle), -1);
        }
        assert_eq!(handle, 0);
        ERROR.with(|error| assert!(error.borrow().contains("only on Linux")));
    }
    #[cfg(not(target_os = "windows"))]
    #[test]
    fn hwnd_does_not_become_an_implicit_xlib_handle() {
        let mut handle = u64::MAX;
        unsafe {
            assert_eq!(youi_create(10, 10, 1, &mut handle), -1);
        }
        assert_eq!(handle, 0);
        ERROR.with(|error| assert!(error.borrow().contains("Windows HWND only")));
    }
    #[test]
    fn invalid_handle_is_error() {
        assert_eq!(youi_resize(u64::MAX, 10, 10), -1);
        assert_eq!(youi_destroy(u64::MAX), -1);
    }
    #[test]
    fn null_ffi_arguments_are_rejected() {
        unsafe {
            assert_eq!(youi_create(10, 10, 0, std::ptr::null_mut()), -1);
            assert_eq!(
                youi_render(
                    0,
                    std::ptr::null(),
                    2,
                    std::ptr::null(),
                    0,
                    std::ptr::null_mut()
                ),
                -1
            );
        }
    }
    #[test]
    fn text_cell_abi_layout_and_transactional_copy() {
        assert_eq!(youi_text_cell_size(), 32);
        assert_eq!(std::mem::align_of::<TextCell>(), 4);
        assert_eq!(std::mem::offset_of!(TextCell, start), 0);
        assert_eq!(std::mem::offset_of!(TextCell, end), 4);
        assert_eq!(std::mem::offset_of!(TextCell, line), 8);
        assert_eq!(std::mem::offset_of!(TextCell, flags), 12);
        assert_eq!(std::mem::offset_of!(TextCell, x), 16);
        assert_eq!(std::mem::offset_of!(TextCell, y), 20);
        assert_eq!(std::mem::offset_of!(TextCell, width), 24);
        assert_eq!(std::mem::offset_of!(TextCell, height), 28);
        let cells = [
            TextCell {
                end: 1,
                width: 5.,
                ..TextCell::default()
            },
            TextCell {
                start: 1,
                end: 3,
                x: 5.,
                width: 8.,
                ..TextCell::default()
            },
        ];
        let sentinel = TextCell {
            start: 999,
            ..TextCell::default()
        };
        let mut output = [sentinel; 3];
        let mut count = 77;
        unsafe {
            assert_eq!(
                guard(|| copy_text_cells(&cells, std::ptr::null_mut(), 0, &mut count)),
                0
            );
            assert_eq!(count, 2);
            assert_eq!(
                guard(|| copy_text_cells(&cells, output.as_mut_ptr(), 1, &mut count)),
                -1
            );
            assert_eq!(count, 2);
            assert_eq!(output, [sentinel; 3]);
            assert_eq!(
                guard(|| copy_text_cells(&cells, output.as_mut_ptr(), 3, &mut count)),
                0
            );
            assert_eq!(count, 2);
            assert_eq!(&output[..2], &cells);
            assert_eq!(output[2], sentinel);
        }
    }

    #[test]
    fn text_cell_abi_rejects_malformed_input_without_mutating_outputs() {
        let sentinel = TextCell {
            start: 999,
            ..TextCell::default()
        };
        let mut cell = sentinel;
        let mut count = 77;
        let mut storage = [0_u64; 8];
        let misaligned = unsafe { storage.as_mut_ptr().cast::<u8>().add(1) };
        unsafe {
            for (text, len, flags) in [
                (std::ptr::null(), 1, 0),
                (b"x".as_ptr(), 16 * 1024 * 1024 + 1, 0),
                (b"\xff".as_ptr(), 1, 0),
                (b"x".as_ptr(), 1, 4),
                (b"x".as_ptr(), 1, 0),
            ] {
                assert_eq!(
                    youi_text_cells(
                        u64::MAX,
                        text,
                        len,
                        16.,
                        200.,
                        flags,
                        &mut cell,
                        1,
                        &mut count
                    ),
                    -1
                );
                assert_eq!(cell, sentinel);
                assert_eq!(count, 77);
            }
            assert_eq!(
                youi_text_cells(
                    0,
                    std::ptr::null(),
                    0,
                    16.,
                    200.,
                    0,
                    std::ptr::null_mut(),
                    0,
                    std::ptr::null_mut()
                ),
                -1
            );
            assert_eq!(
                youi_text_cells(
                    0,
                    std::ptr::null(),
                    0,
                    16.,
                    200.,
                    0,
                    std::ptr::null_mut(),
                    1,
                    &mut count
                ),
                -1
            );
            assert_eq!(
                youi_text_cells(
                    0,
                    std::ptr::null(),
                    0,
                    16.,
                    200.,
                    0,
                    misaligned.cast(),
                    1,
                    &mut count
                ),
                -1
            );
            assert_eq!(
                youi_text_cells(
                    0,
                    std::ptr::null(),
                    0,
                    16.,
                    200.,
                    0,
                    &mut cell,
                    1,
                    misaligned.cast()
                ),
                -1
            );
        }
        assert_eq!(cell, sentinel);
        assert_eq!(count, 77);
        assert_eq!(storage, [0; 8]);
    }
}
