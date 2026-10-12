//! Host-driven 2D renderer. This crate has no dependency on the UI framework.
mod failure;
pub mod ffi;
pub mod ir;
pub mod renderer;
mod resource;
mod text;
pub use ir::*;
pub use renderer::Renderer;
pub use resource::{ResourceBudget, ResourceUsage};

pub use text::{TextCell, TEXT_CELL_RTL};
