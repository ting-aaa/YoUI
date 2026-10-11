use crate::Rect;
use cosmic_text::{
    Attrs, AttrsList, Buffer, BufferLine, CacheKey, Family, FontSystem, LayoutGlyph, LayoutLine,
    LayoutRun, LineEnding, Metrics, ShapeGlyph, ShapeLine, Shaping, SwashCache, SwashContent,
    Weight, Wrap,
};
use std::collections::HashMap;
use unicode_segmentation::UnicodeSegmentation;
pub const ATLAS_SIZE: u32 = 2048;
/// The cell's logical start is its right edge, and its logical end its left edge.
pub const TEXT_CELL_RTL: u32 = 1;
/// A grapheme advance box from the same cached shaping used by the painter.
///
/// Offsets are absolute UTF-8 byte boundaries. `line` is the zero-based visual
/// line (including wrapped/empty lines). Coordinates are physical pixels from
/// the text layout origin. Flags currently contain only [`TEXT_CELL_RTL`].
/// Cells are ordered by visual line, then x. Empty lines have one start == end,
/// zero-width cell. Hard line endings have no cell; their bytes remain reflected
/// in subsequent offsets. Whitespace suppressed by wrapping has a zero-width
/// cell at its adjacent shaped caret. Ligature advances are equally subdivided
/// at extended-grapheme boundaries, in the cluster's logical direction; this is
/// not font GDEF caret extraction. No atlas coordinates or font handles escape.
#[repr(C)]
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct TextCell {
    pub start: u32,
    pub end: u32,
    pub line: u32,
    pub flags: u32,
    pub x: f32,
    pub y: f32,
    pub width: f32,
    pub height: f32,
}

#[derive(Clone, Copy)]
pub struct Glyph {
    pub rect: Rect,
    // Includes font face, glyph ID, size, subpixel bins and raster flags.
    pub key: CacheKey,
}
#[derive(Clone)]
pub struct TextLayout {
    pub glyphs: Vec<Glyph>,
    pub cells: Vec<TextCell>,
    pub width: f32,
    pub height: f32,
}
#[derive(Clone, Copy)]
struct AtlasEntry {
    x: u32,
    y: u32,
    w: u32,
    h: u32,
}
// Fonts are immutable for this cache's lifetime. Font replacement must replace this
// cache too: fontdb IDs are only stable within the owning FontSystem.
struct LayoutCache {
    fonts: FontSystem,
    swash: SwashCache,
    layouts: HashMap<(String, u32, u32, bool, bool), TextLayout>,
    hits: u32,
}
#[derive(Default)]
struct Atlas {
    glyphs: HashMap<CacheKey, AtlasEntry>,
    images: HashMap<u32, AtlasEntry>,
    free_images: Vec<AtlasEntry>,
    x: u32,
    y: u32,
    row_h: u32,
}
impl Atlas {
    fn new() -> Self {
        Self {
            x: 1,
            y: 1,
            ..Self::default()
        }
    }
    // Transactional failure: exhaustion must not move the allocation cursor.
    // Allocations never move/reuse glyph slots during frame preparation. Images
    // may reuse only explicitly released image slots, never glyph slots.
    fn allocate(&mut self, w: u32, h: u32) -> Result<AtlasEntry, String> {
        if w == 0 || h == 0 || w > ATLAS_SIZE - 2 || h > ATLAS_SIZE - 2 {
            return Err("invalid atlas allocation dimensions".into());
        }
        let (x, y, row_h) = if self.x + w + 1 > ATLAS_SIZE {
            (1, self.y + self.row_h + 2, 0)
        } else {
            (self.x, self.y, self.row_h)
        };
        if y + h + 1 > ATLAS_SIZE {
            return Err("atlas budget exhausted; recreate renderer or use fewer resources".into());
        }
        let entry = AtlasEntry { x, y, w, h };
        self.x = x + w + 2;
        self.y = y;
        self.row_h = row_h.max(h);
        Ok(entry)
    }
    fn allocate_image(&mut self, w: u32, h: u32) -> Result<AtlasEntry, String> {
        if let Some(index) = self.free_images.iter().position(|e| e.w == w && e.h == h) {
            Ok(self.free_images.swap_remove(index))
        } else {
            self.allocate(w, h)
        }
    }
    fn release_image(&mut self, id: u32) -> Result<(), String> {
        let entry = self
            .images
            .remove(&id)
            .ok_or("invalid or released image handle")?;
        self.free_images.push(entry);
        Ok(())
    }
    fn resolve(
        &mut self,
        key: CacheKey,
        w: u32,
        h: u32,
        upload: impl FnOnce(AtlasEntry),
    ) -> Result<[f32; 4], String> {
        let entry = if let Some(entry) = self.glyphs.get(&key) {
            *entry
        } else {
            let entry = self.allocate(w, h)?;
            upload(entry);
            self.glyphs.insert(key, entry);
            entry
        };
        Ok(entry.uv())
    }
}
impl AtlasEntry {
    fn uv(self) -> [f32; 4] {
        let n = ATLAS_SIZE as f32;
        [
            self.x as f32 / n,
            self.y as f32 / n,
            self.w as f32 / n,
            self.h as f32 / n,
        ]
    }
}
pub struct TextSystem {
    layout: LayoutCache,
    atlas: Atlas,
    pub texture: wgpu::Texture,
}
impl TextSystem {
    pub fn new(device: &wgpu::Device) -> Self {
        let texture = device.create_texture(&wgpu::TextureDescriptor {
            label: Some("glyph atlas"),
            size: wgpu::Extent3d {
                width: ATLAS_SIZE,
                height: ATLAS_SIZE,
                depth_or_array_layers: 1,
            },
            mip_level_count: 1,
            sample_count: 1,
            dimension: wgpu::TextureDimension::D2,
            format: wgpu::TextureFormat::Rgba8UnormSrgb,
            usage: wgpu::TextureUsages::TEXTURE_BINDING | wgpu::TextureUsages::COPY_DST,
            view_formats: &[],
        });
        Self {
            layout: LayoutCache::new(),
            atlas: Atlas::new(),
            texture,
        }
    }
    pub fn glyph_count(&self) -> u32 {
        self.atlas.glyphs.len() as u32
    }
    pub fn hits(&self) -> u32 {
        self.layout.hits
    }
    pub fn reset_hits(&mut self) {
        self.layout.hits = 0;
    }
    pub fn image_uv(&self, id: u32) -> Result<[f32; 4], String> {
        self.atlas
            .images
            .get(&id)
            .map(|e| e.uv())
            .ok_or_else(|| "invalid, foreign or released image handle".into())
    }
    pub fn upload_image(
        &mut self,
        queue: &wgpu::Queue,
        w: u32,
        h: u32,
        rgba: &[u8],
    ) -> Result<u32, String> {
        if w == 0
            || h == 0
            || w > ATLAS_SIZE - 2
            || h > ATLAS_SIZE - 2
            || rgba.len() != w as usize * h as usize * 4
        {
            return Err("invalid RGBA image dimensions/length".into());
        }
        let e = self.atlas.allocate_image(w, h)?;
        static NEXT: std::sync::atomic::AtomicU32 = std::sync::atomic::AtomicU32::new(1);
        let id = NEXT
            .fetch_update(
                std::sync::atomic::Ordering::Relaxed,
                std::sync::atomic::Ordering::Relaxed,
                |n| n.checked_add(1),
            )
            .map_err(|_| "image ID space exhausted")?;
        queue.write_texture(
            wgpu::TexelCopyTextureInfo {
                texture: &self.texture,
                mip_level: 0,
                origin: wgpu::Origin3d {
                    x: e.x,
                    y: e.y,
                    z: 0,
                },
                aspect: wgpu::TextureAspect::All,
            },
            rgba,
            wgpu::TexelCopyBufferLayout {
                offset: 0,
                bytes_per_row: Some(w * 4),
                rows_per_image: Some(h),
            },
            wgpu::Extent3d {
                width: w,
                height: h,
                depth_or_array_layers: 1,
            },
        );
        self.atlas.images.insert(id, e);
        Ok(id)
    }
    pub fn release_image(&mut self, id: u32) -> Result<(), String> {
        self.atlas.release_image(id)
    }
    pub fn layout(&mut self, text: &str, size: f32, width: f32, bold: bool) -> TextLayout {
        self.layout.layout(text, size, width, bold)
    }
    pub fn layout_with_wrap(
        &mut self,
        text: &str,
        size: f32,
        width: f32,
        bold: bool,
        wrap: bool,
    ) -> TextLayout {
        self.layout.layout_with_wrap(text, size, width, bold, wrap)
    }
    // Called only during draw preparation. Cached layouts contain no atlas UVs.
    pub fn resolve_glyph(
        &mut self,
        queue: &wgpu::Queue,
        glyph: &Glyph,
    ) -> Result<[f32; 4], String> {
        if let Some(entry) = self.atlas.glyphs.get(&glyph.key) {
            return Ok(entry.uv());
        }
        let img = self
            .layout
            .swash
            .get_image(&mut self.layout.fonts, glyph.key)
            .as_ref()
            .ok_or("cached glyph raster unavailable")?;
        let w = img.placement.width;
        let h = img.placement.height;
        let mut pixels = vec![255u8; (w * h * 4) as usize];
        for (i, px) in pixels.chunks_exact_mut(4).enumerate() {
            match img.content {
                SwashContent::Mask => px[3] = img.data[i],
                SwashContent::Color => px.copy_from_slice(&img.data[i * 4..i * 4 + 4]),
                SwashContent::SubpixelMask => {
                    px[3] = img.data[i * 4..i * 4 + 3]
                        .iter()
                        .copied()
                        .max()
                        .unwrap_or(0)
                }
            }
        }
        self.atlas.resolve(glyph.key, w, h, |e| {
            queue.write_texture(
                wgpu::TexelCopyTextureInfo {
                    texture: &self.texture,
                    mip_level: 0,
                    origin: wgpu::Origin3d {
                        x: e.x,
                        y: e.y,
                        z: 0,
                    },
                    aspect: wgpu::TextureAspect::All,
                },
                &pixels,
                wgpu::TexelCopyBufferLayout {
                    offset: 0,
                    bytes_per_row: Some(w * 4),
                    rows_per_image: Some(h),
                },
                wgpu::Extent3d {
                    width: w,
                    height: h,
                    depth_or_array_layers: 1,
                },
            );
        })
    }
}
impl LayoutCache {
    fn new() -> Self {
        Self {
            fonts: FontSystem::new(),
            swash: SwashCache::new(),
            layouts: HashMap::new(),
            hits: 0,
        }
    }
    pub fn layout(&mut self, text: &str, size: f32, width: f32, bold: bool) -> TextLayout {
        self.layout_with_wrap(text, size, width, bold, true)
    }
    pub fn layout_with_wrap(
        &mut self,
        text: &str,
        size: f32,
        width: f32,
        bold: bool,
        wrap: bool,
    ) -> TextLayout {
        let key = (text.to_owned(), size.to_bits(), width.to_bits(), bold, wrap);
        if let Some(v) = self.layouts.get(&key) {
            self.hits += 1;
            return v.clone();
        }
        let buffer = self.shape(text, size, width, bold, wrap);
        let mut layout = TextLayout {
            glyphs: Vec::new(),
            cells: Vec::new(),
            width: 0.,
            height: 0.,
        };
        // BufferLine preserves line ending lengths, including CRLF/LFCR.
        let mut offset = 0;
        let lines = buffer
            .buffer
            .lines
            .iter()
            .map(|line| {
                let start = offset;
                offset += line.text().len() + line.ending().as_str().len();
                let mut boundaries = line
                    .text()
                    .grapheme_indices(true)
                    .map(|(i, _)| i)
                    .collect::<Vec<_>>();
                boundaries.push(line.text().len());
                (start, boundaries)
            })
            .collect::<Vec<_>>();
        let mut paragraph_cells = vec![Vec::new(); lines.len()];
        for (visual_line, run) in buffer.layout_runs().enumerate() {
            layout.width = layout.width.max(run.line_w);
            layout.height = layout.height.max(run.line_top + run.line_height);
            append_run_cells(
                &run,
                &lines[run.line_i].1,
                lines[run.line_i].0,
                visual_line as u32,
                &mut paragraph_cells[run.line_i],
            );
            for glyph in run.glyphs {
                let p = glyph.physical((0., 0.), 1.);
                let Some(img) = self.swash.get_image(&mut self.fonts, p.cache_key) else {
                    continue;
                };
                let placement = img.placement;
                if placement.width == 0 || placement.height == 0 {
                    continue;
                }
                layout.glyphs.push(Glyph {
                    rect: Rect::new(
                        (p.x + placement.left) as f32,
                        run.line_y + p.y as f32 - placement.top as f32,
                        placement.width as f32,
                        placement.height as f32,
                    ),
                    key: p.cache_key,
                });
            }
        }
        for ((offset, boundaries), mut cells) in lines.into_iter().zip(paragraph_cells) {
            complete_paragraph_cells(offset, &boundaries, &mut cells);
            layout.cells.extend(cells);
        }
        layout.cells.sort_by(|a, b| {
            a.line
                .cmp(&b.line)
                .then_with(|| a.x.total_cmp(&b.x))
                .then(a.start.cmp(&b.start))
        });
        if self.layouts.len() >= 512 {
            self.layouts.clear();
        }
        self.layouts.insert(key, layout.clone());
        layout
    }
    fn shape(&mut self, text: &str, size: f32, width: f32, bold: bool, wrap: bool) -> ShapedText {
        let mut buffer = Buffer::new(&mut self.fonts, Metrics::new(size, size * 1.35));
        buffer.set_size(&mut self.fonts, Some(width.max(1.)), None);
        if !wrap {
            buffer.set_wrap(&mut self.fonts, Wrap::None);
        }
        let attrs = Attrs::new().family(Family::SansSerif).weight(if bold {
            Weight::BOLD
        } else {
            Weight::NORMAL
        });
        buffer.set_text(&mut self.fonts, text, &attrs, Shaping::Advanced);
        // cosmic-text's set_text intentionally omits a final empty paragraph.
        // Preserve it in the shared buffer, so painting, measurement and editing
        // geometry all agree about the final caret's line and text height.
        if text.ends_with(['\r', '\n']) {
            buffer.lines.push(BufferLine::new(
                "",
                LineEnding::None,
                AttrsList::new(&attrs),
                Shaping::Advanced,
            ));
        }
        buffer.shape_until_scroll(&mut self.fonts, false);
        ShapedText::new(buffer, size, width)
    }
}

// cosmic-text 0.14's WordOrGlyph fallback wraps individual shaped glyphs, even
// when several glyphs belong to one extended grapheme. Keep its normal layout
// unchanged unless that happens. For affected paragraphs, layout atomic shaped
// clusters and then restore every original glyph, face, advance and offset.
// If a paragraph needs correction and also contains an EGC spanning embedding
// levels, it conservatively falls back to the original unwrapped paragraph.
// The cross-level EGC need not itself be the split one. This preserves bidi
// semantics instead of inventing an atomic level or reordering policy.
// Painting and editing both consume the resulting runs. No text is re-shaped.
struct ShapedText {
    buffer: Buffer,
    corrected: HashMap<usize, Vec<LayoutLine>>,
}
impl ShapedText {
    fn new(buffer: Buffer, size: f32, width: f32) -> Self {
        let mut corrected = HashMap::new();
        if buffer.wrap() != Wrap::None {
            for (i, line) in buffer.lines.iter().enumerate() {
                let mut boundaries = line
                    .text()
                    .grapheme_indices(true)
                    .map(|(i, _)| i)
                    .collect::<Vec<_>>();
                boundaries.push(line.text().len());
                // The mere presence of a cross-bidi EGC does not disable
                // wrapping. The original layout must first actually split an
                // EGC in this paragraph before correction/fallback is considered.
                if splits_grapheme(line.layout_opt().expect("shaped line layout"), &boundaries) {
                    let layout = layout_atomic_graphemes(
                        line.shape_opt().expect("shaped line"),
                        &boundaries,
                        size,
                        width.max(1.),
                        buffer.wrap(),
                    );
                    debug_assert!(!splits_grapheme(&layout, &boundaries));
                    corrected.insert(i, layout);
                }
            }
        }
        Self { buffer, corrected }
    }
    fn layout_runs(&self) -> impl Iterator<Item = LayoutRun<'_>> {
        // Equivalent to cosmic's LayoutRunIter for our full-height, unscrolled
        // buffers, using corrected LayoutLines only where an EGC was split.
        self.buffer
            .lines
            .iter()
            .enumerate()
            .flat_map(|(line_i, line)| {
                let layouts = self
                    .corrected
                    .get(&line_i)
                    .unwrap_or_else(|| line.layout_opt().expect("shaped line layout"));
                layouts.iter().map(move |layout| (line_i, line, layout))
            })
            .scan(0., |top, (line_i, line, layout)| {
                let line_height = layout
                    .line_height_opt
                    .unwrap_or(self.buffer.metrics().line_height);
                let line_top = *top;
                *top += line_height;
                let glyph_height = layout.max_ascent + layout.max_descent;
                Some(LayoutRun {
                    line_i,
                    text: line.text(),
                    rtl: line.shape_opt().expect("shaped line").rtl,
                    glyphs: &layout.glyphs,
                    line_y: line_top + (line_height - glyph_height) / 2. + layout.max_ascent,
                    line_top,
                    line_height,
                    line_w: layout.w,
                })
            })
    }
}
fn grapheme_range(start: usize, end: usize, boundaries: &[usize]) -> (usize, usize) {
    (
        boundaries
            .partition_point(|&i| i <= start)
            .saturating_sub(1),
        boundaries.partition_point(|&i| i < end),
    )
}
fn splits_grapheme(lines: &[LayoutLine], boundaries: &[usize]) -> bool {
    let mut owners = vec![usize::MAX; boundaries.len().saturating_sub(1)];
    for (line, layout) in lines.iter().enumerate() {
        for glyph in &layout.glyphs {
            let (start, end) = grapheme_range(glyph.start, glyph.end, boundaries);
            for owner in &mut owners[start..end] {
                if *owner != usize::MAX && *owner != line {
                    return true;
                }
                *owner = line;
            }
        }
    }
    false
}
struct AtomicGlyphs {
    start: usize,
    end: usize,
    members: Vec<usize>,
    glyphs: Vec<LayoutGlyph>,
    left: f32,
    right: f32,
}
fn layout_atomic_graphemes(
    source: &ShapeLine,
    boundaries: &[usize],
    size: f32,
    width: f32,
    wrap: Wrap,
) -> Vec<LayoutLine> {
    // Give original glyphs temporary identity tags so their exact positions
    // inside an unbroken shaped cluster can survive bidi reordering. The tags
    // never escape, and every original metadata value is restored below.
    let mut shape = source.clone();
    let mut originals = Vec::<ShapeGlyph>::new();
    let mut levels = Vec::new();
    let mut ranges = Vec::new();
    for span in &mut shape.spans {
        for word in &mut span.words {
            for glyph in &mut word.glyphs {
                let id = originals.len();
                originals.push(glyph.clone());
                levels.push(span.level);
                glyph.metadata = id;
                let (start, end) = grapheme_range(glyph.start, glyph.end, boundaries);
                ranges.push((start, end, id));
            }
        }
    }
    ranges.sort_unstable();
    let mut groups = Vec::<AtomicGlyphs>::new();
    for (start, end, id) in ranges {
        if let Some(previous) = groups.last_mut() {
            if start < previous.end || (start == previous.start && end == previous.end) {
                previous.end = previous.end.max(end);
                previous.members.push(id);
                continue;
            }
        }
        groups.push(AtomicGlyphs {
            start,
            end,
            members: vec![id],
            glyphs: Vec::new(),
            left: f32::INFINITY,
            right: f32::NEG_INFINITY,
        });
    }
    // This paragraph already needs atomic wrap correction. If ANY EGC crosses
    // embedding levels (even if a different EGC caused the split), it has no
    // single existing bidi level for an atomic placeholder. Preserve cosmic's exact bidi
    // placement and keep this affected paragraph unwrapped rather than invent
    // a new direction/reordering policy or split a user's grapheme.
    if groups.iter().any(|group| {
        group
            .members
            .iter()
            .any(|&id| levels[id] != levels[group.members[0]])
    }) {
        return source.layout(size, Some(width), Wrap::None, None, None);
    }
    let mut group_for = vec![0; originals.len()];
    for (index, group) in groups.iter_mut().enumerate() {
        group.members.sort_unstable();
        for &id in &group.members {
            group_for[id] = index;
        }
    }
    // This is line placement over the SAME shaped glyphs, not another shaping
    // pass. It establishes cluster-internal positions across font/word boundaries.
    // All glyphs in an atomic group have the same original embedding level.
    for layout in shape.layout(size, Some(width), Wrap::None, None, None) {
        for mut glyph in layout.glyphs {
            let group = &mut groups[group_for[glyph.metadata]];
            group.left = group.left.min(glyph.x.min(glyph.x + glyph.w));
            group.right = group.right.max(glyph.x.max(glyph.x + glyph.w));
            glyph.metadata = originals[glyph.metadata].metadata;
            group.glyphs.push(glyph);
        }
    }
    // Each connected EGC/shape-cluster group appears exactly once in the
    // wrapping input at its original logical slot and unchanged embedding level;
    // the original glyph positions/levels within it survive.
    for span in &mut shape.spans {
        for word in &mut span.words {
            word.glyphs = std::mem::take(&mut word.glyphs)
                .into_iter()
                .filter_map(|mut glyph| {
                    let index = group_for[glyph.metadata];
                    let group = &groups[index];
                    if glyph.metadata != group.members[0] {
                        return None;
                    }
                    glyph.start = boundaries[group.start];
                    glyph.end = boundaries[group.end];
                    glyph.metadata = index;
                    glyph.metrics_opt = None;
                    glyph.x_advance = (group.right - group.left) / size;
                    glyph.y_advance = group
                        .members
                        .iter()
                        .map(|&id| {
                            let g = &originals[id];
                            g.metrics_opt.map_or(size, |m| m.font_size) * g.y_advance
                        })
                        .sum::<f32>()
                        / size;
                    glyph.ascent = group
                        .members
                        .iter()
                        .map(|&id| {
                            let g = &originals[id];
                            g.metrics_opt.map_or(size, |m| m.font_size) * g.ascent
                        })
                        .fold(0., f32::max)
                        / size;
                    glyph.descent = group
                        .members
                        .iter()
                        .map(|&id| {
                            let g = &originals[id];
                            g.metrics_opt.map_or(size, |m| m.font_size) * g.descent
                        })
                        .fold(0., f32::max)
                        / size;
                    Some(glyph)
                })
                .collect();
        }
        span.words.retain(|word| !word.glyphs.is_empty());
    }
    shape.spans.retain(|span| !span.words.is_empty());
    let mut layouts = shape.layout(size, Some(width), wrap, None, None);
    for layout in &mut layouts {
        let mut expanded = Vec::new();
        layout.max_ascent = 0.;
        layout.max_descent = 0.;
        for aggregate in std::mem::take(&mut layout.glyphs) {
            let group = &groups[aggregate.metadata];
            let origin_y = group.glyphs.first().map_or(0., |glyph| glyph.y);
            for glyph in &group.glyphs {
                let mut glyph = glyph.clone();
                glyph.x = aggregate.x + (glyph.x - group.left);
                glyph.y = aggregate.y + (glyph.y - origin_y);
                expanded.push(glyph);
            }
            for &id in &group.members {
                let glyph = &originals[id];
                let font_size = glyph.metrics_opt.map_or(size, |m| m.font_size);
                layout.max_ascent = layout.max_ascent.max(font_size * glyph.ascent);
                layout.max_descent = layout.max_descent.max(font_size * glyph.descent);
            }
        }
        layout.glyphs = expanded;
    }
    layouts
}

#[derive(Clone, Copy, Debug)]
struct Cluster {
    start: usize,
    end: usize,
    left: f32,
    right: f32,
    rtl: bool,
}
fn append_run_cells(
    run: &LayoutRun<'_>,
    boundaries: &[usize],
    offset: usize,
    line: u32,
    cells: &mut Vec<TextCell>,
) {
    let clusters = run
        .glyphs
        .iter()
        .map(|glyph| {
            // Font fallback/combining glyphs can have scalar-level cluster bounds.
            // Expand them to the paragraph's EGC bounds before combining advances.
            let (start, end) = grapheme_range(glyph.start, glyph.end, boundaries);
            Cluster {
                start,
                end,
                left: glyph.x.min(glyph.x + glyph.w),
                right: glyph.x.max(glyph.x + glyph.w),
                rtl: glyph.level.is_rtl(),
            }
        })
        .collect::<Vec<_>>();
    let merged = merge_clusters(clusters);
    let template = TextCell {
        line,
        y: run.line_top,
        height: run.line_height,
        ..TextCell::default()
    };
    let before = cells.len();
    for cluster in merged {
        append_cluster_cells(cluster, boundaries, offset, template, cells);
    }
    if cells.len() == before {
        cells.push(TextCell {
            start: offset as u32,
            end: offset as u32,
            flags: if run.rtl { TEXT_CELL_RTL } else { 0 },
            ..template
        });
    }
}
fn merge_clusters(mut clusters: Vec<Cluster>) -> Vec<Cluster> {
    clusters.sort_by_key(|cluster| (cluster.start, cluster.end));
    let mut merged: Vec<Cluster> = Vec::new();
    for cluster in clusters {
        if let Some(previous) = merged.last_mut() {
            if cluster.start < previous.end
                || (cluster.start == previous.start && cluster.end == previous.end)
            {
                previous.end = previous.end.max(cluster.end);
                previous.left = previous.left.min(cluster.left);
                previous.right = previous.right.max(cluster.right);
                continue;
            }
        }
        merged.push(cluster);
    }
    merged
}
fn append_cluster_cells(
    cluster: Cluster,
    boundaries: &[usize],
    offset: usize,
    template: TextCell,
    cells: &mut Vec<TextCell>,
) {
    let count = cluster.end - cluster.start;
    if count == 0 {
        return;
    }
    let width = (cluster.right - cluster.left) / count as f32;
    for i in 0..count {
        let visual = if cluster.rtl { count - 1 - i } else { i };
        cells.push(TextCell {
            start: (offset + boundaries[cluster.start + i]) as u32,
            end: (offset + boundaries[cluster.start + i + 1]) as u32,
            flags: if cluster.rtl { TEXT_CELL_RTL } else { 0 },
            x: cluster.left + width * visual as f32,
            width,
            ..template
        });
    }
}
fn complete_paragraph_cells(offset: usize, boundaries: &[usize], cells: &mut Vec<TextCell>) {
    // Word wrapping may omit separating whitespace from LayoutRun::glyphs.
    // Preserve its logical range at the adjacent shaped edge without inventing
    // an advance, re-measuring a prefix, or moving any painted glyph.
    cells.sort_by_key(|cell| (cell.start, cell.line));
    let shaped = cells.clone();
    let mut cursor = 0;
    for pair in boundaries.windows(2) {
        let start = (offset + pair[0]) as u32;
        let end = (offset + pair[1]) as u32;
        while cursor < shaped.len() && shaped[cursor].end <= start {
            cursor += 1;
        }
        if shaped
            .get(cursor)
            .is_some_and(|cell| cell.start == start && cell.end == end)
        {
            continue;
        }
        let (anchor, at_end) = if cursor > 0 {
            (shaped[cursor - 1], true)
        } else if let Some(first) = shaped.first() {
            (*first, false)
        } else {
            continue;
        };
        let rtl = anchor.flags & TEXT_CELL_RTL != 0;
        cells.push(TextCell {
            start,
            end,
            x: anchor.x + if at_end != rtl { anchor.width } else { 0. },
            width: 0.,
            ..anchor
        });
    }
    if boundaries.len() > 1 {
        cells.retain(|cell| cell.start != cell.end);
    }
}

#[cfg(test)]
impl TextSystem {
    // Test-only mapping loss, NOT reclamation or an atlas-capacity reset. Keeping
    // old slots occupied preserves any UVs already prepared for a live frame.
    pub(crate) fn forget_glyph_residency(&mut self) {
        self.atlas.glyphs.clear();
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn key() -> CacheKey {
        CacheKey::new(
            Default::default(),
            1,
            16.,
            (0., 0.),
            cosmic_text::CacheKeyFlags::empty(),
        )
        .0
    }
    fn rect(rect: Rect) -> [f32; 4] {
        [rect.x, rect.y, rect.w, rect.h]
    }

    #[test]
    fn cached_layout_resolves_new_residency_without_changing_geometry() {
        // Synthetic font-independent raster uploads exercise the same resolver
        // as GPU preparation, but do not claim GPU/pixel coverage.
        let glyph = Glyph {
            key: key(),
            rect: Rect::new(2., 3., 5., 7.),
        };
        let layout = TextLayout {
            glyphs: vec![glyph],
            cells: Vec::new(),
            width: 9.,
            height: 12.,
        };
        let mut atlas = Atlas::new();
        let mut writes = Vec::new();
        let old_uv = atlas
            .resolve(glyph.key, 5, 7, |e| writes.push(e.uv()))
            .unwrap();
        assert_eq!(
            atlas
                .resolve(glyph.key, 5, 7, |_| panic!("resident glyph uploaded again"))
                .unwrap(),
            old_uv
        );
        let image = atlas.allocate_image(8, 8).unwrap();
        atlas.images.insert(1, image);
        let image_uv = image.uv();
        let prepared_uvs = [old_uv, image_uv];
        // Discard only lookup mappings: the occupied slots remain reserved.
        atlas.glyphs.clear();
        let cached = &layout.glyphs[0];
        let new_uv = atlas
            .resolve(
                cached.key,
                cached.rect.w as u32,
                cached.rect.h as u32,
                |e| writes.push(e.uv()),
            )
            .unwrap();
        assert_ne!(new_uv, old_uv);
        assert_eq!(writes, [old_uv, new_uv]);
        assert_eq!(rect(cached.rect), [2., 3., 5., 7.]);
        assert_eq!((layout.width, layout.height), (9., 12.));
        assert_eq!(atlas.images[&1].uv(), image_uv);
        assert_eq!(prepared_uvs, [old_uv, image_uv]);
        // The new write is strictly beyond both live prepared allocations.
        assert!(new_uv[0] >= image_uv[0] + image_uv[2]);
        let disposable = atlas.allocate_image(8, 8).unwrap();
        atlas.images.insert(2, disposable);
        atlas.release_image(2).unwrap();
        assert!(atlas.release_image(2).is_err());
        assert_eq!(atlas.allocate_image(8, 8).unwrap().uv(), disposable.uv());
        assert_eq!(atlas.images[&1].uv(), image_uv);
        assert_eq!(atlas.glyphs[&glyph.key].uv(), new_uv);
    }

    #[test]
    fn atlas_exhaustion_is_transactional_and_never_evicts_live_entries() {
        let mut atlas = Atlas::new();
        let uv = atlas
            .resolve(key(), ATLAS_SIZE - 2, ATLAS_SIZE - 2, |_| {})
            .unwrap();
        let cursor = (atlas.x, atlas.y, atlas.row_h);
        for (w, h) in [(1, 1), (0, 1), (u32::MAX, 1), (1, u32::MAX)] {
            assert!(atlas.allocate(w, h).is_err());
            assert_eq!((atlas.x, atlas.y, atlas.row_h), cursor);
            assert_eq!(atlas.glyphs[&key()].uv(), uv);
        }
    }

    #[test]
    fn glyph_identity_keeps_size_subpixels_flags_and_font_face() {
        let mut atlas = Atlas::new();
        let base = key();
        let mut keys = vec![base];
        keys.push(CacheKey {
            font_size_bits: 17f32.to_bits(),
            ..base
        });
        keys.push(CacheKey {
            glyph_id: 2,
            ..base
        });
        keys.push(CacheKey {
            x_bin: cosmic_text::SubpixelBin::One,
            ..base
        });
        keys.push(CacheKey {
            flags: cosmic_text::CacheKeyFlags::FAKE_ITALIC,
            ..base
        });
        // Use distinct fontdb IDs, including fallback faces, without assuming
        // any installed family, glyph coverage, or platform font metrics.
        let fonts = FontSystem::new();
        for face in fonts.db().faces().take(2) {
            keys.push(CacheKey {
                font_id: face.id,
                ..base
            });
        }
        for key in &keys {
            atlas.resolve(*key, 1, 1, |_| {}).unwrap();
        }
        assert_eq!(atlas.glyphs.len(), keys.len());
    }

    #[test]
    fn layout_cache_keys_measurement_empty_text_and_bound() {
        let mut cache = LayoutCache::new();
        let first = cache.layout("office text", 16., 200., false);
        let again = cache.layout("office text", 16., 200., false);
        assert_eq!(cache.hits, 1);
        assert_eq!((first.width, first.height), (again.width, again.height));
        assert_eq!(
            first
                .glyphs
                .iter()
                .map(|g| (g.key, rect(g.rect)))
                .collect::<Vec<_>>(),
            again
                .glyphs
                .iter()
                .map(|g| (g.key, rect(g.rect)))
                .collect::<Vec<_>>()
        );
        for (size, width, bold) in [(17., 200., false), (16., 20., false), (16., 200., true)] {
            cache.layout("office text", size, width, bold);
        }
        assert_eq!(cache.layouts.len(), 4);
        assert_eq!(cache.hits, 1);
        assert!(cache.layout("", 16., 200., false).glyphs.is_empty());
        assert!(cache.layout("   ", 16., 200., false).glyphs.is_empty());
        // Unknown glyphs may be replaced by a font's .notdef, or omitted if
        // no raster exists. Neither path needs atlas residency to measure.
        let missing = cache.layout("\u{10ffff}", 16., 200., false);
        assert!(missing.width.is_finite() && missing.height.is_finite());
        for n in 0..520 {
            cache.layout(&format!("{n}"), 16., 200., false);
        }
        assert!(cache.layouts.len() <= 512);
    }

    fn assert_close(left: f32, right: f32) {
        assert!((left - right).abs() < 0.001, "{left} != {right}");
    }

    #[test]
    fn cells_preserve_graphemes_spaces_newlines_and_empty_trailing_lines() {
        let mut cache = LayoutCache::new();
        for (text, starts) in [
            ("", vec![0]),
            ("\n", vec![0, 1]),
            ("\r\n", vec![0, 2]),
            ("a\r\n\r\nb\n", vec![0, 3, 5, 7]),
            ("a\rb\n\rc", vec![0, 2, 5]),
        ] {
            let layout = cache.layout(text, 20., 400., false);
            assert_close(layout.height, starts.len() as f32 * 27.);
            for (line, start) in starts.into_iter().enumerate() {
                let first = layout
                    .cells
                    .iter()
                    .find(|cell| cell.line == line as u32)
                    .unwrap();
                assert_eq!(first.start, start);
                assert_close(first.y, line as f32 * 27.);
                assert_close(first.height, 27.);
            }
        }
        for text in [
            "office",
            "A\u{301}e\u{308}",
            "😀👩‍👩‍👧‍👦🇺🇸👍🏽",
            "क्षि",
            "   ",
            "abc אבג لا العربية 123",
            "a\u{200b}b\u{2060}c",
        ] {
            for width in [24., 400.] {
                let layout = cache.layout(text, 18., width, false);
                let mut boundaries = text
                    .grapheme_indices(true)
                    .map(|(i, _)| i as u32)
                    .collect::<Vec<_>>();
                boundaries.push(text.len() as u32);
                for cell in &layout.cells {
                    assert!(boundaries.contains(&cell.start), "{text:?}: {cell:?}");
                    assert!(boundaries.contains(&cell.end), "{text:?}: {cell:?}");
                    assert!(cell.start <= cell.end && cell.flags & !TEXT_CELL_RTL == 0);
                    assert!(cell.x.is_finite() && cell.y.is_finite());
                    assert!(cell.width.is_finite() && cell.width >= 0.);
                    assert_close(cell.height, 18. * 1.35);
                }
                for pair in boundaries.windows(2) {
                    assert!(
                        layout
                            .cells
                            .iter()
                            .any(|cell| cell.start == pair[0] && cell.end == pair[1]),
                        "lost grapheme {pair:?} in {text:?}: {:?}",
                        layout.cells
                    );
                }
            }
        }
        let spaces = cache.layout("   ", 18., 400., false);
        assert!(spaces.glyphs.is_empty());
        assert_eq!(spaces.cells.len(), 3);
        assert!(spaces.cells.iter().all(|cell| cell.width > 0.));
    }

    #[test]
    fn cluster_geometry_merges_advances_and_subdivides_rtl_ligatures() {
        let clusters = merge_clusters(vec![
            Cluster {
                start: 0,
                end: 2,
                left: 10.,
                right: 20.,
                rtl: true,
            },
            Cluster {
                start: 0,
                end: 2,
                left: 20.,
                right: 30.,
                rtl: true,
            },
            // A zero-advance combining glyph must not create another cell.
            Cluster {
                start: 1,
                end: 2,
                left: 30.,
                right: 30.,
                rtl: true,
            },
        ]);
        assert_eq!(clusters.len(), 1);
        let mut cells = Vec::new();
        append_cluster_cells(
            clusters[0],
            &[0, 2, 4],
            5,
            TextCell {
                height: 20.,
                ..TextCell::default()
            },
            &mut cells,
        );
        assert_eq!(cells.len(), 2);
        assert_eq!(
            (cells[0].start, cells[0].end, cells[0].x, cells[0].width),
            (5, 7, 20., 10.)
        );
        assert_eq!(
            (cells[1].start, cells[1].end, cells[1].x, cells[1].width),
            (7, 9, 10., 10.)
        );
        assert!(cells.iter().all(|cell| cell.flags == TEXT_CELL_RTL));
    }

    #[test]
    fn cached_cells_follow_painted_shaping_including_bidi_and_ligatures() {
        let mut cache = LayoutCache::new();
        let text = "office A\u{301} abc אבג لا العربية 123";
        let size = 22.;
        let width = 400.;
        let buffer = cache.shape(text, size, width, false, true);
        let layout = cache.layout(text, size, width, false);
        let cached = cache.layout(text, size, width, false);
        assert_eq!(layout.cells, cached.cells);
        assert_eq!(cache.hits, 1);
        assert!(layout.cells.iter().any(|cell| cell.flags == TEXT_CELL_RTL));
        assert!(layout.cells.iter().any(|cell| cell.flags == 0));
        let mut painted = Vec::new();
        for (line, run) in buffer.layout_runs().enumerate() {
            for glyph in run.glyphs {
                let covered = layout
                    .cells
                    .iter()
                    .filter(|cell| {
                        cell.line == line as u32
                            && cell.start < glyph.end as u32
                            && cell.end > glyph.start as u32
                    })
                    .collect::<Vec<_>>();
                assert!(!covered.is_empty());
                let left = covered
                    .iter()
                    .map(|cell| cell.x)
                    .fold(f32::INFINITY, f32::min);
                let right = covered
                    .iter()
                    .map(|cell| cell.x + cell.width)
                    .fold(f32::NEG_INFINITY, f32::max);
                assert!(left <= glyph.x + 0.001);
                assert!(right >= glyph.x + glyph.w - 0.001);
                for cell in covered {
                    assert_close(cell.y, run.line_top);
                    assert_close(cell.height, run.line_height);
                }
                let physical = glyph.physical((0., 0.), 1.);
                if let Some(image) = cache.swash.get_image(&mut cache.fonts, physical.cache_key) {
                    let p = image.placement;
                    if p.width != 0 && p.height != 0 {
                        painted.push((
                            physical.cache_key,
                            [
                                (physical.x + p.left) as f32,
                                run.line_y + physical.y as f32 - p.top as f32,
                                p.width as f32,
                                p.height as f32,
                            ],
                        ));
                    }
                }
            }
        }
        assert_eq!(
            layout
                .glyphs
                .iter()
                .map(|g| (g.key, rect(g.rect)))
                .collect::<Vec<_>>(),
            painted
        );
        // Shape an RTL ligature when the selected font supplies one. The pure
        // cluster test above also covers this convention without font assumptions.
        let buffer = cache.shape("لا", size, width, false, true);
        let lam_alef = cache.layout("لا", size, width, false);
        for run in buffer.layout_runs() {
            if run
                .glyphs
                .iter()
                .any(|g| g.start == 0 && g.end == "لا".len())
            {
                let lam = lam_alef.cells.iter().find(|cell| cell.start == 0).unwrap();
                let alef = lam_alef.cells.iter().find(|cell| cell.start == 2).unwrap();
                assert!(lam.x > alef.x);
                assert_close(lam.width, alef.width);
            }
        }
    }

    #[test]
    fn wrapping_keeps_dropped_spaces_and_nowrap_is_a_distinct_cache_entry() {
        let mut cache = LayoutCache::new();
        let text = "hello   world again";
        let wrapped = cache.layout_with_wrap(text, 20., 70., false, true);
        let nowrap = cache.layout_with_wrap(text, 20., 70., false, false);
        assert!(wrapped.cells.iter().any(|cell| cell.line > 0));
        assert!(nowrap.cells.iter().all(|cell| cell.line == 0));
        assert!(nowrap.width > 70.);
        assert_eq!(cache.layouts.len(), 2);
        assert_eq!(
            nowrap.cells,
            cache.layout_with_wrap(text, 20., 70., false, false).cells
        );
        for (i, grapheme) in text.grapheme_indices(true) {
            assert!(wrapped
                .cells
                .iter()
                .any(|cell| cell.start == i as u32 && cell.end == (i + grapheme.len()) as u32));
        }
        assert!(wrapped
            .cells
            .iter()
            .any(|cell| cell.width == 0. && &text[cell.start as usize..cell.end as usize] == " "));
        // Right-aligned RTL cells use the actual finite layout width, not a
        // giant sentinel width. Rendering reads this same cached entry.
        let rtl = cache.layout_with_wrap("אבג", 20., 100., false, false);
        assert_close(
            rtl.cells
                .iter()
                .map(|cell| cell.x + cell.width)
                .fold(0., f32::max),
            100.,
        );
    }

    #[test]
    fn indic_graphemes_are_atomic_when_narrow_wrapping_overflows() {
        let mut cache = LayoutCache::new();
        // These are each one EGC in the native Unicode segmentation version,
        // even when a fallback font emits several scalar-level glyph clusters.
        for text in ["क्षि", "क्‍ष"] {
            assert_eq!(text.graphemes(true).count(), 1);
            for width in [1., 10.] {
                let wrapped = cache.layout_with_wrap(text, 18., width, false, true);
                let nowrap = cache.layout_with_wrap(text, 18., width, false, false);
                assert_eq!(
                    wrapped.cells.len(),
                    1,
                    "split {text:?} at width {width}: {:?}",
                    wrapped.cells
                );
                assert_eq!(
                    (
                        wrapped.cells[0].start,
                        wrapped.cells[0].end,
                        wrapped.cells[0].line
                    ),
                    (0, text.len() as u32, 0)
                );
                assert_eq!(wrapped.cells, nowrap.cells);
                assert_eq!(
                    wrapped
                        .glyphs
                        .iter()
                        .map(|g| (g.key, rect(g.rect)))
                        .collect::<Vec<_>>(),
                    nowrap
                        .glyphs
                        .iter()
                        .map(|g| (g.key, rect(g.rect)))
                        .collect::<Vec<_>>(),
                    "reflow must preserve the complete EGC's original shaped glyphs"
                );
                assert_close(wrapped.height, 18. * 1.35);
            }
        }
    }

    #[test]
    fn wrapping_between_graphemes_never_duplicates_a_grapheme_on_another_line() {
        let mut cache = LayoutCache::new();
        for text in [
            "क्षिक्षिक्षि",
            "क्‍षक्‍ष",
            "Aक्षिBक्‍षC",
            "क्षि क्षि क्षि",
            "👩‍👩‍👧‍👦👩🏽‍🚀",
            "a\u{301}a\u{308}",
            "العربية क्षि العربية",
            "বাংলাক্ষি",
            "\u{0600}A\u{0600}B",
            "\u{0600}क्षि",
            "\u{0600}אבג",
        ] {
            for width in [1., 10., 20., 40., 70.] {
                let layout = cache.layout(text, 18., width, false);
                let expected = text
                    .grapheme_indices(true)
                    .map(|(start, g)| (start as u32, (start + g.len()) as u32))
                    .collect::<Vec<_>>();
                for (start, end) in expected {
                    let cells = layout
                        .cells
                        .iter()
                        .filter(|cell| cell.start == start && cell.end == end)
                        .collect::<Vec<_>>();
                    assert_eq!(
                        cells.len(),
                        1,
                        "{text:?}, width {width}, range {start}..{end}: {:?}",
                        layout.cells
                    );
                }
            }
        }
        let cells = cache.layout("क्षिक्षिक्षि", 18., 10., false).cells;
        assert_eq!(cells.len(), 3);
        assert_eq!(
            cells.iter().map(|cell| cell.line).collect::<Vec<_>>(),
            vec![0, 1, 2]
        );
    }

    fn synthetic_shape(rtl: bool) -> ShapeLine {
        let glyphs = (0..4)
            .map(|i| ShapeGlyph {
                start: i * 3,
                end: (i + 1) * 3,
                x_advance: if i % 2 == 0 { 3. } else { 7. },
                y_advance: 0.,
                x_offset: 0.125,
                y_offset: 0.25,
                ascent: 0.8,
                descent: 0.2,
                font_monospace_em_width: None,
                font_id: Default::default(),
                glyph_id: i as u16,
                color_opt: None,
                metadata: 100 + i,
                cache_key_flags: cosmic_text::CacheKeyFlags::empty(),
                metrics_opt: None,
            })
            .collect();
        ShapeLine {
            rtl,
            spans: vec![cosmic_text::ShapeSpan {
                level: if rtl {
                    unicode_bidi::Level::rtl()
                } else {
                    unicode_bidi::Level::ltr()
                },
                words: vec![cosmic_text::ShapeWord {
                    blank: false,
                    glyphs,
                }],
            }],
            metrics_opt: None,
        }
    }
    #[test]
    fn synthetic_multiglyph_graphemes_wrap_atomically_without_fonts() {
        for rtl in [false, true] {
            let source = synthetic_shape(rtl);
            let boundaries = [0, 6, 12];
            let split = source.layout(1., Some(5.), Wrap::WordOrGlyph, None, None);
            assert!(splits_grapheme(&split, &boundaries));
            let fixed = layout_atomic_graphemes(&source, &boundaries, 1., 5., Wrap::WordOrGlyph);
            assert!(!splits_grapheme(&fixed, &boundaries));
            assert_eq!(fixed.len(), 2);
            for line in &fixed {
                assert_eq!(line.glyphs.len(), 2);
                assert_close(line.w, 10.);
                assert_close(line.max_ascent, 0.8);
                assert_close(line.max_descent, 0.2);
                for glyph in &line.glyphs {
                    assert_eq!(glyph.metadata, glyph.glyph_id as usize + 100);
                    assert_eq!((glyph.x_offset, glyph.y_offset), (0.125, 0.25));
                    assert_eq!(glyph.level.is_rtl(), rtl);
                }
            }
        }
    }
    #[test]
    fn synthetic_cross_embedding_grapheme_preserves_unwrapped_bidi_without_fonts() {
        let mut source = synthetic_shape(false);
        let tail = source.spans[0].words[0].glyphs.split_off(1);
        source.spans.push(cosmic_text::ShapeSpan {
            level: unicode_bidi::Level::rtl(),
            words: vec![cosmic_text::ShapeWord {
                blank: false,
                glyphs: tail,
            }],
        });
        let fixed = layout_atomic_graphemes(&source, &[0, 6, 12], 1., 5., Wrap::WordOrGlyph);
        let original = source.layout(1., Some(5.), Wrap::None, None, None);
        assert_eq!(fixed.len(), 1);
        assert_eq!(format!("{fixed:?}"), format!("{original:?}"));
        assert!(!splits_grapheme(&fixed, &[0, 6, 12]));
    }
    #[test]
    fn unaffected_layout_runs_keep_cosmic_positions_exactly() {
        let mut cache = LayoutCache::new();
        for text in ["office text", "abc אבג 123", "مرحبا", "A\n\nB\r\n"] {
            let shaped = cache.shape(text, 18., 100., false, true);
            assert!(shaped.corrected.is_empty());
            let original = shaped
                .buffer
                .layout_runs()
                .map(|run| {
                    (
                        run.line_i,
                        run.rtl,
                        run.line_y,
                        run.line_top,
                        run.line_height,
                        run.line_w,
                        format!("{:?}", run.glyphs),
                    )
                })
                .collect::<Vec<_>>();
            let actual = shaped
                .layout_runs()
                .map(|run| {
                    (
                        run.line_i,
                        run.rtl,
                        run.line_y,
                        run.line_top,
                        run.line_height,
                        run.line_w,
                        format!("{:?}", run.glyphs),
                    )
                })
                .collect::<Vec<_>>();
            assert_eq!(actual, original);
        }
    }

    #[test]
    fn long_paragraph_cross_bidi_fallback_is_conditional_and_shared_with_paint() {
        let mut cache = LayoutCache::new();
        let text = format!(
            "{} \u{0600}A {}",
            "alpha beta ".repeat(12),
            "gamma delta ".repeat(12)
        );
        let ordinary = cache.shape(&text, 18., 120., false, true);
        assert!(
            ordinary.corrected.is_empty(),
            "a cross-level EGC that does not split must retain normal wrapping"
        );
        assert!(ordinary.layout_runs().count() > 1);
        let narrow = cache.shape(&text, 18., 1., false, true);
        assert!(!narrow.corrected.is_empty());
        assert_eq!(
            narrow.layout_runs().count(),
            1,
            "affected paragraph intentionally overflows"
        );
        let wrapped = cache.layout_with_wrap(&text, 18., 1., false, true);
        let nowrap = cache.layout_with_wrap(&text, 18., 1., false, false);
        assert_eq!(wrapped.cells, nowrap.cells);
        assert_eq!(
            wrapped
                .glyphs
                .iter()
                .map(|g| (g.key, rect(g.rect)))
                .collect::<Vec<_>>(),
            nowrap
                .glyphs
                .iter()
                .map(|g| (g.key, rect(g.rect)))
                .collect::<Vec<_>>()
        );
        assert!(wrapped.cells.iter().all(|cell| cell.line == 0));
    }

    #[test]
    fn conservative_fallback_covers_safe_cross_level_egc_plus_other_split_egc() {
        let mut source = synthetic_shape(false);
        let mut glyphs = std::mem::take(&mut source.spans[0].words[0].glyphs);
        glyphs[0].x_advance = 1.;
        glyphs[1].x_advance = 1.;
        let tail = glyphs.split_off(2);
        let middle = glyphs.split_off(1);
        source.spans[0].words[0].glyphs = glyphs;
        source.spans.push(cosmic_text::ShapeSpan {
            level: unicode_bidi::Level::rtl(),
            words: vec![cosmic_text::ShapeWord {
                blank: false,
                glyphs: middle,
            }],
        });
        source.spans.push(cosmic_text::ShapeSpan {
            level: unicode_bidi::Level::ltr(),
            words: vec![cosmic_text::ShapeWord {
                blank: false,
                glyphs: tail,
            }],
        });
        let original = source.layout(1., Some(5.), Wrap::WordOrGlyph, None, None);
        let lines_for = |start: usize, end: usize| {
            original
                .iter()
                .enumerate()
                .filter_map(|(line, run)| {
                    run.glyphs
                        .iter()
                        .any(|g| g.start < end && g.end > start)
                        .then_some(line)
                })
                .collect::<Vec<_>>()
        };
        assert_eq!(lines_for(0, 6).len(), 1, "cross-level EGC initially fits");
        assert!(
            lines_for(6, 12).len() > 1,
            "another EGC requires correction"
        );
        let fixed = layout_atomic_graphemes(&source, &[0, 6, 12], 1., 5., Wrap::WordOrGlyph);
        assert_eq!(fixed.len(), 1);
        assert_eq!(
            format!("{fixed:?}"),
            format!("{:?}", source.layout(1., Some(5.), Wrap::None, None, None))
        );
    }
}
