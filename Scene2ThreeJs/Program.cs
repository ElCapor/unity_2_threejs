using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Vec3 = Vim.Math3d.Vector3;


public interface IFromAssetTypeValueField<T>
{
    public static T From(AssetTypeValueField field)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Wrapper for AssetTypeValueField
/// </summary>
public abstract class UnityObject : IFromAssetTypeValueField<UnityObject>
{
    protected AssetTypeValueField? baseField = null;

    public static T From<T>(AssetTypeValueField field) where T : UnityObject, new()
    {
        var wrapper = new T();
        wrapper.baseField = field;
        return wrapper;
    }

    public void SetField(AssetTypeValueField field)
    {
        baseField = field;
    }

    public AssetTypeValueField this[string key] => baseField![key];
}

/// <summary>
/// Wrapper for PPtr<T>
/// </summary>
public class PPtr<T> : IFromAssetTypeValueField<PPtr<T>> where T : UnityObject, new()
{
    public int FileID { get; }
    public long PathID { get; }

    private PPtr(int fileID, long pathID)
    {
        FileID = fileID;
        PathID = pathID;
    }

    public static PPtr<T> From(AssetTypeValueField pptr)
    {
        int fileID = pptr["m_FileID"].AsInt;
        long pathID = pptr["m_PathID"].AsLong;

        return new PPtr<T>(fileID, pathID);
    }

    public AssetExternal GetExt(AssetsFileInstance fileInst, AssetsManager manager)
    {
        return manager.GetExtAsset(fileInst, FileID, PathID);
    }

    public T GetObject(AssetsFileInstance fileInst, AssetsManager manager)
    {
        var ext = GetExt(fileInst, manager);

        return UnityObject.From<T>(ext.baseField);
    }
}

/// <summary>
/// Represents an array of AssetTypeValueField (or wrapped T)
/// </summary>
/// <typeparam name="T"></typeparam>
public class Vector<T> : UnityObject
{
    public List<T> Items
    {
        get
        {
            var array = this["Array"];
            List<T> items = new List<T>();

            // Get the type of T
            var type = typeof(T);

            foreach (var item in array)
            {
                // Check if T is a PPtr<> type
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(PPtr<>))
                {
                    // Call PPtr<>.From using reflection
                    var fromMethod = type.GetMethod("From", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    var result = fromMethod!.Invoke(null, new object[] { item });
                    items.Add((T)result!);
                }
                // Check if T is a UnityObject subclass
                else if (typeof(UnityObject).IsAssignableFrom(type))
                {
                    // Call UnityObject.From<T> using reflection
                    var fromMethod = typeof(UnityObject).GetMethod("From", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    var genericMethod = fromMethod!.MakeGenericMethod(type);
                    var result = genericMethod.Invoke(null, new object[] { item });
                    items.Add((T)result!);
                }
                else
                {
                    throw new NotSupportedException($"Type {type.Name} is not supported in Vector<T>");
                }
            }
            return items;
        }
    }
}


/// <summary>
/// Wrapper for GameObject
/// </summary>
public class GameObject : UnityObject
{
    public string m_Name => this["m_Name"].AsString;
    public Vector<ComponentPair> m_Component =>
        Vector<ComponentPair>.From<Vector<ComponentPair>>(this["m_Component"]);
}

public class ComponentPair : UnityObject
{
    public PPtr<Component> component =>
        PPtr<Component>.From(this["component"]);


}

/// <summary>
/// Wrapper for Component
/// </summary>
public class Component : UnityObject
{

}

/// <summary>
/// Wrapper for Transform
/// </summary>
public class Transform : Component
{
    public PPtr<GameObject> m_GameObject =>
        PPtr<GameObject>.From(this["m_GameObject"]);

    public PPtr<Transform> m_Father =>
        PPtr<Transform>.From(this["m_Father"]);
}



public class TerrainData : Component
{
    // Terrain dimensions
    public Vec3 m_Heightmap_Scale => new Vec3(
        this["m_Heightmap.m_Scale.x"].AsFloat,
        this["m_Heightmap.m_Scale.y"].AsFloat,
        this["m_Heightmap.m_Scale.z"].AsFloat
    );

    public int m_Heightmap_Resolution => this["m_Heightmap.m_Resolution"].AsInt;

    // Heights are stored as 16-bit integers (0-65535)
    public short[] m_Heightmap_Heights
    {
        get
        {
            var heightsField = this["m_Heightmap.m_Heights"];
            var byteArray = heightsField.AsByteArray;

            // Convert bytes to shorts
            short[] heights = new short[byteArray.Length / 2];
            Buffer.BlockCopy(byteArray, 0, heights, 0, byteArray.Length);
            return heights;
        }
    }
}

public class Terrain : Component
{
    public PPtr<TerrainData> m_TerrainData =>
        PPtr<TerrainData>.From(this["m_TerrainData"]);
}

/// <summary>
/// Represents a unity level file = Scene
/// </summary>
public class Level
{
    public AssetsFileInstance fileInstance;
    public AssetsFile file;
    public AssetsManager assetsManager;

    public List<Transform> rootTransforms;
    public List<GameObject> rootGameObjects;

    public Level(AssetsManager assetsManager, string filePath)
    {
        this.assetsManager = assetsManager;

        fileInstance = assetsManager.LoadAssetsFile(filePath, true);
        file = fileInstance.file;

        assetsManager.LoadClassDatabaseFromPackage(file.Metadata.UnityVersion);

        rootTransforms = GetRootTransforms();
        rootGameObjects = GetRootGameObjects();
    }

    public List<Transform> GetRootTransforms()
    {
        var transformInfos = file.GetAssetsOfType(AssetClassID.Transform);

        var result = new List<Transform>();

        foreach (var info in transformInfos)
        {
            var baseField = assetsManager.GetBaseField(fileInstance, info);
            long father = baseField["m_Father"]["m_PathID"].AsLong;

            if (father == 0)
            {
                var wrapper = new Transform();
                wrapper.SetField(baseField);
                result.Add(wrapper);
            }
        }

        return result;
    }

    public List<GameObject> GetRootGameObjects()
    {
        List<GameObject> result = new();
        if (this.rootTransforms.Count == 0)
        {
            return result;
        }

        foreach (var t in this.rootTransforms)
        {
            var go = t.m_GameObject.GetObject(fileInstance, assetsManager);
            result.Add(go);
        }

        return result;
    }
}

public class Core
{
    public static AssetsManager assetsManager = new();

    public static void Main(string[] args)
    {
        Init();

        string filePath = Path.Combine(GamePath, "level2");
        Level level = new Level(assetsManager, filePath);

        foreach (GameObject obj in level.rootGameObjects)
        {
            foreach (ComponentPair compPair in obj.m_Component.Items)
            {
                var fullCompInfo = compPair.component.GetExt(level.fileInstance, level.assetsManager);
                var compTypeInfo = (AssetClassID)fullCompInfo.info.TypeId;

                if (compTypeInfo == AssetClassID.Terrain)
                {
                    var terrain = new Terrain();
                    terrain.SetField(fullCompInfo.baseField);
                }
            }
        }
        /*
                foreach (var t in level.rootTransforms)
                {
                    var go = t.m_GameObject.GetObject(level.fileInstance, assetsManager);
                    foreach (ComponentPair compPair in go.m_Component.Items)
                    {
                        var fullCompInfo = compPair.component.GetExt(level.fileInstance, level.assetsManager);
                        var compTypeInfo = (AssetClassID)fullCompInfo.info.TypeId;

                        if (compTypeInfo == AssetClassID.Transform)
                        {
                            var transform = new Transform();
                            transform.SetField(fullCompInfo.baseField);
                            Console.WriteLine(transform.m_GameObject.GetObject(level.fileInstance, level.assetsManager).m_Name);
                        }
                    }
                    Console.WriteLine(go.m_Name);
                }
            }
            */
    }

    public static void Init()
    {
        assetsManager.LoadClassPackage("uncompressed.tpk");
    }

    public static string GamePath =
        "C:\\Program Files (x86)\\Steam\\steamapps\\common\\Eterspire\\Eterspire_Data";
}
