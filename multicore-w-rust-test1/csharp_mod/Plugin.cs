using System;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;

namespace KspPhysicsBridge
{
    /// <summary>
    /// Unity Render Bridge for KSP 1 (BepInEx 5 plugin).
    /// Reads vessel transforms written by the Rust physics daemon into the
    /// memory-mapped file ksp_physics_bridge.dat and applies them to matching
    /// GameObject transforms during LateUpdate(). Zero per-frame allocations.
    /// </summary>
    [BepInPlugin("ksp.physics.bridge", "KSP Physics Bridge - Unity Render Bridge", "1.0.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        private const string SharedMemoryFile = "ksp_physics_bridge.dat";
        private const uint MagicHeader = 0x50484B53;
        private const int MaxVessels = 64;

        private MemoryMappedFile _mmf;
        private MemoryMappedViewAccessor _accessor;
        private bool _attached;
        private readonly int _headerSize;
        private readonly int _vesselSize;
        private readonly int _blockSize;
        private byte[] _readBuffer;
        private GCHandle _bufferHandle;

        private readonly GameObject[] _vesselObjects = new GameObject[MaxVessels];
        private Process _daemonProc;

        public Plugin()
        {
            _headerSize = Marshal.SizeOf<PhysicsHeader>();      // 24 bytes
            _vesselSize = Marshal.SizeOf<VesselData>();         // 44 bytes
            _blockSize = _headerSize + MaxVessels * _vesselSize; // 2840 bytes
        }

        public void Awake()
        {
            try
            {
                // Auto-launch the Rust physics daemon if shared memory is not yet available.
                EnsureDaemonRunning();

                if (!File.Exists(SharedMemoryFile))
                {
                    Logger.LogError($"[PhysicsBridge] Shared memory file still missing after daemon launch: {SharedMemoryFile}");
                    return;
                }

                _mmf = MemoryMappedFile.OpenExisting(SharedMemoryFile);
                _accessor = _mmf.CreateViewAccessor();

                // Single pinned read buffer reused every frame = zero per-frame allocations.
                _readBuffer = new byte[_blockSize];
                _bufferHandle = GCHandle.Alloc(_readBuffer, GCHandleType.Pinned);

                _attached = true;
                Logger.LogInfo($"[PhysicsBridge] Attached to shared memory block ({_blockSize} bytes).");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[PhysicsBridge] Failed to attach shared memory: {ex}");
                Detach();
            }
        }

        /// <summary>
        /// Spawns rust_daemon.exe as a detached child process if the shared memory
        /// file does not yet exist. Waits up to 5 seconds for the file to appear.
        /// </summary>
        private void EnsureDaemonRunning()
        {
            if (File.Exists(SharedMemoryFile))
                return; // daemon already running

            string kspRoot = AppContext.BaseDirectory;
            string daemonPath = Path.Combine(kspRoot, "PhysicsBridge", "rust_daemon.exe");

            if (!File.Exists(daemonPath))
            {
                Logger.LogError($"[PhysicsBridge] Rust daemon not found at: {daemonPath}");
                return;
            }

            Logger.LogInfo($"[PhysicsBridge] Auto-launching Rust daemon: {daemonPath}");

            var psi = new ProcessStartInfo
            {
                FileName = daemonPath,
                WorkingDirectory = kspRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };

            _daemonProc = Process.Start(psi);

            // Wait up to 5 seconds for the shared memory file to be created.
            for (int i = 0; i < 50; i++)
            {
                Thread.Sleep(100);
                if (File.Exists(SharedMemoryFile))
                {
                    Logger.LogInfo("[PhysicsBridge] Rust daemon started; shared memory file created.");
                    return;
                }
            }

            Logger.LogWarning("[PhysicsBridge] Timed out waiting for Rust daemon shared memory file.");
        }

        public void LateUpdate()
        {
            if (!_attached || _accessor == null || _readBuffer == null)
                return;

            // Bulk copy from the mapped file into the pinned buffer (no per-vessel marshaling).
            _accessor.ReadArray(0, _readBuffer, 0, _blockSize);

            unsafe
            {
                byte* basePtr = (byte*)_bufferHandle.AddrOfPinnedObject().ToPointer();
                var header = (PhysicsHeader*)basePtr;

                if (header->magic_header != MagicHeader)
                    return; // daemon not running or shutting down

                uint count = header->vessel_count;
                if (count > MaxVessels)
                    count = MaxVessels;

                var vessels = (VesselData*)(basePtr + _headerSize);

                for (int i = 0; i < count; i++)
                {
                    uint id = vessels[i].vessel_id;
                    if (id >= MaxVessels)
                        continue;

                    ref var vessel = ref vessels[i];
                    var go = GetOrCreateVesselObject(id);
                    if (go == null)
                        continue;

                    go.transform.position =
                        new Vector3(vessel.position[0], vessel.position[1], vessel.position[2]);
                    go.transform.rotation =
                        new Quaternion(vessel.rotation[0], vessel.rotation[1], vessel.rotation[2], vessel.rotation[3]);
                }
            }
        }

        private GameObject GetOrCreateVesselObject(uint id)
        {
            var go = _vesselObjects[id];
            if (go == null)
            {
                go = new GameObject($"Vessel_{id}");
                _vesselObjects[id] = go;
            }
            return go;
        }

        private void Detach()
        {
            _attached = false;
            if (_bufferHandle.IsAllocated)
                _bufferHandle.Free();
            _accessor?.Dispose();
            _accessor = null;
            _mmf?.Dispose();
            _mmf = null;

            // If we spawned the daemon, terminate it on shutdown.
            if (_daemonProc != null && !_daemonProc.HasExited)
            {
                try { _daemonProc.Kill(); } catch { /* ignore */ }
                _daemonProc.Dispose();
                _daemonProc = null;
            }
        }

        public void OnDestroy()
        {
            Detach();
        }

        public void OnApplicationQuit()
        {
            Detach();
        }
    }

    // ==================================================================
    // Binary contract (C-compatible). Must match rust_daemon/src/shared_structs.rs:
    //
    //   Rust Header (repr C): u32, u32, u32, [align pad], f64  -> 24 bytes
    //   Rust Vessel (repr C): u32, f32[3], f32[4], f32[3]      -> 44 bytes
    //   Total block: 24 + 64*44 = 2840 bytes
    // ==================================================================
    [StructLayout(LayoutKind.Sequential)]
    public struct PhysicsHeader
    {
        public uint magic_header;
        public uint frame_index;
        public uint vessel_count;
        public double timestamp;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct VesselData
    {
        public uint vessel_id;
        public fixed float position[3]; // x, y, z
        public fixed float rotation[4]; // x, y, z, w
        public fixed float velocity[3]; // vx, vy, vz
    }
}