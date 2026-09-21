use bytemuck::{Pod, Zeroable};

/// ABI v1: all coordinates are physical pixels, top-left origin, +Y down.
/// Colors are straight-alpha sRGB. The GPU blends premultiplied linear color.
#[repr(C)]
#[derive(Clone, Copy, Debug, Default, Pod, Zeroable)]
pub struct Rect {
    pub x: f32,
    pub y: f32,
    pub w: f32,
    pub h: f32,
}
impl Rect {
    pub fn new(x: f32, y: f32, w: f32, h: f32) -> Self {
        Self { x, y, w, h }
    }
    pub fn intersect(self, b: Self) -> Self {
        let x = self.x.max(b.x);
        let y = self.y.max(b.y);
        Self::new(
            x,
            y,
            (self.x + self.w).min(b.x + b.w).sub(x).max(0.),
            (self.y + self.h).min(b.y + b.h).sub(y).max(0.),
        )
    }
    pub fn valid(self) -> bool {
        [self.x, self.y, self.w, self.h]
            .iter()
            .all(|v| v.is_finite())
            && self.w >= 0.
            && self.h >= 0.
    }
}
use std::ops::Sub;

/// Fixed-width draw packet. text_offset/text_length address the frame's UTF-8 arena.
/// kind: 0 rounded rectangle, 1 text, 2 push clip, 3 pop clip,
/// 4 begin isolated layer (`color[3]` opacity), 5 end layer,
/// 6 triangle (rect.xy=p0, rect.wh=p1, reserved=p2), 7 atlas image (flags=resource ID).
#[repr(C)]
#[derive(Clone, Copy, Debug, Default, Pod, Zeroable)]
pub struct Command {
    pub kind: u32,
    pub flags: u32,
    pub text_offset: u32,
    pub text_length: u32,
    pub rect: Rect,
    pub color: [f32; 4],
    pub color2: [f32; 4],
    pub radius: f32,
    pub font_size: f32,
    pub reserved: [f32; 2],
}
impl Command {
    pub fn rect(rect: Rect, color: [f32; 4], radius: f32) -> Self {
        Self {
            rect,
            color,
            color2: color,
            radius,
            ..Default::default()
        }
    }
    pub fn text(rect: Rect, color: [f32; 4], font_size: f32, offset: u32, length: u32) -> Self {
        Self {
            kind: 1,
            rect,
            color,
            color2: color,
            font_size,
            text_offset: offset,
            text_length: length,
            ..Default::default()
        }
    }
    pub fn triangle(points: [[f32; 2]; 3], color: [f32; 4]) -> Self {
        Self {
            kind: 6,
            rect: Rect::new(points[0][0], points[0][1], points[1][0], points[1][1]),
            reserved: points[2],
            color,
            color2: color,
            ..Default::default()
        }
    }
}

pub fn validate(commands: &[Command], text: &[u8]) -> Result<(), String> {
    if commands.len() > 1_000_000 || text.len() > 16 * 1024 * 1024 {
        return Err("frame budget exceeded".into());
    }
    let mut clips = 0;
    let mut layers = 0;
    for c in commands {
        let geometry_ok = if c.kind == 6 {
            [
                c.rect.x,
                c.rect.y,
                c.rect.w,
                c.rect.h,
                c.reserved[0],
                c.reserved[1],
            ]
            .iter()
            .all(|v| v.is_finite())
        } else {
            c.rect.valid()
        };
        if !geometry_ok
            || !c
                .color
                .iter()
                .chain(c.color2.iter())
                .chain([&c.radius, &c.font_size])
                .all(|v| v.is_finite())
        {
            return Err("non-finite or negative geometry".into());
        }
        if c.color
            .iter()
            .chain(c.color2.iter())
            .any(|v| !(0.0..=1.0).contains(v))
        {
            return Err("color outside [0,1]".into());
        }
        match c.kind {
            0 => {
                if c.radius < 0. {
                    return Err("negative radius".into());
                }
            }
            1 => {
                if !(1.0..=512.0).contains(&c.font_size) {
                    return Err("font size outside [1,512]".into());
                }
                let end = (c.text_offset as usize)
                    .checked_add(c.text_length as usize)
                    .ok_or("text range overflow")?;
                let s = text
                    .get(c.text_offset as usize..end)
                    .ok_or("text range out of bounds")?;
                std::str::from_utf8(s).map_err(|_| "invalid UTF-8")?;
            }
            2 => {
                clips += 1;
                if clips > 64 {
                    return Err("clip depth exceeds 64".into());
                }
            }
            3 => {
                if clips == 0 {
                    return Err("clip stack underflow".into());
                }
                clips -= 1;
            }
            4 => {
                layers += 1;
                if layers > 8 {
                    return Err("layer depth exceeds 8".into());
                }
            }
            5 => {
                if layers == 0 {
                    return Err("layer stack underflow".into());
                }
                layers -= 1;
            }
            6 => {}
            7 => {
                if c.flags == 0 {
                    return Err("null image handle".into());
                }
            }
            _ => return Err(format!("unsupported command {}", c.kind)),
        }
    }
    if clips != 0 || layers != 0 {
        return Err("unbalanced frame scopes".into());
    }
    Ok(())
}

#[repr(C)]
#[derive(Clone, Copy, Default, Debug)]
pub struct FrameStats {
    pub commands: u32,
    pub instances: u32,
    pub draw_calls: u32,
    pub layers: u32,
    pub upload_bytes: u64,
    pub prepare_us: u64,
    pub submit_us: u64,
    pub atlas_glyphs: u32,
    pub text_cache_hits: u32,
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn abi_layout() {
        assert_eq!(std::mem::size_of::<Command>(), 80);
        assert_eq!(std::mem::size_of::<FrameStats>(), 48);
    }
    #[test]
    fn intersection_is_empty_outside() {
        assert_eq!(
            Rect::new(0., 0., 5., 5.)
                .intersect(Rect::new(8., 0., 5., 5.))
                .w,
            0.
        );
    }
    #[test]
    fn rejects_bad_text_and_scopes() {
        let mut c = Command {
            kind: 1,
            font_size: 16.,
            text_length: 2,
            ..Default::default()
        };
        assert!(validate(&[c], b"x").is_err());
        c.kind = 3;
        assert!(validate(&[c], b"").is_err());
        c.kind = 4;
        assert!(validate(&[c], b"").is_err());
    }
    #[test]
    fn rejects_nan() {
        let c = Command {
            radius: f32::NAN,
            ..Default::default()
        };
        assert!(validate(&[c], b"").is_err());
    }
}
