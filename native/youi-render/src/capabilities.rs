//! Immutable diagnostics for the selected device, not allocation guarantees.
use crate::{resource::MAX_TARGET_DIMENSION, text::ATLAS_SIZE, ResourceBudget};

/// Stable YoUI values, independent of wgpu's discriminants. Other is a fallback.
#[repr(u32)]
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub enum RendererBackend {
    #[default]
    Other = 0,
    Vulkan = 1,
    Metal = 2,
    Dx12 = 3,
    Gl = 4,
    BrowserWebGpu = 5,
}

impl From<wgpu::Backend> for RendererBackend {
    fn from(value: wgpu::Backend) -> Self {
        match value {
            wgpu::Backend::Vulkan => Self::Vulkan,
            wgpu::Backend::Metal => Self::Metal,
            wgpu::Backend::Dx12 => Self::Dx12,
            wgpu::Backend::Gl => Self::Gl,
            wgpu::Backend::BrowserWebGpu => Self::BrowserWebGpu,
            wgpu::Backend::Noop => Self::Other,
        }
    }
}

/// Adapter classification reported by wgpu, not an independently verified claim.
#[repr(u32)]
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub enum RendererDeviceKind {
    #[default]
    Other = 0,
    IntegratedGpu = 1,
    DiscreteGpu = 2,
    VirtualGpu = 3,
    Cpu = 4,
}

impl From<wgpu::DeviceType> for RendererDeviceKind {
    fn from(value: wgpu::DeviceType) -> Self {
        match value {
            wgpu::DeviceType::Other => Self::Other,
            wgpu::DeviceType::IntegratedGpu => Self::IntegratedGpu,
            wgpu::DeviceType::DiscreteGpu => Self::DiscreteGpu,
            wgpu::DeviceType::VirtualGpu => Self::VirtualGpu,
            wgpu::DeviceType::Cpu => Self::Cpu,
        }
    }
}

/// Selected backend and effective static ceilings of a successfully created renderer.
/// Uses enabled device limits, not the adapter's potentially larger supported limits.
/// Resource budgets, shared atlas capacity/fragmentation and frame validation still apply.
/// No feature support, free-space, device health or native-window guarantee is implied.
#[repr(C)]
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct RendererCapabilities {
    pub backend: RendererBackend,
    pub device_kind: RendererDeviceKind,
    pub max_target_dimension_2d: u32,
    pub max_image_dimension_2d: u32,
    pub atlas_dimension_2d: u32,
    pub reserved: u32,
    pub max_instance_buffer_bytes: u64,
}

impl RendererCapabilities {
    pub(crate) fn new(
        backend: wgpu::Backend,
        device_kind: wgpu::DeviceType,
        enabled_limits: &wgpu::Limits,
        budget: ResourceBudget,
    ) -> Self {
        Self {
            backend: backend.into(),
            device_kind: device_kind.into(),
            max_target_dimension_2d: MAX_TARGET_DIMENSION
                .min(enabled_limits.max_texture_dimension_2d),
            max_image_dimension_2d: ATLAS_SIZE - 2,
            atlas_dimension_2d: ATLAS_SIZE,
            reserved: 0,
            max_instance_buffer_bytes: budget
                .max_instance_buffer_bytes
                .min(enabled_limits.max_buffer_size),
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn enum_values_are_explicit_and_complete() {
        for (backend, expected) in [
            (wgpu::Backend::Noop, 0),
            (wgpu::Backend::Vulkan, 1),
            (wgpu::Backend::Metal, 2),
            (wgpu::Backend::Dx12, 3),
            (wgpu::Backend::Gl, 4),
            (wgpu::Backend::BrowserWebGpu, 5),
        ] {
            assert_eq!(RendererBackend::from(backend) as u32, expected);
        }
        for (kind, expected) in [
            (wgpu::DeviceType::Other, 0),
            (wgpu::DeviceType::IntegratedGpu, 1),
            (wgpu::DeviceType::DiscreteGpu, 2),
            (wgpu::DeviceType::VirtualGpu, 3),
            (wgpu::DeviceType::Cpu, 4),
        ] {
            assert_eq!(RendererDeviceKind::from(kind) as u32, expected);
        }
    }

    #[test]
    fn limits_are_effective_ceilings_not_adapter_maxima_or_free_capacity() {
        let mut limits = wgpu::Limits {
            max_texture_dimension_2d: 4096,
            max_buffer_size: 200_000,
            ..Default::default()
        };
        let mut budget = ResourceBudget::default();
        let query = |limits: &wgpu::Limits, budget| {
            RendererCapabilities::new(wgpu::Backend::Vulkan, wgpu::DeviceType::Cpu, limits, budget)
        };
        let result = query(&limits, budget);
        assert_eq!(result.backend, RendererBackend::Vulkan);
        assert_eq!(result.device_kind, RendererDeviceKind::Cpu);
        assert_eq!(result.max_target_dimension_2d, 4096);
        assert_eq!(result.max_instance_buffer_bytes, 200_000);
        assert_eq!(result.max_image_dimension_2d, 2046);
        assert_eq!(result.atlas_dimension_2d, 2048);
        assert_eq!(result.reserved, 0);
        limits.max_texture_dimension_2d = 16384;
        budget.max_instance_buffer_bytes = 114_689;
        let result = query(&limits, budget);
        assert_eq!(result.max_target_dimension_2d, 8192);
        assert_eq!(result.max_instance_buffer_bytes, 114_689);
    }
}
