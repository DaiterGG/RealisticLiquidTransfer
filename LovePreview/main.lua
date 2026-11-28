local shaderPath = "shader.glsl"
local shader
local lastSource
local elapsed = 0
local reloadTimer = 0
local reloadInterval = 0.25
local status = "Loading shader..."

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
    love.graphics.setBackgroundColor(0.025, 0.04, 0.08)
    reloadShader()
    if not shader then
        error(status)
    end
end

function love.update(dt)
    elapsed = elapsed + dt
    reloadTimer = reloadTimer + dt

    if reloadTimer >= reloadInterval then
        reloadTimer = 0
        local source = love.filesystem.read(shaderPath)
        if source and source ~= lastSource then
            reloadShader()
        end
    end

    if shader then
        shader:send("elapsed", elapsed)
        shader:send("resolution", love.graphics.getDimensions())
    end
end

function love.draw()
    if shader then
        love.graphics.setShader(shader)
        love.graphics.setColor(1, 1, 1, 1)
        love.graphics.rectangle("fill", 0, 0, love.graphics.getDimensions())
        love.graphics.setShader()
    end

    love.graphics.setColor(0.02, 0.04, 0.08, 0.72)
    love.graphics.rectangle("fill", 16, 16, 390, 58, 8, 8)
    love.graphics.setColor(1, 1, 1, 1)
    love.graphics.print("Edit shader.glsl to hot reload", 28, 25)
    love.graphics.print("R: reload now    Esc: quit    " .. status, 28, 47)
end

function love.keypressed(key)
    if key == "r" then
        reloadShader()
    elseif key == "escape" then
        love.event.quit()
    end
end
