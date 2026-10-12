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
    uvs: Option<Vec<[f32; 2]>>,
}
impl Mesh {
    pub fn new(vertices: Vec<[f32; 2]>, indices: Vec<u32>) -> Result<Self, String> {
        if !indices.len().is_multiple_of(3)
            || indices.iter().any(|&i| i as usize >= vertices.len())
            || vertices.iter().flatten().any(|v| !v.is_finite())
        {
            return Err("invalid triangle mesh".into());
        }
        Ok(Self {
            vertices,
            indices,
            uvs: None,
        })
    }
    /// Construct geometry with finite, normalized image-local UVs, one per vertex.
    /// Image resources are bound per node, so this mesh can be shared across images.
    pub fn textured(
        vertices: Vec<[f32; 2]>,
        indices: Vec<u32>,
        uvs: Vec<[f32; 2]>,
    ) -> Result<Self, String> {
        if uvs.len() != vertices.len()
            || uvs
                .iter()
                .flatten()
                .any(|v| !v.is_finite() || !(0.0..=1.0).contains(v))
        {
            return Err("invalid mesh UVs".into());
        }
        let mut mesh = Self::new(vertices, indices)?;
        mesh.uvs = Some(uvs);
        Ok(mesh)
    }

    /// Create a textured nine-slice with borders [left, top, right, bottom] in
    /// source pixels. Destination borders retain their pixel sizes unless the
    /// target is too small, in which case opposite borders shrink proportionally.
    /// Cells with zero destination or source area are omitted; a zero-sized target is empty.
    pub fn nine_slice(
        width: f32,
        height: f32,
        source_width: f32,
        source_height: f32,
        borders: [f32; 4],
    ) -> Result<Self, String> {
        let [left, top, right, bottom] = borders;
        if [width, height].iter().any(|v| !v.is_finite() || *v < 0.)
            || [source_width, source_height]
                .iter()
                .any(|v| !v.is_finite() || *v <= 0.)
            || borders.iter().any(|v| !v.is_finite() || *v < 0.)
            || left as f64 + right as f64 > source_width as f64
            || top as f64 + bottom as f64 > source_height as f64
        {
            return Err("invalid nine-slice dimensions or borders".into());
        }
        fn axis(size: f32, first: f32, last: f32) -> [f32; 4] {
            // f64 prevents overflow and keeps the collapsed center exactly empty.
            let sum = first as f64 + last as f64;
            if (size as f64) <= sum && sum > 0. {
                let seam = ((size as f64) * (first as f64) / sum) as f32;
                [0., seam, seam, size]
            } else {
                [0., first, size - last, size]
            }
        }
        fn source_axis(size: f32, first: f32, last: f32) -> [f32; 4] {
            let seam = first / size;
            let end = if first as f64 + last as f64 == size as f64 {
                seam
            } else {
                ((size as f64 - last as f64) / size as f64) as f32
            };
            [0., seam, end, 1.]
        }
        let xs = axis(width, left, right);
        let ys = axis(height, top, bottom);
        let us = source_axis(source_width, left, right);
        let vs = source_axis(source_height, top, bottom);
        let mut vertices = Vec::with_capacity(16);
        let mut uvs = Vec::with_capacity(16);
        for y in 0..4 {
            for x in 0..4 {
                vertices.push([xs[x], ys[y]]);
                uvs.push([us[x], vs[y]]);
            }
        }
        let mut indices = Vec::with_capacity(54);
        for y in 0..3 {
            for x in 0..3 {
                if xs[x + 1] <= xs[x]
                    || ys[y + 1] <= ys[y]
                    || us[x + 1] <= us[x]
                    || vs[y + 1] <= vs[y]
                {
                    continue;
                }
                let a = (y * 4 + x) as u32;
                indices.extend_from_slice(&[a, a + 1, a + 5, a, a + 5, a + 4]);
            }
        }
        Self::textured(vertices, indices, uvs)
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
    image: Option<u32>,
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
            image: None,
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
    /// Bind solid geometry, rejecting UV meshes and clearing any previous image.
    pub fn set_mesh(&mut self, id: NodeId, mesh: Arc<Mesh>, color: [f32; 4]) -> Result<(), String> {
        if mesh.uvs.is_some() {
            return Err("UV mesh requires set_textured_mesh".into());
        }
        if color
            .iter()
            .any(|v| !v.is_finite() || !(0.0..=1.0).contains(v))
        {
            return Err("invalid color".into());
        }
        let node = self.node_mut(id)?;
        node.mesh = Some(mesh);
        node.color = color;
        node.image = None;
        self.dirty = true;
        Ok(())
    }
    /// Bind a UV mesh and straight-alpha tint to a nonzero image resource ID.
    /// The caller owns the image and must keep it live in the renderer used to
    /// consume this scene. Renderer validation rejects foreign/released IDs.
    pub fn set_textured_mesh(
        &mut self,
        id: NodeId,
        mesh: Arc<Mesh>,
        image: u32,
        tint: [f32; 4],
    ) -> Result<(), String> {
        if mesh.uvs.is_none() || image == 0 {
            return Err("textured mesh requires UVs and a nonzero image ID".into());
        }
        if tint
            .iter()
            .any(|v| !v.is_finite() || !(0.0..=1.0).contains(v))
        {
            return Err("invalid tint".into());
        }
        let node = self.node_mut(id)?;
        node.mesh = Some(mesh);
        node.image = Some(image);
        node.color = tint;
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
                let points = [
                    world.point(mesh.vertices[tri[0] as usize]),
                    world.point(mesh.vertices[tri[1] as usize]),
                    world.point(mesh.vertices[tri[2] as usize]),
                ];
                let command = match (&mesh.uvs, node.image) {
                    (Some(uvs), Some(image)) => Command::textured_triangle(
                        points,
                        [
                            uvs[tri[0] as usize],
                            uvs[tri[1] as usize],
                            uvs[tri[2] as usize],
                        ],
                        image,
                        color,
                    ),
                    _ => Command::triangle(points, color),
                };
                out.push(command);
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
    fn textured_triangle() -> Arc<Mesh> {
        Arc::new(
            Mesh::textured(
                vec![[0., 0.], [2., 0.], [0., 3.]],
                vec![0, 1, 2],
                vec![[0., 0.], [1., 0.], [0., 1.]],
            )
            .unwrap(),
        )
    }
    #[test]
    fn textured_mesh_validation() {
        let vertices = vec![[0., 0.], [1., 0.], [0., 1.]];
        let uvs = vertices.clone();
        for indices in [vec![0, 1], vec![0, 1, 3]] {
            assert!(Mesh::textured(vertices.clone(), indices, uvs.clone()).is_err());
        }
        assert!(Mesh::textured(vertices.clone(), vec![0, 1, 2], vec![[0., 0.]]).is_err());
        for value in [f32::NAN, f32::INFINITY, -0.01, 1.01] {
            let mut bad = uvs.clone();
            bad[1][0] = value;
            assert!(Mesh::textured(vertices.clone(), vec![0, 1, 2], bad).is_err());
        }
        for value in [f32::NAN, f32::INFINITY] {
            let mut bad = vertices.clone();
            bad[1][1] = value;
            assert!(Mesh::textured(bad, vec![0, 1, 2], uvs.clone()).is_err());
        }
    }
    #[test]
    fn nine_slice_topology_and_corners() {
        let mesh = Mesh::nine_slice(200., 100., 40., 20., [8., 4., 12., 6.]).unwrap();
        assert_eq!(mesh.vertices.len(), 16);
        assert_eq!(mesh.indices.len(), 54);
        assert_eq!(mesh.vertices[0], [0., 0.]);
        assert_eq!(mesh.vertices[5], [8., 4.]);
        assert_eq!(mesh.vertices[10], [188., 94.]);
        assert_eq!(mesh.vertices[15], [200., 100.]);
        let uvs = mesh.uvs.as_ref().unwrap();
        assert_eq!(uvs[0], [0., 0.]);
        assert_eq!(uvs[5], [0.2, 0.2]);
        assert_eq!(uvs[10], [0.7, 0.7]);
        assert_eq!(uvs[15], [1., 1.]);
        for tri in mesh.indices.chunks_exact(3) {
            let [a, b, c] = [
                mesh.vertices[tri[0] as usize],
                mesh.vertices[tri[1] as usize],
                mesh.vertices[tri[2] as usize],
            ];
            assert!((b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0]) > 0.);
        }
    }
    #[test]
    fn nine_slice_shrinks_and_omits_empty_cells() {
        let mesh = Mesh::nine_slice(10., 5., 40., 20., [8., 4., 12., 6.]).unwrap();
        assert_eq!(mesh.vertices[5], [4., 2.]);
        assert_eq!(mesh.vertices[10], [4., 2.]);
        assert_eq!(mesh.indices.len(), 24);
        assert_eq!(mesh.uvs.as_ref().unwrap()[5], [0.2, 0.2]);
        for (w, h) in [(0., 5.), (10., 0.), (0., 0.)] {
            assert!(Mesh::nine_slice(w, h, 40., 20., [8., 4., 12., 6.])
                .unwrap()
                .indices
                .is_empty());
        }
        assert_eq!(
            Mesh::nine_slice(10., 10., 20., 20., [0.; 4])
                .unwrap()
                .indices
                .len(),
            6
        );
        assert_eq!(
            Mesh::nine_slice(20., 10., 40., 20., [8., 4., 12., 6.])
                .unwrap()
                .indices
                .len(),
            24
        );
    }
    #[test]
    fn nine_slice_omits_zero_source_area_and_has_exact_seams() {
        let mesh = Mesh::nine_slice(100., 100., 20., 20., [10.; 4]).unwrap();
        assert_eq!(mesh.indices.len(), 24);
        let thirds = Mesh::nine_slice(100., 100., 3., 3., [1., 1., 2., 2.]).unwrap();
        assert_eq!(thirds.indices.len(), 24);
        let thirds_uvs = thirds.uvs.unwrap();
        assert_eq!(thirds_uvs[5], thirds_uvs[10]);
        let uvs = mesh.uvs.unwrap();
        for tri in mesh.indices.chunks_exact(3) {
            let [a, b, c] = [
                uvs[tri[0] as usize],
                uvs[tri[1] as usize],
                uvs[tri[2] as usize],
            ];
            assert!((b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0]) > 0.);
        }
    }
    #[test]
    fn nine_slice_rejects_invalid_dimensions_and_borders() {
        for value in [-1., f32::NAN, f32::INFINITY] {
            assert!(Mesh::nine_slice(value, 10., 20., 20., [2.; 4]).is_err());
            assert!(Mesh::nine_slice(10., value, 20., 20., [2.; 4]).is_err());
            for index in 0..4 {
                let mut borders = [2.; 4];
                borders[index] = value;
                assert!(Mesh::nine_slice(10., 10., 20., 20., borders).is_err());
            }
        }
        for value in [0., -1., f32::NAN, f32::INFINITY] {
            assert!(Mesh::nine_slice(10., 10., value, 20., [2.; 4]).is_err());
            assert!(Mesh::nine_slice(10., 10., 20., value, [2.; 4]).is_err());
        }
        assert!(Mesh::nine_slice(10., 10., 20., 20., [11.; 4]).is_err());
        assert!(Mesh::nine_slice(10., 10., 20., 20., [0., 11., 0., 10.]).is_err());
        assert!(Mesh::nine_slice(10., 10., 20., 20., [10.; 4]).is_ok());
        assert!(Mesh::nine_slice(1., 1., f32::MAX, f32::MAX, [f32::MAX; 4]).is_err());
    }
    #[test]
    fn textured_transform_opacity_and_cache() {
        let mut scene = Scene::default();
        let parent = scene.create(None).unwrap();
        let child = scene.create(Some(parent)).unwrap();
        scene
            .set_transform(
                parent,
                Transform2D {
                    position: [10., 20.],
                    scale: [2., 3.],
                    rotation: std::f32::consts::FRAC_PI_2,
                },
            )
            .unwrap();
        scene
            .set_transform(
                child,
                Transform2D {
                    position: [1., 2.],
                    ..Default::default()
                },
            )
            .unwrap();
        scene.set_opacity(parent, 0.5).unwrap();
        scene.set_opacity(child, 0.25).unwrap();
        let mesh = textured_triangle();
        scene
            .set_textured_mesh(child, mesh.clone(), 7, [0.2, 0.4, 0.6, 0.8])
            .unwrap();
        let command = scene.prepare()[0];
        assert_eq!(command.kind, 8);
        assert_eq!(command.flags, 7);
        for (actual, expected) in [
            command.rect.x,
            command.rect.y,
            command.rect.w,
            command.rect.h,
            command.reserved[0],
            command.reserved[1],
        ]
        .into_iter()
        .zip([4., 22., 4., 26., -5., 22.])
        {
            assert!((actual - expected).abs() < 0.0001);
        }
        assert_eq!(command.color, [0.2, 0.4, 0.6, 0.1]);
        assert_eq!(command.color2, [0., 0., 1., 0.]);
        assert_eq!([command.radius, command.font_size], [0., 1.]);
        scene.prepare();
        assert_eq!(scene.rebuilds, 1);
        scene.set_textured_mesh(child, mesh, 9, [1.; 4]).unwrap();
        assert_eq!(scene.prepare()[0].flags, 9);
        assert_eq!(scene.rebuilds, 2);
        scene.set_visible(parent, false).unwrap();
        assert!(scene.prepare().is_empty());
    }
    #[test]
    fn node_bindings_validate_and_preserve_painter_order() {
        let mut scene = Scene::default();
        let a = scene.create(None).unwrap();
        let b = scene.create(Some(a)).unwrap();
        let c = scene.create(None).unwrap();
        let uv = textured_triangle();
        let solid = Arc::new(Mesh::new(vec![[0., 0.]; 3], vec![0, 1, 2]).unwrap());
        scene.set_textured_mesh(a, uv.clone(), 11, [1.; 4]).unwrap();
        scene.set_mesh(b, solid.clone(), [1.; 4]).unwrap();
        scene.set_textured_mesh(c, uv.clone(), 22, [1.; 4]).unwrap();
        assert_eq!(
            scene
                .prepare()
                .iter()
                .map(|c| (c.kind, c.flags))
                .collect::<Vec<_>>(),
            vec![(8, 11), (6, 0), (8, 22)]
        );
        assert!(scene.set_mesh(a, uv.clone(), [1.; 4]).is_err());
        assert!(scene
            .set_textured_mesh(a, solid.clone(), 1, [1.; 4])
            .is_err());
        assert!(scene.set_textured_mesh(a, uv.clone(), 0, [1.; 4]).is_err());
        for value in [f32::NAN, f32::INFINITY, -1., 1.1] {
            assert!(scene
                .set_textured_mesh(a, uv.clone(), 1, [value; 4])
                .is_err());
        }
        assert_eq!(scene.prepare()[0].flags, 11);
        scene.set_mesh(a, solid, [1.; 4]).unwrap();
        assert_eq!((scene.prepare()[0].kind, scene.prepare()[0].flags), (6, 0));
        scene.reparent(a, None).unwrap();
        assert_eq!(
            scene.prepare().iter().map(|c| c.flags).collect::<Vec<_>>(),
            vec![22, 0, 0]
        );
    }
}
