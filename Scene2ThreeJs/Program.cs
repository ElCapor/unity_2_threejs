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
        const string GamePath = @"C:\Program Files (x86)\Steam\steamapps\common\Eterspire\Eterspire_Data";
        static AssetsManager am;

        static void Main(string[] args)
        {
            am = new AssetsManager();
            am.LoadClassPackage( "uncompressed.tpk" );

            var levelFiles = Directory.GetFiles( GamePath, "level60" );

            // We will store ALL terrains here to save one big map file
            var fullMapData = new { terrains = new List<object>() };

            foreach (string file in levelFiles)
            {
                try
                {
                    Console.WriteLine( $"Opening {Path.GetFileName( file )}..." );
                    var inst = am.LoadAssetsFile( file, true );
                    am.LoadClassDatabaseFromPackage( inst.file.Metadata.UnityVersion );

                    // STEP 1: Look for 'Terrain' Components (Class ID 218), NOT 'TerrainData'
                    // The Component exists in the scene and holds the link to the Data + Position
                    var terrainComponents = inst.file.GetAssetsOfType( AssetClassID.Terrain );

                    foreach (var terrInfo in terrainComponents)
                    {
                        try
                        {
                            // Get the Terrain Component
                            var terrainBase = am.GetBaseField( inst, terrInfo );

                            // 1. Get Position (X, Y, Z)
                            // We must follow: Terrain -> GameObject -> Transform -> Position
                            var pos = GetPositionFromTerrain( inst, terrainBase );

                            // 2. Get Terrain Data (Geometry)
                            // Follow: Terrain -> m_TerrainData (Pointer)
                            var tDataPtr = terrainBase["m_TerrainData"];

                            // 3. Extract Geometry
                            // We pass the pointer, resolve it, and get the data object
                            var chunkData = ExtractTerrainChunk( inst, tDataPtr, pos, $"Terrain_{terrInfo.PathId}" );

                            if (chunkData != null)
                            {
                                fullMapData.terrains.Add( chunkData );
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine( $"Skipped a chunk: {ex.Message}" );
                        }
                    }
                }
                catch (Exception ex) { Console.WriteLine( $"Error: {ex.Message}" ); }
            }

            // Save everything to ONE file
            string json = JsonConvert.SerializeObject( fullMapData, Formatting.None );
            File.WriteAllText( "FullMap.json", json );
            Console.WriteLine( "\n[SUCCESS] Saved FullMap.json containing all chunks!" );
        }

        // Helper to navigate the messy Unity Component Graph
        static float[] GetPositionFromTerrain(AssetsFileInstance inst, AssetTypeValueField terrainBase)
        {
            float[] position = new float[] { 0, 0, 0 };

            try
            {
                // 1. Get GameObject pointer from Terrain
                var goPtr = terrainBase["m_GameObject"];
                var goAsset = am.GetExtAsset( inst, goPtr );
                var goBase = am.GetBaseField( goAsset.file, goAsset.info );

                // 2. Iterate GameObject components to find the 'Transform' (Class ID 4)
                var components = goBase["m_Component.Array"];
                foreach (var component in components)
                {
                    var componentPtr = component["component"];
                    var compAsset = am.GetExtAsset( inst, componentPtr ); // Resolves to the file containing it

                    // Check if this component is a Transform (ID 4)
                    if (compAsset.info.TypeId == (int)AssetClassID.Transform)
                    {
                        var transformBase = am.GetBaseField( compAsset.file, compAsset.info );
                        var m_LocalPosition = transformBase["m_LocalPosition"];

                        position[0] = m_LocalPosition["x"].AsFloat;
                        position[1] = m_LocalPosition["y"].AsFloat;
                        position[2] = m_LocalPosition["z"].AsFloat;

                        Console.WriteLine( $"   -> Found Position: {position[0]}, {position[1]}, {position[2]}" );
                        return position;
                    }
                }
            }
            catch
            {
                Console.WriteLine( "   -> Could not determine position (Transform missing?)" );
            }
            return position;
        }

        static object ExtractTerrainChunk(AssetsFileInstance inst, AssetTypeValueField tDataPtr, float[] pos, string name)
        {
            // Resolve the TerrainData asset
            var tDataAsset = am.GetExtAsset( inst, tDataPtr );
            if (tDataAsset.info == null) return null;

            var tData = am.GetBaseField( tDataAsset.file, tDataAsset.info );

            Console.WriteLine( $"Processing Geometry: {name}..." );

            var m_Heightmap = tData["m_Heightmap"];
            if (m_Heightmap.IsDummy) return null;

            // Dimensions
            int res = m_Heightmap["m_Resolution"].AsInt;
            var m_Scale = m_Heightmap["m_Scale"];
            float scaleX = m_Scale["x"].AsFloat;
            float scaleY = m_Scale["y"].AsFloat;
            float scaleZ = m_Scale["z"].AsFloat;

            float worldWidth = (res - 1) * scaleX;
            float worldDepth = (res - 1) * scaleZ;
            float maxHeight = scaleY;

            // Extract Heights
            var heightsField = m_Heightmap["m_Heights"]["Array"];
            int numHeights = heightsField.AsArray.size;
            List<float> heightList = new List<float>( numHeights );

            for (int i = 0; i < numHeights; i++)
            {
                int val = heightsField[i].AsInt;
                float normalized = (float)val / 32768.0f;
                heightList.Add( normalized * maxHeight );
            }

            // Return the Object (to be added to the list)
            return new
            {
                name = name,
                x = pos[0], // Unity X
                y = pos[1], // Unity Y (Height)
                z = pos[2], // Unity Z
                width = worldWidth,
                depth = worldDepth,
                maxHeight = maxHeight,
                resolution = res,
                heightMap = heightList
            };
        }
    }
}