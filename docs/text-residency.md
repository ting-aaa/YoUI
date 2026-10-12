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
Glyph allocations are permanent for the renderer lifetime. Explicitly released
image regions are now available to both smaller/equal-sized images and glyphs.
The allocator chooses a fitting region with the smallest padded area, then splits
it into disjoint right/bottom rectangles. Every resulting slot retains its own
one-texel gutter on all sides. Image uploads initialize the complete gutter to
transparent black so smaller images cannot filter stale pixels from a former
occupant; image UVs and sampling semantics are unchanged. Fragments too small for a padded 1×1 allocation
are discarded. If no free rectangle fits, allocation falls back to the existing
shelf. Failed dimension/capacity checks leave the shelf and free regions unchanged.

This is **free-region reuse**, not glyph eviction or complete capacity recovery.
Regions are not coalesced, rotated or compacted; fragmentation can still reject
an allocation even when aggregate free area is sufficient. Pure text or changing
font-size workloads can still exhaust the atlas because resident glyphs are never
released. There is no LRU, multipage atlas, reset API, or CPU raster-cache budget.

Live glyph/image slots never move or get overwritten. Layout cache entries still
resolve glyph keys during preparation; previously prepared UVs remain valid.
Released image handles stay invalid and are never resurrected by slot reuse.
No public API or ABI changes are needed. Existing synchronous owner-thread calls
and queue ordering ensure a submitted frame finishes sampling before later
recycled-slot writes on that queue. Release is not a GPU wait. Future externally
recorded/deferred command buffers must establish a different lifetime contract
before using this allocator.

A failed frame may already have uploaded permanent glyphs into recycled space;
it does not roll back residency, just as with shelf allocation. It does not submit
partial drawing, change the previous output/FrameStats or invalidate live images.
The 16 MiB texture allocation and GPU payload budgets are unchanged. Free-region
metadata is CPU state outside the existing GPU budget.

The next smallest safe glyph-reclamation contract would be an explicit
between-frame operation, with no retained prepared command buffers, that clears
only glyph residency while preserving all live image locations. It would require
tracking occupied glyph rectangles and making those padded rectangles reusable,
plus proving queue ordering and re-resolution from cached layouts under failed
frames. It would not be a full atlas reset, promise contiguous space, shrink the
texture, or bound CPU caches. This contract is only a proposal; it is not exposed
or implemented in this increment.

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

The GPU gate additionally runs
`renderer::text_residency_tests::released_image_regions_recover_space_with_live_text_and_images`.
It fills the shelf with a real image, observes a large cached glyph fail to fit,
then releases that image and reuses the space for the glyph and a smaller image.
It checks retained live UVs, stale handles, a submitted frame still sampling the
released image, instance-budget failure without partial submission, fixed payload
usage and exact pixels against a fresh atlas. CPU tests cover gutter-aware splits,
small fragments, exhaustion/failure preservation and repeated mixed-size reuse.
