using AssetsTools.NET;
using AssetsTools.NET.Extra;


public abstract class AssetTypeValueFieldWrapper
{
    protected AssetTypeValueField baseField;

    public void SetField(AssetTypeValueField field)
    {
        baseField = field;
    }

    public AssetTypeValueField this[string key] => baseField[key];
}

public class PPtr<T> where T : AssetTypeValueFieldWrapper, new()
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

        return new PPtr<T>( fileID, pathID );
    }

    public AssetExternal GetExt(AssetsFileInstance fileInst, AssetsManager manager)
    {
        return manager.GetExtAsset( fileInst, FileID, PathID );
    }

    public T GetObject(AssetsFileInstance fileInst, AssetsManager manager)
    {
        var ext = GetExt( fileInst, manager );

        var wrapper = new T();
        wrapper.SetField( ext.baseField );
        return wrapper;
    }
}


public class GameObject : AssetTypeValueFieldWrapper
{
    public string m_Name => this["m_Name"].AsString;
}


public class Transform : AssetTypeValueFieldWrapper
{
    public PPtr<GameObject> m_GameObject =>
        PPtr<GameObject>.From( this["m_GameObject"] );

    public PPtr<Transform> m_Father =>
        PPtr<Transform>.From( this["m_Father"] );
}

public class Level
{
    public AssetsFileInstance fileInstance;
    public AssetsFile file;
    public AssetsManager assetsManager;

    public List<Transform> rootTransforms;

    public Level(AssetsManager assetsManager, string filePath)
    {
        this.assetsManager = assetsManager;

        fileInstance = assetsManager.LoadAssetsFile( filePath, true );
        file = fileInstance.file;

        assetsManager.LoadClassDatabaseFromPackage( file.Metadata.UnityVersion );

        rootTransforms = GetRootTransforms();
    }

    public List<Transform> GetRootTransforms()
    {
        var transformInfos = file.GetAssetsOfType( AssetClassID.Transform );

        var result = new List<Transform>();

        foreach (var info in transformInfos)
        {
            var baseField = assetsManager.GetBaseField( fileInstance, info );
            long father = baseField["m_Father"]["m_PathID"].AsLong;

            if (father == 0)
            {
                var wrapper = new Transform();
                wrapper.SetField( baseField );
                result.Add( wrapper );
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

        string filePath = Path.Combine( GamePath, "level2" );
        Level level = new Level( assetsManager, filePath );

        foreach (var t in level.rootTransforms)
        {
            string name = t.m_GameObject.GetObject( level.fileInstance, assetsManager ).m_Name;
            Console.WriteLine( name );
        }
    }

    public static void Init()
    {
        assetsManager.LoadClassPackage( "uncompressed.tpk" );
    }

    public static string GamePath =
        "C:\\Program Files (x86)\\Steam\\steamapps\\common\\Eterspire\\Eterspire_Data";
}
