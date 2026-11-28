local Simulation = require("liquid_simulation")

local shaderPath = "shader.glsl"
local fieldShaderPath = "liquid_field.glsl"
local shader
local fieldShader
local lastSource
local simulation
local densityCanvas
local colorCanvas
local opacityCanvas
local densityCanvasWidth
local densityCanvasHeight
local selectedKind = 1
local reloadTimer = 0
local reloadInterval = 0.25
local paused = false
local shaderEnabled = true
local status = "Loading shader..."
local draggingControl
local fieldPixelsPerUnit = 0.5
local edgeBlur = 1.5

local function clamp(value, minimum, maximum)
    return math.max(minimum, math.min(maximum, value))
end

local function tankBounds()
    local width, height = love.graphics.getDimensions()
    return {
        left = 32,
        top = 84,
        right = width - 32,
        bottom = height - 158,
    }
end

local function ensureLiquidCanvases(bounds)
    local width = math.max(1, math.ceil((bounds.right - bounds.left) * fieldPixelsPerUnit))
    local height = math.max(1, math.ceil((bounds.bottom - bounds.top) * fieldPixelsPerUnit))
    if width == densityCanvasWidth and height == densityCanvasHeight then
        return
    end

    local newDensityCanvas = love.graphics.newCanvas(width, height, { format = "rgba16f" })
    local newColorCanvas = love.graphics.newCanvas(width, height, { format = "rgba16f" })
    local newOpacityCanvas = love.graphics.newCanvas(width, height, { format = "rgba16f" })
    newDensityCanvas:setFilter("linear", "linear")
    newColorCanvas:setFilter("linear", "linear")
    newOpacityCanvas:setFilter("linear", "linear")
    newDensityCanvas:setWrap("clamp", "clamp")
    newColorCanvas:setWrap("clamp", "clamp")
    newOpacityCanvas:setWrap("clamp", "clamp")

    if densityCanvas then
        densityCanvas:release()
        colorCanvas:release()
        opacityCanvas:release()
    end

    densityCanvas = newDensityCanvas
    colorCanvas = newColorCanvas
    opacityCanvas = newOpacityCanvas
    densityCanvasWidth = width
    densityCanvasHeight = height
end

local function renderLiquidFields(bounds)
    ensureLiquidCanvases(bounds)
    local boundsWidth = bounds.right - bounds.left
    local boundsHeight = bounds.bottom - bounds.top
    local scaleX = (densityCanvasWidth - 1) / boundsWidth
    local scaleY = (densityCanvasHeight - 1) / boundsHeight

    love.graphics.push("all")
    love.graphics.setBlendMode("add", "premultiplied")
    love.graphics.setShader(fieldShader)
    love.graphics.setColor(1, 1, 1, 1)

    for mode = 0, 2 do
        local target = mode == 0 and densityCanvas
            or mode == 1 and colorCanvas
            or opacityCanvas
        love.graphics.setCanvas(target)
        love.graphics.clear(0, 0, 0, 0)
        fieldShader:send("fieldMode", mode)

        for _, drop in ipairs(simulation.drops) do
            local centerX = (drop.x - bounds.left) * scaleX
            local centerY = (drop.y - bounds.top) * scaleY
            local supportRadius = drop.radius * 2.2
            local speed = math.sqrt(drop.vx * drop.vx + drop.vy * drop.vy)
            local tailLength = drop.radius * 2.5 * clamp(speed / 600, 0, 1)
            local extent = supportRadius + tailLength

            fieldShader:send("center", { centerX, centerY })
            fieldShader:send("pixelScale", { scaleX, scaleY })
            fieldShader:send("velocity", { drop.vx, drop.vy })
            fieldShader:send("dropOpacity", drop.opacity)
            fieldShader:send("dropGroup", drop.kind - 1)
            fieldShader:send("dropRadius", drop.radius)
            love.graphics.rectangle(
                "fill",
                centerX - extent * scaleX,
                centerY - extent * scaleY,
                extent * 2 * scaleX,
                extent * 2 * scaleY
            )
        end
    end

    love.graphics.setCanvas()
    love.graphics.pop()
end

local function controlLayout()
    local height = love.graphics.getHeight()
    return {
        panelTop = height - 146,
        size = { x = 150, y = height - 124, width = 240 },
        edgeBlur = { labelX = 480, x = 555, y = height - 124, width = 180 },
        groupStartY = height - 92,
        groupStep = 30,
        hue = { labelX = 110, x = 150, width = 140 },
        saturation = { labelX = 310, x = 350, width = 100 },
        value = { labelX = 470, x = 510, width = 100 },
        opacity = { labelX = 630, x = 700, width = 100 },
        density = { labelX = 815, x = 880, width = 140 },
    }
end

local function loadFieldShader()
    local source, readError = love.filesystem.read(fieldShaderPath)
    if not source then
        error("Could not read liquid field shader: " .. tostring(readError))
    end

    local ok, result = pcall(love.graphics.newShader, source)
    if not ok then
        error("Could not compile liquid field shader: " .. tostring(result))
    end
    fieldShader = result
end

local function sliderValue(slider, x)
    return clamp((x - slider.x) / slider.width, 0, 1)
end

local function setControlValue(control, x)
    local value = sliderValue(control.slider, x)
    if control.kind == "size" then
        simulation:setDropRadius(4 + value * 38)
    elseif control.kind == "edgeBlur" then
        edgeBlur = value * 6
    elseif control.kind == "hue" then
        simulation:setTypeHue(control.group, value)
    elseif control.kind == "saturation" then
        simulation:setTypeSaturation(control.group, value)
    elseif control.kind == "value" then
        simulation:setTypeValue(control.group, value)
    elseif control.kind == "opacity" then
        simulation:setTypeOpacity(control.group, 1 - value)
    elseif control.kind == "density" then
        simulation:setTypeDensity(control.group, 0.4 + value * 1.4)
    end
end

local function drawSlider(slider, value, color)
    love.graphics.setColor(0.24, 0.31, 0.36, 1)
    love.graphics.rectangle("fill", slider.x, slider.y - 3, slider.width, 6, 3, 3)

    love.graphics.setColor(color[1], color[2], color[3], 0.95)
    love.graphics.rectangle(
        "fill",
        slider.x,
        slider.y - 3,
        slider.width * value,
        6,
        3,
        3
    )

    local thumbX = slider.x + value * slider.width
    love.graphics.setColor(1, 1, 1, 1)
    love.graphics.circle("fill", thumbX, slider.y, 5)
    love.graphics.setColor(0.04, 0.08, 0.11, 1)
    love.graphics.circle("line", thumbX, slider.y, 5)
end

local function drawHueSlider(slider, hue, saturation, brightness)
    local segmentCount = 35
    local segmentWidth = slider.width / segmentCount
    for segment = 0, segmentCount - 1 do
        local color = Simulation.colorForHue(segment / segmentCount, saturation, brightness)
        love.graphics.setColor(color[1], color[2], color[3], 1)
        love.graphics.rectangle(
            "fill",
            slider.x + segment * segmentWidth,
            slider.y - 4,
            segmentWidth + 1,
            8
        )
    end

    local thumbX = slider.x + hue * slider.width
    love.graphics.setColor(1, 1, 1, 1)
    love.graphics.circle("fill", thumbX, slider.y, 5)
    love.graphics.setColor(0.04, 0.08, 0.11, 1)
    love.graphics.circle("line", thumbX, slider.y, 5)
end

local function controlAt(x, y)
    local layout = controlLayout()
    local sizeSlider = {
        x = layout.size.x,
        y = layout.size.y,
        width = layout.size.width,
    }
    if math.abs(y - sizeSlider.y) <= 11 and x >= sizeSlider.x - 6 and x <= sizeSlider.x + sizeSlider.width + 6 then
        return { kind = "size", slider = sizeSlider }
    end

    local edgeBlurSlider = layout.edgeBlur
    if math.abs(y - edgeBlurSlider.y) <= 11
        and x >= edgeBlurSlider.x - 6
        and x <= edgeBlurSlider.x + edgeBlurSlider.width + 6
    then
        return { kind = "edgeBlur", slider = edgeBlurSlider }
    end

    for group = 1, #Simulation.types do
        local rowY = layout.groupStartY + (group - 1) * layout.groupStep
        local hueSlider = { x = layout.hue.x, y = rowY, width = layout.hue.width }
        if math.abs(y - rowY) <= 11 and x >= hueSlider.x - 6 and x <= hueSlider.x + hueSlider.width + 6 then
            return { kind = "hue", group = group, slider = hueSlider }
        end

        local saturationSlider = { x = layout.saturation.x, y = rowY, width = layout.saturation.width }
        if math.abs(y - rowY) <= 11 and x >= saturationSlider.x - 6 and x <= saturationSlider.x + saturationSlider.width + 6 then
            return { kind = "saturation", group = group, slider = saturationSlider }
        end

        local valueSlider = { x = layout.value.x, y = rowY, width = layout.value.width }
        if math.abs(y - rowY) <= 11 and x >= valueSlider.x - 6 and x <= valueSlider.x + valueSlider.width + 6 then
            return { kind = "value", group = group, slider = valueSlider }
        end

        local opacitySlider = { x = layout.opacity.x, y = rowY, width = layout.opacity.width }
        if math.abs(y - rowY) <= 11 and x >= opacitySlider.x - 6 and x <= opacitySlider.x + opacitySlider.width + 6 then
            return { kind = "opacity", group = group, slider = opacitySlider }
        end

        local densitySlider = { x = layout.density.x, y = rowY, width = layout.density.width }
        if math.abs(y - rowY) <= 11 and x >= densitySlider.x - 6 and x <= densitySlider.x + densitySlider.width + 6 then
            return { kind = "density", group = group, slider = densitySlider }
        end
    end
end

local function reloadShader()
    local source, readError = love.filesystem.read(shaderPath)
    if not source then
        status = "Could not read shader: " .. tostring(readError)
        print("[shader] " .. status)
        return false
    end

    lastSource = source

    local ok, result = pcall(love.graphics.newShader, source)
    if not ok then
        status = "Compile failed; keeping last working shader"
        print("[shader] " .. status .. ": " .. tostring(result))
        return false
    end

    shader = result
    status = "Shader loaded"
    print("[shader] " .. status)
    return true
end

function love.load()
    love.graphics.setBackgroundColor(0, 0, 0, 1)
    love.math.setRandomSeed(os.time())
    simulation = Simulation.new(tankBounds())
    loadFieldShader()
    reloadShader()
    if not shader then
        error(status)
    end
end

function love.update(dt)
    reloadTimer = reloadTimer + dt

    if reloadTimer >= reloadInterval then
        reloadTimer = 0
        local source = love.filesystem.read(shaderPath)
        if source and source ~= lastSource then
            reloadShader()
        end
    end

    simulation:setBounds(tankBounds())
    if not paused then
        simulation:update(dt)
    end
end

function love.draw()
    local bounds = tankBounds()
    local counts = simulation:countByType()
    local types = Simulation.types

    love.graphics.setColor(0, 0, 0, 1)
    love.graphics.rectangle(
        "fill",
        bounds.left,
        bounds.top,
        bounds.right - bounds.left,
        bounds.bottom - bounds.top
    )
    love.graphics.setLineWidth(2)
    love.graphics.setColor(0.56, 0.76, 0.84, 0.95)
    love.graphics.rectangle(
        "line",
        bounds.left,
        bounds.top,
        bounds.right - bounds.left,
        bounds.bottom - bounds.top
    )

    if shaderEnabled then
        renderLiquidFields(bounds)
    else
        for _, drop in ipairs(simulation.drops) do
            love.graphics.setColor(
                drop.color[1],
                drop.color[2],
                drop.color[3],
                drop.opacity
            )
            love.graphics.circle("fill", drop.x, drop.y, drop.radius)
            love.graphics.setColor(1, 1, 1, drop.opacity)
            love.graphics.circle("line", drop.x, drop.y, drop.radius)
        end
    end

    if shaderEnabled and shader then
        shader:send("densityField", densityCanvas)
        shader:send("colorField", colorCanvas)
        shader:send("opacityField", opacityCanvas)
        shader:send("tankOrigin", { bounds.left, bounds.top })
        shader:send(
            "tankSize",
            { bounds.right - bounds.left, bounds.bottom - bounds.top }
        )
        shader:send(
            "fieldTexelSize",
            { 1 / densityCanvasWidth, 1 / densityCanvasHeight }
        )
        shader:send("lightColor", Simulation.types[1].color)
        shader:send("mediumColor", Simulation.types[2].color)
        shader:send("heavyColor", Simulation.types[3].color)
        shader:send("edgeBlur", edgeBlur)

        love.graphics.setBlendMode("alpha", "alphamultiply")
        love.graphics.setShader(shader)
        love.graphics.setColor(1, 1, 1, 1)
        love.graphics.rectangle(
            "fill",
            bounds.left,
            bounds.top,
            bounds.right - bounds.left,
            bounds.bottom - bounds.top
        )
        love.graphics.setShader()
    end

    love.graphics.setColor(1, 1, 1, 1)
    love.graphics.print("LIQUID SANDBOX  |  " .. #simulation.drops .. " drops", 34, 12)
    love.graphics.setColor(0.16, 0.18, 0.2, 1)
    love.graphics.rectangle("fill", 270, 12, 16, 16, 2, 2)
    love.graphics.setColor(0.78, 0.84, 0.86, 1)
    love.graphics.rectangle("line", 270, 12, 16, 16, 2, 2)
    if shaderEnabled then
        love.graphics.setColor(0.5, 0.95, 0.65, 1)
        love.graphics.line(273, 20, 277, 24, 284, 15)
    end
    love.graphics.setColor(1, 1, 1, 1)
    love.graphics.print("Surface shader", 294, 12)

    for kind, liquid in ipairs(types) do
        local x = 36 + (kind - 1) * 185
        if selectedKind == kind then
            love.graphics.setColor(1, 1, 1, 0.18)
            love.graphics.rectangle("fill", x - 5, 37, 174, 27, 4, 4)
        end
        love.graphics.setColor(0.34, 0.4, 0.44, 1)
        love.graphics.circle("fill", x + 5, 50, 6)
        love.graphics.setColor(
            liquid.color[1],
            liquid.color[2],
            liquid.color[3],
            liquid.opacity
        )
        love.graphics.circle("fill", x + 5, 50, 5)
        love.graphics.setColor(1, 1, 1, 1)
        love.graphics.print(
            string.format("%s  %.2fx  (%d)", liquid.name, liquid.density, counts[kind]),
            x + 16,
            42
        )
    end

    love.graphics.setColor(1, 1, 1, 0.9)
    love.graphics.print(
        "1-3 select  |  click tank adds a drop  |  Space pause  |  Backspace reset  |  R reload shader",
        34,
        64
    )

    local layout = controlLayout()
    love.graphics.setColor(0.035, 0.035, 0.035, 0.98)
    love.graphics.rectangle(
        "fill",
        32,
        layout.panelTop,
        love.graphics.getWidth() - 64,
        136,
        7,
        7
    )
    love.graphics.setColor(0.38, 0.56, 0.64, 0.9)
    love.graphics.rectangle(
        "line",
        32,
        layout.panelTop,
        love.graphics.getWidth() - 64,
        136,
        7,
        7
    )

    love.graphics.setColor(1, 1, 1, 1)
    love.graphics.print("Drop radius", 43, layout.size.y - 7)
    local radiusValue = (simulation.dropRadius - 4) / 38
    drawSlider(layout.size, radiusValue, { 0.82, 0.91, 0.95 })
    love.graphics.print(string.format("%.1f px", simulation.dropRadius), 400, layout.size.y - 7)

    love.graphics.setColor(1, 1, 1, 1)
    love.graphics.print("Edge blur", layout.edgeBlur.labelX, layout.edgeBlur.y - 7)
    drawSlider(layout.edgeBlur, edgeBlur / 6, { 0.55, 0.78, 0.9 })
    love.graphics.print(
        string.format("%.1f px", edgeBlur),
        layout.edgeBlur.x + layout.edgeBlur.width + 10,
        layout.edgeBlur.y - 7
    )

    for kind, liquid in ipairs(types) do
        local rowY = layout.groupStartY + (kind - 1) * layout.groupStep
        love.graphics.setColor(liquid.color[1], liquid.color[2], liquid.color[3])
        love.graphics.print(liquid.name, 43, rowY - 7)
        love.graphics.setColor(0.34, 0.4, 0.44, 1)
        love.graphics.circle("fill", 96, rowY, 7)
        love.graphics.setColor(
            liquid.color[1],
            liquid.color[2],
            liquid.color[3],
            liquid.opacity
        )
        love.graphics.circle("fill", 96, rowY, 6)

        love.graphics.setColor(1, 1, 1, 0.85)
        love.graphics.print("Hue", layout.hue.labelX, rowY - 7)
        local hueSlider = { x = layout.hue.x, y = rowY, width = layout.hue.width }
        drawHueSlider(hueSlider, liquid.hue, liquid.saturation, liquid.value)

        love.graphics.setColor(1, 1, 1, 0.85)
        love.graphics.print("Sat.", layout.saturation.labelX, rowY - 7)
        local saturationSlider = {
            x = layout.saturation.x,
            y = rowY,
            width = layout.saturation.width,
        }
        drawSlider(saturationSlider, liquid.saturation, liquid.color)

        love.graphics.setColor(1, 1, 1, 0.85)
        love.graphics.print("Value", layout.value.labelX, rowY - 7)
        local valueSlider = { x = layout.value.x, y = rowY, width = layout.value.width }
        drawSlider(
            valueSlider,
            liquid.value,
            Simulation.colorForHue(liquid.hue, liquid.saturation, 1)
        )

        love.graphics.setColor(1, 1, 1, 0.85)
        love.graphics.print("Transp.", layout.opacity.labelX, rowY - 7)
        local opacitySlider = { x = layout.opacity.x, y = rowY, width = layout.opacity.width }
        drawSlider(opacitySlider, 1 - liquid.opacity, liquid.color)

        love.graphics.setColor(1, 1, 1, 0.85)
        love.graphics.print("Density", layout.density.labelX, rowY - 7)
        local densitySlider = { x = layout.density.x, y = rowY, width = layout.density.width }
        local densityValue = (liquid.density - 0.4) / 1.4
        drawSlider(densitySlider, densityValue, liquid.color)
        love.graphics.setColor(1, 1, 1, 1)
        love.graphics.print(string.format("%.2fx", liquid.density), 1025, rowY - 7)
    end

    love.graphics.setColor(1, 1, 1, 1)
    local pauseText = paused and "PAUSED" or "RUNNING"
    love.graphics.printf(
        pauseText .. "  |  " .. status,
        420,
        12,
        love.graphics.getWidth() - 454,
        "right"
    )
end

function love.keypressed(key)
    if key == "r" then
        reloadShader()
    elseif key == "1" or key == "2" or key == "3" then
        selectedKind = tonumber(key)
    elseif key == "space" then
        paused = not paused
    elseif key == "backspace" then
        simulation:reset(tankBounds())
    elseif key == "escape" then
        love.event.quit()
    end
end

function love.mousepressed(x, y, button)
    if button == 1 and x >= 264 and x <= 440 and y >= 8 and y <= 34 then
        shaderEnabled = not shaderEnabled
        return
    end

    if button == 1 then
        local control = controlAt(x, y)
        if control then
            draggingControl = control
            if control.group then
                selectedKind = control.group
            end
            setControlValue(control, x)
            return
        end
    end

    local bounds = tankBounds()
    if button == 1 and x >= bounds.left and x <= bounds.right and y >= bounds.top and y <= bounds.bottom then
        simulation:addDrop(selectedKind, x, y)
    end
end

function love.mousemoved(x)
    if draggingControl then
        setControlValue(draggingControl, x)
    end
end

function love.mousereleased()
    draggingControl = nil
end
