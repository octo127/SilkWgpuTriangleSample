@vertex fn main_vs(@builtin(vertex_index) index: u32) -> @builtin(position) vec4f
{
    if (index == 0u)
    {
        // (x, y) = (0.0, 0.5)
        return vec4f(0.0, 0.5, 0.0, 1.0);
    }
    else if (index == 1u)
    {
        // (x, y) = (-0.5, -0.5)
        return vec4f(-0.5, -0.5, 0.0, 1.0);
    }
    else
    {
        // (x, y) = (0.5, -0.5)
        return vec4f(0.5, -0.5, 0.0, 1.0);
    }
}

@fragment fn main_fs() -> @location(0) vec4f
{
    // (r, g, b, a) = (1.0, 0.0, 0.0, 1.0)
    return vec4f(1.0, 0.0, 0.0, 1.0);
}