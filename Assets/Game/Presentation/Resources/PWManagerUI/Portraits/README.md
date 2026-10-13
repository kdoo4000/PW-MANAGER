# Portrait parts

Source: `output/portrait-body-parts` and `output/portrait-women-parts`.

Run `node output/portrait-editor/import-unity.mjs` after adding parts to the source catalogs. It copies the current artwork, regenerates the shared part counts and hair/mask rules, excludes wrinkles, and retains only the completed standard/slim/heavy body presets. Keep existing category IDs and indices; append new parts to avoid changing saved appearances.

All layers share a 784×706 canvas. Do not trim individual textures or clip clothing to bare-body alpha: cups and straps intentionally draw over the arms. The compositor follows the source editor's order, material regions, closed eyes and face-specific mask/beard variants.

Player creation exposes the three named body choices and all appearance parts. Saved wrestler editing permits only clothes, masks, eyewear, headwear and their colors. Female clothing index 2 is the default sports bra and displays as `의상 1`; the empty clothing index is unavailable. Masks exclude eyewear, and full masks exclude headwear. Missing old-save appearances are seeded by wrestler ID; existing portrait resource paths are preserved.

Runtime textures are composed at 512×461 and reused when colors or wardrobe change. Raw reference artwork, backups and width slider variants are not game assets.

Color controls provide quick swatches, an expandable 44-color palette and custom hex input. Profile and editor portrait frames follow the illustration's aspect ratio. The wardrobe button is in the profile card header, and players can edit their own wardrobe without a contract.
