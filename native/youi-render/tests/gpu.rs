use youi_render::{Command, Rect, Renderer};

#[test]
#[ignore = "requires a GPU adapter; run explicitly in the quality gate"]
fn gpu_pixels_order_clip_alpha_layers_and_reuse() -> Result<(), String> {
    let mut renderer = unsafe { Renderer::new(64, 64, 0)? };
    let rect = |x, y, w, h, color| Command::rect(Rect::new(x, y, w, h), color, 0.);
    let frame = vec![
        rect(0., 0., 64., 64., [0., 0., 0., 1.]),
        rect(0., 0., 32., 32., [1., 0., 0., 1.]),
        rect(0., 0., 32., 32., [0., 1., 0., 0.5]),
        Command {
            kind: 2,
            rect: Rect::new(40., 0., 10., 10.),
            ..Default::default()
        },
        rect(30., 0., 30., 30., [0., 0., 1., 1.]),
        Command {
            kind: 3,
            ..Default::default()
        },
        Command {
            kind: 4,
            color: [0., 0., 0., 0.5],
            ..Default::default()
        },
        rect(0., 40., 24., 24., [1., 0., 0., 1.]),
        rect(8., 40., 24., 24., [0., 1., 0., 1.]),
        Command {
            kind: 5,
            ..Default::default()
        },
    ];
    let stats = renderer.render(&frame, b"")?;
    if let Some(dir) = std::env::var_os("YOUI_ARTIFACT_DIR") {
        let dir = std::path::PathBuf::from(dir);
        std::fs::create_dir_all(&dir).map_err(|e| e.to_string())?;
        renderer.save_png(
            dir.join("gpu-reference.png")
                .to_str()
                .ok_or("invalid path")?,
        )?;
    }
    let pixels = renderer.read_pixels()?;
    let at = |x: usize, y: usize| &pixels[(y * 64 + x) * 4..(y * 64 + x) * 4 + 4];
    let overlap = at(10, 10);
    for (x, y) in [(31, 30), (30, 31), (31, 31)] {
        assert_eq!(at(x, y), overlap, "opaque rectangle corner at {x},{y}");
    }
    assert!(
        pixels.chunks_exact(4).all(|pixel| pixel[3] == 255),
        "a full-target opaque background must stay opaque at every pixel"
    );
    assert!(
        (overlap[0] as i32 - 188).abs() < 4 && (overlap[1] as i32 - 188).abs() < 4,
        "linear premultiplied overlap: {overlap:?}"
    );
    assert_eq!(at(44, 5), &[0, 0, 255, 255]);
    assert_eq!(at(55, 5), &[0, 0, 0, 255]);
    let group = at(12, 50);
    assert!(
        group[0] < 3 && (group[1] as i32 - 188).abs() < 4,
        "group opacity isolates overlap: {group:?}"
    );
    assert_eq!(stats.layers, 1);
    assert_eq!(stats.draw_calls, 3);
    renderer.render(&frame, b"")?;
    assert_eq!(pixels, renderer.read_pixels()?);
    renderer.resize(32, 32)?;
    renderer.render(&[rect(0., 0., 32., 32., [0., 0., 1., 1.])], b"")?;
    assert_eq!(renderer.read_pixels()?.len(), 32 * 32 * 4);
    let image = renderer.upload_image(2, 2, &[255, 0, 255, 255].repeat(4))?;
    renderer.render(
        &[
            Command {
                kind: 7,
                flags: image,
                rect: Rect::new(0., 0., 16., 16.),
                color: [1.; 4],
                color2: [1.; 4],
                ..Default::default()
            },
            Command::triangle([[16., 0.], [32., 0.], [16., 16.]], [0., 1., 0., 1.]),
        ],
        b"",
    )?;
    let pixels = renderer.read_pixels()?;
    let at = |x: usize, y: usize| &pixels[(y * 32 + x) * 4..(y * 32 + x) * 4 + 4];
    assert_eq!(at(8, 8), &[255, 0, 255, 255]);
    assert_eq!(at(20, 4), &[0, 255, 0, 255]);
    renderer.release_image(image)?;
    assert!(renderer
        .render(
            &[Command {
                kind: 7,
                flags: image,
                ..Default::default()
            }],
            b""
        )
        .is_err());
    let replacement = renderer.upload_image(2, 2, &[255, 0, 255, 255].repeat(4))?;
    assert_ne!(image, replacement);
    println!("GPU reference pixels verified on {}", renderer.adapter_name);
    Ok(())
}

#[test]
#[ignore = "requires a GPU adapter; run explicitly in the quality gate"]
fn resource_budgets_reject_growth_and_preserve_live_resources() -> Result<(), String> {
    use youi_render::ResourceBudget;
    let atlas = 2048 * 2048 * 4;
    let target = 32 * 32 * 4;
    let budget = ResourceBudget {
        max_texture_bytes: atlas + target * 2,
        max_instance_buffer_bytes: 1025 * 112,
    };
    let mut renderer = unsafe { Renderer::new_with_budget(32, 32, 0, budget)? };
    eprintln!("resource-budget adapter: {}", renderer.adapter_name);
    assert_eq!(renderer.resource_budget(), budget);
    let initial = renderer.resource_usage();
    assert_eq!(initial.atlas_bytes, atlas);
    assert_eq!(initial.output_bytes, target);
    assert_eq!(initial.layer_bytes, 0);
    assert_eq!(initial.instance_buffer_bytes, 1024 * 112);
    let image = renderer.upload_image(32, 32, &[255, 0, 255, 255].repeat(32 * 32))?;
    let image_command = Command {
        kind: 7,
        flags: image,
        rect: Rect::new(0., 0., 32., 32.),
        color: [1.; 4],
        ..Default::default()
    };
    renderer.render(&[image_command], b"")?;
    let pixels = renderer.read_pixels()?;
    assert_eq!(&pixels[..4], &[255, 0, 255, 255]);
    let layer = Command {
        kind: 4,
        color: [1.; 4],
        ..Default::default()
    };
    let end = Command {
        kind: 5,
        ..Default::default()
    };
    renderer.render(&[layer, image_command, end], b"")?;
    let retained = renderer.resource_usage();
    assert_eq!(retained.layer_count, 1);
    assert_eq!(retained.layer_bytes, target);
    assert_eq!(pixels, renderer.read_pixels()?);
    assert!(renderer
        .render(&[layer, layer, image_command, end, end], b"")
        .unwrap_err()
        .contains("texture payload budget"));
    assert_eq!(retained, renderer.resource_usage());
    assert_eq!(pixels, renderer.read_pixels()?);
    // Exact instance budget includes one final presentation quad, even offscreen.
    renderer.render(&vec![image_command; 1024], b"")?;
    assert_eq!(renderer.resource_usage().instance_buffer_bytes, 1025 * 112);
    let grown = renderer.resource_usage();
    assert!(renderer
        .render(&vec![image_command; 1025], b"")
        .unwrap_err()
        .contains("instance buffer payload budget"));
    assert_eq!(grown, renderer.resource_usage());
    assert_eq!(pixels, renderer.read_pixels()?);
    let text_command = Command::text(Rect::new(0., 0., 32., 32.), [1.; 4], 12., 0, 1);
    assert!(renderer
        .render(&vec![text_command; 1025], b"M")
        .unwrap_err()
        .contains("instance buffer payload budget"));
    assert_eq!(grown, renderer.resource_usage());
    assert_eq!(pixels, renderer.read_pixels()?);
    // Rejected frame must not allocate otherwise-admissible layer targets.
    renderer.resize(31, 32)?;
    let before = renderer.resource_usage();
    let mut too_many = vec![layer];
    too_many.extend(vec![image_command; 1024]);
    too_many.push(end);
    assert!(renderer.render(&too_many, b"").is_err());
    assert_eq!(before, renderer.resource_usage());
    renderer.resize(32, 32)?;
    renderer.render(&[image_command], b"")?;
    let before = renderer.resource_usage();
    assert!(renderer
        .resize(65, 32)
        .unwrap_err()
        .contains("texture payload budget"));
    assert!(renderer.resize(0, 1).is_err());
    assert_eq!(before, renderer.resource_usage());
    assert_eq!(pixels, renderer.read_pixels()?);
    renderer.render(&[image_command], b"")?;
    assert_eq!(pixels, renderer.read_pixels()?);
    renderer.release_image(image)?;
    assert_eq!(
        before,
        renderer.resource_usage(),
        "release changes occupancy, not allocated atlas bytes"
    );
    renderer.render(&[], b"")?;
    assert_eq!(
        before,
        renderer.resource_usage(),
        "idle frames retain the high-water buffer"
    );
    Ok(())
}

#[test]
#[ignore = "requires a GPU adapter; run explicitly in the quality gate"]
fn resource_budget_resize_exact_limit_and_retained_layers() -> Result<(), String> {
    use youi_render::ResourceBudget;
    let budget = ResourceBudget {
        max_texture_bytes: 2048 * 2048 * 4 + 64 * 32 * 4,
        ..Default::default()
    };
    let mut renderer = unsafe { Renderer::new_with_budget(32, 32, 0, budget)? };
    let layer = Command {
        kind: 4,
        color: [1.; 4],
        ..Default::default()
    };
    let end = Command {
        kind: 5,
        ..Default::default()
    };
    renderer.render(&[layer, end], b"")?;
    assert_eq!(renderer.resource_usage().layer_count, 1);
    renderer.render(&[], b"")?;
    assert_eq!(renderer.resource_usage().layer_count, 1);
    renderer.resize(32, 32)?;
    assert_eq!(
        renderer.resource_usage().layer_count,
        1,
        "same-size resize is a no-op"
    );
    renderer.resize(64, 32)?;
    assert_eq!(renderer.resource_usage().layer_count, 0);
    assert_eq!(renderer.resource_usage().output_bytes, 64 * 32 * 4);
    renderer.render(&[], b"")?;
    assert!(renderer.render(&[layer, end], b"").is_err());
    renderer.resize(1, 1)?;
    renderer.render(&[layer, end], b"")?;
    assert_eq!(renderer.resource_usage().layer_bytes, 4);
    Ok(())
}

#[test]
#[ignore = "requires a GPU adapter; run explicitly in the quality gate"]
fn text_geometry_shares_cached_draw_layout_and_abi_contract() -> Result<(), String> {
    use youi_render::{ffi::*, TextCell};
    let mut renderer = unsafe { Renderer::new(256, 96, 0)? };
    let text = "office אבג لا";
    let cells = renderer.text_cells(text, 22., 120., false, false)?;
    assert!(!cells.is_empty());
    assert!(cells.iter().all(|cell| cell.line == 0));
    assert_eq!(renderer.render(&[], b"")?.atlas_glyphs, 0);
    let command = Command {
        flags: 2,
        ..Command::text(
            Rect::new(8., 8., 120., 72.),
            [1.; 4],
            22.,
            0,
            text.len() as u32,
        )
    };
    let stats = renderer.render(&[command], text.as_bytes())?;
    assert_eq!(
        stats.text_cache_hits, 1,
        "painting must reuse the queried no-wrap layout"
    );
    assert!(
        stats.atlas_glyphs > 0,
        "a usable font is required for this GPU test"
    );
    assert!(renderer
        .read_pixels()?
        .chunks_exact(4)
        .any(|pixel| pixel[3] > 0));
    assert_eq!(cells, renderer.text_cells(text, 22., 120., false, false)?);
    // A scrolling caller can opt out of the command rectangle's clip, while
    // its explicit viewport clip must still be enforced at every pixel.
    let overflow_text = b"mmmmmmmmmmmmmmmm";
    let overflow = Command {
        flags: 6,
        ..Command::text(
            Rect::new(-20., 0., 60., 48.),
            [1.; 4],
            24.,
            0,
            overflow_text.len() as u32,
        )
    };
    let viewport = Command {
        kind: 2,
        rect: Rect::new(0., 0., 100., 48.),
        ..Command::default()
    };
    let pop = Command {
        kind: 3,
        ..Command::default()
    };
    renderer.render(&[viewport, overflow, pop], overflow_text)?;
    let unclipped = renderer.read_pixels()?;
    assert!(unclipped
        .chunks_exact(4)
        .enumerate()
        .any(|(i, px)| i % 256 > 60 && px[3] > 0));
    assert!(unclipped
        .chunks_exact(4)
        .enumerate()
        .all(|(i, px)| (i % 256 < 100 && i / 256 < 48) || px[3] == 0));
    renderer.render(
        &[
            viewport,
            Command {
                flags: 2,
                ..overflow
            },
            pop,
        ],
        overflow_text,
    )?;
    let clipped = renderer.read_pixels()?;
    assert!(clipped
        .chunks_exact(4)
        .enumerate()
        .all(|(i, px)| i % 256 < 40 || px[3] == 0));
    for (size, width) in [
        (0., 10.),
        (513., 10.),
        (f32::NAN, 10.),
        (f32::INFINITY, 10.),
        (16., -1.),
        (16., f32::NAN),
        (16., f32::INFINITY),
    ] {
        assert!(renderer.text_cells(text, size, width, false, true).is_err());
    }
    assert!(renderer
        .text_cells(&"x".repeat(16 * 1024 * 1024 + 1), 16., 10., false, true)
        .is_err());
    // Exercise the exported two-call protocol using a real checked renderer ID.
    let mut id = 0;
    let mut count = 999;
    unsafe {
        assert_eq!(youi_create(256, 96, 0, &mut id), 0);
        assert_eq!(
            youi_text_cells(
                id,
                text.as_ptr(),
                text.len() as u32,
                22.,
                120.,
                2,
                std::ptr::null_mut(),
                0,
                &mut count
            ),
            0
        );
        assert_eq!(count as usize, cells.len());
        let sentinel = TextCell {
            start: u32::MAX,
            ..TextCell::default()
        };
        let mut output = vec![sentinel; count as usize + 1];
        let required = count;
        assert_eq!(
            youi_text_cells(
                id,
                text.as_ptr(),
                text.len() as u32,
                22.,
                120.,
                2,
                output.as_mut_ptr(),
                required - 1,
                &mut count
            ),
            -1
        );
        assert_eq!(count, required);
        assert!(output.iter().all(|cell| *cell == sentinel));
        assert_eq!(
            youi_text_cells(
                id,
                text.as_ptr(),
                text.len() as u32,
                22.,
                120.,
                2,
                output.as_mut_ptr(),
                required,
                &mut count
            ),
            0
        );
        assert_eq!(&output[..count as usize], &cells);
        assert_eq!(output[count as usize], sentinel);
        for (size, width, flags) in [(0., 10., 0), (16., f32::NAN, 0), (16., 10., 4)] {
            count = 999;
            assert_eq!(
                youi_text_cells(
                    id,
                    text.as_ptr(),
                    text.len() as u32,
                    size,
                    width,
                    flags,
                    output.as_mut_ptr(),
                    required,
                    &mut count
                ),
                -1
            );
            assert_eq!(count, 999);
            assert_eq!(&output[..required as usize], &cells);
        }
        assert_eq!(
            youi_text_cells(
                id,
                std::ptr::null(),
                0,
                16.,
                0.,
                0,
                std::ptr::null_mut(),
                0,
                &mut count
            ),
            0
        );
        assert_eq!(count, 1);
        assert_eq!(youi_destroy(id), 0);
    }
    println!(
        "Text geometry/paint cache and C ABI verified on {}",
        renderer.adapter_name
    );
    Ok(())
}

#[test]
#[ignore = "requires a GPU adapter; run explicitly in the quality gate"]
fn unicode_cluster_ranges_succeed_through_abi_and_keep_renderers_usable() {
    use unicode_segmentation::UnicodeSegmentation;
    use youi_render::{ffi::*, FrameStats, TextCell};

    fn success(status: i32) {
        let mut message = [0; 512];
        let length = unsafe { youi_last_error(message.as_mut_ptr(), message.len() as u32) };
        assert_eq!(
            status,
            0,
            "{}",
            String::from_utf8_lossy(&message[..(length as usize).min(message.len())])
        );
    }

    unsafe {
        let mut first = 0;
        success(youi_create(256, 128, 0, &mut first));
        let mut adapter = [0; 256];
        let mut written = 0;
        success(youi_adapter_name(
            first,
            adapter.as_mut_ptr(),
            adapter.len() as u32,
            &mut written,
        ));
        for text in [
            "\u{0600}\u{0301}\u{0600}",
            "\u{0600}\u{0301}\u{0600}aaب",
            "\u{0600}\u{202a}\u{2060}\u{0301}👩‍👧\u{202c}",
        ] {
            for flags in [0, 2] {
                let mut count = 0;
                success(youi_text_cells(
                    first,
                    text.as_ptr(),
                    text.len() as u32,
                    18.,
                    30.,
                    flags,
                    std::ptr::null_mut(),
                    0,
                    &mut count,
                ));
                assert_eq!(count as usize, text.graphemes(true).count());
                let mut cells = vec![TextCell::default(); count as usize];
                success(youi_text_cells(
                    first,
                    text.as_ptr(),
                    text.len() as u32,
                    18.,
                    30.,
                    flags,
                    cells.as_mut_ptr(),
                    count,
                    &mut count,
                ));
                for (start, grapheme) in text.grapheme_indices(true) {
                    assert_eq!(
                        cells
                            .iter()
                            .filter(|cell| {
                                cell.start == start as u32
                                    && cell.end == (start + grapheme.len()) as u32
                            })
                            .count(),
                        1
                    );
                }
                assert!(cells.iter().all(|cell| cell.width.is_finite()
                    && cell.width >= 0.
                    && cell.x.is_finite()
                    && cell.y.is_finite()));
                let command = Command {
                    flags: flags | 4,
                    ..Command::text(
                        Rect::new(8., 8., 30., 112.),
                        [1.; 4],
                        18.,
                        0,
                        text.len() as u32,
                    )
                };
                let mut stats = FrameStats::default();
                success(youi_render(
                    first,
                    &command,
                    1,
                    text.as_ptr(),
                    text.len() as u32,
                    &mut stats,
                ));
                assert!(
                    stats.atlas_glyphs > 0,
                    "the regression must retain raster glyphs"
                );
                assert_eq!(
                    stats.text_cache_hits, 1,
                    "painting must reuse queried geometry"
                );
            }
        }
        // These operations used to fail after the contained panic poisoned the
        // process-wide registry. Success is required, not merely containment or
        // a recovered mutex: both existing and newly created engines must work.
        let mut second = 0;
        success(youi_create(256, 128, 0, &mut second));
        for id in [first, second] {
            let text = b"renderer remains usable";
            let (mut width, mut height) = (0., 0.);
            success(youi_measure(
                id,
                text.as_ptr(),
                text.len() as u32,
                18.,
                240.,
                0,
                &mut width,
                &mut height,
            ));
            assert!(width > 0. && height > 0. && width.is_finite() && height.is_finite());
            let command = Command::text(
                Rect::new(8., 8., 240., 112.),
                [1.; 4],
                18.,
                0,
                text.len() as u32,
            );
            let mut stats = FrameStats::default();
            success(youi_render(
                id,
                &command,
                1,
                text.as_ptr(),
                text.len() as u32,
                &mut stats,
            ));
            assert!(stats.atlas_glyphs > 0);
            success(youi_destroy(id));
        }
        println!(
            "Unicode cluster ranges and C ABI lifecycle verified on {}",
            String::from_utf8_lossy(&adapter[..written as usize])
        );
    }
}

#[test]
#[ignore = "requires a GPU adapter; run explicitly in the quality gate"]
fn gpu_textured_triangles_uv_alpha_clipping_order_and_lifetime() -> Result<(), String> {
    let mut renderer = unsafe { Renderer::new(64, 64, 0)? };
    let mut rgba = Vec::new();
    for y in 0..4 {
        for x in 0..4 {
            rgba.extend_from_slice(match (x < 2, y < 2) {
                (true, true) => &[255, 0, 0, 255],
                (false, true) => &[0, 255, 0, 255],
                (true, false) => &[0, 0, 255, 255],
                _ => &[255, 255, 255, 128],
            });
        }
    }
    let image = renderer.upload_image(4, 4, &rgba)?;
    let _neighbor = renderer.upload_image(4, 4, &[255, 0, 255, 255].repeat(16))?;
    let quad = |x: f32, y: f32, size: f32, tint| {
        [
            Command::textured_triangle(
                [[x, y], [x + size, y], [x, y + size]],
                [[0., 0.], [1., 0.], [0., 1.]],
                image,
                tint,
            ),
            Command::textured_triangle(
                [[x, y + size], [x + size, y], [x + size, y + size]],
                [[0., 1.], [1., 0.], [1., 1.]],
                image,
                tint,
            ),
        ]
    };
    let mut frame = vec![Command::rect(
        Rect::new(0., 0., 64., 64.),
        [0., 0., 0., 1.],
        0.,
    )];
    frame.extend(quad(0., 0., 32., [1.; 4]));
    frame.push(Command {
        kind: 2,
        rect: Rect::new(40., 0., 16., 32.),
        ..Default::default()
    });
    frame.extend(quad(32., 0., 32., [1., 1., 1., 0.5]));
    frame.push(Command {
        kind: 3,
        ..Default::default()
    });
    frame.push(Command {
        kind: 4,
        color: [0., 0., 0., 0.5],
        ..Default::default()
    });
    frame.extend(quad(0., 32., 32., [1.; 4]));
    frame.push(Command {
        kind: 5,
        ..Default::default()
    });
    frame.push(Command::rect(
        Rect::new(0., 0., 2., 2.),
        [1., 1., 0., 1.],
        0.,
    ));
    let stats = renderer.render(&frame, b"")?;
    let pixels = renderer.read_pixels()?;
    let at = |x: usize, y: usize| &pixels[(y * 64 + x) * 4..(y * 64 + x) * 4 + 4];
    assert_eq!(at(0, 0), &[255, 255, 0, 255]);
    assert_eq!(at(4, 4), &[255, 0, 0, 255]);
    assert_eq!(at(28, 4), &[0, 255, 0, 255]);
    assert_eq!(at(4, 28), &[0, 0, 255, 255]);
    assert_eq!(
        at(31, 0),
        &[0, 255, 0, 255],
        "UV boundary cannot sample neighboring atlas entries"
    );
    let close = |pixel: &[u8], expected: [u8; 4]| {
        for (a, b) in pixel.iter().zip(expected) {
            assert!(
                (*a as i32 - b as i32).abs() <= 3,
                "{pixel:?} versus {expected:?}"
            );
        }
    };
    close(at(28, 28), [188, 188, 188, 255]);
    assert_eq!(at(36, 4), &[0, 0, 0, 255]);
    close(at(42, 4), [188, 0, 0, 255]);
    assert_eq!(at(60, 4), &[0, 0, 0, 255]);
    close(at(4, 36), [188, 0, 0, 255]);
    close(at(28, 60), [137, 137, 137, 255]);
    assert_eq!(stats.layers, 1);
    renderer.render(&frame, b"")?;
    assert_eq!(pixels, renderer.read_pixels()?);
    if let Some(dir) = std::env::var_os("YOUI_ARTIFACT_DIR") {
        std::fs::create_dir_all(&dir).map_err(|e| e.to_string())?;
        renderer.save_png(
            std::path::Path::new(&dir)
                .join("gpu-textured-mesh.png")
                .to_str()
                .ok_or("invalid path")?,
        )?;
    }
    // Transparent readback checks linear tint and exactly-once alpha multiplication.
    renderer.render(
        &[Command::textured_triangle(
            [[-8., -8.], [40., -8.], [-8., 40.]],
            [[0.9, 0.9]; 3],
            image,
            [0.5, 1., 1., 0.5],
        )],
        b"",
    )?;
    let transparent = renderer.read_pixels()?;
    close(
        &transparent[(2 * 64 + 2) * 4..(2 * 64 + 2) * 4 + 4],
        [66, 137, 137, 64],
    );
    assert_eq!(&transparent[(63 * 64 + 63) * 4..], &[0, 0, 0, 0]);
    let single = renderer.upload_image(1, 1, &[0, 255, 255, 255])?;
    renderer.render(
        &[Command::textured_triangle(
            [[0., 0.], [32., 0.], [0., 32.]],
            [[0., 0.], [1., 0.], [0., 1.]],
            single,
            [1.; 4],
        )],
        b"",
    )?;
    let one = renderer.read_pixels()?;
    assert_eq!(
        &one[(2 * 64 + 2) * 4..(2 * 64 + 2) * 4 + 4],
        &[0, 255, 255, 255]
    );
    renderer.release_image(single)?;
    renderer.release_image(image)?;
    assert!(renderer.render(&frame, b"").is_err());
    println!(
        "Textured triangle pixels verified on {}",
        renderer.adapter_name
    );
    Ok(())
}

#[test]
#[ignore = "requires a GPU adapter; run explicitly in the quality gate"]
fn textured_mesh_budget_exact_fit_rejection_and_recovery() -> Result<(), String> {
    use youi_render::ResourceBudget;
    let budget = ResourceBudget {
        max_texture_bytes: 2048 * 2048 * 4 + 16 * 16 * 4 * 2,
        max_instance_buffer_bytes: 1025 * 112,
    };
    let mut renderer = unsafe { Renderer::new_with_budget(16, 16, 0, budget)? };
    let image = renderer.upload_image(1, 1, &[255, 0, 255, 255])?;
    let triangle = Command::textured_triangle(
        [[-16., -16.], [48., -16.], [-16., 48.]],
        [[0., 0.], [1., 0.], [0., 1.]],
        image,
        [1.; 4],
    );
    // The final presentation instance is charged even for an offscreen renderer.
    let stats = renderer.render(&vec![triangle; 1024], b"")?;
    assert_eq!(stats.instances, 1024);
    assert_eq!(stats.upload_bytes, 1025 * 112);
    let before = renderer.resource_usage();
    assert_eq!(before.instance_buffer_bytes, 1025 * 112);
    let pixels = renderer.read_pixels()?;
    assert_eq!(&pixels[..4], &[255, 0, 255, 255]);
    let begin = Command {
        kind: 4,
        color: [1.; 4],
        ..Default::default()
    };
    let end = Command {
        kind: 5,
        ..Default::default()
    };
    for frame in [vec![triangle; 1025], {
        let mut frame = vec![begin];
        frame.extend(vec![triangle; 1024]);
        frame.push(end);
        frame
    }] {
        assert!(renderer
            .render(&frame, b"")
            .unwrap_err()
            .contains("instance buffer payload budget"));
        assert_eq!(renderer.resource_usage(), before);
        assert_eq!(renderer.read_pixels()?, pixels);
    }
    renderer.render(&[begin, triangle, end], b"")?;
    assert_eq!(renderer.resource_usage().layer_count, 1);
    assert_eq!(renderer.read_pixels()?, pixels);
    let retained = renderer.resource_usage();
    renderer.release_image(image)?;
    assert!(renderer.render(&[triangle], b"").is_err());
    assert_eq!(renderer.resource_usage(), retained);
    assert_eq!(renderer.read_pixels()?, pixels);
    println!(
        "Textured mesh budget and recovery verified on {}",
        renderer.adapter_name
    );
    Ok(())
}
