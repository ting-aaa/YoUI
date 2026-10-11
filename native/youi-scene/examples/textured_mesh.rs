//! Standalone, asset-free textured mesh / nine-slice scene demonstration.
use std::sync::Arc;
use youi_render::{Command, Rect, Renderer};
use youi_scene::{Mesh, Scene, Transform2D};

fn main() -> Result<(), String> {
    let mut renderer = unsafe { Renderer::new(720, 480, 0)? };
    // Colored corners and alternating center tiles make stretching easy to see.
    let mut pixels = Vec::with_capacity(64 * 64 * 4);
    for y in 0..64 {
        for x in 0..64 {
            let color = match (x < 12, x >= 52, y < 12, y >= 52) {
                (true, _, true, _) => [255, 90, 75, 255],
                (_, true, true, _) => [255, 210, 70, 255],
                (true, _, _, true) => [100, 230, 135, 255],
                (_, true, _, true) => [110, 150, 255, 255],
                (true, _, _, _) | (_, true, _, _) | (_, _, true, _) | (_, _, _, true) => {
                    [235, 235, 245, 255]
                }
                _ if (x / 8 + y / 8) % 2 == 0 => [70, 175, 220, 255],
                _ => [35, 85, 155, 200],
            };
            pixels.extend_from_slice(&color);
        }
    }
    let image = renderer.upload_image(64, 64, &pixels)?;
    let mut scene = Scene::default();
    let group = scene.create(None)?;
    scene.set_transform(
        group,
        Transform2D {
            position: [135., 170.],
            scale: [1.15, 0.85],
            rotation: 0.32,
        },
    )?;
    scene.set_opacity(group, 0.85)?;
    let mesh = Arc::new(Mesh::textured(
        vec![[-60., -60.], [80., -40.], [65., 85.], [-75., 60.]],
        vec![0, 1, 2, 0, 2, 3],
        vec![[0., 0.], [1., 0.], [1., 1.], [0., 1.]],
    )?);
    let first = scene.create(Some(group))?;
    scene.set_textured_mesh(first, mesh.clone(), image, [1., 0.9, 0.9, 0.8])?;
    let second = scene.create(Some(group))?;
    scene.set_transform(
        second,
        Transform2D {
            position: [65., 55.],
            scale: [0.7, 0.7],
            rotation: -0.55,
        },
    )?;
    scene.set_textured_mesh(second, mesh, image, [0.6, 1., 0.8, 0.65])?;
    for (position, size) in [([330., 110.], [310., 160.]), ([330., 345.], [16., 18.])] {
        let node = scene.create(None)?;
        scene.set_transform(
            node,
            Transform2D {
                position,
                ..Default::default()
            },
        )?;
        scene.set_textured_mesh(
            node,
            Arc::new(Mesh::nine_slice(size[0], size[1], 64., 64., [12.; 4])?),
            image,
            [1.; 4],
        )?;
    }
    let mut commands = vec![
        Command::rect(Rect::new(0., 0., 720., 480.), [0.035, 0.055, 0.09, 1.], 0.),
        // Scene output is ordinary IR, so host-owned clip stacks work unchanged.
        Command {
            kind: 2,
            rect: Rect::new(24., 80., 650., 310.),
            ..Default::default()
        },
    ];
    commands.extend_from_slice(scene.prepare());
    commands.push(Command {
        kind: 3,
        ..Default::default()
    });
    let mut text = String::new();
    for (label, rect, size) in [
        (
            "YoUI textured meshes / nine-slice",
            Rect::new(28., 22., 664., 42.),
            23.,
        ),
        (
            "Affine + translucent shared mesh",
            Rect::new(28., 405., 310., 30.),
            17.,
        ),
        ("Fixed 12px borders", Rect::new(330., 280., 330., 30.), 17.),
        (
            "Small target: borders shrink",
            Rect::new(365., 338., 310., 30.),
            17.,
        ),
    ] {
        commands.push(Command::text(
            rect,
            [1.; 4],
            size,
            text.len() as u32,
            label.len() as u32,
        ));
        text.push_str(label);
    }
    let stats = renderer.render(&commands, text.as_bytes())?;
    renderer.save_png(
        &std::env::args()
            .nth(1)
            .unwrap_or_else(|| "textured-mesh.png".into()),
    )?;
    renderer.release_image(image)?;
    println!("{} | {:?}", renderer.adapter_name, stats);
    Ok(())
}
