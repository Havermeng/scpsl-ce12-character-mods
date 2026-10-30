using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using ScpCeMods.Editor;
internal static class EngineDriver
{
    private static readonly JsonSerializerOptions Options=new JsonSerializerOptions {IncludeFields=true,WriteIndented=true};
    private static string Encode(object o)=>JsonSerializer.Serialize(o,o.GetType(),Options);
    private static Engine Create(string root)=>new Engine(root){Decode=(s,t)=>JsonSerializer.Deserialize(s,t,Options),Encode=Encode};
    private static void Write(string path,string s){Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,s,new UTF8Encoding(false));}
    private static int checks;
    private static void Check(bool ok,string message){checks++;if(!ok)throw new Exception("FAIL: "+message);}
    private static void Selftest(string parent)
    {
        string root=Path.Combine(parent,"engine-selftest-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var engine=Create(root);var bytes=new byte[]{239,187,191}.Concat(Encoding.UTF8.GetBytes("enum Roles {\r\n    Overwatch = 21\r\n}\r\n")).ToArray();
        const string rel="Assets/Roles.cs";Directory.CreateDirectory(Path.Combine(root,"ProjectSettings"));Directory.CreateDirectory(Path.Combine(root,"Assets"));File.WriteAllBytes(engine.Target(rel),bytes);
        string original=Engine.Normal(bytes),hash=Engine.Hash(Encoding.UTF8.GetBytes(original));
        Manifest Make(string id,string addition,string shared)
        {
            var dir=Path.Combine(root,"Assets/ScpCeMods",id);Directory.CreateDirectory(dir);
            using(var zip=new System.IO.Compression.ZipArchive(File.Create(Path.Combine(dir,"payload.zip.bytes")),System.IO.Compression.ZipArchiveMode.Create)){}
            var m=new Manifest {schema=2,id=id,name=id,version="1.0.0",components=new[]{new Component{id="core",required=true}},entries=new[]{new Entry {path=rel,component="core",kind="patch",baseNormalizedSha256=hash,patches=new[]{new Patch{before="    Overwatch = 21",after=addition+"    Overwatch = 21"},new Patch{before="enum Roles {",after="enum Roles {"+shared}}}}};
            Write(Path.Combine(dir,"manifest.json"),Encode(m));return engine.ReadManifest(dir);
        }
        var a=Make("scp939-ce12","    Scp939 = 22,\n"," // shared");var b=Make("scp3114-ce12","    Scp3114 = 41,\n"," // shared");
        Selection S(Manifest m)=>new Selection{id=m.id,components=new[]{"core"}};
        var mods=new[]{a,b};
        void Apply(params Selection[] s){var p=engine.CreatePlan(mods,s);Check(p.CanApply,string.Join(";",p.conflicts));engine.Apply(p);}
        Apply(S(a));Check(File.ReadAllText(engine.Target(rel)).Contains("Scp939"),"first mod installed");
        Apply(S(a),S(b));var both=File.ReadAllBytes(engine.Target(rel));Check(Engine.Normal(both).Contains("Scp939")&&Engine.Normal(both).Contains("Scp3114"),"both patches");
        Check(Engine.Normal(both).Split(new[]{"// shared"},StringSplitOptions.None).Length==2,"shared patch deduplicated");
        Check(both[0]==239&&Encoding.UTF8.GetString(both).Contains("\r\n"),"BOM/CRLF kept");
        Apply(S(b));Check(!Engine.Normal(File.ReadAllBytes(engine.Target(rel))).Contains("Scp939")&&Engine.Normal(File.ReadAllBytes(engine.Target(rel))).Contains("Scp3114"),"independent uninstall");
        Apply();Check(File.ReadAllBytes(engine.Target(rel)).SequenceEqual(bytes),"exact baseline restored");
        Apply(S(b));Apply(S(a),S(b));Check(File.ReadAllBytes(engine.Target(rel)).SequenceEqual(both),"install order independent");
        Apply(S(a));Apply();Check(File.ReadAllBytes(engine.Target(rel)).SequenceEqual(bytes),"reverse order uninstall exact");
        Apply(S(a));var installed=File.ReadAllBytes(engine.Target(rel));File.AppendAllText(engine.Target(rel),"// user edit");var bad=engine.CreatePlan(mods,Array.Empty<Selection>());Check(!bad.CanApply,"manual edits blocked");Check(File.ReadAllText(engine.Target(rel)).Contains("user edit"),"manual edits preserved");File.WriteAllBytes(engine.Target(rel),installed);
        var plan=engine.CreatePlan(mods,new[]{S(b)});engine.BeforeWrite=p=>throw new IOException("injected I/O fault");try{engine.Apply(plan);throw new Exception("fault not injected");}catch(IOException){}engine.BeforeWrite=null;
        Check(File.ReadAllBytes(engine.Target(rel)).SequenceEqual(installed),"transaction rollback exact");Check(engine.ReadState().mods.Single().id==a.id,"state rollback exact");Check(!File.Exists(Path.Combine(root,Engine.JournalPath)),"recovery journal cleared");
        var race=engine.CreatePlan(mods,Array.Empty<Selection>());File.AppendAllText(engine.Target(rel),"race");try{engine.Apply(race);throw new Exception("race not blocked");}catch(IOException){}Check(File.ReadAllText(engine.Target(rel)).EndsWith("race"),"post-preview edit preserved");File.WriteAllBytes(engine.Target(rel),installed);
        foreach(var p in new[]{"../x","Assets/../x","Assets/C:/x","Assets//x","Assets/ScpCeModManager/Editor/x.cs","Translations/../../x","C:/x"})
        {try{engine.Target(p);throw new Exception("unsafe path accepted: "+p);}catch(IOException){checks++;}}
        var owned=engine.ReadState().files.Single();var backup=Path.Combine(root,"ScpCeModBackups",owned.backup);var saved=File.ReadAllBytes(backup);File.AppendAllText(backup,"corrupt");Check(!engine.CreatePlan(mods,Array.Empty<Selection>()).CanApply,"damaged backup blocked");File.WriteAllBytes(backup,saved);
        Apply();Check(File.ReadAllBytes(engine.Target(rel)).SequenceEqual(bytes),"final exact restore");
        var metaBytes=Encoding.UTF8.GetBytes("fileFormatVersion: 2\nguid: 1234567890abcdef1234567890abcdef\n");
        var metaEntry=new Entry{path="Assets/Optional/Picture.asset.meta",component="core",kind="add",blob="files/Assets/Optional/Picture.asset.meta",sha256=Engine.Hash(metaBytes)};
        a.entries=a.entries.Concat(new[]{metaEntry}).ToArray();
        using(var zip=new System.IO.Compression.ZipArchive(File.Create(Path.Combine(a.directory,"payload.zip.bytes")),System.IO.Compression.ZipArchiveMode.Create))
        using(var stream=zip.CreateEntry(metaEntry.blob).Open())stream.Write(metaBytes,0,metaBytes.Length);
        engine.GuidOwner=g=>"assets/optional/picture.asset";
        Check(engine.CreatePlan(new[]{a},new[]{S(a)}).CanApply,"GUID path case does not create a false conflict");
        engine.GuidOwner=g=>"Assets/Other.asset";
        Check(!engine.CreatePlan(new[]{a},new[]{S(a)}).CanApply,"real GUID collision blocked");
        engine.GuidOwner=null;metaEntry.sha256=new string('0',64);
        Check(!engine.CreatePlan(new[]{a},new[]{S(a)}).CanApply,"tampered payload blocked");
        Check(!File.Exists(engine.Target(metaEntry.path)),"failed preflights made no target writes");
        Console.WriteLine(Encode(new{result="PASS",checks,fixture=root}));
    }
    public static int Main(string[] args)
    {
        try { Selftest(Path.GetFullPath(args.Length > 0 ? args[0] : "./test-output")); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex.ToString()); return 1; }
    }
}
