# ReDiv Login UI Generation

Generated as an original Japanese fantasy mobile RPG login UI kit for a 1920x1080 Unity canvas.

## Deliverables

- `login_ui_preview.png`: complete visual composition reference
- `login_background.png`: clean environment background
- `heroine_chroma.png`: original character source on chroma background
- `controls_sheet_chroma.png`: 3x2 component source sheet
- `icons_sheet_chroma.png`: 4x2 icon source sheet
- `decorations_sheet_chroma.png`: 4x2 decoration source sheet

Chroma sources are converted to transparent PNG and split into individual assets under:

`ReDiv_Online/Assets/Art/UI/Login/Generated/`

The generated images intentionally contain no UI labels. Add localized text in Unity with TextMeshPro.

## Generation

- CLI: bundled `image_gen.py`
- Model: `gpt-image-2`
- Style: original Japanese fantasy mobile RPG visual language
- Chroma key: `#FF00FF`, removed locally with the bundled `remove_chroma_key.py`

