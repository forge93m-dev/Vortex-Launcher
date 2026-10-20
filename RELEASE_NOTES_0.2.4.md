# Vortex Launcher 0.2.4 — Showcase & Launcher Wardrobe

## Launcher

- Showcase library layout: full-width featured instance, Play and instance management on its cover, and a compact horizontal collection below it. Keyboard arrows navigate the virtualized collection without creating hundreds of hidden cards; smaller windows can scroll without hiding actions.
- New Wardrobe entry in the launcher. Sign in to Vortex and select **Use nickname for Minecraft** in Profiles, then open Wardrobe.
- Import and retain skin/cape PNGs by account. Preview real texture UVs, switch front/back, select slim arms, choose bundled capes or restore the original appearance. Arbitrary cape pictures are fitted onto both cape faces; source files are never modified.
- Apply online changes only the selected cosmetic and checks the server response. Matching legacy launcher and Java offline-server UUID aliases are both published and checked, subject to the service's ownership checks. No activation code is needed for a new owned Vortex nickname. Existing protected profiles still require their original ownership key.
- Custom instance covers take precedence; otherwise the launcher can use a PNG from that instance's screenshots folder. No AI-generated artwork or private gameplay screenshots are bundled.
- Recognized Forge, NeoForge or Quilt metadata in an instance using that same loader is informational rather than an automatic warning dialog. Full dependency validation still belongs to the loader; unreadable archives and foreign-loader warnings remain visible.

## Vortex Client 0.2.1

- Showcase-inspired main/pause menu controls and compact two-column module cards with labeled states and switches. Wardrobe keeps its separate layout; inventory and hotbar are unchanged.
- Account appearance can load before joining a world. An online account skin takes precedence over an older local skin until you apply a new local selection during that session. Failed cape texture downloads remain retryable.
- Exact Fabric builds: 26.3, 26.2, 26.1, 26.1.1, 26.1.2 and 1.21.11. New ports remain experimental: automated compilation, mixin transformation and initialization checks passed; they have not received complete real-world gameplay/multiplayer/macOS testing. 1.21.10 and 1.21.9 are not supported yet.
- Existing instances: use **Update Vortex Client** in the instance tools to install the new bundled client. Launcher and client updates are separate.

## macOS

- ARM64 and Intel application packages include a signed microphone permission helper, a microphone purpose description and the audio-input entitlement.
- Launching an enabled Simple Voice Chat JAR requests microphone permission when needed. Choose **Allow** in the macOS prompt. If previously denied, enable **Vortex Launcher** under **System Settings → Privacy & Security → Microphone**, then restart Minecraft.
- A denial or unavailable helper is explained in the game console; the launcher does not disable the mod's permission check or change system privacy settings.
- Native package/signature/startup checks are automated on both Mac architectures. Actual recording and TCC permission inheritance must still be confirmed on a user's Mac. Beta packages are ad-hoc signed, not Apple-notarized; Gatekeeper may still require per-app Open Anyway.

Online cosmetics are Vortex cosmetics, not Microsoft account skin changes. Other players need a compatible Vortex Client; a launcher's nickname alone is not proof of ownership on an arbitrary offline server.
