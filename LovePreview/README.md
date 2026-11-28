# LÖVE shader hot-reload demo

Run the PowerShell launcher from the repository root:

```powershell
.\LovePreview\Run.ps1
```

It uses `..\love-11.5-win64\love.exe` when present, or an installed
`love.exe`. To select another executable, pass
`-LoveExecutable "C:\path\to\love.exe"`.

The preview starts with 100 drops split evenly between three liquid types.
Each type has its own color and density; droplets collide with each other and
the tank, and nearby density inversions get a buoyancy-like push so denser
liquid settles below lighter liquid.

Use **1–3** to select a liquid and click inside the tank to add a drop. The
bottom controls adjust drop radius and each liquid's color hue and density.
**Space** pauses, **Backspace** resets, and **R** reloads `shader.glsl`. The
animated background shader also reloads automatically when saved; a compile
failure keeps the last working shader and prints its error to the console.
