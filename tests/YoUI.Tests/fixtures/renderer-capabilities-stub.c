/* Isolated managed ABI guard fixture. This library never creates a GPU device.
 * Compile three variants in separate copies of the managed test output:
 *   cc -shared -fPIC renderer-capabilities-stub.c -o libyoui_render.so
 *     dotnet YoUI.Tests.dll --missing-renderer-capabilities-api
 *   cc -shared -fPIC -DYOUI_CAPABILITIES_SIZE=32 renderer-capabilities-stub.c -o libyoui_render.so
 *     dotnet YoUI.Tests.dll --missing-renderer-capabilities-api
 *   cc -shared -fPIC -DYOUI_CAPABILITIES_SIZE=31 -DYOUI_CAPABILITIES_ABORT_QUERY renderer-capabilities-stub.c -o libyoui_render.so
 *     dotnet YoUI.Tests.dll --incompatible-renderer-capabilities-api
 * Windows: compile as youi_render.dll with a C compiler; exports are Cdecl.
 * macOS: use -dynamiclib and libyoui_render.dylib instead.
 * Never overwrite the repository's real native output or use this with --gpu-*.
 */
#include <stdint.h>
#include <stdlib.h>

#if defined(_WIN32)
#define YOUI_TEST_EXPORT __declspec(dllexport)
#define YOUI_TEST_CALL __cdecl
#else
#define YOUI_TEST_EXPORT __attribute__((visibility("default")))
#define YOUI_TEST_CALL
#endif

YOUI_TEST_EXPORT uint32_t YOUI_TEST_CALL youi_abi_version(void) {
    return 0x00010000;
}

#if defined(YOUI_CAPABILITIES_SIZE)
YOUI_TEST_EXPORT uint32_t YOUI_TEST_CALL youi_renderer_capabilities_size(void) {
    return YOUI_CAPABILITIES_SIZE;
}
#endif

#if defined(YOUI_CAPABILITIES_ABORT_QUERY)
YOUI_TEST_EXPORT int32_t YOUI_TEST_CALL youi_renderer_capabilities(uint64_t renderer, void* output) {
    (void)renderer;
    (void)output;
    abort(); /* Layout verification must reject before entering this function. */
}
#endif
