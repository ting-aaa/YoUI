use crate::Rect;
use cosmic_text::{
    Attrs, Buffer, CacheKey, Family, FontSystem, Metrics, Shaping, SwashCache, SwashContent, Weight,
};
use std::collections::HashMap;
pub const ATLAS_SIZE: u32 = 2048;
#[derive(Clone, Copy)]
pub struct Glyph {
    pub rect: Rect,
    pub uv: [f32; 4],
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
    left: i32,
    top: i32,
}
pub struct TextSystem {
    fonts: FontSystem,
    swash: SwashCache,
    glyphs: HashMap<CacheKey, AtlasEntry>,
    layouts: HashMap<(String, u32, u32, bool), TextLayout>,
    x: u32,
    y: u32,
    row_h: u32,
    pub texture: wgpu::Texture,
    pub hits: u32,
    images: HashMap<u32, AtlasEntry>,
    free_images: Vec<AtlasEntry>,
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
            fonts: FontSystem::new(),
            swash: SwashCache::new(),
            glyphs: HashMap::new(),
            layouts: HashMap::new(),
            x: 1,
            y: 1,
            row_h: 0,
            texture,
            hits: 0,
            images: HashMap::new(),
            free_images: Vec::new(),
        }
    }
    pub fn glyph_count(&self) -> u32 {
        self.glyphs.len() as u32
    }
    pub fn image_uv(&self, id: u32) -> Result<[f32; 4], String> {
        let e = self
            .images
            .get(&id)
            .ok_or("invalid, foreign or released image handle")?;
        let n = ATLAS_SIZE as f32;
        Ok([
            e.x as f32 / n,
            e.y as f32 / n,
            e.w as f32 / n,
            e.h as f32 / n,
        ])
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
            || w + 2 > ATLAS_SIZE
            || h + 2 > ATLAS_SIZE
            || rgba.len() != w as usize * h as usize * 4
        {
            return Err("invalid RGBA image dimensions/length".into());
        }
        let e = if let Some(index) = self.free_images.iter().position(|e| e.w == w && e.h == h) {
            self.free_images.swap_remove(index)
        } else {
            if self.x + w + 1 > ATLAS_SIZE {
                self.x = 1;
                self.y += self.row_h + 2;
                self.row_h = 0;
            }
            if self.y + h + 1 > ATLAS_SIZE {
                return Err("image atlas budget exhausted".into());
            }
            let e = AtlasEntry {
                x: self.x,
                y: self.y,
                w,
                h,
                left: 0,
                top: 0,
            };
            self.x += w + 2;
            self.row_h = self.row_h.max(h);
            e
        };
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
        self.images.insert(id, e);
        Ok(id)
    }
    pub fn release_image(&mut self, id: u32) -> Result<(), String> {
        let e = self
            .images
            .remove(&id)
            .ok_or("invalid or released image handle")?;
        self.free_images.push(e);
        Ok(())
    }
    pub fn layout(
        &mut self,
        queue: &wgpu::Queue,
        text: &str,
        size: f32,
        width: f32,
        bold: bool,
    ) -> Result<TextLayout, String> {
        let key = (text.to_owned(), size.to_bits(), width.to_bits(), bold);
        if let Some(v) = self.layouts.get(&key) {
            self.hits += 1;
            return Ok(v.clone());
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
                let entry = if let Some(e) = self.glyphs.get(&p.cache_key) {
                    *e
                } else {
                    let Some(img) = self.swash.get_image(&mut self.fonts, p.cache_key) else {
                        continue;
                    };
                    let w = img.placement.width;
                    let h = img.placement.height;
                    if w == 0 || h == 0 {
                        continue;
                    }
                    if self.x + w + 1 > ATLAS_SIZE {
                        self.x = 1;
                        self.y += self.row_h + 2;
                        self.row_h = 0;
                    }
                    if self.y + h + 1 > ATLAS_SIZE {
                        return Err("glyph atlas budget exhausted; recreate renderer or use fewer font sizes".into());
                    }
                    let e = AtlasEntry {
                        x: self.x,
                        y: self.y,
                        w,
                        h,
                        left: img.placement.left,
                        top: img.placement.top,
                    };
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
                    self.x += w + 2;
                    self.row_h = self.row_h.max(h);
                    self.glyphs.insert(p.cache_key, e);
                    e
                };
                let n = ATLAS_SIZE as f32;
                layout.glyphs.push(Glyph {
                    rect: Rect::new(
                        (p.x + entry.left) as f32,
                        run.line_y + p.y as f32 - entry.top as f32,
                        entry.w as f32,
                        entry.h as f32,
                    ),
                    uv: [
                        entry.x as f32 / n,
                        entry.y as f32 / n,
                        entry.w as f32 / n,
                        entry.h as f32 / n,
                    ],
                });
            }
        }
        if self.layouts.len() >= 512 {
            self.layouts.clear();
        }
        self.layouts.insert(key, layout.clone());
        Ok(layout)
    }
}
