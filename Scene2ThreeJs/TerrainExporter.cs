using System.Text.Json;
using System.Text.Json.Serialization;
using Vec3 = Vim.Math3d.Vector3;
using Vec4 = Vim.Math3d.Vector4;

namespace Scene2ThreeJs;

/// <summary>
/// JSON structure for terrain size
/// </summary>
public class SizeJson
{
    [JsonPropertyName("width")]
    public double Width { get; set; }

    [JsonPropertyName("length")]
    public double Length { get; set; }

    [JsonPropertyName("height")]
    public double Height { get; set; }
}

/// <summary>
/// JSON structure for vector3 data
/// </summary>
public class PositionJson
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("z")]
    public double Z { get; set; }
}

/// <summary>
/// JSON structure for quaternion data
/// </summary>
public class QuaternionJson
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("z")]
    public double Z { get; set; }

    [JsonPropertyName("w")]
    public double W { get; set; }
}

/// <summary>
/// JSON structure for transform (position, rotation, scale)
/// </summary>
public class TransformJson
{
    [JsonPropertyName("position")]
    public PositionJson Position { get; set; } = new();

    [JsonPropertyName("rotation")]
    public QuaternionJson Rotation { get; set; } = new();

    [JsonPropertyName("scale")]
    public PositionJson Scale { get; set; } = new();
}

/// <summary>
/// JSON structure for heightmap data
/// </summary>
public class HeightmapJson
{
    [JsonPropertyName("format")]
    public string Format { get; set; } = "raw";

    [JsonPropertyName("data")]
    public float[] Data { get; set; } = Array.Empty<float>();
}

/// <summary>
/// Root JSON structure for terrains export
/// </summary>
public class TerrainsJson
{
    [JsonPropertyName("metadata")]
    public MetadataJson Metadata { get; set; } = new();

    [JsonPropertyName("terrains")]
    public List<TerrainInstanceJson> Terrains { get; set; } = new();
}

public class MetadataJson
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";

    [JsonPropertyName("generator")]
    public string Generator { get; set; } = "Unity2ThreeJS";

    [JsonPropertyName("exportDate")]
    public string ExportDate { get; set; } = DateTime.UtcNow.ToString("o");
}

public class TerrainInstanceJson
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("transform")]
    public TransformJson Transform { get; set; } = new();

    [JsonPropertyName("resolution")]
    public int Resolution { get; set; }

    [JsonPropertyName("size")]
    public SizeJson Size { get; set; } = new();

    [JsonPropertyName("heightmap")]
    public HeightmapJson Heightmap { get; set; } = new();
}

/// <summary>
/// Exports Unity terrain data to JSON format for Three.js
/// </summary>
public static class TerrainExporter
{
    /// <summary>
    /// Converts Unity left-handed coordinates to Three.js right-handed
    /// </summary>
    public static (double x, double y, double z) UnityToThreeJS(double x, double y, double z)
    {
        return (-x, y, z);  // Negate X for coordinate system conversion
    }

    /// <summary>
    /// Normalizes a 16-bit height value to 0-1 range
    /// </summary>
    public static float NormalizeHeight(short heightValue)
    {
        // Treat as unsigned 16-bit integer to avoid negative values
        return (ushort)heightValue / 65535.0f;
    }

    /// <summary>
    /// Exports single terrain data to JSON format
    /// </summary>
    public static TerrainInstanceJson ExportTerrain(string name, Vec3 position, Vec4 rotation, Vec3 scale, TerrainData terrainData)
    {
        var resolution = terrainData.m_Heightmap_Resolution;
        var terrainScale = terrainData.m_Heightmap_Scale;
        var heights = terrainData.m_Heightmap_Heights;

        // Normalize heights to 0-1 range and transpose for Three.js
        // Unity stores as [y, x] (row-major), Three.js PlaneGeometry expects [x, y] (row by row)
        // We transpose to match Three.js coordinate system
        float[] normalizedHeights = new float[heights.Length];

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                // Unity index: y * resolution + x
                int unityIndex = y * resolution + x;

                // Three.js index: x * resolution + y (transposed)
                int threeIndex = x * resolution + y;

                if (unityIndex < heights.Length && threeIndex < normalizedHeights.Length)
                {
                    normalizedHeights[threeIndex] = NormalizeHeight(heights[unityIndex]);
                }
            }
        }

        // Convert Unity coordinates to Three.js
        var (xSize, ySize, zSize) = UnityToThreeJS(
            terrainScale.X * (resolution - 1),
            terrainScale.Y,
            terrainScale.Z * (resolution - 1)
        );

        var (posX, posY, posZ) = UnityToThreeJS(position.X, position.Y, position.Z);

        // Unity (Left-handed) to Three.js (Right-handed) Quaternion conversion
        // Typically: -x, y, z, -w (or similar depending on basis vectors)
        // Let's use -x, y, z, -w as a starting point.

        return new TerrainInstanceJson
        {
            Name = name,
            Transform = new TransformJson
            {
                Position = new PositionJson { X = posX, Y = posY, Z = posZ },
                Rotation = new QuaternionJson { X = -rotation.X, Y = rotation.Y, Z = rotation.Z, W = -rotation.W },
                Scale = new PositionJson { X = scale.X, Y = scale.Y, Z = scale.Z }
            },
            Resolution = resolution,
            Size = new SizeJson
            {
                Width = Math.Abs(xSize),
                Length = zSize,
                Height = ySize
            },
            Heightmap = new HeightmapJson
            {
                Format = "raw",
                Data = normalizedHeights
            }
        };
    }

    /// <summary>
    /// Exports multiple terrains to JSON file
    /// </summary>
    public static void ExportToFile(TerrainsJson terrainsJson, string filePath)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };

        var json = JsonSerializer.Serialize(terrainsJson, options);
        File.WriteAllText(filePath, json);

        Console.WriteLine($"\nExported {terrainsJson.Terrains.Count} terrain(s) to: {filePath}");
        foreach (var terrain in terrainsJson.Terrains)
        {
            Console.WriteLine($"  - {terrain.Name}");
            Console.WriteLine($"    Position: ({terrain.Transform.Position.X:F2}, {terrain.Transform.Position.Y:F2}, {terrain.Transform.Position.Z:F2})");
            Console.WriteLine($"    Resolution: {terrain.Resolution}x{terrain.Resolution}");
            Console.WriteLine($"    Size: {terrain.Size.Width:F2} x {terrain.Size.Length:F2} x {terrain.Size.Height:F2}");
        }
    }
}
