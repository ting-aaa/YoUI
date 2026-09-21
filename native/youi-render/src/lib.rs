//! Host-driven 2D renderer. This crate has no dependency on the UI framework.
pub mod ffi;
pub mod ir;
pub mod renderer;
mod text;
pub use ir::*;
pub use renderer::Renderer;
