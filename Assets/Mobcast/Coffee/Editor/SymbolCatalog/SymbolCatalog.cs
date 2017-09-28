using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditorInternal;
using UnityEngine;
using System.Reflection;

namespace Mobcast.Coffee.Editor
{
	/// <summary>
	/// [アセット]定義シンボルカタログクラス.
	/// プロジェクトで利用できる定義シンボルをカタログ化し、設定/共有しやすい機能を提供します.
	/// </summary>
	public class SymbolCatalog : ScriptableObject
	{
		/// <summary>定義シンボルリスト.</summary>
		public List<Symbol> list = new List<Symbol>();

		/// <summary>有効な定義シンボルリストを適用します.</summary>
		public void Apply()
		{
			//シンボルリストから、有効なシンボルのみ取得.
			List<string> defines = list
				.Where(x => x.style != SymbolStyle.Separator && !string.IsNullOrEmpty(x.name) && x.enabled)
				.Select(x => x.name)
				.Distinct()
				.ToList();
		
			//定義シンボルを文字列に変換し、全プラットフォームに適用.
			string defineSymbols = defines.Any() ? defines.Aggregate((a, b) => a + ";" + b) : string.Empty;
			Apply(defineSymbols);
		}

		/// <summary>有効な定義シンボルリストを適用します.</summary>
		public void Apply(string defineSymbols)
		{
			//全てのターゲットグループの定義シンボルに適用.
			foreach (BuildTargetGroup group in System.Enum.GetValues(typeof(BuildTargetGroup)))
			{
				if ((int)group <= 0)
					continue;
				try
				{
					PlayerSettings.SetScriptingDefineSymbolsForGroup(group, "");
					PlayerSettings.SetScriptingDefineSymbolsForGroup(group, defineSymbols);
				}
				catch (System.Exception e)
				{
					Debug.LogException(e);
				}
			}

		}

		/// <summary>定義シンボルリストを戻します.</summary>
		public void Revert()
		{
			string define = PlayerSettings.GetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
		
			IEnumerable<string> currentDefines = define.Replace(" ", "").Split(new char[] { ';' });

			//カタログに登録済みのシンボルを判定.
			list.ForEach(x =>
				{
					x.enabled = !string.IsNullOrEmpty(x.name) && currentDefines.Contains(x.name);
					if (x.enabled && x.style != SymbolStyle.Symbol)
					{
						foreach (var style in list.Where(style=>style.style == x.style))
							style.enabled = false;
						x.enabled = true;
					}

				});

			//カタログに登録されていないものもシンボルとして追加.
			foreach (string name in currentDefines.Where(x => !string.IsNullOrEmpty(x) && !list.Any(y => y.name == x)))
			{
				list.Add(new Symbol() { enabled = true, name = name });
			}
		}

		/// <summary>
		/// シンボルスタイル.
		/// </summary>
		public enum SymbolStyle
		{
			/// <summary>通常シンボル.</summary>
			Symbol = 1,
			/// <summary>セパレータ.</summary>
			Separator = 10,
			/// <summary>ヘッダー.</summary>
			Header,
		}

		/// <summary>
		/// 定義シンボルクラス.
		/// シンボル名と説明、グループを管理します.
		/// </summary>
		[System.Serializable]
		public class Symbol
		{
			/// <summary>シンボルスタイル.</summary>
			public SymbolStyle style = SymbolStyle.Symbol;

			/// <summary>定義シンボルは有効か.</summary>
			public bool enabled { get; set; }

			/// <summary>定義シンボル名.</summary>
			public string name = "";

			/// <summary>定義シンボル説明.</summary>
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
		Vector2 scrollPosition;

		//---- ▼ GUIキャッシュ ▼ ----
		static readonly ReorderableList ro = new ReorderableList(new List<SymbolCatalog>(), typeof(SymbolCatalog));
		static GUIContent contentApply;
		static GUIStyle styleTitle;
		static GUIStyle styleHeader;
		static GUIStyle styleDescription;
		static readonly Color EnableStyleColor = new Color(0.5f, 0.5f, 0.5f, 1f);
		static readonly Color EnableTextColor = Color.white;
		static readonly Color DisableStyleColor = Color.white;
		static readonly Color DisableTextColor = new Color(1, 1, 1, 0.5f);

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
			styleHeader.fontSize = 13;
			styleHeader.fontStyle = FontStyle.Bold;
			styleHeader.padding = new RectOffset(25, 3, 2, 2);
			styleHeader.alignment = TextAnchor.MiddleLeft;
			styleHeader.wordWrap = false;

			contentApply = new GUIContent("Apply", EditorGUIUtility.FindTexture("vcs_check"));

			// シンボルリスト
			ro.drawElementCallback = DrawSymbol;
			ro.headerHeight = 0;
//			ro.drawHeaderCallback = rect => GUI.Label(rect, "Available Script Symbols");
			ro.onAddDropdownCallback = (rect, list) =>
			{
				var gm = new GenericMenu();
				gm.AddItem(new GUIContent("Symbol"), false, () => AddSymbol(SymbolCatalog.SymbolStyle.Symbol));
				gm.AddItem(new GUIContent("Header"), false, () => AddSymbol(SymbolCatalog.SymbolStyle.Header));
				gm.AddItem(new GUIContent("Separator"), false, () => AddSymbol(SymbolCatalog.SymbolStyle.Separator));
				gm.DropDown(rect);
			};
			ro.onRemoveCallback = list =>
			{
				catalog.list.RemoveAt(list.index);
				if (0 < list.index)
					list.index--;
			};
			ro.onCanRemoveCallback = list => (0 <= list.index && list.index < catalog.list.Count);
			ro.elementHeight = 44;
			ro.onSelectCallback = (list) => EditorGUIUtility.keyboardControl = 0;

			minSize = new Vector2(300, 300);
		}
		//---- ▲ GUIキャッシュ ▲ ----

		int addIndex = -1;

		[MenuItem("Coffee/Symbol Catalog")]
		static void OnOpenFromMenu()
		{
			EditorWindow.GetWindow<SymbolCatalogEditor>("Symbol Catalog");
		}

		/// <summary>新しいScript Define Symbolsアセットを生成します.</summary>
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
		/// インスペクタを表示.
		/// </summary>
		void OnGUI()
		{
			Initialize();

			// カタログアセットを取得/作成
			catalog = catalog
			?? AssetDatabase.FindAssets("t:" + typeof(SymbolCatalog).Name)
				.Select(x => AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(x), typeof(SymbolCatalog)) as SymbolCatalog)
				.FirstOrDefault()
			?? CreateCatalog();

			//変更チェック.PlayerSettingsなどで直接変えた場合も検知.
			string define = PlayerSettings.GetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
			if (currentDefine != define)
			{
				currentDefine = define;
				catalog.Revert();
			}

			using (var svs = new EditorGUILayout.ScrollViewScope(scrollPosition))
			{
				scrollPosition = svs.scrollPosition;

				//
				GUILayout.Label(new GUIContent("   Available Script Symbol Define", EditorGUIUtility.ObjectContent(catalog, typeof(SymbolCatalog)).image), styleTitle);

				//シンボルリスト表示.
				ro.list = catalog.list;
				ro.DoLayoutList();


				using (new EditorGUILayout.HorizontalScope())
				{
					GUILayout.FlexibleSpace();

					//Applyボタン. PlayerSettingsのDefineSymbolに適用.
					using (new EditorGUI.DisabledGroupScope(EditorApplication.isCompiling))
					{
						if (GUILayout.Button(contentApply, GUILayout.Width(57)))
						{
							catalog.Apply();
						}
					}
				}
			}

			//コンパイル中.
			if (EditorApplication.isCompiling)
			{
				Repaint();
			}
		}

		/// <summary>
		/// シンボルを表示.
		/// </summary>
		void AddSymbol(SymbolCatalog.SymbolStyle style)
		{
			catalog.list.Add(new SymbolCatalog.Symbol(){
				style = style,
				name = style == SymbolCatalog.SymbolStyle.Symbol ? "NEW_SYMBOL" : "",
				description = style != SymbolCatalog.SymbolStyle.Separator ? "Comment(<i>option, ritch-text is available</i>)" : "",
			});
			addIndex = catalog.list.Count - 1;
			EditorUtility.SetDirty(catalog);
		}

		/// <summary>
		/// シンボルを表示.
		/// </summary>
		void DrawSymbol(Rect rect, int index, bool isActive, bool isFocused)
		{
			SymbolCatalog.Symbol currentSymbol = ro.list[index] as SymbolCatalog.Symbol;

			//セパレータの場合、線引くだけ.
			if (currentSymbol.style == SymbolCatalog.SymbolStyle.Separator)
			{
				GUI.Label(new Rect(rect.x + 10, rect.y + 24, rect.width - 20, 16), GUIContent.none, "sv_iconselector_sep");
			}
			//ヘッダーの場合、説明テキストだけ.
			else if (currentSymbol.style == SymbolCatalog.SymbolStyle.Header)
			{
				GUI.contentColor = Color.black;
				string symbolDescriptionId = string.Format("symbol desctription {0}", index);
				GUI.SetNextControlName(symbolDescriptionId);
				styleHeader.richText = GUI.GetNameOfFocusedControl() != symbolDescriptionId;
				currentSymbol.description = GUI.TextField(new Rect(rect.x - 19, rect.y + rect.height - 24, rect.width + 23, 20), currentSymbol.description, styleHeader);
				GUI.contentColor = Color.white;
			}
			//通常シンボル.
			else
			{
				DrawDefaultSymbol(rect, index, currentSymbol);
			}

			EditorUtility.SetDirty(catalog);
			GUI.color = Color.white;
			GUI.contentColor = Color.white;
		}

		/// <summary>
		/// 通常シンボルを表示.
		/// </summary>
		/// <param name="rect">描画座標.</param>
		/// <param name="index">シンボルインデックス.</param>
		/// <param name="symbol">表示するシンボル.</param>
		void DrawDefaultSymbol(Rect rect, int index, SymbolCatalog.Symbol symbol)
		{
			//シンボル説明を描画します.
			string symbolDescriptionId = string.Format("symbol desctription {0}", index);
			GUI.SetNextControlName(symbolDescriptionId);
			styleDescription.richText = GUI.GetNameOfFocusedControl() != symbolDescriptionId;
			symbol.description = GUI.TextArea(new Rect(rect.x, rect.y + 12, rect.width, rect.height - 13), symbol.description, styleDescription);

			//背景を描画します.
			GUI.color = symbol.enabled ? EnableStyleColor : DisableStyleColor;
			GUI.Label(new Rect(rect.x, rect.y, rect.width, 16), GUIContent.none, "ShurikenEffectBg");//"flow node flow" + (int)symbol.style);
			GUI.color = Color.white;

			//有効トグルを描画します.
			//				bool isGrouped = symbol.style != SymbolCatalog.SymbolStyle.Symbol;
			bool isGrouped = false;
			bool isEnable = EditorGUI.Toggle(new Rect(rect.x + 5, rect.y, 15, 16), symbol.enabled, isGrouped ? "Radio" : "Toggle");
			if (symbol.enabled != isEnable)
			{
				//グループシンボルの場合、同じグループのシンボルを無効化.
				if (isGrouped)
				{
					foreach (var style in catalog.list.Where(style=>style.style == symbol.style))
						style.enabled = false;
					symbol.enabled = true;
				}
				else
				{
					symbol.enabled = isEnable;
				}
			}

			//シンボル名を描画します.
			string symbolNameId = string.Format("symbol neme {0}", index);
			GUI.SetNextControlName(symbolNameId);
			GUI.color = symbol.enabled ? EnableTextColor : DisableTextColor;
			GUIStyle labelStyle = EditorGUIUtility.isProSkin ? EditorStyles.boldLabel : EditorStyles.whiteBoldLabel;
			symbol.name = GUI.TextField(new Rect(rect.x + 21, rect.y, rect.width - 50, 16), symbol.name, labelStyle);
			GUI.color = Color.white;

			//Addされたら自動でシンボル名の入力開始します.
			if (addIndex == index)
			{
				EditorGUI.FocusTextInControl(symbolNameId);
				addIndex = -1;
			}
		}
	}
}