extern vec2 center;
extern vec2 pixelScale;
extern vec2 velocity;
extern number dropOpacity;
extern number dropGroup;
extern number dropRadius;
extern number fieldMode;

vec4 effect(vec4 color, Image texture, vec2 texture_coords, vec2 screen_coords)
{
    vec2 offset = (screen_coords - center) / pixelScale;
    number supportRadius = dropRadius * 2.2;
    number speed = length(velocity);
    bool hasDirection = speed > 0.001;
    vec2 direction = hasDirection ? velocity / speed : vec2(0.0);
    number tailLength = hasDirection
        ? dropRadius * 2.5 * clamp(speed / 600.0, 0.0, 1.0)
        : 0.0;

    number normalizedDistanceSquared;
    if (hasDirection)
    {
        number along = dot(offset, direction) + tailLength * 0.5;
        number across = -offset.x * direction.y + offset.y * direction.x;
        number majorRadius = supportRadius + tailLength * 0.5;
        normalizedDistanceSquared = along * along / (majorRadius * majorRadius)
            + across * across / (supportRadius * supportRadius);
    }
    else
    {
        normalizedDistanceSquared = dot(offset, offset) / (supportRadius * supportRadius);
    }

    if (normalizedDistanceSquared >= 1.0)
    {
        return vec4(0.0);
    }

    number remaining = 1.0 - normalizedDistanceSquared;
    number contribution = 1.2 * remaining * remaining;
    if (fieldMode < 0.5)
    {
        vec2 velocityRatio = clamp(velocity / 800.0, -1.0, 1.0);
        vec2 encodedVelocity = velocityRatio * 0.5 + 0.5;
        return vec4(
            contribution,
            encodedVelocity.x * contribution,
            encodedVelocity.y * contribution,
            0.0);
    }

    if (fieldMode < 1.5)
    {
        vec3 groupWeights = vec3(0.0);
        if (dropGroup < 0.5)
        {
            groupWeights.r = dropOpacity * contribution;
        }
        else if (dropGroup < 1.5)
        {
            groupWeights.g = dropOpacity * contribution;
        }
        else
        {
            groupWeights.b = dropOpacity * contribution;
        }
        return vec4(groupWeights, 0.0);
    }

    return vec4(dropOpacity * contribution, 0.0, 0.0, 0.0);
}
