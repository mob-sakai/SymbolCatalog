using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Mobcast.Coffee.Editor
{
	/// <summary>
	/// [アセット]定義シンボルカタログクラス.
	/// プロジェクトで利用できる定義シンボルをカタログ化し、設定/共有しやすい機能を提供します.
	/// Symbol Catalog.
	/// Catalog 'Scripting Define Symbols' available in the project, for setting and sharing.
	/// </summary>
	public class SymbolCatalog : ScriptableObject
	{
		/// <summary>
		/// 定義シンボルリスト.
		/// Scripting Define Symbols.
		/// </summary>
		public List<Symbol> list = new List<Symbol>();

		/// <summary>
		/// 有効な定義シンボルリストを適用します.
		/// Apply 'Scripting Define Symbols' to project settings.
		/// </summary>
		public void Apply(params BuildTargetGroup[] targetGroups)
		{
			// 
			targetGroups =  0 < targetGroups.Length
				? targetGroups
				: (BuildTargetGroup[])System.Enum.GetValues(typeof(BuildTargetGroup));
			
			// シンボルリストから、有効なシンボルのみ取得.
			// Collect active symbols.
			List<string> defines = list
				.Where(x => x.style == SymbolStyle.Symbol && !string.IsNullOrEmpty(x.name) && x.enabled)
				.Select(x => x.name)
				.Distinct()
				.ToList();
		
			// 有効なシンボルを文字列に変換します.
			// Convert active symbols to string.
			string defineSymbols = defines.Any() ? defines.Aggregate((a, b) => a + ";" + b) : string.Empty;

			Debug.Log(defineSymbols);
			// ターゲットグループの定義シンボルに適用.
			// Apply to build target platforms.
			foreach (BuildTargetGroup group in targetGroups)
			{
				// Group is Obsoleted.
				if (typeof(BuildTargetGroup).GetMember(group.ToString())[0].GetCustomAttributes(typeof(System.ObsoleteAttribute), false).Length != 0)
					continue;

				// Group is nothing.
				if ((int)group <= 0)
					continue;

				try
				{
					Debug.Log(group + ", " + (int)group);
					PlayerSettings.SetScriptingDefineSymbolsForGroup(group, "");
					PlayerSettings.SetScriptingDefineSymbolsForGroup(group, defineSymbols);
				}
				catch (System.Exception e)
				{
					Debug.LogException(e);
				}
			}
		}

		/// <summary>
		/// 定義シンボルリストを戻します.
		/// Revert 'Scripting Define Symbols' to project settings.
		/// </summary>
		public void Revert()
		{
			string define = PlayerSettings.GetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
		
			// 現在の有効シンボル.
			// Current active symbols.
			IEnumerable<string> currentDefines = define.Replace(" ", "")
				.Split(new char[] { ';' })
				.Where(x => !string.IsNullOrEmpty(x));

			// カタログに登録済みのシンボルを判定.
			// Is sybmol enable?
			list.ForEach(symbol => symbol.enabled = currentDefines.Contains(symbol.name));

			// 登録されていないシンボルをカタログに追加.
			// Add symbols that are not added in the catalog.
			foreach (string symbolName in currentDefines.Where(x => list.All(y => y.name != x)))
			{
				list.Add(new Symbol() { enabled = true, name = symbolName });
			}
		}

		/// <summary>
		/// シンボルスタイル.
		/// Style.
		/// </summary>
		public enum SymbolStyle
		{
			/// <summary>
			/// 通常シンボル.
			/// Default symbol.
			/// </summary>
			Symbol = 1,

			/// <summary>
			/// セパレータ.
			/// Separator.
			/// </summary>
			Separator = 10,

			/// <summary>
			/// ヘッダー.
			/// Header.
			/// </summary>
			Header,
		}

		/// <summary>
		/// 定義シンボルクラス.
		/// シンボル名と説明、スタイルを管理します.
		/// Symbol.
		/// Included header and separator.
		/// </summary>
		[System.Serializable]
		public class Symbol
		{
			/// <summary>
			/// シンボルスタイル.
			/// Style.
			/// </summary>
			public SymbolStyle style = SymbolStyle.Symbol;

			/// <summary>
			/// 定義シンボルは有効か.
			/// Is sybmol enable?
			/// </summary>
			public bool enabled { get; set; }

			/// <summary>
			/// 定義シンボル名.
			/// Name.
			/// </summary>
			public string name = "";

			/// <summary>
			/// 定義シンボル説明.
			/// Description.
			/// </summary>
			public string description = "";
		}
	}

	/// <summary>
	/// 定義シンボルカタログエディタ.
	/// </summary>
	public class SymbolCatalogEditor : EditorWindow
	{
		SymbolCatalog catalog;
		string currentDefine;
		string focus;
		Vector2 scrollPosition;

		//---- ▼ GUIキャッシュ ▼ ----
		static readonly ReorderableList ro = new ReorderableList(new List<SymbolCatalog>(), typeof(SymbolCatalog));
		static GUIStyle styleTitle;
		static GUIStyle styleHeader;
		static GUIStyle styleDescription;
		static readonly Color EnableStyleColor = new Color(0.5f, 0.5f, 0.5f, 1f);
		static readonly Color EnableTextColor = Color.white;
		static readonly Color DisableStyleColor = Color.white;
		static readonly Color DisableTextColor = new Color(1, 1, 1, 0.8f);

		void Initialize()
		{
			if (styleDescription != null)
				return;

			// タイトル
			styleTitle = new GUIStyle("IN BigTitle");
			styleTitle.alignment = TextAnchor.UpperLeft;
			styleTitle.fontSize = 12;
			styleTitle.stretchWidth = true;
			styleTitle.margin = new RectOffset();

			// シンボル説明スタイル
			styleDescription = new GUIStyle("HelpBox");
			styleDescription.richText = true;
			styleDescription.padding = new RectOffset(3, 3, 5, 1);
			styleDescription.fontSize = 10;

			// ヘッダースタイル
			styleHeader = new GUIStyle("VCS_StickyNote");
			styleHeader.richText = true;
			styleHeader.fontSize = 12;
			styleHeader.fontStyle = FontStyle.Bold;
			styleHeader.padding = new RectOffset(25, 3, 2, 2);
			styleHeader.alignment = TextAnchor.MiddleLeft;
			styleHeader.wordWrap = false;

			// シンボルリスト
			ro.drawElementCallback = DrawSymbol;
			ro.headerHeight = 0;
			ro.onAddDropdownCallback = (rect, list) =>
			{
				var gm = new GenericMenu();
				gm.AddItem(new GUIContent("Symbol"), false, () => AddSymbol(SymbolCatalog.SymbolStyle.Symbol));
				gm.AddItem(new GUIContent("Header"), false, () => AddSymbol(SymbolCatalog.SymbolStyle.Header));
				gm.AddItem(new GUIContent("Separator"), false, () => AddSymbol(SymbolCatalog.SymbolStyle.Separator));
				gm.DropDown(rect);
			};
			ro.onRemoveCallback = list => RemoveSymbol(catalog.list[list.index]);
			ro.onCanRemoveCallback = list => (0 <= list.index && list.index < catalog.list.Count);
			ro.elementHeight = 44;
			ro.onSelectCallback = (list) => EditorGUIUtility.keyboardControl = 0;

			minSize = new Vector2(300, 300);
		}
		//---- ▲ GUIキャッシュ ▲ ----

		[MenuItem("Coffee/Symbol Catalog")]
		static void OnOpenFromMenu()
		{
			EditorWindow.GetWindow<SymbolCatalogEditor>("Symbol Catalog");
		}

		/// <summary>
		/// 新しいSymbolCatalogアセットを生成します.
		/// Create new SymbolCatalog asset.
		/// </summary>
		static SymbolCatalog CreateCatalog()
		{
			if (!Directory.Exists("Assets/Editor"))
				AssetDatabase.CreateFolder("Assets", "Editor");

			//DefineSymbolsアセット生成して保存.
			SymbolCatalog catalog = ScriptableObject.CreateInstance(typeof(SymbolCatalog)) as SymbolCatalog;
			AssetDatabase.CreateAsset(catalog, "Assets/Editor/" + typeof(SymbolCatalog).Name + ".asset");
			AssetDatabase.SaveAssets();
			return catalog;
		}


		/// <summary>
		/// GUIを表示します.
		/// Draw GUI.
		/// </summary>
		void OnGUI()
		{
			Initialize();

			// カタログアセットを取得/作成します.
			// Get/Create catalog asset.
			catalog = catalog
			?? AssetDatabase.FindAssets("t:" + typeof(SymbolCatalog).Name)
						.Select(x => AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(x), typeof(SymbolCatalog)) as SymbolCatalog)
						.FirstOrDefault()
			?? CreateCatalog();

			// 変更チェック.PlayerSettingsなどで直接変えた場合も検知します.
			// Sync script define symbol.
			string define = PlayerSettings.GetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
			if (currentDefine != define)
			{
				currentDefine = define;
				catalog.Revert();
			}

			using (var svs = new EditorGUILayout.ScrollViewScope(scrollPosition))
			{
				scrollPosition = svs.scrollPosition;
				EditorGUI.BeginChangeCheck();

				// タイトル
				// Title
				GUILayout.Label(new GUIContent("   Available Scripting Define Symbols", EditorGUIUtility.ObjectContent(catalog, typeof(SymbolCatalog)).image), styleTitle);

				// シンボルリスト.
				// Draw all symbols in catalog.
				ro.list = catalog.list;
				ro.DoLayoutList();

				// Applyボタン.
				// Button to apply scripting define symbols.
				using (new EditorGUI.DisabledGroupScope(EditorApplication.isCompiling))
				{
					using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
					{
						GUILayout.Label(new GUIContent("Apply To", EditorGUIUtility.FindTexture("vcs_check")));

						if (GUILayout.Button("All Targets"))
						{
							catalog.Apply();
						}
						if (GUILayout.Button(string.Format("Current ({0})", EditorUserBuildSettings.selectedBuildTargetGroup)))
						{
							catalog.Apply(EditorUserBuildSettings.selectedBuildTargetGroup);
						}
					}
				}

				if (EditorGUI.EndChangeCheck())
					EditorUtility.SetDirty(catalog);
			}

			// コンパイル中.
			// Request repain during compiling.
			if (EditorApplication.isCompiling)
			{
				Repaint();
			}

			// Addされたら自動でシンボル名の入力開始します.
			// When add new symbol, focus input field.
			if (!string.IsNullOrEmpty(focus))
			{
				EditorGUI.FocusTextInControl(focus);
				focus = null;
			}
		}

		/// <summary>
		/// シンボルをカタログに追加します.
		/// Add symbol to catalog.
		/// </summary>
		void AddSymbol(SymbolCatalog.SymbolStyle style)
		{
			// 新しいシンボルを作成します.
			// Create new symbol.
			SymbolCatalog.Symbol symbol = new SymbolCatalog.Symbol(){ style = style };
			switch (style)
			{
				case SymbolCatalog.SymbolStyle.Symbol:
					symbol.name = "SYMBOL_NAME";
					symbol.description = "symbol description(<i>ritch-text is available</i>)";
					break;
				case SymbolCatalog.SymbolStyle.Header:
					symbol.name = "Header(<i>ritch-text is available</i>)";
					break;
				case SymbolCatalog.SymbolStyle.Separator:
					break;
			}

			// シンボルをカタログに追加します.
			// Add symbol to catalog.
			catalog.list.Add(symbol);

			// シンボル名の編集にフォーカス.
			// Focus to input field.
			focus = string.Format("symbol neme {0}", catalog.list.IndexOf(symbol));

			EditorUtility.SetDirty(catalog);
		}

		/// <summary>
		/// シンボルをカタログに追加します.
		/// Add symbol to catalog.
		/// </summary>
		void RemoveSymbol(SymbolCatalog.Symbol symbol)
		{
			EditorApplication.delayCall += () =>
			{
				catalog.list.Remove(symbol);
				ro.index = Mathf.Clamp(ro.index, 0, catalog.list.Count - 1);
				EditorUtility.SetDirty(catalog);
				Repaint();
			};
		}

		/// <summary>
		/// シンボルを描画します.
		/// Draw symbol.
		/// </summary>
		void DrawSymbol(Rect rect, int index, bool isActive, bool isFocused)
		{
			SymbolCatalog.Symbol symbol = ro.list[index] as SymbolCatalog.Symbol;

			// シンボルスタイルに応じて描画します.
			// Draw symbol based on style.
			switch (symbol.style)
			{
				case SymbolCatalog.SymbolStyle.Symbol:
					DrawDefaultSymbol(rect, symbol);
					break;
				case SymbolCatalog.SymbolStyle.Separator:
					GUI.Label(new Rect(rect.x + 10, rect.y + 24, rect.width - 20, 16), GUIContent.none, "sv_iconselector_sep");
					break;
				case SymbolCatalog.SymbolStyle.Header:
					DrawHeader(rect, symbol);
					break;
			}
			
			GUI.color = Color.white;
			GUI.contentColor = Color.white;
		}


		/// <summary>
		/// ヘッダーを描画します.
		/// Draw header.
		/// </summary>
		/// <param name="rect">描画座標.</param>
		/// <param name="index">シンボルインデックス.</param>
		/// <param name="symbol">表示するシンボル.</param>
		void DrawHeader(Rect rect, SymbolCatalog.Symbol symbol)
		{
			int index = catalog.list.IndexOf(symbol);

			// シンボル名を描画します.
			// Draw symbol name(editable).
			GUI.contentColor = Color.black;
			string symbolNameId = string.Format("symbol neme {0}", index);
			GUI.SetNextControlName(symbolNameId);
			styleHeader.richText = GUI.GetNameOfFocusedControl() != symbolNameId;
			symbol.name = GUI.TextField(new Rect(rect.x - 19, rect.y + rect.height - 24, rect.width + 23, 20), symbol.name, styleHeader);
			GUI.contentColor = Color.white;
		}

		/// <summary>
		/// 通常シンボルを描画します.
		/// Draw default symbol.
		/// </summary>
		/// <param name="rect">描画座標.</param>
		/// <param name="index">シンボルインデックス.</param>
		/// <param name="symbol">表示するシンボル.</param>
		void DrawDefaultSymbol(Rect rect, SymbolCatalog.Symbol symbol)
		{
			int index = catalog.list.IndexOf(symbol);

			// シンボル説明を描画します.
			// Draw symbol description(editable).
			string symbolDescriptionId = string.Format("symbol desctription {0}", index);
			GUI.SetNextControlName(symbolDescriptionId);
			styleDescription.richText = GUI.GetNameOfFocusedControl() != symbolDescriptionId;
			symbol.description = GUI.TextArea(new Rect(rect.x, rect.y + 12, rect.width, rect.height - 13), symbol.description, styleDescription);

			// 背景を描画します.
			// Draw symbol name background.
			GUI.color = symbol.enabled ? EnableStyleColor : DisableStyleColor;
			GUI.Label(new Rect(rect.x, rect.y, rect.width, 16), GUIContent.none, "ShurikenEffectBg");//"flow node flow" + (int)symbol.style);
			GUI.color = Color.white;

			// トグルを描画します.
			// Draw toggle.
			symbol.enabled = GUI.Toggle(new Rect(rect.x + 5, rect.y, 15, 16), symbol.enabled, GUIContent.none);

			// シンボル名を描画します.
			// Draw symbol name(editable).
			string symbolNameId = string.Format("symbol neme {0}", index);
			GUI.SetNextControlName(symbolNameId);
			GUI.color = symbol.enabled ? EnableTextColor : DisableTextColor;
			GUIStyle labelStyle = EditorGUIUtility.isProSkin ? EditorStyles.boldLabel : EditorStyles.whiteBoldLabel;
			symbol.name = GUI.TextField(new Rect(rect.x + 20, rect.y, rect.width - 40, 16), symbol.name, labelStyle);
			GUI.color = Color.white;

			// シンボル削除ボタンを描画します.
			// Draw delete button.
			if (GUI.Button(new Rect(rect.x + rect.width - 20, rect.y, 20, 20), EditorGUIUtility.FindTexture("treeeditor.trash"), EditorStyles.label))
			{
				RemoveSymbol(symbol);
			}
		}
	}
}