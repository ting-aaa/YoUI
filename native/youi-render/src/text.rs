use crate::Rect;
use cosmic_text::{
    Attrs, Buffer, CacheKey, Family, FontSystem, Metrics, Shaping, SwashCache, SwashContent, Weight,
};
use std::collections::HashMap;
pub const ATLAS_SIZE: u32 = 2048;
#[derive(Clone, Copy)]
pub struct Glyph {
    pub rect: Rect,
    // Includes font face, glyph ID, size, subpixel bins and raster flags.
    pub key: CacheKey,
}
#[derive(Clone)]
pub struct TextLayout {
    pub glyphs: Vec<Glyph>,
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
    layouts: HashMap<(String, u32, u32, bool), TextLayout>,
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
        let key = (text.to_owned(), size.to_bits(), width.to_bits(), bold);
        if let Some(v) = self.layouts.get(&key) {
            self.hits += 1;
            return v.clone();
        }
        let mut buffer = Buffer::new(&mut self.fonts, Metrics::new(size, size * 1.35));
        buffer.set_size(&mut self.fonts, Some(width.max(1.)), None);
        buffer.set_text(
            &mut self.fonts,
            text,
            &Attrs::new().family(Family::SansSerif).weight(if bold {
                Weight::BOLD
            } else {
                Weight::NORMAL
            }),
            Shaping::Advanced,
        );
        buffer.shape_until_scroll(&mut self.fonts, false);
        let mut layout = TextLayout {
            glyphs: Vec::new(),
            width: 0.,
            height: 0.,
        };
        for run in buffer.layout_runs() {
            layout.width = layout.width.max(run.line_w);
            layout.height = layout.height.max(run.line_top + run.line_height);
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
        if self.layouts.len() >= 512 {
            self.layouts.clear();
        }
        self.layouts.insert(key, layout.clone());
        layout
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
}
