#ifndef YOUI_H
#define YOUI_H
#include <stdint.h>
#if defined(_WIN32)
#define YOUI_CALL __cdecl
#else
#define YOUI_CALL
#endif
#ifdef __cplusplus
extern "C" {
#endif
/* v1, 80-byte commands, 48-byte stats; natural 4/8-byte alignment.
   All functions return 0 on success except version/size/last_error.
   Callers own readable/writable buffers for the duration of each call.
   count is a command count; text/path/buffer lengths and capacities are bytes.
   Text and paths are UTF-8. HWND outlives renderer.
   Image uploads: straight-alpha RGBA8 sRGB. Release invalidates the ID.
   Coordinates: physical pixels, top-left origin, +Y down. */
typedef struct { float x, y, w, h; } youi_rect;
typedef struct {
    uint32_t kind, flags, text_offset, text_length;
    youi_rect rect;
    float color[4], color2[4], radius, font_size, reserved[2];
} youi_command;
typedef struct {
    uint32_t commands, instances, draw_calls, layers;
    uint64_t upload_bytes, prepare_us, submit_us;
    uint32_t atlas_glyphs, text_cache_hits;
} youi_frame_stats;
/* ABI 0x00010000. Status functions return 0, -1 (error), or -2 (contained panic).
   Read youi_last_error on the failing thread. Panic recovery is not guaranteed.
   See docs/api/native.md for ownership, frame budgets and full contracts. */
uint32_t YOUI_CALL youi_abi_version(void);
uint32_t YOUI_CALL youi_command_size(void);
/* Dimensions: 1..8192 physical pixels. hwnd=0 selects offscreen GPU rendering.
   result must be writable; it is set to zero before creation. */
int32_t YOUI_CALL youi_create(uint32_t width, uint32_t height, intptr_t hwnd, uint64_t* result);
/* Destroy before the borrowed HWND; a second destroy is an error. */
int32_t YOUI_CALL youi_destroy(uint64_t renderer);
int32_t YOUI_CALL youi_resize(uint64_t renderer, uint32_t width, uint32_t height);
/* Synchronous input borrowing, asynchronous GPU work. stats must be writable.
   NULL input is allowed only for zero count/length. Clip/layer scopes must balance. */
int32_t YOUI_CALL youi_render(uint64_t renderer, const youi_command* commands, uint32_t count, const uint8_t* text, uint32_t length, youi_frame_stats* stats);
/* size: 1..512, max_width: finite and >=0; outputs are physical pixels.
   Any nonzero bold selects bold text. Measurement may populate the GPU atlas. */
int32_t YOUI_CALL youi_measure(uint64_t renderer, const uint8_t* text, uint32_t length, float size, float max_width, uint32_t bold, float* width, float* height);
/* Tightly packed RGBA8; length == width*height*4; each side 1..2046.
   Returned image belongs only to this renderer and must remain live through render. */
int32_t YOUI_CALL youi_upload_image(uint64_t renderer, uint32_t width, uint32_t height, const uint8_t* rgba, uint32_t length, uint32_t* image);
int32_t YOUI_CALL youi_release_image(uint64_t renderer, uint32_t image);
/* Blocking readback; overwrites path, requires an existing parent directory.
   Output preserves render-target premultiplication; no unpremultiplication. */
int32_t YOUI_CALL youi_save_png(uint64_t renderer, const uint8_t* path, uint32_t length);
/* Returns total UTF-8 byte length; NULL/0 queries it. Copies up to capacity,
   without a NUL terminator. Successful calls do not clear the previous error. */
uint32_t YOUI_CALL youi_last_error(uint8_t* output, uint32_t capacity);
/* Both outputs must be non-NULL. written is bytes COPIED, not required capacity.
   No NUL terminator; insufficient capacity can truncate UTF-8. */
int32_t YOUI_CALL youi_adapter_name(uint64_t renderer, uint8_t* output, uint32_t capacity, uint32_t* written);
#ifdef __cplusplus
}
static_assert(sizeof(youi_command) == 80, "YoUI command ABI mismatch");
static_assert(sizeof(youi_frame_stats) == 48, "YoUI stats ABI mismatch");
#endif
#endif
