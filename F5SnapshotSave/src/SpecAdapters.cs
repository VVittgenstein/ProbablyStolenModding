using System;
using System.Collections.Generic;
using System.Reflection;

namespace F5SnapshotSave
{
    // Compatibility spec v1 (design/t029-plan.md section 2, design/t029-spec-v1.md section 3). A mod that follows it
    // listens with ModHelper.OnMessage on "F5SnapshotSave:snapshot.v1.adapters" and, when this mod sends that message,
    // hands over an adapter: its own field table (made from its DLL at build time) and functions written in that mod
    // which read and write its objects, find its types and methods, create its objects, and optionally find objects
    // again by key and keep extra data for game objects. The functions run ModReflect with that mod's scope, so this mod
    // stores and rebuilds the mod's objects without listing it as a prerequisite, and the mod's updates need no release of
    // this mod. Only game and .NET types cross between the two mods.
    //
    // The adapter as this mod keeps it after the checks of SpecAdapters.Validate: the values are copied out of the
    // dictionary the mod handed over, so a later change of that dictionary has no effect.
    internal sealed class Adapter
    {
        internal readonly string Mod;
        internal readonly Assembly Assembly;
        internal readonly string AssemblyName;
        internal readonly string Mvid;
        internal readonly FieldTable Table;
        internal readonly Func<object, string, object> Get;
        internal readonly Action<object, string, object> Set;
        internal readonly Func<Type, string, object> GetStatic;
        internal readonly Func<string, Type> FindType;
        internal readonly Func<Type, string, Type[], MethodBase> Method;
        internal readonly Func<Type, object> Create;
        internal readonly Func<object, string> Key;
        internal readonly Func<Type, string, object> Find;
        internal readonly Func<object, object> SaveExtra;
        internal readonly Action<object, object> LoadExtra;

        internal Adapter(string mod, Assembly assembly, string assemblyName, string mvid, FieldTable table,
            Func<object, string, object> get, Action<object, string, object> set, Func<Type, string, object> getStatic,
            Func<string, Type> findType, Func<Type, string, Type[], MethodBase> method, Func<Type, object> create,
            Func<object, string> key, Func<Type, string, object> find, Func<object, object> saveExtra, Action<object, object> loadExtra)
        {
            Mod = mod;
            Assembly = assembly;
            AssemblyName = assemblyName;
            Mvid = mvid;
            Table = table;
            Get = get;
            Set = set;
            GetStatic = getStatic;
            FindType = findType;
            Method = method;
            Create = create;
            Key = key;
            Find = find;
            SaveExtra = saveExtra;
            LoadExtra = loadExtra;
        }
    }

    // The adapters accepted in one collection, by assembly. Only the objects of one operation hold a set (the
    // SnapshotContext of a save, of a snapshot read, or the step that gives extra data back); no static field holds a
    // set, an adapter or one of its functions (plan 2.2 point 5). This limits how long the functions exist; it is not a
    // protection, since a mod that lists this mod as a prerequisite can reach this mod's objects through ModReflect.
    internal sealed class AdapterSet
    {
        private readonly Dictionary<Assembly, Adapter> byAssembly;

        internal AdapterSet(Dictionary<Assembly, Adapter> adapters)
        {
            // A copy: the dictionary that collected them was reachable through the registration function while the
            // message was sent.
            byAssembly = adapters != null ? new Dictionary<Assembly, Adapter>(adapters) : new Dictionary<Assembly, Adapter>();
        }

        internal Adapter For(Assembly assembly)
        {
            Adapter adapter;
            return assembly != null && byAssembly.TryGetValue(assembly, out adapter) ? adapter : null;
        }

        internal ICollection<Adapter> All
        {
            get { return byAssembly.Values; }
        }

        internal int Count
        {
            get { return byAssembly.Count; }
        }
    }

    internal static class SpecAdapters
    {
        // ModHelper.SendMessage calls the handlers registered under "<sender>:<channel>", so the mods listen on
        // "F5SnapshotSave:snapshot.v1.adapters". An incompatible change of the spec gets a new channel; v1 stays.
        internal const string Channel = "snapshot.v1.adapters";
        // The spec version written with every extra data record (GraphWriter.WriteExtra).
        internal const string Version = "1";

        private static readonly Assembly OwnAssembly = typeof(SpecAdapters).Assembly;
        private static readonly Assembly GameAssembly = typeof(StoreClient).Assembly;

        // What has been written to the log in this game start: "ok|<mod>|<module>" and "no|<mod>|<reason>".
        private static readonly HashSet<string> logged = new HashSet<string>();

        // Parsed field tables of the adapters, by assembly name and module version id. Names and flags only; the text is
        // kept to notice a different table handed over for the same module.
        private sealed class TableEntry
        {
            internal string Text;
            internal FieldTable Table;
            internal string Problem;
        }
        private static readonly Dictionary<string, TableEntry> tables = new Dictionary<string, TableEntry>();

        // Sends the message with a registration function made for this one collection and returns the adapters accepted
        // while the message was sent. Called before every save (also for the read-back check), before a snapshot is read
        // when a save is loaded, and before extra data is given back.
        internal static AdapterSet Collect()
        {
            var accepted = new Dictionary<Assembly, Adapter>();
            var registration = new Registration(accepted);
            try
            {
                ModHelper.SendMessage(F5SnapshotSaveMod.ModId, Channel, new Action<object>(registration.Register));
            }
            catch (Exception e)
            {
                if (logged.Add("send|" + e.GetType().Name))
                    UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "asking the mods that follow the spec for their adapters failed: " + e);
            }
            finally
            {
                registration.Close();
            }
            return new AdapterSet(accepted);
        }

        // The registration function of one collection, handed to the listening mods as the message's payload. A mod that
        // follows the spec checks that it is a single function declared in this mod's assembly before calling it, because
        // a mod can send under this mod's ID during its Init or OnEnable (ModHelper.RequireCaller). It accepts adapters
        // only while ModHelper.SendMessage runs: Close drops the collection, so a later call does nothing and the
        // function's Target leads to no adapter.
        private sealed class Registration
        {
            private Dictionary<Assembly, Adapter> accepted;

            internal Registration(Dictionary<Assembly, Adapter> accepted)
            {
                this.accepted = accepted;
            }

            internal void Close()
            {
                accepted = null;
            }

            internal void Register(object value)
            {
                Dictionary<Assembly, Adapter> into = accepted;
                if (into == null)
                    return;
                string mod = null;
                try
                {
                    string reason;
                    Adapter adapter = Validate(value, out mod, out reason);
                    if (adapter == null)
                    {
                        LogRejected(mod, reason);
                        return;
                    }
                    // The first valid adapter of an assembly is used.
                    if (into.ContainsKey(adapter.Assembly))
                    {
                        LogRejected(adapter.Mod, "an adapter for " + adapter.AssemblyName + " was already accepted in this collection");
                        return;
                    }
                    into[adapter.Assembly] = adapter;
                    LogAccepted(adapter);
                }
                catch (Exception e)
                {
                    LogRejected(mod, "checking it failed: " + e.GetType().Name + ": " + e.Message);
                }
            }
        }

        // The checks of spec 3.3. Returns null with the reason when the adapter is not accepted.
        private static Adapter Validate(object value, out string mod, out string reason)
        {
            mod = null;
            reason = null;
            var d = value as Dictionary<string, object>;
            if (d == null || value.GetType() != typeof(Dictionary<string, object>))
            {
                reason = "it is not a Dictionary<string, object>";
                return null;
            }
            // Every value is read once; the checks below and the adapter use these copies.
            object spec = Read(d, "spec");
            object modValue = Read(d, "mod");
            object assemblyValue = Read(d, "assembly");
            object fields = Read(d, "fields");
            object get = Read(d, "get"), set = Read(d, "set"), getStatic = Read(d, "getStatic"), findType = Read(d, "findType");
            object method = Read(d, "method"), create = Read(d, "create"), key = Read(d, "key"), find = Read(d, "find");
            object saveExtra = Read(d, "saveExtra"), loadExtra = Read(d, "loadExtra");

            mod = modValue as string;
            if (!(spec is int) || (int)spec != 1)
            {
                reason = "spec is not the int 1";
                return null;
            }
            if (string.IsNullOrEmpty(mod))
            {
                reason = "mod is empty";
                return null;
            }
            // A loaded assembly, not an object of another subclass of Assembly that only reports a name or a location.
            var assembly = assemblyValue as Assembly;
            if (assembly == null || assembly.GetType() != OwnAssembly.GetType())
            {
                reason = "assembly is not a loaded assembly";
                return null;
            }
            if (assembly == OwnAssembly || assembly == GameAssembly)
            {
                reason = "assembly " + assembly.GetName().Name + " is not a mod's own assembly";
                return null;
            }
            string owner = ModLoader.ModIdOfAssembly(assembly);
            if (owner != mod)
            {
                reason = "assembly " + assembly.GetName().Name + " belongs to " + (owner == null ? "no mod" : "mod '" + owner + "'");
                return null;
            }

            var problems = new List<string>();
            var getFn = Function<Func<object, string, object>>(get, "get", assembly, true, problems);
            var setFn = Function<Action<object, string, object>>(set, "set", assembly, true, problems);
            var getStaticFn = Function<Func<Type, string, object>>(getStatic, "getStatic", assembly, true, problems);
            var findTypeFn = Function<Func<string, Type>>(findType, "findType", assembly, true, problems);
            var methodFn = Function<Func<Type, string, Type[], MethodBase>>(method, "method", assembly, true, problems);
            var createFn = Function<Func<Type, object>>(create, "create", assembly, true, problems);
            var keyFn = Function<Func<object, string>>(key, "key", assembly, false, problems);
            var findFn = Function<Func<Type, string, object>>(find, "find", assembly, false, problems);
            var saveExtraFn = Function<Func<object, object>>(saveExtra, "saveExtra", assembly, false, problems);
            var loadExtraFn = Function<Action<object, object>>(loadExtra, "loadExtra", assembly, false, problems);
            if ((key == null) != (find == null))
                problems.Add("key and find are not given together");
            if ((saveExtra == null) != (loadExtra == null))
                problems.Add("saveExtra and loadExtra are not given together");

            string name = assembly.GetName().Name;
            string mvid = assembly.ManifestModule.ModuleVersionId.ToString("N");
            string problem;
            FieldTable table = TableOf(name, mvid, fields as string, out problem);
            if (table == null)
                problems.Add(problem);
            if (problems.Count > 0)
            {
                reason = string.Join("; ", problems.ToArray());
                return null;
            }
            return new Adapter(mod, assembly, name, mvid, table, getFn, setFn, getStaticFn, findTypeFn, methodFn, createFn,
                keyFn, findFn, saveExtraFn, loadExtraFn);
        }

        private static object Read(Dictionary<string, object> d, string key)
        {
            object value;
            return d.TryGetValue(key, out value) ? value : null;
        }

        // A function of the adapter: exactly the delegate type of the spec, a single function (Delegate.Method is the
        // last entry of a multicast delegate), declared in the adapter's assembly, so that no other mod's code is used as
        // this mod's adapter.
        private static T Function<T>(object value, string key, Assembly assembly, bool required, List<string> problems) where T : class
        {
            if (value == null)
            {
                if (required)
                    problems.Add(key + " is missing");
                return null;
            }
            var d = value as Delegate;
            if (d == null || value.GetType() != typeof(T))
            {
                problems.Add(key + " is a " + ShortName(value.GetType()) + ", not a " + ShortName(typeof(T)));
                return null;
            }
            if (d.GetInvocationList().Length != 1)
            {
                problems.Add(key + " is a multicast delegate");
                return null;
            }
            MethodInfo method = d.Method;
            Type declaring = method != null ? method.DeclaringType : null;
            if (declaring == null || declaring.Assembly != assembly)
            {
                problems.Add(key + " is declared in " + (declaring == null ? "no type" : declaring.Assembly.GetName().Name));
                return null;
            }
            // The Invoke method of a delegate type declared in the adapter's assembly would pass the check above while
            // running whatever another mod put into that delegate (new TheirDelegate(otherCode).Invoke), so a function
            // must be a method of an ordinary type, and its target must not be a delegate.
            if (typeof(Delegate).IsAssignableFrom(declaring) || d.Target is Delegate)
            {
                problems.Add(key + " is the Invoke of the delegate type " + declaring.FullName);
                return null;
            }
            return value as T;
        }

        // Func<Object, String, Object> instead of the assembly-qualified generic name.
        private static string ShortName(Type type)
        {
            if (!type.IsGenericType)
                return type.Name;
            string name = type.Name;
            int tick = name.IndexOf('`');
            if (tick > 0)
                name = name.Substring(0, tick);
            Type[] args = type.GetGenericArguments();
            var parts = new string[args.Length];
            for (int i = 0; i < args.Length; i++)
                parts[i] = ShortName(args[i]);
            return name + "<" + string.Join(", ", parts) + ">";
        }

        // The field table of the adapter: Format 2 with exactly one module, the adapter's assembly, whose module version id
        // is the one of the loaded DLL; every type key of that assembly. A table for another build is not used.
        private static FieldTable TableOf(string name, string mvid, string text, out string problem)
        {
            problem = null;
            if (string.IsNullOrEmpty(text))
            {
                problem = "fields is missing";
                return null;
            }
            string cacheKey = name + "|" + mvid;
            TableEntry entry;
            if (tables.TryGetValue(cacheKey, out entry) && string.Equals(entry.Text, text, StringComparison.Ordinal))
            {
                problem = entry.Problem;
                return entry.Table;
            }
            entry = new TableEntry { Text = text };
            try
            {
                SnapshotSchema.Document parsed = SnapshotSchema.Parse(text);
                string recorded;
                if (parsed == null || parsed.Format != 2 || parsed.Modules == null || parsed.Fields == null)
                    entry.Problem = "fields is not a field table of format 2";
                else if (parsed.Modules.Count != 1 || !parsed.Modules.TryGetValue(name, out recorded))
                    entry.Problem = "the field table is not one of " + name + " alone";
                else if (recorded != mvid)
                    entry.Problem = "the field table belongs to module " + recorded + ", the loaded " + name + " is " + mvid + " (made for another build)";
                else
                {
                    string prefix = name + ":";
                    foreach (string type in parsed.Fields.Keys)
                    {
                        if (type == null || !type.StartsWith(prefix, StringComparison.Ordinal))
                        {
                            entry.Problem = "the field table has a type outside " + name + ": " + type;
                            break;
                        }
                    }
                    if (entry.Problem == null)
                        entry.Table = new FieldTable(parsed.Modules, parsed.Fields);
                }
            }
            catch (Exception e)
            {
                entry.Problem = "the field table cannot be read: " + e.GetType().Name + ": " + e.Message;
            }
            tables[cacheKey] = entry;
            problem = entry.Problem;
            return entry.Table;
        }

        private static void LogAccepted(Adapter adapter)
        {
            if (logged.Add("ok|" + adapter.Mod + "|" + adapter.Mvid))
                UnityEngine.Debug.Log(F5SnapshotSaveMod.LogPrefix + "spec adapter accepted: mod '" + adapter.Mod + "', assembly " + adapter.AssemblyName
                    + ", module " + adapter.Mvid + (adapter.Key != null ? ", keys" : "") + (adapter.SaveExtra != null ? ", extra data" : ""));
        }

        private static void LogRejected(string mod, string reason)
        {
            if (logged.Add("no|" + (mod ?? "") + "|" + reason))
                UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "spec adapter not accepted (mod '" + (mod ?? "?") + "'): " + reason);
        }
    }
}
