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
Allocations are append-only for glyphs, and failure leaves the cursor unchanged.
Only explicitly released image slots of the same dimensions can be reused for
images. No eviction/reset can occur partway through preparing a frame, so already
prepared UVs keep their meaning. Existing synchronous owner-thread rendering and
queue ordering are retained. There is no multi-page atlas, LRU, capacity recovery,
resource manager, threading change or production reset API.

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
