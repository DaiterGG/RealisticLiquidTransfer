extern Image densityField;
extern Image colorField;
extern Image opacityField;
extern vec2 tankOrigin;
extern vec2 tankSize;
extern vec2 fieldTexelSize;
extern vec3 lightColor;
extern vec3 mediumColor;
extern vec3 heavyColor;

number getDensity(vec2 uv)
{
    return clamp(Texel(densityField, uv).r, 0.0, 1.0);
}

vec4 getLiquidColor(vec2 uv)
{
    vec3 groupWeights = Texel(colorField, uv).rgb;
    number opacityWeight = max(Texel(opacityField, uv).r, 0.0001);
    number densityWeight = max(Texel(densityField, uv).r, 0.0001);
    vec3 selectedColor = lightColor;
    if (groupWeights.g > groupWeights.r && groupWeights.g >= groupWeights.b)
    {
        selectedColor = mediumColor;
    }
    else if (groupWeights.b > groupWeights.r && groupWeights.b > groupWeights.g)
    {
        selectedColor = heavyColor;
    }

    return vec4(selectedColor, clamp(opacityWeight / densityWeight, 0.0, 1.0));
}

vec4 effect(vec4 vertexColor, Image texture, vec2 texture_coords, vec2 screen_coords)
{
    vec2 uv = clamp((screen_coords - tankOrigin) / tankSize, 0.0, 1.0);
    number density = getDensity(uv);
    vec4 sampledLiquidColor = getLiquidColor(uv);

    number leftDensity = getDensity(uv - vec2(fieldTexelSize.x, 0.0));
    number rightDensity = getDensity(uv + vec2(fieldTexelSize.x, 0.0));
    number downDensity = getDensity(uv - vec2(0.0, fieldTexelSize.y));
    number upDensity = getDensity(uv + vec2(0.0, fieldTexelSize.y));
    vec2 gradient = vec2(rightDensity - leftDensity, upDensity - downDensity);
    vec3 normal = normalize(vec3(-gradient * 5.0, 0.8));
    number pixelLeftDensity = getDensity(uv - vec2(1.0 / tankSize.x, 0.0));
    number pixelRightDensity = getDensity(uv + vec2(1.0 / tankSize.x, 0.0));
    number pixelDownDensity = getDensity(uv - vec2(0.0, 1.0 / tankSize.y));
    number pixelUpDensity = getDensity(uv + vec2(0.0, 1.0 / tankSize.y));
    number screenDensityDerivative = max(
        max(
            abs(pixelRightDensity - pixelLeftDensity),
            abs(pixelUpDensity - pixelDownDensity))
            * 0.5,
        0.0005);
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
    number alpha = step(0.5, density);
    number pixelsFromEdge = (density - 0.5) / screenDensityDerivative;
    number edge = 1.0 - smoothstep(0.0, 2.0, pixelsFromEdge);

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
