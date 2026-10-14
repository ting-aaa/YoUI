use super::*;
use cosmic_text::SwashContent;

fn srgb_byte(linear: f32) -> i32 {
    let value = if linear <= 0.0031308 {
        linear * 12.92
    } else {
        1.055 * linear.powf(1. / 2.4) - 0.055
    };
    (value * 255.).round() as i32
}

// A white mask writes identical linear RGB and alpha before output encoding.
// Alpha is UNORM8, so its stored byte represents a half-byte interval of
// possible linear values; RGB is sRGB8 and must encode that same interval.
// Allow one final sRGB conversion byte, not an arbitrary wider RGB threshold.
// The raw-raster alpha oracle is still checked separately: these bounds alone
// must not excuse missing or extra coverage. D3D11.3 specification sections
// 3.2.3.6/8 (UNORM/sRGB conversion), 7.18.16.1/2 (subtexel/filter precision):
// https://microsoft.github.io/DirectX-Specs/d3d/archive/D3D11_3_FunctionalSpec.htm
fn white_mask_rgb_bounds(stored_alpha: u8) -> (i32, i32) {
    let low = (stored_alpha as f32 - 0.5).max(0.) / 255.;
    let high = (stored_alpha as f32 + 0.5).min(255.) / 255.;
    ((srgb_byte(low) - 1).max(0), (srgb_byte(high) + 1).min(255))
}

fn white_mask_rgb_matches_alpha(pixel: &[u8]) -> bool {
    let (low, high) = white_mask_rgb_bounds(pixel[3]);
    pixel[..3]
        .iter()
        .all(|&c| (low..=high).contains(&(c as i32)))
}

fn white_mask_rgb_matches_coverage(pixel: &[u8], coverage: f32, contributors: u32) -> bool {
    if white_mask_rgb_matches_alpha(pixel) {
        return true;
    }
    // With overlapping glyphs, alpha is rounded to UNORM8 after each blend,
    // while RGB follows its separate sRGB8 encode/decode history. The final
    // alpha byte can no longer describe RGB's single half-byte interval.
    // Keep the original float-oracle RGB tolerance for these pixels only;
    // do not apply it to the isolated low-coverage sampler fixture.
    let ideal = srgb_byte(coverage);
    contributors > 1 && pixel[..3].iter().all(|&c| (c as i32 - ideal).abs() <= 3)
}

#[test]
fn white_mask_rgb_interval_rejects_squared_coverage() {
    assert!(white_mask_rgb_matches_alpha(&[13, 13, 13, 1]));
    assert!(white_mask_rgb_matches_alpha(&[8, 8, 8, 1]));
    assert!(!white_mask_rgb_matches_alpha(&[13, 13, 13, 0]));
    for alpha in [1, 16, 64, 128, 192] {
        let linear = alpha as f32 / 255.;
        let correct = srgb_byte(linear) as u8;
        assert!(white_mask_rgb_matches_alpha(&[
            correct, correct, correct, alpha
        ]));
        let squared = srgb_byte(linear * linear) as u8;
        assert!(
            !white_mask_rgb_matches_alpha(&[squared, squared, squared, alpha]),
            "black gutters multiplying mask coverage twice must fail at alpha={alpha}"
        );
        assert!(!white_mask_rgb_matches_coverage(
            &[squared, squared, squared, alpha],
            linear,
            2
        ));
    }
    assert!(white_mask_rgb_matches_coverage(
        &[15, 15, 15, 2],
        0.004915891,
        2
    ));
    assert!(white_mask_rgb_matches_coverage(
        &[20, 20, 20, 1],
        0.007298441,
        2
    ));
    assert!(!white_mask_rgb_matches_coverage(
        &[15, 15, 15, 2],
        0.004915891,
        1
    ));
    assert!(!white_mask_rgb_matches_coverage(
        &[20, 20, 20, 1],
        0.007298441,
        1
    ));
}

// Read the sampler's f32 result before blending or RGBA8/sRGB output
// conversion. This distinguishes source-format filtering quantization from
// render-target conversion and keeps the tiny-mask diagnosis falsifiable.
const PROBE_PHASE_COUNT: usize = 6;

fn sampler_precision_probe(renderer: &mut Renderer) -> Result<Vec<f32>, String> {
    let image = renderer.upload_image(1, 1, &[255; 4])?;
    let uv = renderer.text.image_uv(image)?;
    let shader = renderer
        .device
        .create_shader_module(wgpu::ShaderModuleDescriptor {
            label: Some("UNORM8 alpha filtering probe"),
            source: wgpu::ShaderSource::Wgsl(
                format!(
                    r#"
@group(0) @binding(0) var atlas: texture_2d<f32>;
@group(0) @binding(1) var tex_sampler: sampler;
@group(0) @binding(2) var<storage, read_write> results: array<f32>;
@compute @workgroup_size(1) fn main() {{
    // The last phases are not on an 8-bit subtexel grid. The high-contrast
    // 255-to-0 mask ideally yields just 0.625/255 and 0.25/255 there.
    let phases = array<f32, 6>(0., 0.25, 0.375, 0.5, 1. - 0.625/255., 1. - 0.25/255.);
    let origin = vec2({x}, {y});
    let dimensions = vec2<f32>(textureDimensions(atlas));
    let texel = vec2<i32>(origin * dimensions);
    let left = textureLoad(atlas, texel, 0).a;
    let right = textureLoad(atlas, texel + vec2(1, 0), 0).a;
    for (var index = 0u; index < 6u; index++) {{
        let uv = origin + vec2(0.5 + phases[index], 0.5) / dimensions;
        results[index] = textureSampleLevel(atlas, tex_sampler, uv, 0.).a;
        results[6u + index] = mix(left, right, phases[index]);
    }}
}}
"#,
                    x = uv[0],
                    y = uv[1]
                )
                .into(),
            ),
        });
    let pipeline = renderer
        .device
        .create_compute_pipeline(&wgpu::ComputePipelineDescriptor {
            label: Some("UNORM8 alpha filtering probe"),
            layout: None,
            module: &shader,
            entry_point: Some("main"),
            compilation_options: Default::default(),
            cache: None,
        });
    let buffer = |usage| {
        renderer.device.create_buffer(&wgpu::BufferDescriptor {
            label: Some("filter precision probe"),
            size: 48,
            usage,
            mapped_at_creation: false,
        })
    };
    let result_buffer = buffer(wgpu::BufferUsages::STORAGE | wgpu::BufferUsages::COPY_SRC);
    let readback = buffer(wgpu::BufferUsages::MAP_READ | wgpu::BufferUsages::COPY_DST);
    let view = renderer.text.texture.create_view(&Default::default());
    let bind = renderer
        .device
        .create_bind_group(&wgpu::BindGroupDescriptor {
            label: Some("UNORM8 alpha filtering probe"),
            layout: &pipeline.get_bind_group_layout(0),
            entries: &[
                (0, wgpu::BindingResource::TextureView(&view)),
                (1, wgpu::BindingResource::Sampler(&renderer.sampler)),
                (2, result_buffer.as_entire_binding()),
            ]
            .map(|(binding, resource)| wgpu::BindGroupEntry { binding, resource }),
        });
    let mut encoder = renderer.device.create_command_encoder(&Default::default());
    {
        let mut pass = encoder.begin_compute_pass(&Default::default());
        pass.set_pipeline(&pipeline);
        pass.set_bind_group(0, &bind, &[]);
        pass.dispatch_workgroups(1, 1, 1);
    }
    encoder.copy_buffer_to_buffer(&result_buffer, 0, &readback, 0, 48);
    renderer.queue.submit(Some(encoder.finish()));
    let (sender, receiver) = std::sync::mpsc::channel();
    readback
        .slice(..)
        .map_async(wgpu::MapMode::Read, move |status| {
            let _ = sender.send(status.map_err(|e| e.to_string()));
        });
    wait_readback(receiver, std::time::Duration::from_secs(10), || {
        renderer.check_device()
    })?;
    let mapped = readback.slice(..).get_mapped_range();
    let result = bytemuck::cast_slice::<u8, f32>(&mapped).to_vec();
    drop(mapped);
    readback.unmap();
    renderer.release_image(image)?;
    Ok(result)
}

// Independent CPU oracle: integrate the raw raster mask with bilinear filtering
// over its entire support, then apply the public text/ancestor clip contracts.
// In particular this does not reuse the GPU quad or atlas UV calculations.
fn reference_alpha(renderer: &mut Renderer, command: &Command, text: &str, clip: Rect) -> Vec<f32> {
    reference_mask(renderer, command, text, clip).0
}

fn reference_mask(
    renderer: &mut Renderer,
    command: &Command,
    text: &str,
    clip: Rect,
) -> (Vec<f32>, Vec<u32>) {
    let mut alpha = vec![0.; (renderer.width * renderer.height) as usize];
    let mut contributors = vec![0; alpha.len()];
    let layout = renderer.text.layout_with_wrap(
        text,
        command.font_size,
        command.rect.w,
        command.flags & 1 != 0,
        command.flags & 2 == 0,
    );
    assert!(
        !layout.glyphs.is_empty(),
        "test requires a font with Latin glyphs"
    );
    for glyph in layout.glyphs {
        let image = renderer.text.raster_image(&glyph);
        assert_eq!(
            image.content,
            SwashContent::Mask,
            "Latin fixture must use coverage masks"
        );
        let sample = |x: i32, y: i32| {
            if x < 0
                || y < 0
                || x >= image.placement.width as i32
                || y >= image.placement.height as i32
            {
                0.
            } else {
                image.data[(y as u32 * image.placement.width + x as u32) as usize] as f32 / 255.
            }
        };
        for y in 0..renderer.height {
            for x in 0..renderer.width {
                let px = x as f32 + 0.5;
                let py = y as f32 + 0.5;
                if px < clip.x || py < clip.y || px >= clip.x + clip.w || py >= clip.y + clip.h {
                    continue;
                }
                let sx = x as f32 - command.rect.x - glyph.rect.x;
                let sy = y as f32 - command.rect.y - glyph.rect.y;
                let ix = sx.floor() as i32;
                let iy = sy.floor() as i32;
                let fx = sx - ix as f32;
                let fy = sy - iy as f32;
                let coverage = sample(ix, iy) * (1. - fx) * (1. - fy)
                    + sample(ix + 1, iy) * fx * (1. - fy)
                    + sample(ix, iy + 1) * (1. - fx) * fy
                    + sample(ix + 1, iy + 1) * fx * fy;
                let index = (y * renderer.width + x) as usize;
                contributors[index] += u32::from(coverage > 0.);
                let pixel = &mut alpha[index];
                *pixel = coverage + *pixel * (1. - coverage);
            }
        }
    }
    (alpha, contributors)
}

#[test]
#[ignore = "requires a GPU adapter and a font with Latin glyphs; run explicitly"]
fn fractional_text_preserves_raster_filter_support_and_explicit_clips() -> Result<(), String> {
    let mut renderer = unsafe { Renderer::new(256, 80, 0)? };
    // Match the Linux acceptance screenshot where available. Assertions derive
    // from the actual selected font's raster, so fallback is also supported.
    renderer.text.set_test_font_family("DejaVu Sans");
    let viewport = Rect::new(0., 0., 256., 80.);
    let mut failures = Vec::new();
    for (text, size, bold) in [
        ("prototype", 26., true),
        ("Clicked 2 times", 14., false),
        ("jÁÉÅg", 17.5, false),
    ] {
        for step in 0..8 {
            let x = 16. + step as f32 / 8.;
            let y = 16. + ((step * 3) % 8) as f32 / 8.;
            for mode in 0..3 {
                let rect = if mode == 1 {
                    Rect::new(x, y, 72., 12.)
                } else {
                    Rect::new(x, y, 224., size * 1.35)
                };
                let mut command = Command::text(rect, [1.; 4], size, 0, text.len() as u32);
                command.flags = u32::from(bold) | 2 | if mode == 0 { 4 } else { 0 };
                let ancestor = if mode == 2 {
                    Rect::new(x + 2.25, y + 8.25, 65., 11.5)
                } else {
                    viewport
                };
                let clip = if command.flags & 4 == 0 {
                    ancestor.intersect(rect)
                } else {
                    ancestor
                };
                let (expected, contributors) = reference_mask(&mut renderer, &command, text, clip);
                renderer.render(
                    &[
                        Command {
                            kind: 2,
                            rect: ancestor,
                            ..Default::default()
                        },
                        command,
                        Command {
                            kind: 3,
                            ..Default::default()
                        },
                    ],
                    text.as_bytes(),
                )?;
                let pixels = renderer.read_pixels()?;
                let mut worst = 0;
                let mut lost = 0;
                let mut worst_rgb = 0;
                let mut rgb_failures = 0;
                let mut first_rgb_failure = None;
                for (index, (pixel, alpha)) in pixels.chunks_exact(4).zip(expected).enumerate() {
                    let target = (alpha * 255.).round() as i32;
                    worst = worst.max((pixel[3] as i32 - target).abs());
                    // White mask RGB must not receive a second multiplication
                    // from black padding when coverage is bilinearly filtered.
                    let rgb = srgb_byte(alpha);
                    let error = pixel[..3]
                        .iter()
                        .map(|&v| (v as i32 - rgb).abs())
                        .max()
                        .unwrap();
                    worst_rgb = worst_rgb.max(error);
                    if !white_mask_rgb_matches_coverage(pixel, alpha, contributors[index]) {
                        rgb_failures += 1;
                        first_rgb_failure.get_or_insert_with(|| format!(
                            "pixel=({}, {}) actual={pixel:?} coverage={alpha:.9} alphaBytes={:.6} expectedAlpha={target} idealRgb={rgb} contributors={} storedAlphaRgbBounds={:?}",
                            index % renderer.width as usize, index / renderer.width as usize,
                            alpha * 255., contributors[index], white_mask_rgb_bounds(pixel[3])
                        ));
                    }
                    if target > 2 && pixel[3] == 0 {
                        lost += 1;
                    }
                }
                if worst > 2 || rgb_failures > 0 {
                    failures.push(format!("{text:?} size={size} step={step} clip={mode}: max alpha error={worst}, fully lost pixels={lost}, max RGB error={worst_rgb}, RGB failures={rgb_failures}, first={first_rgb_failure:?}"));
                }
            }
        }
    }
    assert!(
        failures.is_empty(),
        "raw-mask versus GPU differences:\n{}",
        failures.join("\n")
    );
    println!(
        "72 fractional text/clip cases match raw raster masks on {}",
        renderer.adapter_name
    );
    Ok(())
}

#[test]
#[ignore = "requires a GPU adapter and a font with Latin glyphs; run explicitly"]
fn fractional_glyph_gutters_reject_recycled_white_texels() -> Result<(), String> {
    let mut recycled = unsafe { Renderer::new(96, 64, 0)? };
    let mut fresh = unsafe { Renderer::new(96, 64, 0)? };
    let image = recycled.upload_image(2046, 2046, &vec![255; 2046 * 2046 * 4])?;
    recycled.release_image(image)?;
    let mut covered_halo = false;
    for step in 0..8 {
        let mut command = Command::text(
            Rect::new(
                16. + step as f32 / 8.,
                12. + ((step * 3) % 8) as f32 / 8.,
                80.,
                40.,
            ),
            [1.; 4],
            26.,
            0,
            1,
        );
        command.flags = 2 | 4;
        let expected = reference_alpha(&mut fresh, &command, "H", Rect::new(0., 0., 96., 64.));
        recycled.render(&[command], b"H")?;
        fresh.render(&[command], b"H")?;
        let actual = recycled.read_pixels()?;
        assert_eq!(
            actual,
            fresh.read_pixels()?,
            "recycled white image contaminated glyph phase {step}"
        );
        for (index, (pixel, coverage)) in actual.chunks_exact(4).zip(expected).enumerate() {
            if coverage == 0. {
                assert_eq!(
                    pixel,
                    [0; 4],
                    "padding pollution at phase={step}, pixel=({}, {})",
                    index % 96,
                    index / 96
                );
            } else {
                covered_halo |= coverage < 1. && pixel[3] > 0;
            }
        }
    }
    assert!(
        covered_halo,
        "clamping must preserve fractional filtered coverage"
    );
    Ok(())
}

#[test]
#[ignore = "requires a GPU adapter and a font with Latin glyphs; run explicitly"]
fn tiny_mask_coverage_reports_fixed_point_filter_precision() -> Result<(), String> {
    let mut renderer = unsafe { Renderer::new(32, 32, 0)? };
    let probe = sampler_precision_probe(&mut renderer)?;
    println!(
        "UNORM8 alpha sampler f32 values on {}, mask=255: hardware={:?}, float-bilinear={:?}",
        renderer.adapter_name,
        &probe[..PROBE_PHASE_COUNT],
        &probe[PROBE_PHASE_COUNT..]
    );
    for (hardware, ideal) in probe[..PROBE_PHASE_COUNT]
        .iter()
        .zip(&probe[PROBE_PHASE_COUNT..])
    {
        assert!(
            (hardware - ideal).abs() <= 1. / 255.,
            "sampler exceeded UNORM8/subtexel precision: hardware={hardware}, ideal={ideal}"
        );
    }
    let glyph = *renderer
        .text
        .layout_with_wrap("H", 18., 64., false, false)
        .glyphs
        .first()
        .ok_or("test requires a Latin font")?;
    let mut samples = Vec::new();
    for value in [1, 2, 16, 128, 255] {
        renderer.purge_glyph_residency();
        let mut raster = renderer.text.raster_image(&glyph);
        raster.content = SwashContent::Mask;
        raster.placement.width = 1;
        raster.placement.height = 1;
        raster.placement.left = 0;
        raster.placement.top = 1;
        raster.data = vec![value];
        renderer.text.set_test_raster(&glyph, raster);
        let ink = renderer
            .text
            .layout_with_wrap("H", 18., 64., false, false)
            .glyphs[0]
            .rect;
        for (step, phase) in (0..8)
            .map(|step| step as f32 / 8.)
            .chain([1. - 0.625 / 255., 1. - 0.25 / 255.])
            .enumerate()
        {
            let mut command = Command::text(
                Rect::new(8. + phase - ink.x, 8. - ink.y, 64., 32.),
                [1.; 4],
                18.,
                0,
                1,
            );
            command.flags = 2 | 4;
            let expected =
                reference_alpha(&mut renderer, &command, "H", Rect::new(0., 0., 32., 32.));
            renderer.render(&[command], b"H")?;
            let pixels = renderer.read_pixels()?;
            for (index, (pixel, coverage)) in pixels
                .chunks_exact(4)
                .zip(expected.iter().copied())
                .enumerate()
            {
                if coverage == 0. {
                    assert_eq!(pixel, [0; 4], "tiny mask polluted zero coverage at value={value} step={step} pixel=({}, {})", index % 32, index / 32);
                    continue;
                }
                let (low, high) = white_mask_rgb_bounds(pixel[3]);
                assert!(white_mask_rgb_matches_alpha(pixel),
                    "mask RGB diverged from stored alpha at value={value} step={step} pixel=({}, {}) actual={pixel:?} coverage={coverage:.9} rgbBounds=({low},{high})", index % 32, index / 32);
                assert!((pixel[3] as f32 - coverage * 255.).abs() <= 1.5,
                    "tiny-mask alpha precision lost: value={value} step={step} actual={pixel:?} coverage={coverage:.9}");
            }
            let pixel = &pixels[(8 * 32 + 8) * 4..(8 * 32 + 8) * 4 + 4];
            if (value == 1 && (step == 3 || step == 4)) || (value == 255 && step >= 8) {
                samples.push(format!(
                    "mask={value} phase={phase:.9} coverageBytes={:.6} idealRGB={} actual={pixel:?}",
                    expected[8 * 32 + 8] * 255.,
                    srgb_byte(expected[8 * 32 + 8])
                ));
            }
        }
    }
    println!(
        "UNORM8 mask filtering on {}:\n{}",
        renderer.adapter_name,
        samples.join("\n")
    );
    Ok(())
}

#[test]
#[ignore = "requires a GPU adapter and a font with Latin glyphs; run explicitly"]
fn ancestor_clip_intersecting_only_filter_halo_keeps_coverage() -> Result<(), String> {
    let mut renderer = unsafe { Renderer::new(64, 64, 0)? };
    let text = "H";
    let layout = renderer.text.layout(text, 26., 64., false);
    let glyph = layout.glyphs.first().ok_or("test requires a Latin font")?;
    let mut command = Command::text(
        Rect::new(16.75 - glyph.rect.x, 16. - glyph.rect.y, 64., 40.),
        [1.; 4],
        26.,
        0,
        1,
    );
    command.flags = 4;
    let clip = Rect::new(16., 0., 0.7, 64.);
    let expected = reference_alpha(&mut renderer, &command, text, clip);
    assert!(
        expected.iter().any(|&a| a > 0.01),
        "fixture must have a nonempty left filter halo"
    );
    renderer.render(
        &[
            Command {
                kind: 2,
                rect: clip,
                ..Default::default()
            },
            command,
            Command {
                kind: 3,
                ..Default::default()
            },
        ],
        text.as_bytes(),
    )?;
    for (pixel, alpha) in renderer.read_pixels()?.chunks_exact(4).zip(expected) {
        assert!(
            (pixel[3] as f32 - alpha * 255.).abs() <= 2.,
            "halo-only clip discarded coverage"
        );
    }
    Ok(())
}

#[test]
#[ignore = "diagnostic screenshot, requires software GPU and Latin fonts"]
fn text_filter_screenshot() -> Result<(), String> {
    let Some(dir) = std::env::var_os("YOUI_ARTIFACT_DIR") else {
        return Ok(());
    };
    let dir = std::path::PathBuf::from(dir);
    std::fs::create_dir_all(&dir).map_err(|e| e.to_string())?;
    let mut renderer = unsafe { Renderer::new(720, 160, 0)? };
    renderer.text.set_test_font_family("DejaVu Sans");
    let heading = "YoUI · Linux X11 prototype";
    let text = format!("{heading}Clicked 2 times");
    for step in 0..8 {
        let offset = step as f32 / 8.;
        let mut title = Command::text(
            Rect::new(24., 24. + offset, 672., 35.1),
            [237. / 255., 243. / 255., 250. / 255., 1.],
            26.,
            0,
            heading.len() as u32,
        );
        title.flags = 1;
        renderer.render(
            &[
                Command::rect(
                    Rect::new(0., 0., 720., 160.),
                    [11. / 255., 16. / 255., 25. / 255., 1.],
                    0.,
                ),
                title,
                Command::text(
                    Rect::new(24., 75.1 + offset, 672., 18.9),
                    [237. / 255., 243. / 255., 250. / 255., 1.],
                    14.,
                    heading.len() as u32,
                    (text.len() - heading.len()) as u32,
                ),
            ],
            text.as_bytes(),
        )?;
        renderer.save_png(
            dir.join(format!("text-{step}.png"))
                .to_str()
                .ok_or("invalid path")?,
        )?;
    }
    Ok(())
}
