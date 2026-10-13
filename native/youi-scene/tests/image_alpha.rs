use std::sync::Arc;
use youi_render::{Command, Rect, Renderer};
use youi_scene::{Mesh, Scene, Transform2D};

fn linear(s: f64) -> f64 {
    if s <= 0.04045 {
        s / 12.92
    } else {
        ((s + 0.055) / 1.055).powf(2.4)
    }
}
fn srgb(l: f64) -> u8 {
    let s = if l <= 0.0031308 {
        l * 12.92
    } else {
        1.055 * l.powf(1. / 2.4) - 0.055
    };
    (s * 255.).round() as u8
}

fn commands(mode: usize, image: u32, rect: Rect, tint: [f32; 4]) -> Vec<Command> {
    if mode == 0 {
        return vec![Command {
            kind: 7,
            flags: image,
            rect,
            color: tint,
            ..Default::default()
        }];
    }
    let mesh = if mode == 1 {
        Mesh::textured(
            vec![[0., 0.], [rect.w, 0.], [rect.w, rect.h], [0., rect.h]],
            vec![0, 1, 2, 0, 2, 3],
            vec![[0., 0.], [1., 0.], [1., 1.], [0., 1.]],
        )
        .unwrap()
    } else {
        Mesh::nine_slice(rect.w, rect.h, 4., 4., [1.; 4]).unwrap()
    };
    let mut scene = Scene::default();
    let node = scene.create(None).unwrap();
    scene
        .set_textured_mesh(node, Arc::new(mesh), image, tint)
        .unwrap();
    scene
        .set_transform(
            node,
            Transform2D {
                position: [rect.x, rect.y],
                ..Default::default()
            },
        )
        .unwrap();
    scene.prepare().to_vec()
}

// An independent reference derived from public straight-alpha input. The
// fixture is constant vertically; only its two interior columns are sampled.
// Never sample an outer edge here: Image and mesh have different border rules.
fn reference(pair: [[u8; 4]; 2], source_x: f64, tint: [f32; 4], opacity: f64) -> [u8; 4] {
    let t = source_x - 1.5; // texel centers are 1.5 and 2.5
    assert!(
        (0.0..=1.0).contains(&t),
        "not an internal filter sample: {source_x}"
    );
    let a = pair[0][3] as f64 / 255.;
    let b = pair[1][3] as f64 / 255.;
    let alpha = (a * (1. - t) + b * t) * tint[3] as f64 * opacity;
    let mut result = [0; 4];
    for c in 0..3 {
        let premul = linear(pair[0][c] as f64 / 255.) * a * (1. - t)
            + linear(pair[1][c] as f64 / 255.) * b * t;
        result[c] = srgb(premul * linear(tint[c] as f64) * tint[3] as f64 * opacity);
    }
    result[3] = (alpha * 255.).round() as u8;
    result
}

#[test]
#[ignore = "requires a GPU adapter; run explicitly in the quality gate"]
fn image_mesh_nine_slice_filter_linear_premultiplied_pixels() -> Result<(), String> {
    let mut renderer = unsafe { Renderer::new(40, 32, 0)? };
    println!("Image alpha adapter: {}", renderer.adapter_name);
    let pairs = [
        [[255, 0, 0, 255], [0, 255, 255, 0]], // transparent colored texel must contribute no RGB
        [[255, 0, 0, 255], [0, 0, 0, 0]], // transparent black must not darken the filtered edge twice
        [[190, 83, 214, 96], [35, 201, 62, 192]], // nonlinear decode before premultiplication
        [[190, 83, 214, 255], [35, 201, 62, 255]],
    ];
    for pair in pairs {
        let row = [pair[0], pair[0], pair[1], pair[1]].concat();
        let image = renderer.upload_image(4, 4, &row.repeat(4))?;
        for mode in 0..3 {
            for rect in [
                Rect::new(4., 4., 16., 16.),
                Rect::new(4.25, 3.75, 23.5, 19.25),
            ] {
                for tint in [[1.; 4], [0.5, 0.75, 0.25, 0.6]] {
                    for opacity in [1., 0.5] {
                        let mut frame = commands(mode, image, rect, tint);
                        if opacity != 1. {
                            frame.insert(
                                0,
                                Command {
                                    kind: 4,
                                    color: [1., 1., 1., opacity as f32],
                                    ..Default::default()
                                },
                            );
                            frame.push(Command {
                                kind: 5,
                                ..Default::default()
                            });
                        }
                        renderer.render(&frame, b"")?;
                        let pixels = renderer.read_pixels()?;
                        let middle = (rect.x + rect.w / 2.).floor() as usize;
                        for x in [middle - 1, middle] {
                            let y = (rect.y + rect.h / 2.).floor() as usize;
                            let local_x = x as f64 + 0.5 - rect.x as f64;
                            let sx = if mode == 2 {
                                1. + (local_x - 1.) / (rect.w as f64 - 2.) * 2.
                            } else {
                                local_x / rect.w as f64 * 4.
                            };
                            let expected = reference(pair, sx, tint, opacity);
                            let actual = &pixels[(y * 40 + x) * 4..(y * 40 + x) * 4 + 4];
                            for c in 0..4 {
                                assert!((actual[c] as i32 - expected[c] as i32).abs() <= 3,
                                    "mode={mode} pair={pair:?} rect={rect:?} tint={tint:?} layer={opacity} ({x},{y}) actual={actual:?} expected={expected:?}");
                            }
                        }
                    }
                }
            }
        }
        renderer.release_image(image)?;
    }
    if let Ok(dir) = std::env::var("YOUI_ARTIFACT_DIR") {
        std::fs::create_dir_all(&dir).map_err(|e| e.to_string())?;
        renderer.save_png(&format!("{dir}/image-alpha-filter.png"))?;
    }
    Ok(())
}

#[test]
#[ignore = "requires a GPU adapter; run explicitly in the quality gate"]
fn image_and_mesh_keep_distinct_outer_border_policies() -> Result<(), String> {
    let mut renderer = unsafe { Renderer::new(16, 16, 0)? };
    let image = renderer.upload_image(4, 4, &[255, 0, 0, 255].repeat(16))?;
    for mode in 0..3 {
        renderer.render(
            &commands(mode, image, Rect::new(0., 0., 16., 16.), [1.; 4]),
            b"",
        )?;
        let pixels = renderer.read_pixels()?;
        let actual = &pixels[8 * 16 * 4..8 * 16 * 4 + 4];
        let expected = if mode == 0 {
            [srgb(0.625), 0, 0, 159]
        } else {
            [255, 0, 0, 255]
        };
        for c in 0..4 {
            assert!(
                (actual[c] as i32 - expected[c] as i32).abs() <= 2,
                "mode={mode}: {actual:?} versus {expected:?}"
            );
        }
    }
    Ok(())
}
