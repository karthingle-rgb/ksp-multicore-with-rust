//! Binary shared-memory contract between the Rust physics daemon and the
//! Unity render bridge. Must match `csharp_mod/Plugin.cs` exactly.
//
//! Layout (C-compatible, repr(C)):
//!   Header : u32 magic_header, u32 frame_index, u32 vessel_count, f64 timestamp  (24 bytes)
//!   Vessel : u32 vessel_id, f32 position[3], f32 rotation[4], f32 velocity[3]   (44 bytes)
//!   Block  : Header + 64 x Vessel                                                (2840 bytes)

use std::mem;

pub const MAGIC_HEADER: u32 = 0x5048_4B53; // "PKSH" on disk (little-endian "KSPH")
pub const MAX_VESSELS: usize = 64;

#[repr(C)]
#[derive(Debug, Clone, Copy, Default)]
pub struct Header {
    pub magic_header: u32,
    pub frame_index: u32,
    pub vessel_count: u32,
    pub timestamp: f64,
}

#[repr(C)]
#[derive(Debug, Clone, Copy, Default)]
pub struct VesselData {
    pub vessel_id: u32,
    pub position: [f32; 3],   // x, y, z
    pub rotation: [f32; 4],   // x, y, z, w
    pub velocity: [f32; 3],   // vx, vy, vz
}

#[repr(C)]
#[derive(Debug, Clone, Copy)]
pub struct SharedPhysicsBlock {
    pub header: Header,
    pub vessels: [VesselData; MAX_VESSELS],
}

impl SharedPhysicsBlock {
    pub const SIZE: usize = mem::size_of::<SharedPhysicsBlock>();     // 2840
    pub const HEADER_SIZE: usize = mem::size_of::<Header>();          // 24
    pub const VESSEL_SIZE: usize = mem::size_of::<VesselData>();      // 44
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn layout_matches_c_sharp_contract() {
        assert_eq!(mem::size_of::<Header>(), 24);
        assert_eq!(mem::size_of::<VesselData>(), 44);
        assert_eq!(mem::size_of::<SharedPhysicsBlock>(), 2840);
        assert_eq!(SharedPhysicsBlock::SIZE, 24 + 64 * 44);
        assert_eq!(SharedPhysicsBlock::HEADER_SIZE, 24);
        assert_eq!(SharedPhysicsBlock::VESSEL_SIZE, 44);
    }
}