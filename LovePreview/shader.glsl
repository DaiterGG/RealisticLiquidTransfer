extern number elapsed;
extern vec2 resolution;

vec4 effect(vec4 color, Image texture, vec2 texture_coords, vec2 screen_coords)
{
    vec2 uv = screen_coords / resolution;
    vec2 p = uv * 2.0 - 1.0;
    p.x *= resolution.x / resolution.y;

    number wave = sin(p.y * 8.0 + sin(p.x * 3.0 + elapsed) * 1.4 - elapsed * 1.8);
    number ripple = smoothstep(0.72, 0.98, wave);
    number glow = 0.5 + 0.5 * sin(p.x * 2.0 - p.y * 3.0 + elapsed * 0.7);

    vec3 deep = vec3(0.015, 0.08, 0.18);
    vec3 liquid = vec3(0.02, 0.42, 0.68);
    vec3 highlight = vec3(0.25, 0.86, 0.94);
    vec3 result = mix(deep, liquid, 0.45 + glow * 0.3);
    result = mix(result, highlight, ripple * 0.8);

    return vec4(result, 1.0) * color;
}
