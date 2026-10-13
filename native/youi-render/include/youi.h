#ifndef YOUI_H
#define YOUI_H
#include <stddef.h>
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
   Coordinates: physical pixels, top-left origin, +Y down.
   Text command flags: bit 0 bold, bit 1 no-wrap, bit 2 skip command rect clip
   while honoring the clip stack; all other text command bits are reserved. */
typedef struct { float x, y, w, h; } youi_rect;
/* UTF-8 byte boundaries, visual-line index and physical-pixel advance bounds.
   flags bit 0: RTL (logical start at right edge); all other bits are zero.
   Ligatures divide their advance equally at extended-grapheme boundaries.
   Hard line ending bytes have no cell; empty lines have start=end, width=0.
   Wrap-suppressed whitespace has width=0 at the adjacent shaped caret.
   Ordered by visual line then x. Geometry never allocates glyph atlas space. */
typedef struct {
    uint32_t start, end, line, flags;
    float x, y, width, height;
} youi_text_cell;
typedef struct {
    uint32_t kind, flags, text_offset, text_length;
    youi_rect rect;
    float color[4], color2[4], radius, font_size, reserved[2];
} youi_command;
/* kind 8: textured triangle (requires a native library supporting A06).
   The 80-byte layout and ABI 0x00010000 are unchanged; version/size checks
   alone do not prove kind 8 support in an older native library.
   Positions: rect.x/y = p0, rect.w/h = p1, reserved[0/1] = p2 (as kind 6).
   flags = live image ID; color = uniform straight-alpha sRGB tint.
   color2 = {u0,v0,u1,v1}; radius = u2; font_size = v2.
   UVs must be finite in [0,1], image-local with top-left origin.
   Sampling is linear, affine, clamped to the source image texel centers
   at its boundary. RGB tint multiplies in linear space; alpha is applied
   once. No texture repeat, perspective correction or per-vertex colors.
   Image ownership/lifetime requirements are identical to kind 7. */
typedef struct {
    uint32_t commands, instances, draw_calls, layers;
    uint64_t upload_bytes, prepare_us, submit_us;
    uint32_t atlas_glyphs, text_cache_hits;
} youi_frame_stats;
/* Retained GPU allocation ceilings. Defaults used by youi_create are 528 MiB
   for textures and 256 MiB for the instance buffer. Device limits also apply. */
typedef struct {
    uint64_t max_texture_bytes, max_instance_buffer_bytes;
} youi_resource_budget_t;
/* Logical sizes of retained GPU allocations, not driver or process memory.
   Excludes transient replacement overlap and resources still in flight.
   All retained layer targets count, including ones unused by the latest frame.
   reserved is zero. */
typedef struct {
    uint64_t atlas_bytes, output_bytes, layer_bytes, instance_buffer_bytes;
    uint32_t layer_count, reserved;
} youi_resource_usage_t;
/* Stable selected-backend and adapter-reported device-kind identifiers. */
#define YOUI_BACKEND_OTHER 0u
#define YOUI_BACKEND_VULKAN 1u
#define YOUI_BACKEND_METAL 2u
#define YOUI_BACKEND_DX12 3u
#define YOUI_BACKEND_GL 4u
#define YOUI_BACKEND_BROWSER_WEB_GPU 5u
#define YOUI_DEVICE_OTHER 0u
#define YOUI_DEVICE_INTEGRATED_GPU 1u
#define YOUI_DEVICE_DISCRETE_GPU 2u
#define YOUI_DEVICE_VIRTUAL_GPU 3u
#define YOUI_DEVICE_CPU 4u
/* Effective immutable limits, not available capacity or an allocation promise.
   Target ceiling includes renderer policy and enabled device limit. Image ceiling
   excludes atlas padding; atlas space is shared and may be fragmented. Instance
   byte ceiling includes enabled device limit and configured resource budget.
   reserved is zero. All other resource/frame validation continues to apply. */
typedef struct {
    uint32_t backend, device_kind;
    uint32_t max_target_dimension_2d, max_image_dimension_2d, atlas_dimension_2d, reserved;
    uint64_t max_instance_buffer_bytes;
} youi_renderer_capabilities_t;
/* Additive ABI v1 symbols: check availability and 32-byte layout first.
   Queries a successfully created renderer, not pre-creation backend support.
   result must be non-NULL, writable, aligned; unchanged on error. No GPU work.
   Still available after a latched device failure while the handle remains live;
   values do not prove device health or native-window/platform support. */
uint32_t YOUI_CALL youi_renderer_capabilities_size(void);
int32_t YOUI_CALL youi_renderer_capabilities(uint64_t renderer, youi_renderer_capabilities_t* result);
/* ABI 0x00010000. Status functions return 0, -1 (error), or -2 (contained panic).
   Read youi_last_error on the failing thread. Panic recovery is not guaranteed.
   See docs/api/native.md for ownership, frame budgets and full contracts. */
uint32_t YOUI_CALL youi_abi_version(void);
uint32_t YOUI_CALL youi_command_size(void);
/* Additive v1 geometry symbol: check availability and 32-byte struct size. */
uint32_t YOUI_CALL youi_text_cell_size(void);
/* Additive v1 symbols: older v1 libraries may not export these functions.
   Check symbol availability and both sizes before using the resource API. */
uint32_t YOUI_CALL youi_resource_budget_size(void);
uint32_t YOUI_CALL youi_resource_usage_size(void);
/* Dimensions: 1..8192 physical pixels. hwnd=0 selects offscreen GPU rendering.
   result must be writable; it is set to zero before creation. */
int32_t YOUI_CALL youi_create(uint32_t width, uint32_t height, intptr_t hwnd, uint64_t* result);
/* budget must be non-NULL, readable and naturally aligned. result must be
   non-NULL, writable and naturally aligned; a valid result is zeroed on error.
   The budget must cover the initial atlas, output and instance-buffer capacity.
   The budget is copied; the caller may release it immediately after the call. */
int32_t YOUI_CALL youi_create_with_budget(uint32_t width, uint32_t height, intptr_t hwnd, const youi_resource_budget_t* budget, uint64_t* result);
/* Additive v1 Linux Xlib symbols; older v1 libraries may not export them.
   display is a live Xlib Display*, window is its nonzero X11 window XID, and
   screen is the window's valid, nonnegative screen index. These are borrowed:
   destroy the renderer before destroying the window or closing the display.
   Follow the host's Xlib thread rules; avoid concurrent display use unless
   Xlib threading was initialized. No platform window is created or destroyed.
   Other operating systems reject this API before GPU initialization. Null
   display, zero window, negative screen and invalid dimensions are errors;
   pointer validity and window/screen membership remain caller obligations.
   A non-NULL, writable, aligned result is zeroed before creation. The budgeted
   variant copies budget using the same contract as youi_create_with_budget;
   swapchain allocations are excluded from resource accounting. */
int32_t YOUI_CALL youi_create_xlib(uint32_t width, uint32_t height, void* display, uintptr_t window, int32_t screen, uint64_t* result);
int32_t YOUI_CALL youi_create_xlib_with_budget(uint32_t width, uint32_t height, void* display, uintptr_t window, int32_t screen, const youi_resource_budget_t* budget, uint64_t* result);
/* Outputs must be non-NULL, writable and naturally aligned. Invalid pointers
   or handles return an error; outputs remain unchanged on error. */
int32_t YOUI_CALL youi_resource_usage(uint64_t renderer, youi_resource_usage_t* result);
int32_t YOUI_CALL youi_resource_budget(uint64_t renderer, youi_resource_budget_t* result);
/* Destroy before the borrowed window/display; a second destroy is an error. */
int32_t YOUI_CALL youi_destroy(uint64_t renderer);
int32_t YOUI_CALL youi_resize(uint64_t renderer, uint32_t width, uint32_t height);
/* Synchronous input borrowing, asynchronous GPU work. stats must be writable.
   NULL input is allowed only for zero count/length. Clip/layer scopes must balance. */
int32_t YOUI_CALL youi_render(uint64_t renderer, const youi_command* commands, uint32_t count, const uint8_t* text, uint32_t length, youi_frame_stats* stats);
/* Additive v1 symbols; frame stats retain their 48-byte ABI. Pending is 0/1.
   Retry presents the retained output only; wait between retries (e.g. 16 ms).
   Resize discards pending output and requires a fresh render. */
int32_t YOUI_CALL youi_presentation_pending(uint64_t renderer, uint32_t* pending);
int32_t YOUI_CALL youi_retry_presentation(uint64_t renderer, uint32_t* pending);
/* size: 1..512, max_width: finite and >=0; outputs are physical pixels.
   Any nonzero bold selects bold text. Measurement does not populate the GPU atlas. */
int32_t YOUI_CALL youi_measure(uint64_t renderer, const uint8_t* text, uint32_t length, float size, float max_width, uint32_t bold, float* width, float* height);
/* size/max_width have the same meaning as youi_measure. flags bit 0 selects
   bold, bit 1 disables wrapping; other bits are invalid. text length is
   bytes (<=16 MiB), while capacity and count are numbers of youi_text_cell.
   NULL cells with capacity=0 queries the required count and succeeds.
   Nonzero capacity requires an aligned, writable cells buffer. Insufficient
   capacity returns -1 and the required count, without writing partial cells.
   count is required, writable and aligned. All other errors leave outputs
   unchanged. Outputs must not overlap each other or input. No memory retained. */
int32_t YOUI_CALL youi_text_cells(uint64_t renderer, const uint8_t* text, uint32_t length, float size, float max_width, uint32_t flags, youi_text_cell* cells, uint32_t capacity, uint32_t* count);
/* Tightly packed RGBA8; length == width*height*4; each side 1..2046.
   Returned image belongs only to this renderer and must remain live through render. */
int32_t YOUI_CALL youi_upload_image(uint64_t renderer, uint32_t width, uint32_t height, const uint8_t* rgba, uint32_t length, uint32_t* image);
int32_t YOUI_CALL youi_release_image(uint64_t renderer, uint32_t image);
/* Additive ABI v1 symbol; older v1 libraries may not export it.
   Call between render calls on the renderer's owning thread. Reclaims only
   glyph slots; preserves live images, layout/raster caches, last frame/stats
   and GPU payload allocation. Idempotent, no GPU wait. Submitted frames remain
   ordered before later uploads. The next render re-resolves all glyph UVs.
   Fragmentation/current-frame capacity may still exhaust the fixed atlas;
   retry a failed atlas-exhaustion frame at most once, never arbitrary errors. */
int32_t YOUI_CALL youi_purge_glyph_residency(uint64_t renderer);
/* Additive ABI v1 symbol; older v1 libraries may not export it.
   Call between render calls on the renderer's owning thread. Drops all retained
   scratch layer targets, views and bind groups, preserving output, last stats,
   atlas/image/glyph residency and instance-buffer capacity. Idempotent, no GPU
   wait; submitted work retains its resources until safe. ResourceUsage reports
   retained payload, not immediate driver VRAM release. Subsequent admitted
   layered frames recreate needed targets. Prefer idle/modal-close boundaries,
   not every frame. Returns 0 on success or -1 with youi_last_error. */
int32_t YOUI_CALL youi_trim_offscreen_layers(uint64_t renderer);
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
static_assert(sizeof(youi_renderer_capabilities_t) == 32, "YoUI capabilities ABI mismatch");
static_assert(offsetof(youi_renderer_capabilities_t, max_instance_buffer_bytes) == 24, "YoUI capabilities offset mismatch");
static_assert(sizeof(youi_text_cell) == 32, "YoUI text cell ABI mismatch");
static_assert(offsetof(youi_text_cell, x) == 16, "YoUI text cell offset mismatch");
static_assert(sizeof(youi_command) == 80, "YoUI command ABI mismatch");
static_assert(sizeof(youi_frame_stats) == 48, "YoUI stats ABI mismatch");
static_assert(sizeof(youi_resource_budget_t) == 16, "YoUI resource budget ABI mismatch");
static_assert(offsetof(youi_resource_budget_t, max_instance_buffer_bytes) == 8, "YoUI resource budget offset mismatch");
static_assert(sizeof(youi_resource_usage_t) == 40, "YoUI resource usage ABI mismatch");
static_assert(offsetof(youi_resource_usage_t, instance_buffer_bytes) == 24, "YoUI resource usage offset mismatch");
static_assert(offsetof(youi_resource_usage_t, layer_count) == 32, "YoUI resource usage count offset mismatch");
static_assert(offsetof(youi_resource_usage_t, reserved) == 36, "YoUI resource usage reserved offset mismatch");
#endif
#endif
