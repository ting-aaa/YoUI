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
