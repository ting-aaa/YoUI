# Text layout and atlas residency

Text layout caches retain cosmic-text raster keys and positioned ink rectangles,
not atlas coordinates. The keys include font face ID, glyph ID, size, subpixel
bins and raster flags, including fallback-font identity. FontSystem remains
private and immutable in membership for the cache lifetime; replacing its font
configuration must replace the layout cache. Layout lookup still uses text,
size bits, available-width bits, bold and wrapping mode, with the existing 512-entry clear bound.
Residency separation itself does not change shaping, line measurement, physical
glyph placement or painter order. A08 additionally corrects split-grapheme wrapping
and preserves trailing empty lines in the shared geometry; see [text input](text-input.md).
Empty/no-raster glyphs create no draw quads; missing characters still use the
shaper's normal fallback or .notdef behavior.

Measurement shapes/rasterizes for ink geometry but no longer uploads or reserves
atlas space. Visible glyphs resolve their current residency during synchronous
draw preparation. A layout cache hit therefore does not imply an atlas hit.
FrameStats.AtlasGlyphs now counts glyphs resident from drawing rather than also
measurement; fully clipped glyphs need not become resident. The existing Swash
raster cache behavior is unchanged (this is not a new memory-budget policy).
A08 adds text-cell geometry APIs and text command flags without changing the
command/stat structure layouts; see [text input](text-input.md). Measurement no longer fails merely because the atlas is exhausted;
drawing can still fail on exhaustion.

The atlas remains one fixed 2048-square texture shared by images and glyphs.
Glyph allocations persist until an explicit between-frame residency purge.
Released image and purged glyph regions are available to smaller/equal-sized images and glyphs.
The allocator chooses a fitting region with the smallest padded area, then splits
it into disjoint right/bottom rectangles. Every resulting slot retains its own
one-texel gutter on all sides. Image uploads initialize the complete gutter to
transparent black so smaller images cannot filter stale pixels from a former
occupant; image UVs and sampling semantics are unchanged. Fragments too small for a padded 1×1 allocation
are discarded. If no free rectangle fits, allocation falls back to the existing
shelf. Failed dimension/capacity checks leave the shelf and free regions unchanged.

## Explicit between-frame glyph purge

Rust `Renderer::purge_glyph_residency()`, C `youi_purge_glyph_residency(renderer)`
and C# `NativeRenderer.PurgeGlyphResidency()` drain resident glyph rectangles into
that same free-region allocator. Repeating the purge without drawing is a no-op;
rectangles are released exactly once. Live image locations and handles, cached
layouts and CPU rasters are preserved. New uploads initialize every padded texel,
so neither old glyph ink nor image pixels contaminate reused gutters.

Call only between synchronous render calls, on the renderer's owning thread.
Rust requires exclusive access; the C registry serializes calls but is not a
license to violate host/window thread rules or reenter a call. Managed access
checks the creating thread and disposal. All prepare passes/command buffers are
local to `render`; none escape for later submission. Purge clears retained CPU
instances, including those left by failed preparation. Cached layouts contain
raster keys rather than UVs, so the next render resolves residency again.
Previously submitted draws are ordered before subsequent recycled-slot writes
on the renderer's private queue. Purge does not wait for the GPU, submit a frame,
or change the last successful output or FrameStats. `AtlasGlyphs` remains the
last successful frame's residency snapshot until another successful render.
Future externally recorded/deferred command buffers need a different contract.

Recovery is explicit: when a render fails specifically with `atlas budget
exhausted`, call purge once and retry the same commands/text once. Do not retry
arbitrary render errors or loop on failure. A failed frame may have uploaded
some glyphs before running out of atlas or instance capacity; purge also releases
those glyph slots, without changing the previous output. Images referenced by
the retried frame must still be live. Applications may instead purge at a known
text-workload transition before rendering.

This is bounded reclamation, not automatic eviction, a full atlas reset or
complete capacity recovery. Regions are not coalesced, rotated or compacted;
fragmentation can reject an allocation despite sufficient aggregate free area.
The current frame's working set may exceed capacity even after purge. Text churn
can still exhaust space until the caller explicitly purges. There is no LRU,
multipage atlas or CPU cache-budget policy. The fixed 16 MiB atlas texture and
GPU payload budgets do not shrink; free-region metadata is CPU state outside
those budgets. Live image UVs never move; released image handles remain invalid.

The C symbol is additive within ABI v1: version/structure-size checks alone do
not prove an older native library supports it. Deploy matching native binaries.
The managed method reports a missing-symbol error instead of silently pretending
reclamation succeeded. Invalid/disposed C handles return an error without mutation.

## Raster filter footprint

Glyph geometry includes the one-texel transparent gutter already reserved by
the atlas allocator. Its UVs address the matching padded bitmap. This is the
support needed by linear sampling at fractional origins: a raw-bitmap-sized
quad can cut off the filtered top, bottom or side of a glyph even when no
text/control clip touches it. Raster ink rectangles, advances, line metrics,
text cells and fractional positioning are unchanged; explicit command and
ancestor clips still apply, including when only the filter halo reaches a clip.

Mask/subpixel-mask gutters have white RGB and zero coverage, so sampling does
not multiply coverage a second time through black RGB. Color glyph gutters
retain transparent black and the existing color/alpha convention. Ordinary
image UVs and allocation capacity are unchanged. This does not standardize
system-font discovery, bundle fonts or establish cross-platform pixel parity.

## Regression coverage

CPU tests in `native/youi-render/src/text.rs` cover raster-key separation, cached
geometry, cache input identity/bound, empty/missing text, allocation exhaustion,
image-slot reuse, and glyph mapping loss followed by resolution to different UVs.
The mapping-loss test preserves occupied slots and images: it proves re-resolution
and prepared-UV stability, **not** reclaimed capacity or a texture/device reset.
It uses synthetic uploads and is not pixel evidence.

An ignored GPU unit test in `renderer.rs` exercises actual upload,
rendering and pixel readback across mapping loss, alongside a live image:

```sh
cargo test -p youi-render --locked --lib renderer::text_residency_tests::cached_text_pixels_survive_residency_mapping_loss_with_live_image -- --exact --ignored --nocapture
```

It requires a GPU adapter and a usable Latin font and fails explicitly without
them. It verifies changed glyph UVs, unchanged measurement/image UVs, visible text
and identical output pixels. Both `scripts/verify.ps1 -Layer Gpu` and `-Layer All`
run this exact test alongside the GPU integration suite, with the selected
Debug/Release profile and repository target directory. Any failure stops the gate.
CPU/Logic gate success must not be represented as GPU or native-window acceptance.

`renderer::text_filter_tests::` also runs in the GPU gate. Its independent CPU
oracle bilinearly samples actual raw glyph masks, then applies the text/ancestor
clip contracts and compares GPU alpha and white-mask RGB. It covers 26px bold
headings, 14px body text, 17.5px accented/descending glyphs, eight fractional
translation phases, explicit clipping and a clip that intersects only a filter
halo. The tests require a GPU adapter and usable Latin fonts; DejaVu Sans is
selected where available, but the oracle uses the actual fallback raster.
`YOUI_ARTIFACT_DIR` additionally enables eight diagnostic screenshot phases.
CPU tests cover mask/color gutters and atlas edges, row wrapping and image reuse.

Glyph sampling is clamped to the padded slot's gutter texel centers. Geometry
still includes the full filter halo, while image quads retain their existing
sampling convention. The adversarial recycled-white-region test covers eight
fractional phases and requires zero raw-mask coverage to stay exactly transparent.

The white-mask RGB oracle accounts for independent RGBA8 alpha and sRGB8 RGB
encoding: a stored alpha byte represents a half-byte interval of linear values,
which is encoded to sRGB with one final conversion byte of allowance. Only where
the independent raw oracle finds multiple contributing glyphs may the original
float RGB tolerance of three bytes also apply, since each glyph blend rounds the
two channels independently. The independent raw-mask alpha limit of two bytes
remains in force; CPU counterexamples reject coverage being multiplied twice.
The tiny-mask GPU fixture and f32 sampler probe distinguish hardware subtexel
coordinate quantization from render-target encoding. Precision requirements are
documented in the [D3D11.3 specification](https://microsoft.github.io/DirectX-Specs/d3d/archive/D3D11_3_FunctionalSpec.htm),
sections 3.2.3.6/8 and 7.18.16.1/2. This is not a claim of identical bytes across
backends or permission to ignore extra coverage.

The GPU gate additionally runs
`renderer::text_residency_tests::released_image_regions_recover_space_with_live_text_and_images`.
It fills the shelf with a real image, observes a large cached glyph fail to fit,
then releases that image and reuses the space for the glyph and a smaller image.
It checks retained live UVs, stale handles, a submitted frame still sampling the
released image, instance-budget failure without partial submission, fixed payload
usage and exact pixels against a fresh atlas. CPU tests cover gutter-aware splits,
small fragments, exhaustion/failure preservation and repeated mixed-size reuse.

The GPU gate also runs
`renderer::text_residency_tests::glyph_purge_recovers_text_pressure_with_live_images`
and managed `--gpu-glyph-residency`. The native test exhausts the atlas with actual
changing-size glyphs, recovers cached text alongside live images, checks queued
old-frame pixels after recycled uploads, repeats purge/reupload, and reclaims
uploads left by an instance-budget failure. CPU tests prove idempotence,
non-overlapping padded extents, image preservation and remaining fragmentation.
