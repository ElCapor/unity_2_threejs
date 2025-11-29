using AssetsTools.NET;
using AssetsTools.NET.Extra;

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

    public AssetTypeValueField this[string key] => baseField![key];
}

/// <summary>
/// Wrapper for PPtr<T>
/// </summary>
public class PPtr<T> where T : UnityObject, new()
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
where T : IFromAssetTypeValueField<T>
{
    public List<T> Items
    {
        get
        {
            var array = this["Array"];
            List<T> items = new List<T>();
            foreach (var item in array)
            {
                items.Add(T.From(item));
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
    public Vector<PPtr<Component>> m_Component =>
        Vector<PPtr<Component>>.From(this["m_Component"]);
}

public class ComponentPair : UnityObject
{
    public Vector<PPtr<Component>> data =>
        Vector<PPtr<Component>>.From(this["data"]);


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
public class Transform : UnityObject
{
    public PPtr<GameObject> m_GameObject =>
        PPtr<GameObject>.From(this["m_GameObject"]);

    public PPtr<Transform> m_Father =>
        PPtr<Transform>.From(this["m_Father"]);
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

    public Level(AssetsManager assetsManager, string filePath)
    {
        this.assetsManager = assetsManager;

        fileInstance = assetsManager.LoadAssetsFile(filePath, true);
        file = fileInstance.file;

        assetsManager.LoadClassDatabaseFromPackage(file.Metadata.UnityVersion);

        rootTransforms = GetRootTransforms();
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
}


public class Core
{
    public static AssetsManager assetsManager = new();

    public static void Main(string[] args)
    {
        Init();

        string filePath = Path.Combine(GamePath, "level2");
        Level level = new Level(assetsManager, filePath);

        foreach (var t in level.rootTransforms)
        {
            string name = t.m_GameObject.GetObject(level.fileInstance, assetsManager).m_Name;
            Console.WriteLine(name);
        }
    }

    public static void Init()
    {
        assetsManager.LoadClassPackage("uncompressed.tpk");
    }

    public static string GamePath =
        "C:\\Program Files (x86)\\Steam\\steamapps\\common\\Eterspire\\Eterspire_Data";
}
