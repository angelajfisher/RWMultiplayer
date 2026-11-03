#include <dlfcn.h>
#include <stdint.h>
#include <stdio.h>

typedef int (*frame_callback)(void *, int, int, int, void *);
static void fake_walk(frame_callback callback, void *data) {
    for (uintptr_t method = 0x1000; method <= 0x5000; method += 0x1000)
        if (callback((void *)method, 42, -1, 1, data)) break;
}

int main(int argc, char **argv) {
    if (argc != 2) return 2;
    void *lib = dlopen(argv[1], RTLD_NOW);
    if (!lib) { fprintf(stderr, "%s\n", dlerror()); return 2; }
    uint32_t (*capture)(void (*)(frame_callback, void *), uint64_t *, uint32_t, uint32_t) =
        dlsym(lib, "mp_capture_mono_stack_v1");
    if (!capture) return 2;
    uint64_t frames[17] = {0};
    frames[16] = UINT64_MAX;
    uint32_t count = capture(fake_walk, frames, 16, 0);
    if (count != 5 || frames[0] != ~UINT64_C(0x1000) || frames[16] != UINT64_MAX) {
        fprintf(stderr, "Invalid native capture: %u frames\n", count);
        return 1;
    }
    printf("PASS: loaded release bridge and captured %u method handles\n", count);
    return 0;
}
