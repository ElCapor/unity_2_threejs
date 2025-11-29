using System.Text.Json;
using System.Text.Json.Serialization;
using Vec3 = Vim.Math3d.Vector3;

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
/// Root JSON structure for terrain export
/// </summary>
public class TerrainJson
{
    [JsonPropertyName("metadata")]
    public MetadataJson Metadata { get; set; } = new();
    
    [JsonPropertyName("terrain")]
    public TerrainDataJson Terrain { get; set; } = new();
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

public class TerrainDataJson
{
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
        return heightValue / 65535.0f;
    }
    
    /// <summary>
    /// Exports terrain data to JSON format
    /// </summary>
    public static TerrainJson ExportTerrain(TerrainData terrainData)
    {
        var resolution = terrainData.m_Heightmap_Resolution;
        var scale = terrainData.m_Heightmap_Scale;
        var heights = terrainData.m_Heightmap_Heights;
        
        // Normalize heights to 0-1 range
        float[] normalizedHeights = new float[heights.Length];
        for (int i = 0; i < heights.Length; i++)
        {
            normalizedHeights[i] = NormalizeHeight(heights[i]);
        }
        
        // Convert Unity coordinates to Three.js
        var (x, y, z) = UnityToThreeJS(
            scale.X * (resolution - 1),
            scale.Y,
            scale.Z * (resolution - 1)
        );
        
        return new TerrainJson
        {
            Terrain = new TerrainDataJson
            {
                Resolution = resolution,
                Size = new SizeJson
                {
                    Width = Math.Abs(x),   // Use absolute value after negation
                    Length = z,
                    Height = y
                },
                Heightmap = new HeightmapJson
                {
                    Format = "raw",
                    Data = normalizedHeights
                }
            }
        };
    }
    
    /// <summary>
    /// Exports terrain to JSON file
    /// </summary>
    public static void ExportToFile(TerrainJson terrainJson, string filePath)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };
        
        var json = JsonSerializer.Serialize(terrainJson, options);
        File.WriteAllText(filePath, json);
        
        Console.WriteLine($"Terrain exported to: {filePath}");
        Console.WriteLine($"Resolution: {terrainJson.Terrain.Resolution}x{terrainJson.Terrain.Resolution}");
        Console.WriteLine($"Size: {terrainJson.Terrain.Size.Width:F2} x {terrainJson.Terrain.Size.Length:F2} x {terrainJson.Terrain.Size.Height:F2}");
        Console.WriteLine($"Heightmap points: {terrainJson.Terrain.Heightmap.Data.Length}");
    }
}
