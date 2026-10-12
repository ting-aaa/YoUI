use super::*;
use cosmic_text::SwashContent;

// Independent CPU oracle: integrate the raw raster mask with bilinear filtering
// over its entire support, then apply the public text/ancestor clip contracts.
// In particular this does not reuse the GPU quad or atlas UV calculations.
fn reference_alpha(renderer: &mut Renderer, command: &Command, text: &str, clip: Rect) -> Vec<f32> {
    let mut alpha = vec![0.; (renderer.width * renderer.height) as usize];
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
                let pixel = &mut alpha[(y * renderer.width + x) as usize];
                *pixel = coverage + *pixel * (1. - coverage);
            }
        }
    }
    alpha
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
                let expected = reference_alpha(&mut renderer, &command, text, clip);
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
                for (pixel, alpha) in pixels.chunks_exact(4).zip(expected) {
                    let target = (alpha * 255.).round() as i32;
                    worst = worst.max((pixel[3] as i32 - target).abs());
                    // White mask RGB must not receive a second multiplication
                    // from black padding when coverage is bilinearly filtered.
                    let srgb = if alpha <= 0.0031308 {
                        alpha * 12.92
                    } else {
                        1.055 * alpha.powf(1. / 2.4) - 0.055
                    };
                    let rgb = (srgb * 255.).round() as i32;
                    assert!(
                        pixel[..3].iter().all(|&v| (v as i32 - rgb).abs() <= 3),
                        "mask RGB edge differs from coverage: {pixel:?}, expected {rgb}"
                    );
                    if target > 2 && pixel[3] == 0 {
                        lost += 1;
                    }
                }
                if worst > 2 {
                    failures.push(format!("{text:?} size={size} step={step} clip={mode}: max alpha error={worst}, fully lost pixels={lost}"));
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
