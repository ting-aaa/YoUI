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
        max_instance_buffer_bytes: 1025 * 96,
    };
    let mut renderer = unsafe { Renderer::new_with_budget(32, 32, 0, budget)? };
    eprintln!("resource-budget adapter: {}", renderer.adapter_name);
    assert_eq!(renderer.resource_budget(), budget);
    let initial = renderer.resource_usage();
    assert_eq!(initial.atlas_bytes, atlas);
    assert_eq!(initial.output_bytes, target);
    assert_eq!(initial.layer_bytes, 0);
    assert_eq!(initial.instance_buffer_bytes, 1024 * 96);
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
    assert_eq!(renderer.resource_usage().instance_buffer_bytes, 1025 * 96);
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
