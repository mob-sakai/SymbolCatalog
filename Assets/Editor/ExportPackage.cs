using UnityEditor;

namespace Mobcast.Coffee.Symbol
{
	public static class ExportPackage
	{
		const string kPackageName = "SymbolCatalog.unitypackage";
		static readonly string[] kAssetPathes = {
			"Assets/Mobcast/Coffee/Editor/SymbolCatalog",
		};

		[MenuItem ("Export Package/" + kPackageName)]
		[InitializeOnLoadMethod]
		static void Export ()
		{
			if (EditorApplication.isPlayingOrWillChangePlaymode)
				return;
			
			AssetDatabase.ExportPackage (kAssetPathes, kPackageName, ExportPackageOptions.Recurse | ExportPackageOptions.Default);
			UnityEngine.Debug.Log ("Export successfully : " + kPackageName);
		}
	}
}