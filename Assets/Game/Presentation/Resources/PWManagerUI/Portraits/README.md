# Portrait parts

Source: `output/portrait-body-parts` and `output/portrait-women-parts`.

Run `node output/portrait-editor/import-unity.mjs` after adding parts to the source catalogs. It copies the current artwork, regenerates the shared part counts and hair/mask rules, excludes wrinkles, and retains only the completed standard/slim/heavy body presets. Keep existing category IDs and indices; append new parts to avoid changing saved appearances.

All layers share a 784×706 canvas. Do not trim individual textures or clip clothing to bare-body alpha: cups and straps intentionally draw over the arms. The compositor follows the source editor's order, material regions, closed eyes and face-specific mask/beard variants.

Player creation exposes the three named body choices and all appearance parts. Saved wrestler editing permits only clothes, masks, eyewear, headwear and their colors. Female clothing index 2 is the default sports bra and displays as `의상 1`; the empty clothing index is unavailable. Masks exclude eyewear, and full masks exclude headwear. Missing old-save appearances are seeded by wrestler ID; existing portrait resource paths are preserved.

Runtime textures are composed at 512×461 and reused when colors or wardrobe change. Raw reference artwork, backups and width slider variants are not game assets.

Color controls provide quick swatches, an expandable 44-color palette and custom hex input. Profile and editor portrait frames follow the illustration's aspect ratio. The wardrobe button is in the profile card header, and players can edit their own wardrobe without a contract.

Street parts were integrated on 2026-10-14: male hair 17–19, female hair 11–15, headwear 5–7, mask 12, male outfit 14/female outfit 17, male tattoos 13–14/female tattoos 10–11. Existing body-paint IDs remain reserved in tattoo slots and are excluded from selection and generation. Headwear primary/pattern colors are independent. The compositor uses face-specific cropped hair, dread hat variants, fitted bald beanies and matching rear-hair variants.

Unity 6000.5.4f1 passed 15 integration/compatibility tests using identical portrait source and texture copies in an isolated project. Reports are in `output/portrait-editor/unity-integration/street-parts-tests.xml`, `street-parts-compatibility-tests.xml` and `street-parts-verification.json`. The main Editor was already open without a reachable Pipeline server; its live UI was not inspected.
