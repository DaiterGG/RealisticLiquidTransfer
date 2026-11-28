extern Image densityField;
extern Image colorField;
extern Image opacityField;
extern vec2 tankOrigin;
extern vec2 tankSize;
extern vec2 fieldTexelSize;
extern vec2 transitionOffset;
extern number colorTransitionSharpness;

number getDensity(vec2 uv)
{
    return clamp(Texel(densityField, uv).r, 0.0, 1.0);
}

vec4 getLiquidColor(vec2 uv)
{
    vec4 accumulatedColor = Texel(colorField, uv);
    number opacityWeight = max(Texel(opacityField, uv).r, 0.0001);
    number densityWeight = max(Texel(densityField, uv).r, 0.0001);
    return vec4(
        accumulatedColor.rgb / opacityWeight,
        clamp(opacityWeight / densityWeight, 0.0, 1.0));
}

void considerLiquidColorBoundary(
    vec2 uv,
    vec2 offset,
    inout number strongestContrast,
    inout vec4 colorBefore,
    inout vec4 colorAfter)
{
    vec4 before = getLiquidColor(uv - offset);
    vec4 after = getLiquidColor(uv + offset);
    vec3 colorDifference = after.rgb * after.a - before.rgb * before.a;
    number contrast = dot(colorDifference, colorDifference);
    if (contrast > strongestContrast)
    {
        strongestContrast = contrast;
        colorBefore = before;
        colorAfter = after;
    }
}

vec4 sharpenLiquidColorTransition(vec2 uv, vec4 centerColor)
{
    const number inverseSqrtTwo = 0.70710678;
    vec2 horizontalOffset = vec2(transitionOffset.x, 0.0);
    vec2 verticalOffset = vec2(0.0, transitionOffset.y);
    vec2 diagonalOffset = (horizontalOffset + verticalOffset) * inverseSqrtTwo;
    vec2 oppositeDiagonalOffset = (horizontalOffset - verticalOffset) * inverseSqrtTwo;
    number strongestContrast = 0.0;
    vec4 colorBefore = centerColor;
    vec4 colorAfter = centerColor;

    considerLiquidColorBoundary(uv, horizontalOffset, strongestContrast, colorBefore, colorAfter);
    considerLiquidColorBoundary(uv, verticalOffset, strongestContrast, colorBefore, colorAfter);
    considerLiquidColorBoundary(uv, diagonalOffset, strongestContrast, colorBefore, colorAfter);
    considerLiquidColorBoundary(uv, oppositeDiagonalOffset, strongestContrast, colorBefore, colorAfter);

    if (strongestContrast < 0.0001)
    {
        return centerColor;
    }

    vec3 beforePremultiplied = colorBefore.rgb * colorBefore.a;
    vec3 afterPremultiplied = colorAfter.rgb * colorAfter.a;
    vec3 colorSpan = afterPremultiplied - beforePremultiplied;
    vec3 centerPremultiplied = centerColor.rgb * centerColor.a;
    number blend = clamp(
        dot(centerPremultiplied - beforePremultiplied, colorSpan) / strongestContrast,
        0.0,
        1.0);
    number sharpness = clamp(colorTransitionSharpness / 3.0, 0.0, 1.0);
    number transitionWidth = mix(1.0, 0.08, sharpness);
    number splineBlend = smoothstep(
        0.5 - transitionWidth * 0.5,
        0.5 + transitionWidth * 0.5,
        blend);
    number opacity = mix(colorBefore.a, colorAfter.a, splineBlend);
    vec3 premultipliedColor = mix(beforePremultiplied, afterPremultiplied, splineBlend);
    vec3 color = opacity > 0.0001
        ? premultipliedColor / opacity
        : vec3(0.0);
    return vec4(color, opacity);
}

vec4 effect(vec4 vertexColor, Image texture, vec2 texture_coords, vec2 screen_coords)
{
    vec2 uv = clamp((screen_coords - tankOrigin) / tankSize, 0.0, 1.0);
    number density = getDensity(uv);
    vec4 sampledLiquidColor = getLiquidColor(uv);
    if (colorTransitionSharpness > 0.0)
    {
        sampledLiquidColor = sharpenLiquidColorTransition(uv, sampledLiquidColor);
    }

    number leftDensity = getDensity(uv - vec2(fieldTexelSize.x, 0.0));
    number rightDensity = getDensity(uv + vec2(fieldTexelSize.x, 0.0));
    number downDensity = getDensity(uv - vec2(0.0, fieldTexelSize.y));
    number upDensity = getDensity(uv + vec2(0.0, fieldTexelSize.y));
    vec2 gradient = vec2(rightDensity - leftDensity, upDensity - downDensity);
    vec3 normal = normalize(vec3(-gradient * 5.0, 0.8));

    vec4 accumulatedDensity = Texel(densityField, uv);
    vec2 encodedVelocity = clamp(
        accumulatedDensity.gb / max(accumulatedDensity.r, 0.0001),
        0.0,
        1.0) * 2.0 - 1.0;
    number velocityLength = length(encodedVelocity);
    number velocityMagnitude = clamp(velocityLength, 0.0, 1.0);
    vec2 velocityDirection = encodedVelocity / max(velocityLength, 0.0001);
    number trailingEdge = clamp(dot(normal.xy, -velocityDirection), 0.0, 1.0)
        * velocityMagnitude;
    vec3 lightDirection = normalize(vec3(-0.45, 0.65, 0.8));
    number diffuse = 0.58 + 0.42 * clamp(dot(normal, lightDirection), 0.0, 1.0);
    vec3 halfDirection = normalize(lightDirection + vec3(0.0, 0.0, 1.0));
    number specular = pow(clamp(dot(normal, halfDirection), 0.0, 1.0), 28.0);
    number alpha = smoothstep(0.48, 0.52, density);
    number edge = 1.0 - smoothstep(0.55, 0.8, density);

    vec4 result = vec4(sampledLiquidColor.rgb, vertexColor.a * sampledLiquidColor.a);
    result.rgb *= diffuse;
    result.rgb = mix(result.rgb * 0.62, result.rgb, 1.0 - edge * 0.35);
    result.rgb = mix(
        result.rgb,
        result.rgb * vec3(0.62, 0.78, 0.96),
        trailingEdge * 0.35);
    result.rgb += specular * 0.28;
    result.a *= alpha;
    return result;
}
