using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace ScpCeMods.Editor
{
    [Serializable] internal sealed class Receipt { public string[] seen = Array.Empty<string>(); }
    public static class ModInstaller
    {
        public const string Folder = "Assets/ScpCeModManager";
        public const string Bundles = "Assets/ScpCeMods";
        public static string Root => Path.GetFullPath(Path.Combine(Application.dataPath,"..")).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        public static Engine CreateEngine() => new Engine(Root) {
            Decode=(s,t)=>JsonUtility.FromJson(s,t), Encode=o=>JsonUtility.ToJson(o,true), GuidOwner=AssetDatabase.GUIDToAssetPath
        };
        public static List<Manifest> Manifests()
        {
            var engine=CreateEngine();var list=new List<Manifest>();
            var root=Path.Combine(Root,Bundles);
            if(Directory.Exists(root))foreach(var dir in Directory.GetDirectories(root).OrderBy(x=>x))
                if(File.Exists(Path.Combine(dir,"manifest.json")))list.Add(engine.ReadManifest(dir));
            if(list.Select(m=>m.id).Distinct().Count()!=list.Count)throw new IOException("Duplicate mod installers");
            return list;
        }
        public static Selection Defaults(Manifest m) => new Selection {id=m.id,version=m.version,components=m.components.Where(c=>c.required||c.defaultEnabled).Select(c=>c.id).ToArray()};
        public static void Apply(IEnumerable<Selection> selections)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling)throw new InvalidOperationException("Остановите Play и дождитесь компиляции / Stop Play and wait for compilation.");
            if(!Application.unityVersion.StartsWith("6000.3.",StringComparison.Ordinal))throw new IOException("Supported editor: Unity 6000.3 LTS. Current: "+Application.unityVersion);
            var engine=CreateEngine();var plan=engine.CreatePlan(Manifests(),selections);
            if(!plan.CanApply)throw new IOException(string.Join("\n",plan.conflicts));
            AssetDatabase.DisallowAutoRefresh();AssetDatabase.StartAssetEditing();
            try{engine.Apply(plan);RememberSeen();}
            finally{AssetDatabase.StopAssetEditing();AssetDatabase.AllowAutoRefresh();AssetDatabase.Refresh();}
            Debug.Log("[SCP CE12 Mods] Applied "+plan.selection.Length+" mod(s); "+plan.Writes+" file changes. Originals and recovery: ScpCeModBackups.");
            // Resource-only switches must also reset cached role templates, spawn lists
            // and feature flags when Enter Play Mode has domain reload disabled.
            if(plan.Writes>0)EditorUtility.RequestScriptReload();
        }
        private const string ReceiptPath="ProjectSettings/ScpCeModsAutoInstall.json";
        private static Receipt ReadReceipt()
        {
            var p=Path.Combine(Root,ReceiptPath);
            return File.Exists(p)?JsonUtility.FromJson<Receipt>(File.ReadAllText(p))??new Receipt():new Receipt();
        }
        public static void RememberSeen()
        {
            var r=ReadReceipt();r.seen=r.seen.Concat(Manifests().Select(m=>m.id)).Distinct().ToArray();
            File.WriteAllText(Path.Combine(Root,ReceiptPath),JsonUtility.ToJson(r,true));
        }
        public static void Forget(string id)
        {var r=ReadReceipt();r.seen=r.seen.Where(s=>s!=id).ToArray();File.WriteAllText(Path.Combine(Root,ReceiptPath),JsonUtility.ToJson(r,true));}
        public static void AutoInstall()
        {
            var r=ReadReceipt();var mods=Manifests();var pending=mods.Where(m=>!r.seen.Contains(m.id)).ToArray();
            if(pending.Length==0)return;
            // The explicit legacy guard runs before applying or writing a receipt.
            var state=CreateEngine().ReadState();var selection=state.mods.ToList();
            foreach(var m in pending)if(!selection.Any(s=>s.id==m.id))selection.Add(Defaults(m));
            Apply(selection);
        }
        public static void Recover()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling)throw new InvalidOperationException("Stop Play and wait for compilation");
            AssetDatabase.DisallowAutoRefresh();AssetDatabase.StartAssetEditing();
            try{CreateEngine().Recover();}finally{AssetDatabase.StopAssetEditing();AssetDatabase.AllowAutoRefresh();AssetDatabase.Refresh();}
            EditorUtility.RequestScriptReload();
        }
    }
    [InitializeOnLoad] internal static class AutomaticInstall
    {
        private static bool pending=true,failed;
        static AutomaticInstall(){EditorApplication.update+=Update;}
        internal static void Queue(){pending=true;failed=false;}
        private static void Update()
        {
            if(!pending||failed||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;
            pending=false;
            try{ModInstaller.AutoInstall();}
            catch(Exception e){failed=true;Debug.LogWarning("[SCP CE12 Mods] Auto-install stopped without overwriting conflicts. Open SCP > Mods > Character Mod Manager. "+e.Message);}
        }
    }
    internal sealed class ImportWatcher:AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported,string[] deleted,string[] moved,string[] from)
        {if(imported.Any(p=>p.StartsWith(ModInstaller.Bundles+"/")&&p.EndsWith("/manifest.json")))AutomaticInstall.Queue();}
    }
    public sealed class ModManagerWindow:EditorWindow
    {
        private List<Manifest> mods=new List<Manifest>();
        private readonly Dictionary<string,HashSet<string>> selected=new Dictionary<string,HashSet<string>>();
        private readonly HashSet<string> installed=new HashSet<string>();
        private Vector2 scroll;private string error,notice;
        [MenuItem("SCP/Mods/Character Mod Manager")]
        public static void Open(){GetWindow<ModManagerWindow>("SCP Character Mods");}
        [MenuItem("SCP/Mods/SCP-939 Mod Manager")]
        private static void Open939(){Open();}
        [MenuItem("SCP/Mods/SCP-3114 Mod Manager (2.x)")]
        private static void Open3114(){Open();}
        private void OnEnable(){minSize=new Vector2(540,440);Reload();}
        private void Reload()
        {
            error=null;selected.Clear();installed.Clear();
            try
            {
                mods=ModInstaller.Manifests();var state=ModInstaller.CreateEngine().ReadState();
                foreach(var s in state.mods)installed.Add(s.id);
                foreach(var m in mods)
                {var s=state.mods.FirstOrDefault(x=>x.id==m.id);selected[m.id]=new HashSet<string>((s??ModInstaller.Defaults(m)).components);}
            }
            catch(Exception e){error=e.Message;}
        }
        private List<Selection> Desired(string changeId=null,bool remove=false)
        {
            var list=new List<Selection>();
            var saved=ModInstaller.CreateEngine().ReadState().mods.ToDictionary(s=>s.id);
            foreach(var m in mods)
                if((installed.Contains(m.id)||m.id==changeId)&&!(remove&&m.id==changeId))
                    list.Add(new Selection{id=m.id,version=m.version,components=m.id==changeId?selected[m.id].OrderBy(s=>s).ToArray():saved[m.id].components.ToArray()});
            return list;
        }
        private void Run(Action action)
        {
            try{action();notice="Готово / Done";Reload();}
            catch(Exception e){error=e.Message;Debug.LogException(e);}
        }
        private void OnGUI()
        {
            EditorGUILayout.LabelField("Моды персонажей / Character Mods",EditorStyles.boldLabel);
            EditorGUILayout.LabelField("SCP:SL Community Edition 12.0.2 · Unity 6000.3 LTS");
            EditorGUILayout.HelpBox("Выберите компоненты и нажмите «Применить». Удаление одного мода сохраняет второй. Изменения доступны в исходниках проекта.\nChoose components, then Apply. Removing one mod preserves the other. Installed source files are editable.",MessageType.Info);
            if(error!=null)EditorGUILayout.HelpBox(error,MessageType.Error);
            if(notice!=null)EditorGUILayout.HelpBox(notice,MessageType.Info);
            bool busy=EditorApplication.isCompiling||EditorApplication.isPlayingOrWillChangePlaymode;
            scroll=EditorGUILayout.BeginScrollView(scroll);
            using(new EditorGUI.DisabledScope(busy))
            {
                foreach(var m in mods)
                {
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.LabelField(m.name+"  "+m.version,EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(installed.Contains(m.id)?"Установлен / Installed":"Не установлен / Not installed");
                    foreach(var c in m.components)
                    {
                        using(new EditorGUI.DisabledScope(c.required))
                        {
                            bool old=selected[m.id].Contains(c.id);bool next=EditorGUILayout.ToggleLeft(c.name+(c.required?" (обязательно / required)":""),old);
                            if(old!=next)
                            {
                                if(next){selected[m.id].Add(c.id);foreach(var req in c.requires)selected[m.id].Add(req);}
                                else{selected[m.id].Remove(c.id);foreach(var other in m.components.Where(x=>x.requires.Contains(c.id)))selected[m.id].Remove(other.id);}
                            }
                        }
                        if(!string.IsNullOrEmpty(c.description))EditorGUILayout.LabelField(c.description,EditorStyles.wordWrappedMiniLabel);
                    }
                    EditorGUILayout.BeginHorizontal();
                    if(GUILayout.Button(installed.Contains(m.id)?"Применить / Apply":"Установить / Install"))Run(()=>ModInstaller.Apply(Desired(m.id)));
                    using(new EditorGUI.DisabledScope(!installed.Contains(m.id)))
                        if(GUILayout.Button("Удалить мод / Uninstall"))Run(()=>ModInstaller.Apply(Desired(m.id,true)));
                    EditorGUILayout.EndHorizontal();
                    if(!installed.Contains(m.id)&&GUILayout.Button("Удалить также файлы установщика / Remove installer files"))
                    {
                        if(EditorUtility.DisplayDialog("Remove installer","Мод не установлен. Удалить его установочные файлы? Для повторной установки потребуется импорт .unitypackage.\nDelete this inactive installer? Reimport the package to reinstall.","Удалить / Remove","Отмена / Cancel"))
                        {Run(()=>{AssetDatabase.DeleteAsset(m.directory.Substring(ModInstaller.Root.Length+1).Replace('\\','/'));ModInstaller.Forget(m.id);});GUIUtility.ExitGUI();}
                    }
                    if(GUILayout.Button("Открыть README / Open README"))EditorUtility.RevealInFinder(Path.Combine(m.directory,"README_RU.md"));
                    EditorGUILayout.EndVertical();
                }
                if(mods.Count==0)EditorGUILayout.HelpBox("Импортируйте пакет SCP-939 или SCP-3114. / Import an SCP-939 or SCP-3114 .unitypackage.",MessageType.Info);
                if(File.Exists(Path.Combine(ModInstaller.Root,Engine.JournalPath))&&GUILayout.Button("Восстановить прерванную операцию / Recover"))Run(ModInstaller.Recover);
                if(GUILayout.Button("Обновить / Refresh"))Reload();
                EditorGUILayout.HelpBox("Резервные копии: ScpCeModBackups. Если вы редактировали установленные файлы, сохраните изменения перед сменой компонентов: конфликт остановит операцию.\nBackups: ScpCeModBackups. Preserve manual edits before changing components; conflicts stop the operation.",MessageType.None);
            }
            EditorGUILayout.EndScrollView();
            if(busy)EditorGUILayout.HelpBox("Остановите Play и дождитесь компиляции. / Stop Play and wait for compilation.",MessageType.Warning);
        }
    }
    internal sealed class TranslationBuildCopy:IPostprocessBuildWithReport
    {
        public int callbackOrder=>0;
        public void OnPostprocessBuild(BuildReport report)
        {
            if(ModInstaller.CreateEngine().ReadState().mods.Length==0)return;
            string src=Path.Combine(ModInstaller.Root,"Translations"),dest=Path.Combine(Path.GetDirectoryName(report.summary.outputPath),"Translations");
            if(!Directory.Exists(src))return;
            foreach(var p in Directory.GetFiles(src,"*",SearchOption.AllDirectories))
            {string target=Path.Combine(dest,p.Substring(src.Length+1));Directory.CreateDirectory(Path.GetDirectoryName(target));File.Copy(p,target,true);}
        }
    }
}
