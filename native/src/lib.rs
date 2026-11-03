//! Bridge to Unity Mono's public managed stack walker. System unwinding cannot
//! reliably cross Mono's JIT frames, even in an x86-64 process under Rosetta.
use core::ffi::c_void;

type FrameCallback = unsafe extern "C" fn(*mut c_void, i32, i32, i32, *mut c_void) -> i32;
type StackWalk = unsafe extern "C" fn(FrameCallback, *mut c_void);

struct Capture {
    frames: *mut u64,
    capacity: usize,
    written: usize,
    skip: u32,
}

unsafe extern "C" fn visit(
    method: *mut c_void, _native_offset: i32, _il_offset: i32, _managed: i32, data: *mut c_void,
) -> i32 {
    let capture = unsafe { &mut *data.cast::<Capture>() };
    if method.is_null() { return 0; }
    if capture.skip != 0 { capture.skip -= 1; return 0; }
    // Negative values tag MonoMethod handles, distinct from native code addresses.
    unsafe { *capture.frames.add(capture.written) = !(method as u64); }
    capture.written += 1;
    i32::from(capture.written == capture.capacity) // Mono: true stops walking.
}

/// Capture managed method identities without private Mono layouts or JIT decoding.
///
/// # Safety
/// `frames` must point to `capacity` writable slots. `walk` must be the current
/// runtime's mono_stack_walk_no_il and the caller must be attached to that runtime.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn mp_capture_mono_stack_v1(
    walk: Option<StackWalk>, frames: *mut u64, capacity: u32, skip: u32,
) -> u32 {
    let Some(walk) = walk else { return 0; };
    if frames.is_null() || capacity == 0 {
        return 0;
    }
    let mut capture = Capture { frames, capacity: capacity as usize, written: 0, skip };
    unsafe { walk(visit, (&mut capture as *mut Capture).cast()); }
    capture.written as u32
}

#[cfg(test)]
mod tests {
    use super::*;

    unsafe extern "C" fn fake_walk(callback: FrameCallback, data: *mut c_void) {
        for method in [0_usize, 0x1000, 0x2000, 0x3000] {
            if unsafe { callback(method as *mut c_void, 42, -1, 1, data) } != 0 { break; }
        }
    }

    #[test]
    fn capture_respects_capacity_and_null_buffers() {
        unsafe {
            assert_eq!(mp_capture_mono_stack_v1(Some(fake_walk), core::ptr::null_mut(), 8, 0), 0);
            let mut frames = [0_u64, u64::MAX];
            assert_eq!(mp_capture_mono_stack_v1(None, frames.as_mut_ptr(), 1, 0), 0);
            assert_eq!(mp_capture_mono_stack_v1(Some(fake_walk), frames.as_mut_ptr(), 0, 0), 0);
            assert_eq!(mp_capture_mono_stack_v1(Some(fake_walk), frames.as_mut_ptr(), 1, 0), 1);
            assert_eq!(frames[0], !0x1000_u64);
            assert_eq!(frames[1], u64::MAX);
            assert_eq!(mp_capture_mono_stack_v1(Some(fake_walk), frames.as_mut_ptr(), 1, u32::MAX), 0);
        }
    }

    #[test]
    fn skip_counts_methods_and_preserves_order() {
        let mut frames = [0_u64; 4];
        let count = unsafe { mp_capture_mono_stack_v1(Some(fake_walk), frames.as_mut_ptr(), 4, 1) };
        assert_eq!(count, 2);
        assert_eq!(frames, [!0x2000_u64, !0x3000_u64, 0, 0]);
        unsafe {
            assert_eq!(mp_capture_mono_stack_v1(Some(fake_walk), frames.as_mut_ptr(), 4, 0), 3);
        }
    }
}
