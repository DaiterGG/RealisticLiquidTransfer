# LÖVE shader hot-reload demo

Run the PowerShell launcher from the repository root:

```powershell
.\LovePreview\Run.ps1
```

It uses `..\love-11.5-win64\love.exe` when present, or an installed
`love.exe`. To select another executable, pass
`-LoveExecutable "C:\path\to\love.exe"`.

The preview starts with 198 drops split evenly between three liquid types.
Each type has its own color and density; droplets collide with each other and
the tank, and nearby density inversions get a buoyancy-like push so denser
liquid settles below lighter liquid. The bottom controls adjust the 4–42 px
drop radius and each group's hue and density.

Use **1–3** to select a liquid and click inside the tank to add a drop.
**Space** pauses, **Backspace** resets, and **R** reloads `shader.glsl`. That
file is a LÖVE port of the mod's merged density-field surface shader and
automatically reloads when saved. `liquid_field.glsl` builds the per-drop
density, color, and velocity textures used by the surface shader.
