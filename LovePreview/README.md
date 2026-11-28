# LÖVE shader hot-reload demo

Run the PowerShell launcher from the repository root:

```powershell
.\LovePreview\Run.ps1
```

It uses `..\love-11.5-win64\love.exe` when present, or an installed `love.exe`.
To select another executable, pass `-LoveExecutable "C:\path\to\love.exe"`.

The window renders a moving ripple shader. Save changes to `shader.glsl` to
reload it automatically, or press **R** to reload manually. If an edited
shader fails to compile, the demo keeps displaying the last working version
and prints the compiler error to the console.
