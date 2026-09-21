use crate::{ir::validate, text::TextSystem, Command, FrameStats, Rect};
use bytemuck::{Pod, Zeroable};
use std::{ops::Range, time::Instant};
use wgpu::util::DeviceExt;

fn requested_backend(value: &str) -> Result<wgpu::Backends, String> {
    match value.trim().to_ascii_lowercase().as_str() {
        "" | "default" => Ok(if cfg!(target_os = "windows") {
            wgpu::Backends::DX12
        } else {
            wgpu::Backends::PRIMARY
        }),
        "dx12" => Ok(wgpu::Backends::DX12),
        "vulkan" => Ok(wgpu::Backends::VULKAN),
        "gl" | "opengl" => Ok(wgpu::Backends::GL),
        "metal" => Ok(wgpu::Backends::METAL),
        _ => Err(format!(
            "unknown YOUI_BACKEND {value:?}; use default, dx12, vulkan, gl or metal. \
             Windows GL uses WGL in this build; ANGLE requires a separate EGL build"
        )),
    }
}

#[cfg(test)]
mod backend_tests {
    use super::requested_backend;

    #[test]
    fn explicit_backend_is_strict_and_never_a_fallback_list() {
        for (name, expected) in [
            (" Vulkan ", wgpu::Backends::VULKAN),
            ("dx12", wgpu::Backends::DX12),
            ("OpenGL", wgpu::Backends::GL),
            ("metal", wgpu::Backends::METAL),
        ] {
            assert_eq!(requested_backend(name).unwrap(), expected);
        }
        for invalid in ["vulakn", "vulkan,dx12", "angle", "noop"] {
            assert!(requested_backend(invalid).is_err());
        }
    }
}

#[repr(C)]
#[derive(Clone, Copy, Pod, Zeroable)]
struct Instance {
    rect: Rect,
    color: [f32; 4],
    color2: [f32; 4],
    uv: [f32; 4],
    clip: Rect,
    params: [f32; 4],
}
impl Instance {
    fn new(
        rect: Rect,
        color: [f32; 4],
        color2: [f32; 4],
        clip: Rect,
        radius: f32,
        kind: f32,
        uv: [f32; 4],
    ) -> Self {
        Self {
            rect,
            color,
            color2,
            uv,
            clip,
            params: [radius, kind, 0., 0.],
        }
    }
}
struct Target {
    texture: wgpu::Texture,
    view: wgpu::TextureView,
    bind: wgpu::BindGroup,
}
struct Batch {
    range: Range<u32>,
    source: Option<usize>,
}
#[derive(Default)]
struct Pass {
    batches: Vec<Batch>,
}
impl Pass {
    fn add(&mut self, index: u32, source: Option<usize>) {
        if let Some(last) = self.batches.last_mut() {
            if last.source == source && last.range.end == index {
                last.range.end += 1;
                return;
            }
        }
        self.batches.push(Batch {
            range: index..index + 1,
            source,
        });
    }
}

/// Rendering is driven by the host. A window handle, when supplied, must remain
/// alive until this renderer is dropped. Offscreen rendering needs no window.
pub struct Renderer {
    device: wgpu::Device,
    queue: wgpu::Queue,
    surface: Option<wgpu::Surface<'static>>,
    surface_config: Option<wgpu::SurfaceConfiguration>,
    pipeline: wgpu::RenderPipeline,
    surface_pipeline: wgpu::RenderPipeline,
    layout: wgpu::BindGroupLayout,
    uniform: wgpu::Buffer,
    sampler: wgpu::Sampler,
    instances: Vec<Instance>,
    instance_buffer: wgpu::Buffer,
    capacity: usize,
    text: TextSystem,
    atlas_bind: wgpu::BindGroup,
    output: Target,
    layers: Vec<Target>,
    width: u32,
    height: u32,
    pub adapter_name: String,
    pub stats: FrameStats,
}
impl Renderer {
    /// # Safety
    /// A nonzero HWND must be valid, owned by this process, and outlive Self.
    pub unsafe fn new(width: u32, height: u32, hwnd: isize) -> Result<Self, String> {
        if width == 0 || height == 0 || width > 8192 || height > 8192 {
            return Err("target size outside 1..8192".into());
        }
        let trace = |stage: &str| {
            if std::env::var_os("YOUI_TRACE").is_some() {
                eprintln!("YoUI init: {stage}");
            }
        };
        trace("instance");
        // Select exactly one requested API: validation must never silently
        // substitute DX12 when Vulkan or GL initialization fails.
        let requested = std::env::var("YOUI_BACKEND").unwrap_or_default();
        let backends = requested_backend(&requested)?;
        let instance = wgpu::Instance::new(&wgpu::InstanceDescriptor {
            backends,
            ..Default::default()
        });
        let surface = if hwnd != 0 {
            #[cfg(target_os = "windows")]
            {
                use raw_window_handle::*;
                let mut window =
                    Win32WindowHandle::new(std::num::NonZeroIsize::new(hwnd).ok_or("null HWND")?);
                // Vulkan needs the instance belonging to this window's class,
                // which can differ from the renderer DLL's module instance.
                #[link(name = "user32")]
                unsafe extern "system" {
                    #[cfg_attr(target_pointer_width = "64", link_name = "GetWindowLongPtrW")]
                    #[cfg_attr(target_pointer_width = "32", link_name = "GetWindowLongW")]
                    fn window_instance(hwnd: isize, index: i32) -> isize;
                }
                window.hinstance = Some(
                    std::num::NonZeroIsize::new(window_instance(hwnd, -6))
                        .ok_or("could not obtain HWND HINSTANCE")?,
                );
                Some(
                    instance
                        .create_surface_unsafe(wgpu::SurfaceTargetUnsafe::RawHandle {
                            raw_display_handle: RawDisplayHandle::Windows(
                                WindowsDisplayHandle::new(),
                            ),
                            raw_window_handle: RawWindowHandle::Win32(window),
                        })
                        .map_err(|e| e.to_string())?,
                )
            }
            #[cfg(not(target_os = "windows"))]
            {
                return Err("this host adapter accepts Windows HWND only".into());
            }
        } else {
            None
        };
        trace("adapter");
        let adapter = pollster::block_on(instance.request_adapter(&wgpu::RequestAdapterOptions {
            power_preference: wgpu::PowerPreference::HighPerformance,
            compatible_surface: surface.as_ref(),
            force_fallback_adapter: false,
        }))
        .map_err(|e| e.to_string())?;
        let info = adapter.get_info();
        if !backends.contains(info.backend.into()) {
            return Err(format!(
                "requested {backends:?}, received {:?}",
                info.backend
            ));
        }
        let adapter_name = format!(
            "{} ({:?}; {:?}; driver {} {})",
            info.name, info.backend, info.device_type, info.driver, info.driver_info
        );
        trace(&adapter_name);
        trace("device");
        let (device, queue) = pollster::block_on(adapter.request_device(&wgpu::DeviceDescriptor {
            label: Some("YoUI"),
            required_features: wgpu::Features::empty(),
            required_limits: wgpu::Limits::default(),
            memory_hints: wgpu::MemoryHints::Performance,
            trace: wgpu::Trace::Off,
        }))
        .map_err(|e| e.to_string())?;
        let surface_config = surface.as_ref().map(|s| {
            let caps = s.get_capabilities(&adapter);
            let format = caps
                .formats
                .iter()
                .copied()
                .find(|f| f.is_srgb())
                .unwrap_or(caps.formats[0]);
            wgpu::SurfaceConfiguration {
                usage: wgpu::TextureUsages::RENDER_ATTACHMENT,
                format,
                width,
                height,
                present_mode: wgpu::PresentMode::Fifo,
                desired_maximum_frame_latency: 2,
                alpha_mode: caps.alpha_modes[0],
                view_formats: vec![],
            }
        });
        if let (Some(s), Some(c)) = (&surface, &surface_config) {
            s.configure(&device, c);
        }
        let uniform = device.create_buffer_init(&wgpu::util::BufferInitDescriptor {
            label: Some("viewport"),
            contents: bytemuck::cast_slice(&[width as f32, height as f32, 0., 0.]),
            usage: wgpu::BufferUsages::UNIFORM | wgpu::BufferUsages::COPY_DST,
        });
        let sampler = device.create_sampler(&wgpu::SamplerDescriptor {
            label: Some("linear clamp"),
            mag_filter: wgpu::FilterMode::Linear,
            min_filter: wgpu::FilterMode::Linear,
            ..Default::default()
        });
        let layout = device.create_bind_group_layout(&wgpu::BindGroupLayoutDescriptor {
            label: Some("2D resources"),
            entries: &[
                wgpu::BindGroupLayoutEntry {
                    binding: 0,
                    visibility: wgpu::ShaderStages::VERTEX,
                    ty: wgpu::BindingType::Buffer {
                        ty: wgpu::BufferBindingType::Uniform,
                        has_dynamic_offset: false,
                        min_binding_size: None,
                    },
                    count: None,
                },
                wgpu::BindGroupLayoutEntry {
                    binding: 1,
                    visibility: wgpu::ShaderStages::FRAGMENT,
                    ty: wgpu::BindingType::Texture {
                        sample_type: wgpu::TextureSampleType::Float { filterable: true },
                        view_dimension: wgpu::TextureViewDimension::D2,
                        multisampled: false,
                    },
                    count: None,
                },
                wgpu::BindGroupLayoutEntry {
                    binding: 2,
                    visibility: wgpu::ShaderStages::FRAGMENT,
                    ty: wgpu::BindingType::Sampler(wgpu::SamplerBindingType::Filtering),
                    count: None,
                },
            ],
        });
        trace("pipeline");
        let pipeline = Self::make_pipeline(&device, &layout, wgpu::TextureFormat::Rgba8UnormSrgb);
        let surface_pipeline = Self::make_pipeline(
            &device,
            &layout,
            surface_config
                .as_ref()
                .map_or(wgpu::TextureFormat::Rgba8UnormSrgb, |s| s.format),
        );
        trace("fonts");
        let text = TextSystem::new(&device);
        trace("ready");
        let atlas_bind = Self::bind(
            &device,
            &layout,
            &uniform,
            &sampler,
            &text.texture.create_view(&Default::default()),
        );
        let output = Self::target(&device, &layout, &uniform, &sampler, width, height);
        let capacity = 1024;
        let instance_buffer = Self::buffer(&device, capacity);
        Ok(Self {
            device,
            queue,
            surface,
            surface_config,
            pipeline,
            surface_pipeline,
            layout,
            uniform,
            sampler,
            instances: Vec::with_capacity(capacity),
            instance_buffer,
            capacity,
            text,
            atlas_bind,
            output,
            layers: vec![],
            width,
            height,
            adapter_name,
            stats: FrameStats::default(),
        })
    }
    fn buffer(device: &wgpu::Device, capacity: usize) -> wgpu::Buffer {
        device.create_buffer(&wgpu::BufferDescriptor {
            label: Some("reusable quad instances"),
            size: (capacity * std::mem::size_of::<Instance>()) as u64,
            usage: wgpu::BufferUsages::VERTEX | wgpu::BufferUsages::COPY_DST,
            mapped_at_creation: false,
        })
    }
    fn bind(
        d: &wgpu::Device,
        l: &wgpu::BindGroupLayout,
        u: &wgpu::Buffer,
        s: &wgpu::Sampler,
        v: &wgpu::TextureView,
    ) -> wgpu::BindGroup {
        d.create_bind_group(&wgpu::BindGroupDescriptor {
            label: Some("2D bind"),
            layout: l,
            entries: &[
                wgpu::BindGroupEntry {
                    binding: 0,
                    resource: u.as_entire_binding(),
                },
                wgpu::BindGroupEntry {
                    binding: 1,
                    resource: wgpu::BindingResource::TextureView(v),
                },
                wgpu::BindGroupEntry {
                    binding: 2,
                    resource: wgpu::BindingResource::Sampler(s),
                },
            ],
        })
    }
    fn target(
        d: &wgpu::Device,
        l: &wgpu::BindGroupLayout,
        u: &wgpu::Buffer,
        s: &wgpu::Sampler,
        w: u32,
        h: u32,
    ) -> Target {
        let texture = d.create_texture(&wgpu::TextureDescriptor {
            label: Some("pooled isolated layer"),
            size: wgpu::Extent3d {
                width: w,
                height: h,
                depth_or_array_layers: 1,
            },
            mip_level_count: 1,
            sample_count: 1,
            dimension: wgpu::TextureDimension::D2,
            format: wgpu::TextureFormat::Rgba8UnormSrgb,
            usage: wgpu::TextureUsages::RENDER_ATTACHMENT
                | wgpu::TextureUsages::TEXTURE_BINDING
                | wgpu::TextureUsages::COPY_SRC,
            view_formats: &[],
        });
        let view = texture.create_view(&Default::default());
        let bind = Self::bind(d, l, u, s, &view);
        Target {
            texture,
            view,
            bind,
        }
    }
    fn make_pipeline(
        d: &wgpu::Device,
        l: &wgpu::BindGroupLayout,
        format: wgpu::TextureFormat,
    ) -> wgpu::RenderPipeline {
        let shader = d.create_shader_module(wgpu::ShaderModuleDescriptor {
            label: Some("analytic rounded rect and glyph"),
            source: wgpu::ShaderSource::Wgsl(include_str!("quad.wgsl").into()),
        });
        let layout = d.create_pipeline_layout(&wgpu::PipelineLayoutDescriptor {
            label: None,
            bind_group_layouts: &[l],
            push_constant_ranges: &[],
        });
        d.create_render_pipeline(&wgpu::RenderPipelineDescriptor{label:Some("ordered premultiplied 2D"),layout:Some(&layout),vertex:wgpu::VertexState{module:&shader,entry_point:Some("vs"),compilation_options:Default::default(),buffers:&[wgpu::VertexBufferLayout{array_stride:96,step_mode:wgpu::VertexStepMode::Instance,attributes:&wgpu::vertex_attr_array![0=>Float32x4,1=>Float32x4,2=>Float32x4,3=>Float32x4,4=>Float32x4,5=>Float32x4]}]},primitive:Default::default(),depth_stencil:None,multisample:Default::default(),fragment:Some(wgpu::FragmentState{module:&shader,entry_point:Some("fs"),compilation_options:Default::default(),targets:&[Some(wgpu::ColorTargetState{format,blend:Some(wgpu::BlendState::PREMULTIPLIED_ALPHA_BLENDING),write_mask:wgpu::ColorWrites::ALL})]}),multiview:None,cache:None})
    }
    pub fn resize(&mut self, width: u32, height: u32) -> Result<(), String> {
        if width == 0 || height == 0 || width > 8192 || height > 8192 {
            return Err("target size outside 1..8192".into());
        }
        if (width, height) == (self.width, self.height) {
            return Ok(());
        }
        self.width = width;
        self.height = height;
        self.layers.clear();
        self.output = Self::target(
            &self.device,
            &self.layout,
            &self.uniform,
            &self.sampler,
            width,
            height,
        );
        self.queue.write_buffer(
            &self.uniform,
            0,
            bytemuck::cast_slice(&[width as f32, height as f32, 0., 0.]),
        );
        if let (Some(s), Some(c)) = (&self.surface, &mut self.surface_config) {
            c.width = width;
            c.height = height;
            s.configure(&self.device, c);
        }
        Ok(())
    }
    pub fn measure(
        &mut self,
        text: &str,
        size: f32,
        width: f32,
        bold: bool,
    ) -> Result<(f32, f32), String> {
        if !size.is_finite() || !(1.0..=512.0).contains(&size) || !width.is_finite() || width < 0. {
            return Err("invalid text measurement".into());
        }
        let l = self.text.layout(&self.queue, text, size, width, bold)?;
        Ok((l.width, l.height))
    }
    pub fn upload_image(&mut self, width: u32, height: u32, rgba: &[u8]) -> Result<u32, String> {
        self.text.upload_image(&self.queue, width, height, rgba)
    }
    pub fn release_image(&mut self, id: u32) -> Result<(), String> {
        self.text.release_image(id)
    }
    pub fn render(&mut self, commands: &[Command], text: &[u8]) -> Result<FrameStats, String> {
        validate(commands, text)?;
        for c in commands {
            if c.kind == 7 {
                self.text.image_uv(c.flags)?;
            }
        }
        let start = Instant::now();
        self.instances.clear();
        self.text.hits = 0;
        let viewport = Rect::new(0., 0., self.width as f32, self.height as f32);
        let mut clips = vec![viewport];
        let mut passes = vec![Pass::default()];
        let mut stack = vec![0usize];
        let mut opacities = Vec::new();
        for c in commands {
            let current = *stack.last().unwrap();
            let clip = *clips.last().unwrap();
            match c.kind {
                0 => {
                    if c.rect.intersect(clip).w <= 0. || c.rect.intersect(clip).h <= 0. {
                        continue;
                    }
                    passes[current].add(self.instances.len() as u32, None);
                    self.instances.push(Instance::new(
                        c.rect, c.color, c.color2, clip, c.radius, 0., [0.; 4],
                    ));
                }
                1 => {
                    let s = std::str::from_utf8(
                        &text[c.text_offset as usize..(c.text_offset + c.text_length) as usize],
                    )
                    .map_err(|e| e.to_string())?;
                    let layout = self.text.layout(
                        &self.queue,
                        s,
                        c.font_size,
                        c.rect.w,
                        c.flags & 1 != 0,
                    )?;
                    for g in layout.glyphs {
                        let rect =
                            Rect::new(g.rect.x + c.rect.x, g.rect.y + c.rect.y, g.rect.w, g.rect.h);
                        let clip = clip.intersect(c.rect);
                        if rect.intersect(clip).w <= 0. || rect.intersect(clip).h <= 0. {
                            continue;
                        }
                        passes[current].add(self.instances.len() as u32, None);
                        self.instances
                            .push(Instance::new(rect, c.color, c.color, clip, 0., 1., g.uv));
                    }
                }
                2 => clips.push(clip.intersect(c.rect)),
                3 => {
                    clips.pop();
                }
                4 => {
                    // Bound total memory, not only nesting depth: 64 Mi pixels across layers.
                    if passes.len() > 16
                        || passes.len() as u64 * self.width as u64 * self.height as u64
                            > 64 * 1024 * 1024
                    {
                        return Err("offscreen layer budget exceeded".into());
                    }
                    stack.push(passes.len());
                    passes.push(Pass::default());
                    opacities.push((c.color[3], clip));
                }
                5 => {
                    let child = stack.pop().unwrap();
                    let parent = *stack.last().unwrap();
                    let (alpha, layer_clip) = opacities.pop().unwrap();
                    passes[parent].add(self.instances.len() as u32, Some(child - 1));
                    self.instances.push(Instance::new(
                        viewport,
                        [1., 1., 1., alpha],
                        [1.; 4],
                        layer_clip,
                        0.,
                        2.,
                        [0., 0., 1., 1.],
                    ));
                }
                6 => {
                    passes[current].add(self.instances.len() as u32, None);
                    let mut item =
                        Instance::new(c.rect, c.color, c.color, clip, c.reserved[0], 3., [0.; 4]);
                    item.params[2] = c.reserved[1];
                    self.instances.push(item);
                }
                7 => {
                    passes[current].add(self.instances.len() as u32, None);
                    self.instances.push(Instance::new(
                        c.rect,
                        c.color,
                        c.color,
                        clip,
                        0.,
                        1.,
                        self.text.image_uv(c.flags)?,
                    ));
                }
                _ => unreachable!(),
            }
        }
        while self.layers.len() + 1 < passes.len() {
            self.layers.push(Self::target(
                &self.device,
                &self.layout,
                &self.uniform,
                &self.sampler,
                self.width,
                self.height,
            ));
        }
        let present_index = self.instances.len() as u32;
        self.instances.push(Instance::new(
            viewport,
            [1.; 4],
            [1.; 4],
            viewport,
            0.,
            2.,
            [0., 0., 1., 1.],
        ));
        if self.instances.len() > self.capacity {
            self.capacity = self.instances.len().next_power_of_two();
            self.instance_buffer = Self::buffer(&self.device, self.capacity);
        }
        let bytes = bytemuck::cast_slice(&self.instances);
        self.queue.write_buffer(&self.instance_buffer, 0, bytes);
        let prepare_us = start.elapsed().as_micros() as u64;
        let submit = Instant::now();
        let mut encoder = self
            .device
            .create_command_encoder(&wgpu::CommandEncoderDescriptor {
                label: Some("YoUI frame"),
            });
        // Child resources are complete before the parent's ordered composition reads them.
        for index in (0..passes.len()).rev() {
            let view = if index == 0 {
                &self.output.view
            } else {
                &self.layers[index - 1].view
            };
            let attachments = [Some(wgpu::RenderPassColorAttachment {
                view,
                depth_slice: None,
                resolve_target: None,
                ops: wgpu::Operations {
                    load: wgpu::LoadOp::Clear(wgpu::Color::TRANSPARENT),
                    store: wgpu::StoreOp::Store,
                },
            })];
            let mut pass = encoder.begin_render_pass(&wgpu::RenderPassDescriptor {
                label: Some("isolated composition"),
                color_attachments: &attachments,
                depth_stencil_attachment: None,
                timestamp_writes: None,
                occlusion_query_set: None,
            });
            pass.set_pipeline(&self.pipeline);
            pass.set_vertex_buffer(0, self.instance_buffer.slice(..));
            for batch in &passes[index].batches {
                pass.set_bind_group(
                    0,
                    match batch.source {
                        None => &self.atlas_bind,
                        Some(n) => &self.layers[n].bind,
                    },
                    &[],
                );
                pass.draw(0..6, batch.range.clone());
            }
        }
        let mut frame = None;
        if let Some(surface) = &self.surface {
            let acquired = match surface.get_current_texture() {
                Ok(f) => Ok(f),
                Err(wgpu::SurfaceError::Lost | wgpu::SurfaceError::Outdated) => {
                    surface.configure(&self.device, self.surface_config.as_ref().unwrap());
                    surface.get_current_texture()
                }
                Err(e) => Err(e),
            };
            match acquired {
                Ok(f) => {
                    let view = f.texture.create_view(&Default::default());
                    let attachments = [Some(wgpu::RenderPassColorAttachment {
                        view: &view,
                        depth_slice: None,
                        resolve_target: None,
                        ops: wgpu::Operations {
                            load: wgpu::LoadOp::Clear(wgpu::Color::BLACK),
                            store: wgpu::StoreOp::Store,
                        },
                    })];
                    {
                        let mut pass = encoder.begin_render_pass(&wgpu::RenderPassDescriptor {
                            label: Some("present"),
                            color_attachments: &attachments,
                            depth_stencil_attachment: None,
                            timestamp_writes: None,
                            occlusion_query_set: None,
                        });
                        pass.set_pipeline(&self.surface_pipeline);
                        pass.set_vertex_buffer(0, self.instance_buffer.slice(..));
                        pass.set_bind_group(0, &self.output.bind, &[]);
                        pass.draw(0..6, present_index..present_index + 1);
                    }
                    frame = Some(f);
                }
                Err(wgpu::SurfaceError::Timeout) => {}
                Err(e) => return Err(format!("surface: {e}")),
            }
        }
        self.queue.submit(Some(encoder.finish()));
        let presented = frame.is_some();
        if let Some(f) = frame {
            f.present();
        }
        self.stats = FrameStats {
            commands: commands.len() as u32,
            instances: present_index,
            draw_calls: passes.iter().map(|p| p.batches.len() as u32).sum::<u32>()
                + u32::from(presented),
            layers: passes.len() as u32 - 1,
            upload_bytes: bytes.len() as u64,
            prepare_us,
            submit_us: submit.elapsed().as_micros() as u64,
            atlas_glyphs: self.text.glyph_count(),
            text_cache_hits: self.text.hits,
        };
        Ok(self.stats)
    }
    /// Synchronous readback, intended for exports and tests rather than the frame loop.
    pub fn read_pixels(&self) -> Result<Vec<u8>, String> {
        let stride = (self.width * 4).div_ceil(256) * 256;
        let buffer = self.device.create_buffer(&wgpu::BufferDescriptor {
            label: Some("screenshot readback"),
            size: stride as u64 * self.height as u64,
            usage: wgpu::BufferUsages::COPY_DST | wgpu::BufferUsages::MAP_READ,
            mapped_at_creation: false,
        });
        let mut encoder = self.device.create_command_encoder(&Default::default());
        encoder.copy_texture_to_buffer(
            wgpu::TexelCopyTextureInfo {
                texture: &self.output.texture,
                mip_level: 0,
                origin: wgpu::Origin3d::ZERO,
                aspect: wgpu::TextureAspect::All,
            },
            wgpu::TexelCopyBufferInfo {
                buffer: &buffer,
                layout: wgpu::TexelCopyBufferLayout {
                    offset: 0,
                    bytes_per_row: Some(stride),
                    rows_per_image: Some(self.height),
                },
            },
            wgpu::Extent3d {
                width: self.width,
                height: self.height,
                depth_or_array_layers: 1,
            },
        );
        self.queue.submit(Some(encoder.finish()));
        let (tx, rx) = std::sync::mpsc::channel();
        buffer.slice(..).map_async(wgpu::MapMode::Read, move |r| {
            let _ = tx.send(r);
        });
        self.device
            .poll(wgpu::PollType::Wait)
            .map_err(|e| e.to_string())?;
        rx.recv()
            .map_err(|e| e.to_string())?
            .map_err(|e| e.to_string())?;
        let mapped = buffer.slice(..).get_mapped_range();
        let mut result = Vec::with_capacity((self.width * self.height * 4) as usize);
        for row in mapped.chunks_exact(stride as usize) {
            result.extend_from_slice(&row[..(self.width * 4) as usize]);
        }
        drop(mapped);
        buffer.unmap();
        Ok(result)
    }
    pub fn save_png(&self, path: &str) -> Result<(), String> {
        let file = std::fs::File::create(path).map_err(|e| e.to_string())?;
        let mut encoder = png::Encoder::new(file, self.width, self.height);
        encoder.set_color(png::ColorType::Rgba);
        encoder.set_depth(png::BitDepth::Eight);
        encoder
            .write_header()
            .map_err(|e| e.to_string())?
            .write_image_data(&self.read_pixels()?)
            .map_err(|e| e.to_string())
    }
}
