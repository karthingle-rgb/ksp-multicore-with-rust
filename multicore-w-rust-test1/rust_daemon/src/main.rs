mod shared_structs;

use shared_structs::*;
use memmap2::MmapMut;
use rayon::prelude::*;
use std::io;
use std::sync::{Arc, atomic::{AtomicBool, Ordering}};
use std::time::{Duration, Instant};

const SHM_PATH: &str = "ksp_physics_bridge.dat";
const TARGET_HZ: f64 = 200.0;
const DT: f64 = 1.0 / 200.0;

fn create_shared_memory() -> io::Result<MmapMut> {
    let file = std::fs::OpenOptions::new()
        .read(true)
        .write(true)
        .create(true)
        .truncate(true)
        .open(SHM_PATH)?;
    file.set_len(SharedPhysicsBlock::SIZE as u64)?;
    // SAFETY: the file is exactly SharedPhysicsBlock::SIZE bytes and the OS gives us
    // page-aligned, readable/writable memory, which safely casts to the C-repr struct.
    unsafe { MmapMut::map_mut(&file) }
}

fn init_block(block: &mut SharedPhysicsBlock) {
    block.header.magic_header = MAGIC_HEADER;
    block.header.frame_index = 0;
    block.header.vessel_count = MAX_VESSELS as u32;
    block.header.timestamp = 0.0;

    for (i, vessel) in block.vessels.iter_mut().enumerate() {
        let id = i as f32;
        let phase = id * 0.17;
        vessel.vessel_id = i as u32;
        vessel.position = [1000.0 * phase.sin(), 150.0, 1000.0 * phase.cos()];
        vessel.rotation = [0.0, 0.0, 0.0, 1.0];
        vessel.velocity = [40.0 + id * 2.0, 0.0, -15.0 + id * 1.0];
    }
}

fn run() -> io::Result<()> {
    println!("KSP Physics Bridge daemon - Approach 4 (process decoupling)");
    println!("Shared memory block: {} bytes @ {}", SharedPhysicsBlock::SIZE, SHM_PATH);
    println!("Simulation target: {:.0} Hz (dt = {:.6} s, parallel via Rayon)", TARGET_HZ, DT);
    println!("Press Ctrl-C to stop.");

    let mut mmap = create_shared_memory()?;
    let block_ptr = mmap.as_mut_ptr() as *mut SharedPhysicsBlock;

    {
        let block = unsafe { &mut *block_ptr };
        init_block(block);
    }
    mmap.flush()?;

    let running = Arc::new(AtomicBool::new(true));
    let running_ctrlc = Arc::clone(&running);
    ctrlc::set_handler(move || {
        let _ = running_ctrlc.store(false, Ordering::SeqCst);
    }).map_err(|e| io::Error::new(io::ErrorKind::Other, e))?;

    let start = Instant::now();
    let mut frame_index: u32 = 0;
    let period = Duration::from_secs_f64(1.0 / TARGET_HZ);

    while running.load(Ordering::SeqCst) {
        let frame_start = Instant::now();

        // --- Data-parallel physics integration across all CPU cores ---
        {
            let block = unsafe { &mut *block_ptr };
            block.vessels.par_iter_mut().for_each(|vessel| {
                let id = vessel.vessel_id as f32;
                let dt = DT as f32;

                // Kinematic integration: x' = x + v * dt (stand-in for KSP physics)
                vessel.position[0] += vessel.velocity[0] * dt + (id * 0.01).sin() * 0.05;
                vessel.position[1] += vessel.velocity[1] * dt - 9.81 * dt * 0.25;
                vessel.position[2] += vessel.velocity[2] * dt;

                // Keep the demo bounded around the surface
                if vessel.position[1] < 0.0 {
                    vessel.position[1] = 0.0;
                    vessel.velocity[1] = 0.0;
                }

                // Slow attitude drift for visual interest
                vessel.rotation[1] += 0.25 * dt;
                if vessel.rotation[1] > 6.283185307 {
                    vessel.rotation[1] -= 6.283185307;
                }
            });

            block.header.frame_index = frame_index;
            block.header.timestamp = start.elapsed().as_secs_f64();
        }

        mmap.flush()?;
        frame_index = frame_index.wrapping_add(1);

        // Rate-limit to the target tick rate without busy-spinning.
        let elapsed = frame_start.elapsed();
        if elapsed < period {
            std::thread::sleep(period - elapsed);
        }
    }

    // Shutdown: invalidate the magic header so Unity detaches cleanly.
    {
        let block = unsafe { &mut *block_ptr };
        block.header.magic_header = 0;
    }
    mmap.flush()?;
    println!("Daemon stopped.");
    Ok(())
}

fn main() {
    if let Err(e) = run() {
        eprintln!("Fatal: {}", e);
        std::process::exit(1);
    }
}