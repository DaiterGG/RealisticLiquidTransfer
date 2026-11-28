local Simulation = {}
Simulation.__index = Simulation

Simulation.types = {
    { name = "Light", density = 0.65, hue = 0.54, color = { 0.28, 0.78, 1.0 } },
    { name = "Medium", density = 1.0, hue = 0.36, color = { 0.35, 0.9, 0.46 } },
    { name = "Heavy", density = 1.45, hue = 0.05, color = { 1.0, 0.42, 0.2 } },
}

local DROP_COUNT = 100
local GRAVITY = 430
local SORT_STRENGTH = 1900
local FIXED_STEP = 1 / 60
local RESTITUTION = 0.08

Simulation.defaultDropRadius = 8

local function clamp(value, minimum, maximum)
    return math.max(minimum, math.min(maximum, value))
end

function Simulation.new(bounds)
    local self = setmetatable({
        bounds = bounds,
        drops = {},
        accumulator = 0,
        dropRadius = Simulation.defaultDropRadius,
    }, Simulation)
    self:reset(bounds)
    return self
end

local function hueToColor(hue)
    local sector = hue * 6
    local index = math.floor(sector)
    local fraction = sector - index
    local p = 1 - 0.78
    local q = 1 - 0.78 * fraction
    local t = 1 - 0.78 * (1 - fraction)
    local colors = {
        { 1, t, p },
        { q, 1, p },
        { p, 1, t },
        { p, q, 1 },
        { t, p, 1 },
        { 1, p, q },
    }
    return colors[index % 6 + 1]
end
Simulation.colorForHue = hueToColor

function Simulation:setDropRadius(radius)
    self.dropRadius = clamp(radius, 4, 14)
    for _, drop in ipairs(self.drops) do
        drop.radius = self.dropRadius
        drop.x = clamp(drop.x, self.bounds.left + drop.radius, self.bounds.right - drop.radius)
        drop.y = clamp(drop.y, self.bounds.top + drop.radius, self.bounds.bottom - drop.radius)
    end
end

function Simulation:setTypeHue(kind, hue)
    local liquid = Simulation.types[kind]
    if not liquid then
        return false
    end

    liquid.hue = clamp(hue, 0, 1)
    local color = hueToColor(liquid.hue)
    liquid.color[1], liquid.color[2], liquid.color[3] = color[1], color[2], color[3]
    return true
end

function Simulation:setTypeDensity(kind, density)
    local liquid = Simulation.types[kind]
    if not liquid then
        return false
    end

    liquid.density = clamp(density, 0.4, 1.8)
    for _, drop in ipairs(self.drops) do
        if drop.kind == kind then
            drop.density = liquid.density
        end
    end
    return true
end

function Simulation:setBounds(bounds)
    self.bounds = bounds
    for _, drop in ipairs(self.drops) do
        drop.x = clamp(drop.x, bounds.left + drop.radius, bounds.right - drop.radius)
        drop.y = clamp(drop.y, bounds.top + drop.radius, bounds.bottom - drop.radius)
    end
end

function Simulation:reset(bounds)
    self:setBounds(bounds)
    self.drops = {}
    self.accumulator = 0

    local kinds = {}
    local countPerType = math.floor(DROP_COUNT / #Simulation.types)
    local remainder = DROP_COUNT % #Simulation.types
    for kind in ipairs(Simulation.types) do
        local count = countPerType + (kind <= remainder and 1 or 0)
        for _ = 1, count do
            kinds[#kinds + 1] = kind
        end
    end
    for index = #kinds, 2, -1 do
        local swapIndex = love.math.random(index)
        kinds[index], kinds[swapIndex] = kinds[swapIndex], kinds[index]
    end

    local width = bounds.right - bounds.left
    local spacing = self.dropRadius * 2 + 1
    local columns = math.max(1, math.min(34, math.floor(width / spacing)))
    local startX = (bounds.left + bounds.right - (columns - 1) * spacing) / 2
    local startY = bounds.top + self.dropRadius + 26

    for index, kind in ipairs(kinds) do
        local column = (index - 1) % columns
        local row = math.floor((index - 1) / columns)
        self:addDrop(
            kind,
            startX + column * spacing + love.math.random() * 0.4 - 0.2,
            startY + row * spacing + love.math.random() * 0.4 - 0.2
        )
    end
end

function Simulation:addDrop(kind, x, y)
    local liquid = Simulation.types[kind]
    if not liquid then
        return false
    end

    local bounds = self.bounds
    self.drops[#self.drops + 1] = {
        kind = kind,
        density = liquid.density,
        color = liquid.color,
        radius = self.dropRadius,
        x = clamp(x, bounds.left + self.dropRadius, bounds.right - self.dropRadius),
        y = clamp(y, bounds.top + self.dropRadius, bounds.bottom - self.dropRadius),
        vx = love.math.random() * 24 - 12,
        vy = 0,
    }
    return true
end

local function applyDensitySorting(drops, dt)
    for first = 1, #drops - 1 do
        local a = drops[first]
        for second = first + 1, #drops do
            local b = drops[second]
            local dx = b.x - a.x
            local dy = b.y - a.y
            local distanceSquared = dx * dx + dy * dy
            local range = a.radius + b.radius + 6

            if distanceSquared > 0 and distanceSquared < range * range then
                local upper, lower
                if a.y < b.y then
                    upper, lower = a, b
                else
                    upper, lower = b, a
                end

                local densityDifference = upper.density - lower.density
                if densityDifference > 0 then
                    local distance = math.sqrt(distanceSquared)
                    local proximity = 0.65 + 0.35 * (1 - distance / range)
                    local acceleration = densityDifference * SORT_STRENGTH * proximity
                    upper.vy = upper.vy + acceleration * dt
                    lower.vy = lower.vy - acceleration * dt
                end
            end
        end
    end
end

local function resolveCollisions(drops)
    for first = 1, #drops - 1 do
        local a = drops[first]
        for second = first + 1, #drops do
            local b = drops[second]
            local dx = b.x - a.x
            local dy = b.y - a.y
            local minimumDistance = a.radius + b.radius
            local distanceSquared = dx * dx + dy * dy

            if distanceSquared < minimumDistance * minimumDistance then
                local distance
                local nx, ny
                if distanceSquared < 0.0001 then
                    distance = 0
                    nx, ny = 1, 0
                else
                    distance = math.sqrt(distanceSquared)
                    nx, ny = dx / distance, dy / distance
                end

                local inverseMassA = 1 / a.density
                local inverseMassB = 1 / b.density
                local inverseMassSum = inverseMassA + inverseMassB
                local overlap = minimumDistance - distance
                local correction = overlap * 0.82 / inverseMassSum
                a.x = a.x - nx * correction * inverseMassA
                a.y = a.y - ny * correction * inverseMassA
                b.x = b.x + nx * correction * inverseMassB
                b.y = b.y + ny * correction * inverseMassB

                local relativeVelocityX = b.vx - a.vx
                local relativeVelocityY = b.vy - a.vy
                local approachSpeed = relativeVelocityX * nx + relativeVelocityY * ny
                if approachSpeed < 0 then
                    local impulse = -(1 + RESTITUTION) * approachSpeed / inverseMassSum
                    a.vx = a.vx - impulse * inverseMassA * nx
                    a.vy = a.vy - impulse * inverseMassA * ny
                    b.vx = b.vx + impulse * inverseMassB * nx
                    b.vy = b.vy + impulse * inverseMassB * ny
                end
            end
        end
    end
end

local function containDrop(drop, bounds)
    local left = bounds.left + drop.radius
    local right = bounds.right - drop.radius
    local top = bounds.top + drop.radius
    local bottom = bounds.bottom - drop.radius

    if drop.x < left then
        drop.x = left
        drop.vx = math.abs(drop.vx) * RESTITUTION
    elseif drop.x > right then
        drop.x = right
        drop.vx = -math.abs(drop.vx) * RESTITUTION
    end

    if drop.y < top then
        drop.y = top
        drop.vy = math.abs(drop.vy) * RESTITUTION
    elseif drop.y > bottom then
        drop.y = bottom
        drop.vy = -math.abs(drop.vy) * RESTITUTION
    end
end

function Simulation:update(dt)
    self.accumulator = self.accumulator + math.min(dt, 0.05)

    while self.accumulator >= FIXED_STEP do
        local step = FIXED_STEP
        self.accumulator = self.accumulator - step

        for _, drop in ipairs(self.drops) do
            drop.vy = drop.vy + GRAVITY * step
            drop.vx = drop.vx * 0.998
            drop.vy = drop.vy * 0.998
            drop.x = drop.x + drop.vx * step
            drop.y = drop.y + drop.vy * step
            containDrop(drop, self.bounds)
        end

        applyDensitySorting(self.drops, step)
        resolveCollisions(self.drops)
        resolveCollisions(self.drops)

        for _, drop in ipairs(self.drops) do
            containDrop(drop, self.bounds)
        end
    end
end

function Simulation:countByType()
    local counts = { 0, 0, 0 }
    for _, drop in ipairs(self.drops) do
        counts[drop.kind] = counts[drop.kind] + 1
    end
    return counts
end

return Simulation
