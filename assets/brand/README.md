# Shared application identity

`mark.svg` is the canonical production mark: a simple rounded remote silhouette, with a circular control and a small slot cut out. Brass `#D8B57A` on charcoal `#101113` matches the Android and Windows palette. Keep its proportions, negative space and rounded geometry across platforms. Action controls continue to use Android's existing Material Rounded family; system window controls retain their native appearance.

The built-in image generator produced `generated-concept.png` as the original visual concept. The production SVG simplifies its geometry into clean, flat shapes for reliable small-size rendering. The original raster is preserved and is not shipped as an application icon.

The generation prompt was: "Create a highly minimal, distinctive flat geometric emblem for Bedside Remote: a thick softly rounded vertical remote-control silhouette, a large circular negative-space center button, and one short horizontal negative-space slot above it. One single solid warm brass color #D8B57A, transparent background, centered with generous safety margin, no text, shadows, gradients, texture or mockup; recognizable at 16 pixels."

Run `powershell.exe -NoProfile -File scripts/generate-brand.ps1` from the repository root on Windows to regenerate PNG sizes, the multi-size Windows ICO, browser toolbar icons and Android foreground/monochrome vectors. This uses the built-in WPF vector renderer, with no added dependencies. The ICO includes 16, 20, 24, 32, 40, 48, 64, 128 and 256 pixel frames. Android uses adaptive masks and a monochrome layer for themed icons. The installer, executable, window, tray and browser popup use the same identity.

Browser fonts are the same bundled DM Sans and Manrope used by Android/Windows, with their original OFL notices included in Firefox/Edge packages and the AMO reviewer source archive.
