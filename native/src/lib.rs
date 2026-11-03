//! Adapted from rwmt/Multiplayer acb33a2's native backtrace implementation.
use core::ffi::c_void;

/// Capture instruction addresses without consulting Mono's private thread layout.
///
/// # Safety
/// `frames` must point to `capacity` writable 64-bit slots. Calls must be serialized
/// (the managed caller captures on the simulation thread).
#[unsafe(no_mangle)]
pub unsafe extern "C" fn mp_capture_stack(frames: *mut u64, capacity: u32, skip: u32) -> u32 {
    if frames.is_null() || capacity == 0 {
        return 0;
    }
    let frames = unsafe { core::slice::from_raw_parts_mut(frames, capacity as usize) };
    let mut skipped = 0;
    let mut written = 0;
    unsafe {
        backtrace::trace_unsynchronized(|frame| {
            if skipped < skip {
                skipped += 1;
                return true;
            }
            let ip: *mut c_void = frame.ip();
            frames[written] = ip as u64;
            written += 1;
            written < frames.len()
        });
    }
    written as u32
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn capture_respects_capacity_and_null_buffers() {
        unsafe {
            assert_eq!(mp_capture_stack(core::ptr::null_mut(), 8, 0), 0);
            let mut frames = [0_u64, u64::MAX];
            assert_eq!(mp_capture_stack(frames.as_mut_ptr(), 0, 0), 0);
            assert_eq!(mp_capture_stack(frames.as_mut_ptr(), 1, 0), 1);
            assert_ne!(frames[0], 0);
            assert_eq!(frames[1], u64::MAX);
            assert_eq!(mp_capture_stack(frames.as_mut_ptr(), 1, u32::MAX), 0);
        }
    }
}
