use std::sync::Arc;
use youi_render::{Command, Rect, Renderer};
use youi_scene::{Mesh, Scene, Transform2D};
fn main() -> Result<(), String> {
    let mut scene = Scene::default();
    let group = scene.create(None)?;
    scene.set_transform(
        group,
        Transform2D {
            position: [360., 230.],
            ..Default::default()
        },
    )?;
    let tile = Arc::new(Mesh::rectangle(80., 80.)?);
    for i in 0..16 {
        let n = scene.create(Some(group))?;
        let angle = i as f32 * std::f32::consts::TAU / 16.;
        scene.set_transform(
            n,
            Transform2D {
                position: [angle.cos() * 135., angle.sin() * 135.],
                scale: [0.65; 2],
                rotation: angle,
            },
        )?;
        scene.set_mesh(n, tile.clone(), [0.3 + i as f32 / 32., 0.75, 0.85, 0.7])?;
    }
    let mut renderer = unsafe { Renderer::new(720, 480, 0)? };
    let mut commands = vec![Command::rect(
        Rect::new(0., 0., 720., 480.),
        [0.035, 0.055, 0.09, 1.],
        0.,
    )];
    commands.extend_from_slice(scene.prepare());
    let label = "YoUI scene / affine mesh hierarchy";
    commands.push(Command::text(
        Rect::new(28., 22., 664., 42.),
        [1.; 4],
        23.,
        0,
        label.len() as u32,
    ));
    let stats = renderer.render(&commands, label.as_bytes())?;
    renderer.save_png(
        &std::env::args()
            .nth(1)
            .unwrap_or_else(|| "scene-graph.png".into()),
    )?;
    println!("{} | {:?}", renderer.adapter_name, stats);
    Ok(())
}
