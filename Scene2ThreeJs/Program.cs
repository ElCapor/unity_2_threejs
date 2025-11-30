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

            // 1. Load GlobalGameManagers to get the Scene Names
            string ggmPath = Path.Combine( GamePath, "globalgamemanagers" );
            if (!File.Exists( ggmPath ))
            {
                // Fallback for older Unity versions
                ggmPath = Path.Combine( GamePath, "gamemanagers" );
            }

            Console.WriteLine( "Reading Build Settings..." );
            var ggmInst = am.LoadAssetsFile( ggmPath, true );
            am.LoadClassDatabaseFromPackage( ggmInst.file.Metadata.UnityVersion );

            // Class ID 141 is BuildSettings
            var buildSettingsAsset = ggmInst.file.GetAssetsOfType( (AssetClassID)141 )[0];
            var buildSettingsBase = am.GetBaseField( ggmInst, buildSettingsAsset );

            // Get the list of scenes
            var scenesArray = buildSettingsBase["scenes.Array"];

            Console.WriteLine( $"Found {scenesArray.AsArray.size} scenes in Build Settings." );
            int i = 0;
            // 2. Iterate through every scene defined in the game
            foreach (var scene in scenesArray)
            {
                string rawPath = scene.AsString;
                string sceneName = Path.GetFileNameWithoutExtension( rawPath );

                // Unity maps Index -> levelX
                string levelFileName = $"level{i}";
                string fullLevelPath = Path.Combine( GamePath, levelFileName );

                if (File.Exists( fullLevelPath ))
                {
                    Console.WriteLine( $"\n--- Processing Scene {i}: {sceneName} ({levelFileName}) ---" );
                    ProcessLevel( fullLevelPath, sceneName );
                }
                else
                {
                    // Sometimes levels are skipped or empty
                    Console.WriteLine( $"Skipping Scene {i} ({sceneName}): File {levelFileName} not found." );
                }
                i++;
            }
        }

        static void ProcessLevel(string levelFilePath, string sceneName)
        {
            try
            {
                // Unload previous level assets to free memory/avoid conflicts (optional but good practice)
                // For simplicity in this loop, we just load a new instance. 
                // Note: AssetsManager caches files, so dependencies (sharedassets) work fine.

                var inst = am.LoadAssetsFile( levelFilePath, true );
                if (inst.file.Metadata.TypeTreeEnabled)
                    am.LoadClassDatabaseFromPackage( inst.file.Metadata.UnityVersion );

                // We store the data for this specific scene
                var sceneMapData = new { scene = sceneName, terrains = new List<object>() };

                // Find Terrains
                var terrainComponents = inst.file.GetAssetsOfType( AssetClassID.Terrain );

                if (terrainComponents.Count == 0)
                {
                    Console.WriteLine( "No Terrains found in this scene." );
                    return;
                }

                foreach (var terrInfo in terrainComponents)
                {
                    try
                    {
                        var terrainBase = am.GetBaseField( inst, terrInfo );
                        var pos = GetPositionFromTerrain( inst, terrainBase );
                        var tDataPtr = terrainBase["m_TerrainData"];

                        var chunkData = ExtractTerrainChunk( inst, tDataPtr, pos, $"Terrain_{terrInfo.PathId}" );

                        if (chunkData != null)
                        {
                            sceneMapData.terrains.Add( chunkData );
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine( $"Skipped chunk: {ex.Message}" );
                    }
                }

                // Only save if we actually found terrain data
                if (sceneMapData.terrains.Count > 0)
                {
                    // Sanitize filename (remove characters that are bad for Windows filenames)
                    string safeName = string.Join( "_", sceneName.Split( Path.GetInvalidFileNameChars() ) );
                    string outFile = $"{safeName}.json";

                    string json = JsonConvert.SerializeObject( sceneMapData, Formatting.None );
                    File.WriteAllText( outFile, json );
                    Console.WriteLine( $"[SUCCESS] Saved {outFile}" );
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine( $"Error processing level: {ex.Message}" );
            }
        }

        // --- EXISTING HELPER FUNCTIONS (UNCHANGED) ---

        static float[] GetPositionFromTerrain(AssetsFileInstance inst, AssetTypeValueField terrainBase)
        {
            float[] position = new float[] { 0, 0, 0 };

            try
            {
                var goPtr = terrainBase["m_GameObject"];
                var goAsset = am.GetExtAsset( inst, goPtr );
                var goBase = am.GetBaseField( goAsset.file, goAsset.info );

                // NOTE: Using "m_Component.Array" based on your provided working code
                var components = goBase["m_Component.Array"];
                foreach (var component in components)
                {
                    var componentPtr = component["component"];
                    var compAsset = am.GetExtAsset( inst, componentPtr );

                    if (compAsset.info.TypeId == (int)AssetClassID.Transform)
                    {
                        var transformBase = am.GetBaseField( compAsset.file, compAsset.info );
                        var m_LocalPosition = transformBase["m_LocalPosition"];

                        position[0] = m_LocalPosition["x"].AsFloat;
                        position[1] = m_LocalPosition["y"].AsFloat;
                        position[2] = m_LocalPosition["z"].AsFloat;
                        return position;
                    }
                }
            }
            catch { }
            return position;
        }

        static object ExtractTerrainChunk(AssetsFileInstance inst, AssetTypeValueField tDataPtr, float[] pos, string name)
        {
            var tDataAsset = am.GetExtAsset( inst, tDataPtr );
            if (tDataAsset.info == null) return null;

            var tData = am.GetBaseField( tDataAsset.file, tDataAsset.info );
            var m_Heightmap = tData["m_Heightmap"];
            if (m_Heightmap.IsDummy) return null;

            int res = m_Heightmap["m_Resolution"].AsInt;
            var m_Scale = m_Heightmap["m_Scale"];
            float scaleX = m_Scale["x"].AsFloat;
            float scaleY = m_Scale["y"].AsFloat;
            float scaleZ = m_Scale["z"].AsFloat;

            float worldWidth = (res - 1) * scaleX;
            float worldDepth = (res - 1) * scaleZ;
            float maxHeight = scaleY;

            var heightsField = m_Heightmap["m_Heights"]["Array"];
            int numHeights = heightsField.AsArray.size;
            List<float> heightList = new List<float>( numHeights );

            for (int i = 0; i < numHeights; i++)
            {
                int val = heightsField[i].AsInt;
                float normalized = (float)val / 32768.0f;
                heightList.Add( normalized * maxHeight );
            }

            Console.WriteLine( $"   -> Extracted {name} ({worldWidth:F0}x{worldDepth:F0})" );

            return new
            {
                name = name,
                x = pos[0],
                y = pos[1],
                z = pos[2],
                width = worldWidth,
                depth = worldDepth,
                maxHeight = maxHeight,
                resolution = res,
                heightMap = heightList
            };
        }
    }
}