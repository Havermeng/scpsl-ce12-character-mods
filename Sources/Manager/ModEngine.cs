using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace ScpCeMods.Editor
{
    [Serializable] public sealed class Component { public string id, name, description; public bool required, defaultEnabled; public string[] requires = Array.Empty<string>(); }
    [Serializable] public sealed class Patch { public string before, after; }
    [Serializable] public sealed class Row { public int index; public string value; }
    [Serializable] public sealed class Entry
    {
        public string path, component, kind, blob, sha256, baseNormalizedSha256;
        public Patch[] patches = Array.Empty<Patch>(); public Row[] rows = Array.Empty<Row>();
    }
    [Serializable] public sealed class Requirement { public string path, guid, component; }
    [Serializable] public sealed class Manifest
    {
        public int schema; public string id, name, version, target;
        public Component[] components = Array.Empty<Component>();
        public Entry[] entries = Array.Empty<Entry>();
        public Requirement[] requirements = Array.Empty<Requirement>();
        [NonSerialized] public string directory;
    }
    [Serializable] public sealed class Selection { public string id, version; public string[] components = Array.Empty<string>(); }
    [Serializable] public sealed class OwnedFile { public string path, installedSha256, originalSha256, backup; public bool originalExists; }
    [Serializable] public sealed class State
    {
        public int schema = 2; public string root;
        public Selection[] mods = Array.Empty<Selection>();
        public OwnedFile[] files = Array.Empty<OwnedFile>();
        public string[] directories = Array.Empty<string>();
    }
    [Serializable] public sealed class Rollback { public string path, backup, sha256; public bool existed; }
    [Serializable] public sealed class Journal
    {
        public string root, transaction, oldStateBase64; public bool hadState;
        public Rollback[] files = Array.Empty<Rollback>(); public string[] directories = Array.Empty<string>();
    }
    public sealed class Change { public string path; public byte[] before, after, original; public OwnedFile old; public bool desired; }
    public sealed class Plan
    {
        public string stateHash; public Selection[] selection; public State previous;
        public readonly List<Change> changes = new List<Change>();
        public readonly List<string> conflicts = new List<string>();
        public bool CanApply => conflicts.Count == 0;
        public int Writes => changes.Count(c => !Engine.Same(c.before, c.after));
    }
    // File/patch engine has no Unity API dependency; the editor adapter supplies JSON and refresh hooks.
    public sealed class Engine
    {
        public const string StatePath = "ProjectSettings/ScpCeModsState.json";
        public const string JournalPath = "ProjectSettings/ScpCeModsTransaction.json";
        public readonly string Root;
        public Func<string, Type, object> Decode;
        public Func<object, string> Encode;
        public Func<string, string> GuidOwner;
        public Action<string> BeforeWrite; // Fault-injection seam; null in the editor adapter.
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        public Engine(string root) { Root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        public static string Hash(byte[] data) { using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(data)).Replace("-", "").ToLowerInvariant(); }
        public static bool Same(byte[] a, byte[] b) => a == null ? b == null : b != null && a.SequenceEqual(b);
        public static string Normal(byte[] b) => Utf8.GetString(b).TrimStart('\ufeff').Replace("\r\n", "\n").Replace("\r", "\n");
        private static bool Under(string p, string root) => p.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        private void NoLinks(string path)
        {
            for (var p = path; !string.IsNullOrEmpty(p) && !p.Equals(Root, StringComparison.OrdinalIgnoreCase); p = Path.GetDirectoryName(p))
                if ((File.Exists(p) || Directory.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked path: " + p);
        }
        public string Target(string rel)
        {
            if (string.IsNullOrEmpty(rel) || rel.Contains('\\') || rel.Contains(':') || rel.StartsWith("/") ||
                rel.Split('/').Any(p => p == ".." || p == "." || p.Length == 0) ||
                !(rel.StartsWith("Assets/") || rel.StartsWith("Translations/")) ||
                rel.StartsWith("Assets/ScpCeModManager") || rel.StartsWith("Assets/ScpCeMods/")) throw new IOException("Invalid mod target: " + rel);
            var path = Path.GetFullPath(Path.Combine(Root, rel));
            if (!Under(path, Root)) throw new IOException("Outside project: " + rel);
            NoLinks(path); return path;
        }
        private string Backup(string rel)
        {
            var root = Path.Combine(Root, "ScpCeModBackups");
            if (string.IsNullOrEmpty(rel) || Path.IsPathRooted(rel)) throw new IOException("Invalid backup path");
            var path = Path.GetFullPath(Path.Combine(root, rel));
            if (!Under(path, root)) throw new IOException("Outside backup folder");
            NoLinks(path); return path;
        }
        private string Internal(string rel) { var path = Path.Combine(Root, rel); NoLinks(path); return path; }
        private static byte[] Read(string p) => File.Exists(p) ? File.ReadAllBytes(p) : null;
        private static void Atomic(string p, byte[] data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            var tmp = p + ".scpmods-" + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllBytes(tmp, data); if (File.Exists(p)) File.Replace(tmp, p, null); else File.Move(tmp, p); }
            finally { if (File.Exists(tmp)) File.Delete(tmp); }
        }
        private void Save(string p, object value) => Atomic(p, Utf8.GetBytes(Encode(value)));
        public State ReadState()
        {
            var p = Internal(StatePath); if (!File.Exists(p)) return new State { root = Root };
            var s = (State)Decode(File.ReadAllText(p, Utf8), typeof(State));
            if (s == null || s.schema != 2 || !Root.Equals(s.root, StringComparison.OrdinalIgnoreCase) || s.files == null || s.mods == null) throw new IOException("Invalid state or project path changed. Restore backups with the project.");
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in s.files) { Target(f.path); if (!paths.Add(f.path)) throw new IOException("Duplicate ownership record"); if (f.originalExists) Backup(f.backup); }
            foreach (var d in s.directories) Target(d);
            if (s.mods.Select(m => m.id).Distinct().Count() != s.mods.Length) throw new IOException("Duplicate mod state");
            return s;
        }
        public Manifest ReadManifest(string directory)
        {
            var full = Path.GetFullPath(directory); NoLinks(full);
            if (!Under(full, Path.Combine(Root, "Assets/ScpCeMods"))) throw new IOException("Invalid mod folder");
            var m = (Manifest)Decode(File.ReadAllText(Path.Combine(full, "manifest.json"), Utf8), typeof(Manifest));
            if (m == null || m.schema != 2 || !(m.id == "scp939-ce12" || m.id == "scp3114-ce12") || m.entries == null || m.entries.Length > 5000 || m.components == null) throw new IOException("Unsupported mod manifest");
            m.directory = full;
            var ids = new HashSet<string>(m.components.Select(c => c.id));
            if (ids.Count != m.components.Length || !m.components.Any(c => c.required)) throw new IOException("Invalid components");
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in m.entries)
            {
                Target(e.path);
                if (!paths.Add(e.path) || !ids.Contains(e.component) || !(e.kind == "add" || e.kind == "patch" || e.kind == "rows")) throw new IOException("Invalid manifest entry: " + e.path);
                if (e.kind == "add" && e.blob != "files/" + e.path) throw new IOException("Invalid blob path");
                if (e.kind == "patch" && (e.patches == null || e.patches.Length == 0 || e.patches.Any(p => string.IsNullOrEmpty(p.before)))) throw new IOException("Invalid patch: " + e.path);
                if (e.kind == "rows" && (e.rows == null || e.rows.Any(r => r.index < 0 || r.index > 1024 || r.value.Contains('\n')))) throw new IOException("Invalid translation rows");
            }
            return m;
        }
        private byte[] Original(OwnedFile f)
        {
            if (!f.originalExists) return null;
            var bytes = File.ReadAllBytes(Backup(f.backup));
            if (Hash(bytes) != f.originalSha256) throw new IOException("Damaged original backup: " + f.path);
            return bytes;
        }
        private byte[] Payload(Manifest m, Entry e)
        {
            var p = Path.Combine(m.directory, "payload.zip.bytes"); NoLinks(p);
            using (var zip = ZipFile.OpenRead(p))
            {
                var blob = zip.GetEntry(e.blob);
                if (blob == null || blob.Length > 128L * 1024 * 1024) throw new IOException("Missing/oversized payload: " + e.path);
                using (var stream = blob.Open()) using (var ms = new MemoryStream())
                { stream.CopyTo(ms); var b = ms.ToArray(); if (Hash(b) != e.sha256) throw new IOException("Payload hash mismatch: " + e.path); return b; }
            }
        }
        private sealed class Edit { public int start, end; public string value; }
        private static byte[] EncodeLike(string s, byte[] original)
        {
            if (Utf8.GetString(original).Contains("\r\n")) s = s.Replace("\n", "\r\n");
            var b = Utf8.GetBytes(s);
            return original.Length >= 3 && original[0] == 239 && original[1] == 187 && original[2] == 191 ? new byte[] {239,187,191}.Concat(b).ToArray() : b;
        }
        // Resolve every patch against the immutable baseline, then combine edits. Never reverse
        // an installed file by guessing: uninstall simply recomposes all remaining mods.
        private byte[] Compose(byte[] original, List<Tuple<Manifest, Entry>> entries)
        {
            if (entries.All(x => x.Item2.kind == "add"))
            {
                var bytes = Payload(entries[0].Item1, entries[0].Item2);
                foreach (var x in entries.Skip(1)) if (!Same(bytes, Payload(x.Item1, x.Item2))) throw new IOException("Mods provide different added files");
                if (original != null && !Same(original, bytes)) throw new IOException("A different file already exists");
                return bytes;
            }
            if (original == null || entries.Any(x => x.Item2.kind == "add")) throw new IOException("Missing baseline or mixed replacement/patch ownership");
            var source = Normal(original); var hash = Hash(Utf8.GetBytes(source));
            if (entries.Any(x => x.Item2.baseNormalizedSha256 != hash)) throw new IOException("Source differs from supported clean CE12.0.2");
            if (entries.All(x => x.Item2.kind == "rows"))
            {
                var lines = source.TrimEnd('\n').Split('\n').ToList(); var values = new Dictionary<int,string>();
                foreach (var x in entries) foreach (var row in x.Item2.rows)
                {
                    if (values.TryGetValue(row.index, out var value) && value != row.value) throw new IOException("Conflicting translation row " + row.index);
                    values[row.index] = row.value;
                }
                foreach (var row in values) { while (lines.Count <= row.Key) lines.Add(""); lines[row.Key] = row.Value; }
                return EncodeLike(string.Join("\n", lines) + "\n", original);
            }
            if (entries.Any(x => x.Item2.kind != "patch")) throw new IOException("Mixed patch formats");
            var edits = new List<Edit>();
            foreach (var x in entries) foreach (var p in x.Item2.patches)
            {
                int at = source.IndexOf(p.before, StringComparison.Ordinal);
                if (at < 0 || source.IndexOf(p.before, at + p.before.Length, StringComparison.Ordinal) >= 0) throw new IOException("Patch anchor missing/ambiguous");
                int prefix = 0, suffix = 0;
                while (prefix < p.before.Length && prefix < p.after.Length && p.before[prefix] == p.after[prefix]) prefix++;
                while (suffix < p.before.Length-prefix && suffix < p.after.Length-prefix && p.before[p.before.Length-1-suffix] == p.after[p.after.Length-1-suffix]) suffix++;
                var edit = new Edit { start = at+prefix, end = at+p.before.Length-suffix, value = p.after.Substring(prefix,p.after.Length-prefix-suffix) };
                if (edits.Any(e => e.start == edit.start && e.end == edit.end && e.value == edit.value)) continue;
                foreach (var e in edits)
                    if (Math.Max(e.start,edit.start) < Math.Min(e.end,edit.end) || e.start < edit.start && edit.start < e.end || edit.start < e.start && e.start < edit.end) throw new IOException("Overlapping source patches");
                edits.Add(edit);
            }
            var result = new StringBuilder(); int cursor = 0;
            foreach (var group in edits.OrderBy(e => e.start).ThenBy(e => e.end).GroupBy(e => e.start))
            {
                if (group.Key < cursor) throw new IOException("Conflicting source patch boundaries");
                result.Append(source, cursor, group.Key-cursor);
                foreach (var e in group) result.Append(e.value);
                cursor = group.Max(e => e.end);
            }
            result.Append(source, cursor, source.Length-cursor);
            return EncodeLike(result.ToString(), original);
        }
        public Plan CreatePlan(IEnumerable<Manifest> available, IEnumerable<Selection> desired)
        {
            var plan = new Plan();
            try
            {
                if (File.Exists(Internal(JournalPath))) throw new IOException("Interrupted transaction: recover before changing mods");
                if (File.Exists(Internal("ProjectSettings/Scp3114ModManagerState.json"))) throw new IOException("Legacy SCP-3114 1.1 installed. Uninstall using its original manager first; this operation has made no changes.");
                plan.previous = ReadState(); var old = plan.previous.files.ToDictionary(f => f.path, StringComparer.OrdinalIgnoreCase);
                var stateBytes = Read(Internal(StatePath)); plan.stateHash = stateBytes == null ? null : Hash(stateBytes);
                var manifests = available.ToDictionary(m => m.id);
                plan.selection = desired.OrderBy(s => s.id, StringComparer.Ordinal).ToArray();
                if (plan.selection.Select(s => s.id).Distinct().Count() != plan.selection.Length) throw new IOException("Duplicate selection");
                var selected = new Dictionary<string,List<Tuple<Manifest,Entry>>>(StringComparer.OrdinalIgnoreCase);
                foreach (var s in plan.selection)
                {
                    if (!manifests.TryGetValue(s.id, out var m)) throw new IOException("Reimport missing installer for " + s.id + " before changing installed mods");
                    var enabled = new HashSet<string>(s.components); s.version = m.version;
                    if (m.components.Any(c => c.required && !enabled.Contains(c.id)) || enabled.Any(c => !m.components.Any(x => x.id == c))) throw new IOException("Invalid component selection for " + m.id);
                    foreach (var c in m.components.Where(c => enabled.Contains(c.id)))
                        if (c.requires.Any(r => !enabled.Contains(r))) throw new IOException(c.name + " requires " + string.Join(", ",c.requires));
                    foreach (var r in m.requirements.Where(r => enabled.Contains(r.component)))
                    {
                        if (!File.Exists(Target(r.path))) throw new IOException("Missing vanilla asset: " + r.path);
                        if (!string.IsNullOrEmpty(r.guid) && (!File.Exists(Target(r.path+".meta")) || !File.ReadAllText(Target(r.path+".meta")).Contains("guid: "+r.guid))) throw new IOException("Vanilla GUID differs: " + r.path);
                    }
                    foreach (var e in m.entries.Where(e => enabled.Contains(e.component)))
                    { if (!selected.TryGetValue(e.path,out var list)) selected.Add(e.path,list=new List<Tuple<Manifest,Entry>>()); list.Add(Tuple.Create(m,e)); }
                }
                foreach (var rel in old.Keys.Concat(selected.Keys).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.Ordinal))
                {
                    try
                    {
                        var p = Target(rel); var current = Read(p); old.TryGetValue(rel,out var record);
                        if (record != null && (current == null || Hash(current) != record.installedSha256)) throw new IOException("Edited after install. Preserve your changes before changing components");
                        var original = record == null ? current : Original(record);
                        bool own = selected.TryGetValue(rel,out var entries); var after = own ? Compose(original,entries) : original;
                        if (own && rel.EndsWith(".meta") && original == null && GuidOwner != null)
                        {
                            var guid = Normal(after).Split('\n').FirstOrDefault(l => l.StartsWith("guid: "))?.Substring(6).Trim();
                            var existing = string.IsNullOrEmpty(guid) ? null : GuidOwner(guid);
                            if (!string.IsNullOrEmpty(existing) && !existing.Equals(rel.Substring(0,rel.Length-5),StringComparison.OrdinalIgnoreCase)) throw new IOException("GUID already used by " + existing);
                        }
                        if (!Same(current,after) && current != null && (File.GetAttributes(p) & FileAttributes.ReadOnly) != 0) throw new IOException("Read-only target");
                        plan.changes.Add(new Change { path=rel,before=current,after=after,original=original,old=record,desired=own });
                    }
                    catch (Exception e) { plan.conflicts.Add(rel + ": " + e.Message); }
                }
            }
            catch (Exception e) { plan.conflicts.Add(e.Message); }
            return plan;
        }
        private List<string> MissingParents(IEnumerable<Change> changes)
        {
            var result = new HashSet<string>();
            foreach (var c in changes.Where(c => c.after != null))
                for (var p = Path.GetDirectoryName(Target(c.path)); !Directory.Exists(p); p = Path.GetDirectoryName(p))
                { NoLinks(p); result.Add(p.Substring(Root.Length+1).Replace('\\','/')); }
            return result.OrderBy(s => s.Length).ToList();
        }
        private void DeleteEmpty(IEnumerable<string> directories)
        {
            foreach (var d in directories.OrderByDescending(s => s.Length)) { var p=Target(d); if (Directory.Exists(p) && !Directory.EnumerateFileSystemEntries(p).Any()) Directory.Delete(p); }
        }
        public void Apply(Plan plan)
        {
            if (!plan.CanApply) throw new IOException(string.Join("\n",plan.conflicts));
            if (File.Exists(Internal(JournalPath))) throw new IOException("Interrupted transaction");
            var oldState=Read(Internal(StatePath));
            if ((oldState==null ? null : Hash(oldState)) != plan.stateHash) throw new IOException("State changed after preview; refresh");
            foreach (var c in plan.changes) if (!Same(Read(Target(c.path)),c.before)) throw new IOException("File changed after preview: "+c.path);
            var tx=DateTime.UtcNow.ToString("yyyyMMddTHHmmss")+"-"+Guid.NewGuid().ToString("N");
            var journal=new Journal { root=Root,transaction=tx,hadState=oldState!=null,oldStateBase64=oldState==null?"":Convert.ToBase64String(oldState),directories=MissingParents(plan.changes).ToArray() };
            var rollback=new List<Rollback>(); var owned=new List<OwnedFile>(); int i=0;
            foreach (var c in plan.changes)
            {
                if (c.desired)
                {
                    var f=new OwnedFile { path=c.path,installedSha256=Hash(c.after),originalExists=c.original!=null,originalSha256=c.original==null?null:Hash(c.original),backup=c.old?.backup };
                    if (f.originalExists && string.IsNullOrEmpty(f.backup))
                    { f.backup=tx+"/original/"+i.ToString("D5")+".bin";Atomic(Backup(f.backup),c.original); }
                    owned.Add(f);
                }
                if (!Same(c.before,c.after))
                {
                    var r=new Rollback { path=c.path,existed=c.before!=null,sha256=c.before==null?null:Hash(c.before),backup=tx+"/rollback/"+i.ToString("D5")+".bin" };
                    if (r.existed) Atomic(Backup(r.backup),c.before); rollback.Add(r);
                }
                i++;
            }
            journal.files=rollback.ToArray(); Save(Internal(JournalPath),journal);
            try
            {
                foreach (var c in plan.changes.Where(c => !Same(c.before,c.after)))
                {
                    BeforeWrite?.Invoke(c.path);
                    if (c.after==null) { if (File.Exists(Target(c.path))) File.Delete(Target(c.path)); }
                    else Atomic(Target(c.path),c.after);
                }
                var next=new State { root=Root,mods=plan.selection,files=owned.ToArray(),directories=plan.previous.directories.Concat(journal.directories).Distinct().ToArray() };
                if (next.mods.Length>0) Save(Internal(StatePath),next);
                else if (File.Exists(Internal(StatePath))) File.Delete(Internal(StatePath));
                DeleteEmpty(next.directories);
                Save(Backup(tx+"/completed.json"),next);
                File.Delete(Internal(JournalPath));
            }
            catch { Recover(); throw; }
        }
        public void Recover()
        {
            var p=Internal(JournalPath); if (!File.Exists(p)) return;
            var j=(Journal)Decode(File.ReadAllText(p,Utf8),typeof(Journal));
            if (j==null || !Root.Equals(j.root,StringComparison.OrdinalIgnoreCase)) throw new IOException("Invalid recovery journal");
            foreach (var f in j.files) { Target(f.path); if (f.existed && Hash(File.ReadAllBytes(Backup(f.backup)))!=f.sha256) throw new IOException("Damaged rollback: "+f.path); }
            foreach (var d in j.directories) Target(d);
            foreach (var f in j.files) { if (f.existed) Atomic(Target(f.path),File.ReadAllBytes(Backup(f.backup))); else if (File.Exists(Target(f.path))) File.Delete(Target(f.path)); }
            if (!j.hadState) { if (File.Exists(Internal(StatePath))) File.Delete(Internal(StatePath)); }
            else Atomic(Internal(StatePath),Convert.FromBase64String(j.oldStateBase64));
            DeleteEmpty(j.directories);File.Delete(p);
        }
    }
}
