# Vortex Launcher 0.2.10 — smoother animated capes, Vortex Client 0.2.6

## Vortex Client 0.2.6

- **Animated capes from a GIF now have a crop screen**, like photos: zoom, move and fit or fill, and a cape size of 128,
  256 or 512 wide (512 is the sharpest). The animation plays in the preview, and the same crop is used for every frame.
- **Every frame and the GIF's speed are kept**: up to 120 frames and down to 20 ms per frame. If the cape is too big for
  the service, the picture gets smaller first, and frames are dropped only as a last step.
- **GIFs of any shape work** (portrait or square); before, only frames shaped like a cape texture were accepted.
  WebP files cannot be read: the client says so and asks for a GIF.
- Fixed: saving an animated cape to the wardrobe failed with "Invalid collection entry".
- Everything from 0.2.5: Event Alerts, your skin in chat-head mods, animated custom capes.
- Existing instances: use **Update Vortex Client**. Worlds, mods and settings are preserved.

## Launcher

- No other launcher changes since 0.2.9.

## Notes

- Publishing animated capes with more than 32 frames, a size of 512 or a pace below 50 ms needs the cape service to be updated
  first; until then these capes work on your own computer only.
- Only Minecraft 26.3 is the main target; 26.2, 26.1.2, 26.1.1, 26.1 and 1.21.11 stay experimental.
- Checked by automated tests and a short test run of the new crop screen on 26.3; not played through on every version.
