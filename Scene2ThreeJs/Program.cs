using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

namespace TerrainRipper
{
    class Program
    {
        // Change this path to your game location
        const string GamePath = @"C:\Program Files (x86)\Steam\steamapps\common\Eterspire\Eterspire_Data";

        static AssetsManager am;

        static void Main(string[] args)
        {
            am = new AssetsManager();

            // You only need classdata.tpk (or uncompressed.tpk) for geometry
            am.LoadClassPackage( "uncompressed.tpk" );

            // Look for level files
            var levelFiles = Directory.GetFiles( GamePath, "level2" );

            foreach (string file in levelFiles)
            {
                try
                {
                    Console.WriteLine( $"Opening {Path.GetFileName( file )}..." );
                    var inst = am.LoadAssetsFile( file, true );

                    am.LoadClassDatabaseFromPackage( inst.file.Metadata.UnityVersion );

                    // Find TerrainData
                    var terrainInfos = inst.file.GetAssetsOfType( AssetClassID.TerrainData );

                    foreach (var inf in terrainInfos)
                    {
                        var baseField = am.GetBaseField( inst, inf );
                        ExportGeometryOnly( baseField, $"Terrain_{inf.PathId}" );
                    }
                }
                catch (Exception ex) { Console.WriteLine( $"Error: {ex.Message}" ); }
            }
        }

        static void ExportGeometryOnly(AssetTypeValueField tData, string name)
        {
            Console.WriteLine( $"Processing Geometry: {name}..." );

            // 1. GET HEIGHTMAP FIELD
            var m_Heightmap = tData["m_Heightmap"];
            if (m_Heightmap.IsDummy)
            {
                Console.WriteLine( "m_Heightmap is missing. Skipping." );
                return;
            }

            // 2. CALCULATE DIMENSIONS
            // Using m_Resolution (since m_Width/m_Height were missing in your file)
            int res = m_Heightmap["m_Resolution"].AsInt;

            var m_Scale = m_Heightmap["m_Scale"];
            float scaleX = m_Scale["x"].AsFloat;
            float scaleY = m_Scale["y"].AsFloat; // Max Height
            float scaleZ = m_Scale["z"].AsFloat;

            float worldWidth = (res - 1) * scaleX;
            float worldDepth = (res - 1) * scaleZ;
            float maxHeight = scaleY;

            Console.WriteLine( $"Size: {worldWidth:F1}x{worldDepth:F1}, MaxHeight: {maxHeight:F1}, Res: {res}" );

            // 3. EXTRACT HEIGHTS (The Fix)
            // m_Heights is a "vector" containing "SInt16" (shorts).
            var heightsField = m_Heightmap["m_Heights"]["Array"];

            // Get the number of items in the array
            int numHeights = heightsField.AsArray.size;

            // Safety check
            if (numHeights != res * res)
            {
                Console.WriteLine( $"Warning: Resolution mismatch. Expected {res * res}, got {numHeights}. Using available data." );
            }

            List<float> heightList = new List<float>( numHeights );

            // Iterate through the AssetTools array
            for (int i = 0; i < numHeights; i++)
            {
                // Access the individual Int16 element
                // We use AsInt because AssetsTools reads numbers as generic Ints/Longs
                int val = heightsField[i].AsInt;

                // Unity Int16 logic: 0 to 32768 maps to 0.0 to 1.0
                // Cast to float for precision
                float normalized = (float)val / 32768.0f;

                heightList.Add( normalized * maxHeight );
            }

            // 4. SAVE JSON
            var exportObj = new
            {
                name = name,
                width = worldWidth,
                depth = worldDepth,
                maxHeight = maxHeight,
                resolution = res,
                heightMap = heightList
            };

            string json = JsonConvert.SerializeObject( exportObj, Formatting.None );
            File.WriteAllText( $"{name}.json", json );
            Console.WriteLine( $"Saved {name}.json" );
        }
    }
}