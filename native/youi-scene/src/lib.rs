//! Optional retained 2D scene. The renderer never depends on this crate.
use std::sync::Arc;
use youi_render::Command;

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub struct NodeId {
    index: u32,
    generation: u32,
}
#[derive(Clone, Copy, Debug)]
pub struct Transform2D {
    pub position: [f32; 2],
    pub scale: [f32; 2],
    pub rotation: f32,
}
impl Default for Transform2D {
    fn default() -> Self {
        Self {
            position: [0.; 2],
            scale: [1.; 2],
            rotation: 0.,
        }
    }
}
#[derive(Clone, Copy)]
struct Affine([f32; 6]);
impl Affine {
    const IDENTITY: Self = Self([1., 0., 0., 1., 0., 0.]);
    fn from(t: Transform2D) -> Self {
        let (s, c) = t.rotation.sin_cos();
        Self([
            c * t.scale[0],
            s * t.scale[0],
            -s * t.scale[1],
            c * t.scale[1],
            t.position[0],
            t.position[1],
        ])
    }
    fn point(self, p: [f32; 2]) -> [f32; 2] {
        let m = self.0;
        [
            m[0] * p[0] + m[2] * p[1] + m[4],
            m[1] * p[0] + m[3] * p[1] + m[5],
        ]
    }
    fn then(self, b: Self) -> Self {
        let a = self.0;
        let b = b.0;
        Self([
            a[0] * b[0] + a[2] * b[1],
            a[1] * b[0] + a[3] * b[1],
            a[0] * b[2] + a[2] * b[3],
            a[1] * b[2] + a[3] * b[3],
            a[0] * b[4] + a[2] * b[5] + a[4],
            a[1] * b[4] + a[3] * b[5] + a[5],
        ])
    }
}
pub struct Mesh {
    vertices: Vec<[f32; 2]>,
    indices: Vec<u32>,
}
impl Mesh {
    pub fn new(vertices: Vec<[f32; 2]>, indices: Vec<u32>) -> Result<Self, String> {
        if !indices.len().is_multiple_of(3)
            || indices.iter().any(|&i| i as usize >= vertices.len())
            || vertices.iter().flatten().any(|v| !v.is_finite())
        {
            return Err("invalid triangle mesh".into());
        }
        Ok(Self { vertices, indices })
    }
    pub fn rectangle(width: f32, height: f32) -> Result<Self, String> {
        Self::new(
            vec![[0., 0.], [width, 0.], [width, height], [0., height]],
            vec![0, 1, 2, 0, 2, 3],
        )
    }
}
struct Node {
    parent: Option<NodeId>,
    children: Vec<NodeId>,
    transform: Transform2D,
    mesh: Option<Arc<Mesh>>,
    color: [f32; 4],
    visible: bool,
    opacity: f32,
}
struct Slot {
    generation: u32,
    node: Option<Node>,
}
#[derive(Default)]
pub struct Scene {
    slots: Vec<Slot>,
    free: Vec<u32>,
    roots: Vec<NodeId>,
    dirty: bool,
    commands: Vec<Command>,
    pub rebuilds: u64,
}
impl Scene {
    fn node(&self, id: NodeId) -> Result<&Node, String> {
        self.slots
            .get(id.index as usize)
            .filter(|s| s.generation == id.generation)
            .and_then(|s| s.node.as_ref())
            .ok_or_else(|| "stale or invalid NodeId".into())
    }
    fn node_mut(&mut self, id: NodeId) -> Result<&mut Node, String> {
        self.slots
            .get_mut(id.index as usize)
            .filter(|s| s.generation == id.generation)
            .and_then(|s| s.node.as_mut())
            .ok_or_else(|| "stale or invalid NodeId".into())
    }
    pub fn create(&mut self, parent: Option<NodeId>) -> Result<NodeId, String> {
        if let Some(p) = parent {
            self.check_depth(p, 1)?;
        }
        let index = self.free.pop().unwrap_or(self.slots.len() as u32);
        if index as usize == self.slots.len() {
            self.slots.push(Slot {
                generation: 1,
                node: None,
            });
        }
        let id = NodeId {
            index,
            generation: self.slots[index as usize].generation,
        };
        self.slots[index as usize].node = Some(Node {
            parent,
            children: vec![],
            transform: Default::default(),
            mesh: None,
            color: [1.; 4],
            visible: true,
            opacity: 1.,
        });
        if let Some(p) = parent {
            self.node_mut(p)?.children.push(id);
        } else {
            self.roots.push(id);
        }
        self.dirty = true;
        Ok(id)
    }
    fn check_depth(&self, mut id: NodeId, mut extra: usize) -> Result<(), String> {
        loop {
            if extra > 128 {
                return Err("scene depth exceeds 128".into());
            }
            extra += 1;
            match self.node(id)?.parent {
                Some(p) => id = p,
                None => return Ok(()),
            }
        }
    }
    pub fn set_transform(&mut self, id: NodeId, t: Transform2D) -> Result<(), String> {
        if t.position
            .iter()
            .chain(t.scale.iter())
            .chain([&t.rotation])
            .any(|v| !v.is_finite())
        {
            return Err("non-finite transform".into());
        }
        self.node_mut(id)?.transform = t;
        self.dirty = true;
        Ok(())
    }
    pub fn set_mesh(&mut self, id: NodeId, mesh: Arc<Mesh>, color: [f32; 4]) -> Result<(), String> {
        if color
            .iter()
            .any(|v| !v.is_finite() || !(0.0..=1.0).contains(v))
        {
            return Err("invalid color".into());
        }
        let node = self.node_mut(id)?;
        node.mesh = Some(mesh);
        node.color = color;
        self.dirty = true;
        Ok(())
    }
    pub fn set_visible(&mut self, id: NodeId, visible: bool) -> Result<(), String> {
        self.node_mut(id)?.visible = visible;
        self.dirty = true;
        Ok(())
    }
    pub fn set_opacity(&mut self, id: NodeId, opacity: f32) -> Result<(), String> {
        if !opacity.is_finite() || !(0.0..=1.0).contains(&opacity) {
            return Err("invalid opacity".into());
        }
        self.node_mut(id)?.opacity = opacity;
        self.dirty = true;
        Ok(())
    }
    fn subtree_height(&self, id: NodeId) -> usize {
        1 + self
            .node(id)
            .unwrap()
            .children
            .iter()
            .map(|&id| self.subtree_height(id))
            .max()
            .unwrap_or(0)
    }
    pub fn reparent(&mut self, id: NodeId, parent: Option<NodeId>) -> Result<(), String> {
        let old = self.node(id)?.parent;
        if let Some(p) = parent {
            let mut cursor = Some(p);
            while let Some(n) = cursor {
                if n == id {
                    return Err("scene cycle rejected".into());
                }
                cursor = self.node(n)?.parent;
            }
            self.check_depth(p, self.subtree_height(id))?;
        }
        if let Some(p) = old {
            self.node_mut(p)?.children.retain(|&n| n != id);
        } else {
            self.roots.retain(|&n| n != id);
        }
        if let Some(p) = parent {
            self.node_mut(p)?.children.push(id);
        } else {
            self.roots.push(id);
        }
        self.node_mut(id)?.parent = parent;
        self.dirty = true;
        Ok(())
    }
    pub fn remove(&mut self, id: NodeId) -> Result<(), String> {
        let parent = self.node(id)?.parent;
        if let Some(p) = parent {
            self.node_mut(p)?.children.retain(|&n| n != id);
        } else {
            self.roots.retain(|&n| n != id);
        }
        self.remove_subtree(id);
        self.dirty = true;
        Ok(())
    }
    fn remove_subtree(&mut self, id: NodeId) {
        let node = self.slots[id.index as usize].node.take().unwrap();
        for child in node.children {
            self.remove_subtree(child);
        }
        let slot = &mut self.slots[id.index as usize];
        // Retire the slot permanently on generation overflow, never resurrect stale IDs.
        if let Some(next) = slot.generation.checked_add(1) {
            slot.generation = next;
            self.free.push(id.index);
        }
    }
    fn emit(&self, id: NodeId, parent: Affine, opacity: f32, out: &mut Vec<Command>) {
        let node = self.node(id).unwrap();
        if !node.visible {
            return;
        }
        let world = parent.then(Affine::from(node.transform));
        let opacity = opacity * node.opacity;
        if let Some(mesh) = &node.mesh {
            let mut color = node.color;
            color[3] *= opacity;
            for tri in mesh.indices.chunks_exact(3) {
                out.push(Command::triangle(
                    [
                        world.point(mesh.vertices[tri[0] as usize]),
                        world.point(mesh.vertices[tri[1] as usize]),
                        world.point(mesh.vertices[tri[2] as usize]),
                    ],
                    color,
                ));
            }
        }
        for &child in &node.children {
            self.emit(child, world, opacity, out);
        }
    }
    pub fn prepare(&mut self) -> &[Command] {
        if self.dirty {
            let mut output = std::mem::take(&mut self.commands);
            output.clear();
            for &root in &self.roots {
                self.emit(root, Affine::IDENTITY, 1., &mut output);
            }
            self.commands = output;
            self.rebuilds += 1;
            self.dirty = false;
        }
        &self.commands
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn stale_handle_and_cycles() {
        let mut scene = Scene::default();
        let p = scene.create(None).unwrap();
        let c = scene.create(Some(p)).unwrap();
        assert!(scene.reparent(p, Some(c)).is_err());
        scene.remove(p).unwrap();
        assert!(scene.set_visible(c, true).is_err());
        let new = scene.create(None).unwrap();
        assert_ne!(p, new);
        assert!(scene.set_visible(p, true).is_err());
    }
    #[test]
    fn parent_transform_and_cached_frame() {
        let mut scene = Scene::default();
        let p = scene.create(None).unwrap();
        let c = scene.create(Some(p)).unwrap();
        scene
            .set_transform(
                p,
                Transform2D {
                    position: [20., 10.],
                    ..Default::default()
                },
            )
            .unwrap();
        scene
            .set_transform(
                c,
                Transform2D {
                    position: [5., 7.],
                    ..Default::default()
                },
            )
            .unwrap();
        scene
            .set_mesh(c, Arc::new(Mesh::rectangle(10., 10.).unwrap()), [1.; 4])
            .unwrap();
        let commands = scene.prepare();
        assert_eq!((commands[0].rect.x, commands[0].rect.y), (25., 17.));
        scene.prepare();
        assert_eq!(scene.rebuilds, 1);
    }
    #[test]
    fn mesh_validation() {
        assert!(Mesh::new(vec![[0., 0.]], vec![0, 1, 2]).is_err());
        assert!(Mesh::new(vec![[f32::NAN, 0.]], vec![0, 0, 0]).is_err());
    }
}
