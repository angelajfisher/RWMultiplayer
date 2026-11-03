#include <dlfcn.h>
#include <stdint.h>
#include <stdio.h>

int main(int argc, char **argv) {
    if (argc != 2) return 2;
    void *lib = dlopen(argv[1], RTLD_NOW);
    if (!lib) { fprintf(stderr, "%s\n", dlerror()); return 2; }
    uint32_t (*capture)(uint64_t *, uint32_t, uint32_t) = dlsym(lib, "mp_capture_stack");
    if (!capture) return 2;
    uint64_t frames[17] = {0};
    frames[16] = UINT64_MAX;
    uint32_t count = capture(frames, 16, 0);
    if (count < 2 || count > 16 || !frames[0] || frames[16] != UINT64_MAX) {
        fprintf(stderr, "Invalid native capture: %u frames\n", count);
        return 1;
    }
    printf("PASS: loaded release dylib and captured %u native frames\n", count);
    return 0;
}
