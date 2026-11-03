// Run the managed smoke test with the exact Mono runtime bundled in RimWorld.
#include <dlfcn.h>
#include <stdio.h>
#include <stdlib.h>

int main(int argc, char **argv) {
    if (argc != 5) {
        fprintf(stderr, "usage: mono-host runtime.dylib Managed-dir test.exe tracer.dylib\n");
        return 2;
    }
    void *mono = dlopen(argv[1], RTLD_NOW | RTLD_GLOBAL);
    if (!mono) { fprintf(stderr, "%s\n", dlerror()); return 2; }
    void (*set_path)(const char *) = dlsym(mono, "mono_set_assemblies_path");
    void *(*init)(const char *, const char *) = dlsym(mono, "mono_jit_init_version");
    void *(*open_assembly)(void *, const char *) = dlsym(mono, "mono_domain_assembly_open");
    int (*exec)(void *, void *, int, char **) = dlsym(mono, "mono_jit_exec");
    void (*dllmap)(void *, const char *, const char *, const char *, const char *) = dlsym(mono, "mono_dllmap_insert");
    if (!set_path || !init || !open_assembly || !exec) return 2;
    set_path(argv[2]);
    void *domain = init("Multiplayer tracing smoke test", "v4.0.30319");
    if (!domain) return 2;
    if (dllmap) dllmap(NULL, "System.Native", NULL, "libmono-native.dylib", NULL);
    void *assembly = open_assembly(domain, argv[3]);
    if (!assembly) return 2;
    return exec(domain, assembly, 2, argv + 3);
}
