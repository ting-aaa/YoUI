//! Admission limits for owned GPU payloads, not a measurement of total VRAM.
use crate::text::ATLAS_SIZE;

pub(crate) const MAX_TARGET_DIMENSION: u32 = 8192;

// Derive accounting from the actual GPU payload so added attributes cannot bypass budgets.
pub(crate) const INSTANCE_BYTES: u64 = std::mem::size_of::<crate::renderer::Instance>() as u64;
pub(crate) const INITIAL_CAPACITY: usize = 1024;

/// Immutable per-renderer limits. Includes retained allocations, not just a frame's use.
/// Excludes CPU caches, driver overhead, swapchains, staging and in-flight old allocations.
#[repr(C)]
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct ResourceBudget {
    pub max_texture_bytes: u64,
    pub max_instance_buffer_bytes: u64,
}

impl Default for ResourceBudget {
    fn default() -> Self {
        Self {
            max_texture_bytes: 528 * 1024 * 1024,
            max_instance_buffer_bytes: 256 * 1024 * 1024,
        }
    }
}

/// Payload sizes of currently retained GPU objects. Released images do not shrink the atlas.
#[repr(C)]
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct ResourceUsage {
    pub atlas_bytes: u64,
    pub output_bytes: u64,
    pub layer_bytes: u64,
    pub instance_buffer_bytes: u64,
    pub layer_count: u32,
    pub reserved: u32,
}

impl ResourceBudget {
    pub(crate) fn textures(
        self,
        width: u32,
        height: u32,
        layers: usize,
    ) -> Result<ResourceUsage, String> {
        if width == 0
            || height == 0
            || width > MAX_TARGET_DIMENSION
            || height > MAX_TARGET_DIMENSION
        {
            return Err("target size outside 1..8192".into());
        }
        let atlas_bytes = u64::from(ATLAS_SIZE) * u64::from(ATLAS_SIZE) * 4;
        let output_bytes = u64::from(width) * u64::from(height) * 4;
        let layer_count = u32::try_from(layers).map_err(|_| "layer count overflow")?;
        let layer_bytes = output_bytes
            .checked_mul(layers as u64)
            .ok_or("texture byte count overflow")?;
        let total = atlas_bytes
            .checked_add(output_bytes)
            .and_then(|n| n.checked_add(layer_bytes))
            .ok_or("texture byte count overflow")?;
        if total > self.max_texture_bytes {
            return Err("texture payload budget exceeded".into());
        }
        Ok(ResourceUsage {
            atlas_bytes,
            output_bytes,
            layer_bytes,
            layer_count,
            ..Default::default()
        })
    }

    pub(crate) fn capacity(
        self,
        required: usize,
        current: usize,
        device_limit: u64,
    ) -> Result<usize, String> {
        let limit = self.max_instance_buffer_bytes.min(device_limit) / INSTANCE_BYTES;
        let required = required.max(INITIAL_CAPACITY);
        if required as u64 > limit {
            return Err("instance buffer payload budget exceeded".into());
        }
        if required <= current {
            return Ok(current);
        }
        // Prefer geometric reuse, but allow exact-fit requests near the ceiling.
        Ok(required
            .checked_next_power_of_two()
            .filter(|n| *n as u64 <= limit)
            .unwrap_or(required))
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn texture_accounting_exact_limit_and_invalid_dimensions() {
        let mut budget = ResourceBudget::default();
        let usage = budget.textures(64, 32, 2).unwrap();
        budget.max_texture_bytes = usage.atlas_bytes + usage.output_bytes + usage.layer_bytes;
        assert_eq!(budget.textures(64, 32, 2).unwrap(), usage);
        budget.max_texture_bytes -= 1;
        assert!(budget.textures(64, 32, 2).is_err());
        for (w, h) in [(0, 1), (1, 0), (8193, 1), (1, u32::MAX)] {
            assert!(ResourceBudget::default().textures(w, h, 0).is_err());
        }
        assert!(budget.textures(8192, 8192, usize::MAX).is_err());
    }

    #[test]
    fn instance_capacity_honors_rounding_and_device_limits() {
        let mut budget = ResourceBudget {
            max_instance_buffer_bytes: 1025 * INSTANCE_BYTES,
            ..Default::default()
        };
        assert_eq!(budget.capacity(1025, 1024, u64::MAX).unwrap(), 1025);
        assert!(budget.capacity(1026, 1024, u64::MAX).is_err());
        assert!(budget.capacity(1025, 1024, 1024 * INSTANCE_BYTES).is_err());
        budget.max_instance_buffer_bytes = 1024 * INSTANCE_BYTES - 1;
        assert!(budget.capacity(0, 0, u64::MAX).is_err());
        budget.max_instance_buffer_bytes = u64::MAX;
        assert!(budget
            .capacity(usize::MAX, 0, 1024 * INSTANCE_BYTES)
            .is_err());
        assert_eq!(budget.capacity(1025, 1024, u64::MAX).unwrap(), 2048);
        assert_eq!(budget.capacity(1, 2048, u64::MAX).unwrap(), 2048);
    }
}
