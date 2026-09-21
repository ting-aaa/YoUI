use youi_render::{Command, Rect, Renderer};
fn main() -> Result<(), String> {
    let mut renderer = unsafe { Renderer::new(720, 420, 0)? };
    let mut commands = vec![Command::rect(
        Rect::new(0., 0., 720., 420.),
        [0.04, 0.06, 0.10, 1.],
        0.,
    )];
    for n in 0..12 {
        commands.push(Command::rect(
            Rect::new(
                36. + n as f32 * 48.,
                110. + (n as f32 * 0.7).sin() * 36.,
                90.,
                180.,
            ),
            [0.12, 0.68, 0.75, 0.45],
            20.,
        ));
    }
    let text = "YoUI / independent 2D renderer";
    commands.push(Command::text(
        Rect::new(36., 32., 660., 50.),
        [0.93, 0.96, 1., 1.],
        28.,
        0,
        text.len() as u32,
    ));
    let stats = renderer.render(&commands, text.as_bytes())?;
    let path = std::env::args()
        .nth(1)
        .unwrap_or_else(|| "render-scene.png".into());
    renderer.save_png(&path)?;
    println!("{} | {:?} | {}", renderer.adapter_name, stats, path);
    Ok(())
}
