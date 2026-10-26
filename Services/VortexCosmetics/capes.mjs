// Built-in cape ids shared by every service module. The client bundles their textures; the service only
// stores which one a player chose. Nebula, Flow and Pulse are animated (frames are in the client).
export const BUILT_IN_CAPES = ["none", "vortex", "aurora", "ember", "nebula", "flow", "pulse"];

// Animated custom cape: "VCA1", width, height, frames, milliseconds per frame (big-endian u32), then one zlib stream of
// frames*width*height*4 RGBA bytes. Every size, count and speed is bounded; the same rules apply on the client.
export const ANIMATED_MAX_BYTES = 1000000;
export async function validateAnimated(bytes) {
  if (bytes.length < 24 || bytes.length > ANIMATED_MAX_BYTES) throw new Error("size");
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const width = view.getUint32(4), height = view.getUint32(8), frames = view.getUint32(12), frameMs = view.getUint32(16);
  if (view.getUint32(0) !== 0x56434131 || ![64, 128, 256, 512].includes(width) || height !== width / 2 || frames < 2 || frames > 120 ||
      width * height * frames > 8000000 || frameMs < 20 || frameMs > 1000) throw new Error("header");
  const expected = width * height * 4 * frames;
  const reader = new Response(bytes.slice(20)).body.pipeThrough(new DecompressionStream("deflate")).getReader();
  let total = 0;
  try {
    for (;;) {
      const {value, done} = await reader.read(); if (done) break;
      total += value.length;
      if (total > expected) { await reader.cancel(); throw new Error("too many pixels"); }
    }
    if (total !== expected) throw new Error("truncated");
  } finally { reader.releaseLock(); }
}
