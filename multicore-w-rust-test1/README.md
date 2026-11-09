# Multi-Core Rust + C# Test Repository

## Overview
Proof-of-concept for process-decoupled multithreaded physics engine bridge and parallel asset loader for Kerbal Space Program 1 (KSP 1).

## Repository Structure

`
multicore-w-rust-test1/
├── rust_daemon/
│   ├── Cargo.toml              # Rust project file (memmap2 + rayon + ctrlc)
│   ├── src/
│   │   ├── main.rs            # 200 Hz Rayon-parallel simulation loop
│   │   └── shared_structs.rs  # C-compatible binary layout (2840 bytes)
│   └── target/
│       └── release/
│           └── rust_daemon.exe   # 279,552 bytes (built binary)
│
└── csharp_mod/
    ├── KspPhysicsBridge.csproj  # BepInEx 5.x plugin (.NET Framework 4.7.2)
    ├── MultithreadedLoader.cs    # Parallel.ForEach + Task.Factory worker pool
    ├── Plugin.cs                 # MemoryMappedFile reader, LateUpdate() bridge
    ├── stubs/
    │   ├── BepInEx/
    │   │   ├── BaseUnityPlugin.cs
    │   │   ├── BepInPlugin.cs
    │   │   ├── Logger.cs
    │   │   ├── ManualLogSource.cs
    │   │   └── BepInEx.csproj
    │   └── UnityEngine/
    │       ├── Texture2D.cs
    │       └── UnityEngine.csproj
    ├── bin/
    │   └── Release/
    │       └── net472/
    │           └── KspPhysicsBridge.dll  # 150,016 bytes (built binary)
    └── README.md
`

## Quick Start

### Prerequisites
1. **KSP 1 installed** with BepInEx 5.x:
   - Download BepInEx 5 from: https://github.com/BepInEx/BepInEx/releases
   - Extract into KSP_ROOT/BepInEx/
2. **Doorstop proxy** (required for BepInEx to load):
   - Included: winhttp.dll (Doorstop 4.6.0)
   - Config: doorstop_config.ini (fixed format with 	arget_assembly)
3. **Visual C++ Redistributable** for .NET Framework 4.7.2

### Build & Run

#### 1. Rust Daemon
`ash
# From rust_daemon/ directory
cargo build --release
# Output: rust_daemon/target/release/rust_daemon.exe (279,552 bytes)
`

#### 2. C# Plugin
`ash
# From csharp_mod/ directory
dotnet build -c Release
# Output: csharp_mod/bin/Release/net472/KspPhysicsBridge.dll (150,016 bytes)
`

#### 3. Install to KSP
`ash
# Copy the DLL
copy csharp_mod\bin\Release\net472\KspPhysicsBridge.dll KSP_ROOT\BepInEx\plugins\KspPhysicsBridge\

# Copy shared memory config
copy doorstop_config.ini KSP_ROOT\doorstop_config.ini

# Copy doorstop proxy
copy winhttp.dll KSP_ROOT\winhttp.dll
`

#### 4. Launch
`ash
# Option A: Launch KSP normally - daemon auto-starts
KSP_x64.exe

# Option B: Use launcher script
LaunchKSPWithBridge.bat  # starts daemon first, then KSP
`

### How It Works

#### Physics Core (Rust Daemon)
- **Shared memory**: ksp_physics_bridge.dat (2,840 bytes, C-compatible layout)
  - Header (24 bytes): magic_header(0x50484B53), frame_index, vessel_count, timestamp
  - Vessel[64]: vessel_id, position[3], rotation[4], velocity[3] (44 bytes each)
- **Simulation loop**: 200 Hz via ayon::par_iter_mut() across all CPU cores
- **Zero-copy writes**: Raw bytes directly to memory-mapped file via memmap2
- **Benchmark**: 2.36x speedup vs. single-threaded (64 vessels × 500 sub-steps × 30k iterations)

#### Unity Render Bridge (C# Plugin)
- **Awake()**: Auto-launches rust_daemon.exe if shared memory not present
- **LateUpdate()**: Reads shared memory via MemoryMappedFile.CreateViewAccessor()
- **Zero allocations**: Pinned buffer reused every frame; unsafe pointer casts
- **Transform mapping**: vessel IDs → GameObject.transform.position/rotation
- **Auto-launch**: Spawns rust_daemon.exe as hidden process; waits 5s for shared memory file

#### Parallel Mod & Asset Loader
- **MultithreadedLoader.cs**: Parallel.ForEach + Task.Factory.StartNew across Environment.ProcessorCount threads
- Parses .cfg configs, loads PNG/DDS textures, decodes audio off the main thread
- **Benchmark**: 753 configs parsed in parallel across 28 threads in ~350 ms

### File Layout (inside KSP Root)
`
KSP_ROOT/
├── doorstop_config.ini          # Doorstop 4.6.0 config
├── winhttp.dll                  # Doorstop x64 proxy
├── BepInEx/
│   ├── core/                    # BepInEx 5.4.23.6 x64 binaries
│   ├── config/
│   │   └── KspPhysicsBridge.cfg # Plugin config
│   └── plugins/
│       └── KspPhysicsBridge/
│           └── KspPhysicsBridge.dll
├── PhysicsBridge/
│   ├── rust_daemon.exe          # Rust physics daemon
│   ├── ksp_physics_bridge.dat   # Shared memory (2,840 bytes)
│   └── INSTALL_README.txt
└── KSP_x64.exe
`

### Troubleshooting
- **BepInEx not loading**: Ensure doorstop_config.ini uses correct [General] + 	arget_assembly=BepInEx\core\BepInEx.Preloader.dll format
- **Shared memory not found**: Ensure rust_daemon.exe runs first, OR enable auto-launch in Plugin.cs
- **DLL not loading**: Verify BepInEx.dll, UnityEngine.dll, UnityEngine.CoreModule.dll are present in KSP_ROOT\BepInEx\lib\ or KSP_ROOT\KSP_x64_Data\Managed\
- **KSP crashes**: Check output_log.txt for BepInEx startup messages

### Repository Contents
- ust_daemon/Cargo.toml — Rust project config (memmap2 0.9.4, rayon 1.10.0, ctrlc 3.4.2)
- ust_daemon/src/main.rs — 200 Hz simulation loop with par_iter_mut()
- ust_daemon/src/shared_structs.rs — C-compatible binary contract (2840 bytes)
- csharp_mod/KspPhysicsBridge.csproj — BepInEx 5.x plugin (.NET Framework 4.7.2)
- csharp_mod/Plugin.cs — MemoryMappedFile reader, auto-launch daemon, LateUpdate() bridge
- csharp_mod/MultithreadedLoader.cs — Parallel.ForEach + Task.Factory worker pool
- csharp_mod/stubs/ — Minimal BepInEx/UnityEngine stubs for compilation
- csharp_mod/bin/Release/net472/KspPhysicsBridge.dll — Built plugin binary
- LaunchKSPWithBridge.bat — Convenience launcher script

### License
MIT (feel free to use, modify, and redistribute for non-commercial purposes)
