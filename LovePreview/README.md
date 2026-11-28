# LÖVE shader hot-reload demo

Run this folder with LÖVE, for example:

```powershell
love .\LovePreview
```

The window renders a moving ripple shader. Save changes to `shader.glsl` to
reload it automatically, or press **R** to reload manually. If an edited
shader fails to compile, the demo keeps displaying the last working version
and prints the compiler error to the console.
