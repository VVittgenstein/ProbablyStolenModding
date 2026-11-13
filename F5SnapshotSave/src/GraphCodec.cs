using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;

namespace F5SnapshotSave
{
    internal sealed class ReferenceComparer : IEqualityComparer<object>
    {
        internal static readonly ReferenceComparer Instance = new ReferenceComparer();

        bool IEqualityComparer<object>.Equals(object x, object y)
        {
            return ReferenceEquals(x, y);
        }

        int IEqualityComparer<object>.GetHashCode(object obj)
        {
            return RuntimeHelpers.GetHashCode(obj);
        }
    }

    // Text form of an object graph. One record per line, fields separated by tabs; strings are escaped so that they
    // hold neither tabs nor line breaks.
    //   H  F5SnapshotSave <format>
    //   A  <id> <assembly name> <module version id>
    //   T  <id> n <assembly id> <full name>                            a type
    //   T  <id> g <assembly id> <definition name> <argument type ids>  a constructed generic type
    //   T  <id> a <element type id> <rank>                             an array type
    //   T  <id> b <element type id>                                    a by-ref parameter type
    //   F  <id> <declaring type id> <name>
    //   M  <id> <declaring type id> <name> <parameter type ids> <generic argument type ids>
    //   O  <id> <category> <type id> <payload...>
    //   E  <object id> <provider assembly id> <spec version> <value>   extra data of a mod (format 3)
    //   R  <name> <values...>
    // Values: n null, b0/b1, c<char code>, i<type id>:<integer>, u<unsigned 64-bit>, f<float bits>, d<double bits>,
    // m<decimal>, s<string>, e<type id>:<enum value>, p<type id> (zero pointer), y<type id> (a Type), r<object id>.
    // Object categories: c class fields, s struct fields, P fields set on an existing object, a array, l List,
    // h HashSet, q Queue, k Stack, d Dictionary, v delegate, x existing object, g item by unique ID,
    // G item stored with the game's item encoding, u asset by name, w StringBuilder, o plain object,
    // I StoreClientInstance, Y an object of a mod that follows the spec, found again by the key that mod gave (format 3,
    // spec 3.5).
    // Existing objects are written as a path: S<type id>:<static field id> followed by .<instance field id> steps.
    // Up to 0.7.1 snapshots also had the category K (the current session of CorpPresence) and the path steps
    // .a<array index>, .i<list index>, .k<dictionary key> (paths into the state of CorpPresence), with the root
    // corpPortraits; 0.8.0 no longer supports CorpPresence (T-029, yzz 2026-11-12), and a snapshot with any of them is
    // not read (the whole snapshot falls back).
    internal static class Codec
    {
        internal const string Magic = "F5SnapshotSave";
        // 0.8.0: format 3 adds the root queueSlots (Snapshot), key references (Y) and extra data (E). Formats 1 and 2 are
        // still read; 0.7.1 and older read only 1 and 2, so they use the F5ForceSave-format queue for a format 3 save.
        internal const int Format = 3;
        internal const BindingFlags DeclaredInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        internal const BindingFlags DeclaredStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        internal const BindingFlags DeclaredMethods = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        internal static bool Accepts(string format)
        {
            return format == "1" || format == "2" || format == "3";
        }

        internal static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "";
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        internal static string Unescape(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('\\') < 0)
                return s ?? "";
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != '\\' || i + 1 >= s.Length)
                {
                    sb.Append(c);
                    continue;
                }
                char n = s[++i];
                switch (n)
                {
                    case 't': sb.Append('\t'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    default: sb.Append(n); break;
                }
            }
            return sb.ToString();
        }

        internal static bool IsFrameworkAssembly(Assembly a)
        {
            string n = a.GetName().Name;
            return n == "mscorlib" || n == "netstandard" || n == "System" || n.StartsWith("System.", StringComparison.Ordinal)
                || n.StartsWith("UnityEngine", StringComparison.Ordinal) || n.StartsWith("Unity.", StringComparison.Ordinal)
                || n.StartsWith("Mono.", StringComparison.Ordinal) || n == "0Harmony";
        }

        // 'l' List, 'h' HashSet, 'q' Queue, 'k' Stack, 'd' Dictionary, '\0' otherwise.
        internal static char CollectionKind(Type t)
        {
            if (!t.IsGenericType)
                return '\0';
            Type d = t.GetGenericTypeDefinition();
            if (d == typeof(List<>)) return 'l';
            if (d == typeof(HashSet<>)) return 'h';
            if (d == typeof(Queue<>)) return 'q';
            if (d == typeof(Stack<>)) return 'k';
            if (d == typeof(Dictionary<,>)) return 'd';
            return '\0';
        }

        internal static string Int(long v)
        {
            return v.ToString(CultureInfo.InvariantCulture);
        }

        internal static int ParseInt(string s)
        {
            return int.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        internal static long ParseLong(string s)
        {
            return long.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        // Comparer of a Dictionary or HashSet: "D" the default comparer of the key type, a StringComparer name, or null
        // when the comparer is something else.
        internal static string ComparerKind(object collection, Type keyType)
        {
            ICollectionAccess access = CollectionAccess.For(collection.GetType());
            object comparer = access.Comparer(collection);
            if (comparer == null)
                return "D";
            if (ReferenceEquals(comparer, StringComparer.Ordinal)) return "O";
            if (ReferenceEquals(comparer, StringComparer.OrdinalIgnoreCase)) return "I";
            if (ReferenceEquals(comparer, StringComparer.InvariantCulture)) return "N";
            if (ReferenceEquals(comparer, StringComparer.InvariantCultureIgnoreCase)) return "n";
            object def = access.DefaultComparer;
            if (ReferenceEquals(comparer, def) || comparer.GetType() == def.GetType())
                return "D";
            string name = comparer.GetType().Name;
            if (name.IndexOf("NonRandomized", StringComparison.Ordinal) >= 0 || name.IndexOf("EqualityComparer", StringComparison.Ordinal) >= 0
                && comparer.GetType().Assembly == typeof(object).Assembly)
                return "D";
            return null;
        }

        internal static object Comparer(string kind)
        {
            switch (kind)
            {
                case "O": return StringComparer.Ordinal;
                case "I": return StringComparer.OrdinalIgnoreCase;
                case "N": return StringComparer.InvariantCulture;
                case "n": return StringComparer.InvariantCultureIgnoreCase;
                default: return null;
            }
        }
    }

    // 0.8.0: an object the writer could not store, with the path where it was first reached (for example
    // "queue[2].mainDialogue.endAction"), the type to blame and that type's mod (null for the game and the framework).
    // Snapshot.CaptureOutcome assigns each failure to a customer by the start of its path.
    internal sealed class WriteFailure
    {
        internal string Path;
        internal Type Type;
        internal string Mod;
        internal string Message;
    }

    // 0.8.0: extra data of a mod that follows the spec, rebuilt with a snapshot (spec 3.6): the game object it belongs to,
    // the value, and the assembly of the mod that gives it back with its loadExtra function.
    internal sealed class ExtraValue
    {
        internal Assembly Provider;
        internal object Owner;
        internal object Value;
    }

    internal sealed class GraphWriter
    {
        internal readonly List<string> Errors = new List<string>();
        // The same failures with the type to blame (0.8.0).
        internal readonly List<WriteFailure> Failures = new List<WriteFailure>();
        internal readonly Dictionary<string, int> TypeCounts = new Dictionary<string, int>();
        private readonly SnapshotContext ctx;

        private readonly Dictionary<Assembly, int> asmIds = new Dictionary<Assembly, int>();
        private readonly Dictionary<Type, int> typeIds = new Dictionary<Type, int>();
        private readonly Dictionary<SnapshotField, int> fieldIds = new Dictionary<SnapshotField, int>();
        private readonly Dictionary<MethodInfo, int> methodIds = new Dictionary<MethodInfo, int>();
        private readonly List<string> asmLines = new List<string>();
        private readonly List<string> typeLines = new List<string>();
        private readonly List<string> fieldLines = new List<string>();
        private readonly List<string> methodLines = new List<string>();
        private readonly List<string> objectLines = new List<string>();
        private readonly List<string> extraLines = new List<string>();
        private readonly List<string> rootLines = new List<string>();

        private readonly Dictionary<object, int> ids = new Dictionary<object, int>(ReferenceComparer.Instance);
        private readonly List<object> objects = new List<object>();
        private readonly List<string> paths = new List<string>();
        private readonly Queue<int> pending = new Queue<int>();
        private int nextId;
        // The assembly the reader takes for a name (GraphReader.ResolveTables): the first loaded assembly of that name.
        private Dictionary<string, Assembly> loadedByName;
        // Types the reader would not find, with the reason (CheckFound), so that each is looked up once per writing.
        private readonly Dictionary<Type, string> notFound = new Dictionary<Type, string>();

        internal GraphWriter(SnapshotContext ctx)
        {
            this.ctx = ctx;
        }

        internal int ObjectCount { get { return nextId; } }

        internal void AddRootList(string name, IEnumerable values)
        {
            var tokens = new List<string>();
            int i = 0;
            if (values != null)
            {
                foreach (object v in values)
                {
                    tokens.Add(SafeValue(v, name + "[" + i + "]"));
                    i++;
                }
            }
            Flush();
            rootLines.Add("R\t" + name + (tokens.Count > 0 ? "\t" + string.Join("\t", tokens.ToArray()) : ""));
        }

        internal void AddRoot(string name, object value)
        {
            AddRootList(name, new[] { value });
        }

        // Writes the given fields of an existing object (found again by path when reading) and a root naming it.
        internal void AddPartialRoot(string name, object target, IEnumerable<SnapshotField> fields)
        {
            int id;
            try
            {
                string path;
                if (target == null || !TryPath(target, out path))
                {
                    Fail(name, target == null ? null : target.GetType(), null, "no path to " + (target == null ? "null" : target.GetType().FullName));
                    return;
                }
                // 0.8.0: every type of the chain needs a field table, as for any object whose fields are copied.
                ctx.CopyFields(target.GetType());
                id = nextId++;
                objects.Add(target);
                paths.Add(name);
                var sb = new StringBuilder();
                sb.Append("O\t").Append(id).Append("\tP\t").Append(TypeId(target.GetType())).Append('\t').Append(path);
                foreach (SnapshotField f in fields)
                    sb.Append('\t').Append(FieldId(f)).Append('=').Append(SafeValue(f.GetValue(ctx, target), name + "." + f.Name));
                objectLines.Add(sb.ToString());
            }
            catch (Exception e)
            {
                Fail(name, target == null ? null : target.GetType(), e);
                return;
            }
            Flush();
            rootLines.Add("R\t" + name + "\tr" + id);
        }

        internal string Finish()
        {
            Flush();
            var sb = new StringBuilder();
            sb.Append("H\t").Append(Codec.Magic).Append('\t').Append(Codec.Format).Append('\n');
            foreach (List<string> block in new[] { asmLines, typeLines, fieldLines, methodLines, objectLines, extraLines, rootLines })
            {
                foreach (string line in block)
                    sb.Append(line).Append('\n');
            }
            return sb.ToString();
        }

        // 0.8.0: an object that cannot be written is recorded with its path, type, assembly and mod, and the writer goes
        // on with the next object. Up to 0.7.1 the first such object ended the whole save.
        private void Flush()
        {
            while (pending.Count > 0)
            {
                int id = pending.Dequeue();
                object o = objects[id];
                string path = paths[id];
                try
                {
                    WriteBody(id, o, path);
                }
                catch (Exception e)
                {
                    Fail(path, o.GetType(), e);
                }
            }
        }

        private void Fail(string path, Type culprit, Exception e)
        {
            var typed = e as SnapshotTypeException;
            if (typed != null)
                Fail(path, typed.Culprit ?? culprit, null, e.Message);
            else
                Fail(path, culprit, null, e.GetType().Name + ": " + e.Message);
        }

        private void Fail(string path, Type culprit, string mod, string message)
        {
            if (mod == null && culprit != null)
            {
                try
                {
                    mod = ModLoader.ModIdOfAssembly(culprit.Assembly);
                }
                catch (Exception)
                {
                    mod = null;
                }
            }
            Failures.Add(new WriteFailure { Path = path, Type = culprit, Mod = mod, Message = message });
            var sb = new StringBuilder();
            sb.Append(path).Append(": ").Append(message);
            if (culprit != null || mod != null)
            {
                sb.Append(" [");
                if (culprit != null)
                    sb.Append(culprit.FullName ?? culprit.Name).Append(", ").Append(culprit.Assembly.GetName().Name);
                if (mod != null)
                    sb.Append(culprit != null ? ", " : "").Append("mod ").Append(mod);
                sb.Append(']');
            }
            Errors.Add(sb.ToString());
        }

        private void Count(Type t)
        {
            string n = t.FullName ?? t.Name;
            int c;
            TypeCounts.TryGetValue(n, out c);
            TypeCounts[n] = c + 1;
        }

        // Value, with a failure recorded at this path (a type that would not be found when loading, for example the type
        // of an enum value) and null written in its place.
        private string SafeValue(object v, string path)
        {
            try
            {
                return Value(v, path);
            }
            catch (Exception e)
            {
                Fail(path, v == null ? null : v.GetType(), e);
                return "n";
            }
        }

        internal string Value(object v, string path)
        {
            if (v == null)
                return "n";
            Type t = v.GetType();
            if (t == typeof(string))
                return "s" + Codec.Escape((string)v);
            if (t == typeof(bool))
                return (bool)v ? "b1" : "b0";
            if (t.IsEnum)
            {
                Type u = Enum.GetUnderlyingType(t);
                long raw = u == typeof(ulong) ? unchecked((long)Convert.ToUInt64(v, CultureInfo.InvariantCulture)) : Convert.ToInt64(v, CultureInfo.InvariantCulture);
                return "e" + TypeId(t) + ":" + Codec.Int(raw);
            }
            if (t.IsPrimitive)
            {
                if (t == typeof(float))
                    return "f" + BitConverter.ToInt32(BitConverter.GetBytes((float)v), 0).ToString(CultureInfo.InvariantCulture);
                if (t == typeof(double))
                    return "d" + Codec.Int(BitConverter.DoubleToInt64Bits((double)v));
                if (t == typeof(char))
                    return "c" + ((int)(char)v).ToString(CultureInfo.InvariantCulture);
                if (t == typeof(ulong))
                    return "u" + ((ulong)v).ToString(CultureInfo.InvariantCulture);
                if (t == typeof(IntPtr) || t == typeof(UIntPtr))
                {
                    bool zero = t == typeof(IntPtr) ? (IntPtr)v == IntPtr.Zero : (UIntPtr)v == UIntPtr.Zero;
                    if (!zero)
                        Fail(path, t, null, "native pointer");
                    return "p" + TypeId(t);
                }
                return "i" + TypeId(t) + ":" + Codec.Int(Convert.ToInt64(v, CultureInfo.InvariantCulture));
            }
            if (t == typeof(decimal))
                return "m" + ((decimal)v).ToString(CultureInfo.InvariantCulture);
            if (v is Type)
                return "y" + TypeId((Type)v);
            if (t.IsValueType)
                return "r" + WriteStruct(v, path);

            int id;
            if (!ids.TryGetValue(v, out id))
            {
                id = nextId++;
                ids[v] = id;
                objects.Add(v);
                paths.Add(path);
                pending.Enqueue(id);
            }
            return "r" + id;
        }

        private int WriteStruct(object box, string path)
        {
            int id = nextId++;
            objects.Add(box);
            paths.Add(path);
            try
            {
                WriteStructBody(id, box, path);
            }
            catch (Exception e)
            {
                Fail(path, box.GetType(), e);
            }
            return id;
        }

        private void WriteStructBody(int id, object box, string path)
        {
            Type t = box.GetType();
            Count(t);
            // 0.8.0, spec 3.5: a value of a type of a mod that follows the spec is asked for its key like an object of that
            // mod (WriteObject); with a key only the key is written ('Y'), and find gives the value back. A value has no
            // identity, so every occurrence gets a record of its own. Up to the second code review on 2026-11-12 values
            // were always copied field by field.
            Adapter keyed = ctx.Adapters.For(t.Assembly);
            if (keyed != null && keyed.Key != null)
            {
                string key = keyed.Key(box);
                if (key != null)
                {
                    objectLines.Add("O\t" + id + "\tY\t" + TypeId(t) + "\t" + Codec.Escape(key));
                    return;
                }
            }
            var sb = new StringBuilder();
            sb.Append("O\t").Append(id).Append("\ts\t").Append(TypeId(t));
            AppendFields(sb, box, t, path);
            objectLines.Add(sb.ToString());
        }

        private void AppendFields(StringBuilder sb, object o, Type t, string path)
        {
            foreach (SnapshotField f in ctx.CopyFields(t))
            {
                if (ctx.SkipField(f))
                    continue;
                string at = path + "." + f.Name;
                object fv;
                try
                {
                    fv = f.GetValue(ctx, o);
                }
                catch (Exception e)
                {
                    Fail(at, t, e);
                    continue;
                }
                string token = SafeValue(fv, at);
                sb.Append('\t').Append(FieldId(f)).Append('=').Append(token);
            }
        }

        private void WriteBody(int id, object o, string path)
        {
            Type t = o.GetType();
            Count(t);
            var sb = new StringBuilder();
            sb.Append("O\t").Append(id).Append('\t');

            var del = o as Delegate;
            if (del != null)
            {
                sb.Append("v\t").Append(TypeId(t));
                foreach (Delegate d in del.GetInvocationList())
                {
                    MethodInfo m = d.Method;
                    if (m == null || m.DeclaringType == null || m.GetType().Name.IndexOf("Dynamic", StringComparison.Ordinal) >= 0)
                    {
                        Fail(path, t, null, "delegate to a generated method");
                        continue;
                    }
                    int method;
                    try
                    {
                        method = MethodId(m);
                    }
                    catch (Exception e)
                    {
                        Fail(path, m.DeclaringType, e);
                        continue;
                    }
                    sb.Append('\t').Append(method).Append('@').Append(SafeValue(d.Target, path + ".Target"));
                }
                objectLines.Add(sb.ToString());
                return;
            }

            // 0.8.0: an object of a mod that follows the spec is found again by the key that mod gives it, when it gives
            // one (for example a catalog definition or a ledger record that has to stay the same object), instead of being
            // copied (spec 3.5).
            Adapter keyed = ctx.Adapters.For(t.Assembly);
            if (keyed != null && keyed.Key != null)
            {
                string key = keyed.Key(o);
                if (key != null)
                {
                    sb.Append("Y\t").Append(TypeId(t)).Append('\t').Append(Codec.Escape(key));
                    objectLines.Add(sb.ToString());
                    return;
                }
            }

            var item = o as GameItem;
            if (item != null)
            {
                string json;
                int uid;
                if (ctx.TryItemReference(item, out uid))
                    sb.Append("g\t").Append(TypeId(t)).Append('\t').Append(uid.ToString(CultureInfo.InvariantCulture));
                else if (ctx.TryEncodeItem(item, out json))
                {
                    sb.Append("G\t").Append(TypeId(t)).Append('\t').Append(Codec.Escape(json));
                    objectLines.Add(sb.ToString());
                    WriteExtra(id, o, path);
                    return;
                }
                else
                {
                    Fail(path, t, null, "item '" + item.identifier + "' could not be stored");
                    return;
                }
                objectLines.Add(sb.ToString());
                return;
            }

            var instance = o as StoreClientInstance;
            if (instance != null)
            {
                sb.Append("I\t").Append(TypeId(t)).Append('\t').Append(SafeValue(instance.GetClientBlueprint(), path + ".storeClient"));
                objectLines.Add(sb.ToString());
                WriteExtra(id, o, path);
                return;
            }

            var text = o as StringBuilder;
            if (text != null)
            {
                sb.Append("w\t").Append(TypeId(t)).Append('\t').Append(Codec.Escape(text.ToString()));
                objectLines.Add(sb.ToString());
                return;
            }

            if (t == typeof(object))
            {
                sb.Append("o\t").Append(TypeId(t));
                objectLines.Add(sb.ToString());
                return;
            }

            string existing;
            if (TryPath(o, out existing))
            {
                sb.Append("x\t").Append(TypeId(t)).Append('\t').Append(existing);
                objectLines.Add(sb.ToString());
                return;
            }

            var unityObject = o as UnityEngine.Object;
            if (unityObject != null)
            {
                if (ctx.IsAsset(unityObject))
                {
                    sb.Append("u\t").Append(TypeId(t)).Append('\t').Append(Codec.Escape(unityObject.name));
                    objectLines.Add(sb.ToString());
                }
                else
                    Fail(path, t, null, "scene object " + t.FullName);
                return;
            }

            if (t.IsArray)
            {
                var array = (Array)o;
                if (array.Rank != 1)
                {
                    Fail(path, t, null, "array of rank " + array.Rank);
                    return;
                }
                sb.Append("a\t").Append(TypeId(t)).Append('\t').Append(array.Length);
                for (int i = 0; i < array.Length; i++)
                    sb.Append('\t').Append(SafeValue(array.GetValue(i), path + "[" + i + "]"));
                objectLines.Add(sb.ToString());
                return;
            }

            char kind = Codec.CollectionKind(t);
            if (kind != '\0')
            {
                sb.Append(kind).Append('\t').Append(TypeId(t));
                if (kind == 'h' || kind == 'd')
                {
                    string comparer = Codec.ComparerKind(o, t.GetGenericArguments()[0]);
                    if (comparer == null)
                    {
                        object custom = CollectionAccess.For(t).Comparer(o);
                        Fail(path, custom != null ? custom.GetType() : t, null, "collection with a custom comparer");
                        return;
                    }
                    sb.Append('\t').Append(comparer);
                }
                int i = 0;
                if (kind == 'd')
                {
                    foreach (DictionaryEntry e in (IDictionary)o)
                    {
                        sb.Append('\t').Append(SafeValue(e.Key, path + "{key" + i + "}"));
                        sb.Append('\t').Append(SafeValue(e.Value, path + "{" + i + "}"));
                        i++;
                    }
                }
                else
                {
                    foreach (object e in (IEnumerable)o)
                    {
                        sb.Append('\t').Append(SafeValue(e, path + "[" + i + "]"));
                        i++;
                    }
                }
                objectLines.Add(sb.ToString());
                return;
            }

            if (Codec.IsFrameworkAssembly(t.Assembly))
            {
                Fail(path, t, null, "unsupported type " + t.FullName);
                return;
            }

            sb.Append("c\t").Append(TypeId(t));
            AppendFields(sb, o, t, path);
            objectLines.Add(sb.ToString());
            WriteExtra(id, o, path);
        }

        // 0.8.0, spec 3.6: the extra data that the mods following the spec keep outside the customer objects for a game
        // object that a snapshot creates anew when it is read: a 'c' object of a game type, a StoreClientInstance, an item
        // stored with the game's item encoding. Each adapter with saveExtra is asked; a value is written as a graph value
        // (shared references and cycles kept) with the provider's assembly, which gets an 'A' record with its module
        // version id even when no type of the provider is in the graph, so a changed or missing provider makes the whole
        // snapshot fall back when it is read, as any changed assembly of a snapshot does.
        private void WriteExtra(int id, object o, string path)
        {
            if (o.GetType().Assembly != typeof(StoreClient).Assembly)
                return;
            foreach (Adapter adapter in ctx.Adapters.All)
            {
                if (adapter.SaveExtra == null)
                    continue;
                string at = path + "{extra " + adapter.Mod + "}";
                object extra;
                try
                {
                    extra = adapter.SaveExtra(o);
                }
                catch (Exception e)
                {
                    Fail(at, null, adapter.Mod, "saveExtra threw " + e.GetType().Name + ": " + e.Message);
                    continue;
                }
                if (extra == null)
                    continue;
                string token = SafeValue(extra, at);
                extraLines.Add("E\t" + id + "\t" + AsmId(adapter.Assembly) + "\t" + SpecAdapters.Version + "\t" + token);
            }
        }

        private bool TryPath(object o, out string path)
        {
            SnapshotField holder;
            List<SnapshotField> chain;
            if (!ctx.TryFindPath(o, out holder, out chain))
            {
                path = null;
                return false;
            }
            var sb = new StringBuilder();
            sb.Append('S').Append(TypeId(holder.DeclaringType)).Append(':').Append(FieldId(holder));
            foreach (SnapshotField f in chain)
                sb.Append('.').Append(FieldId(f));
            path = sb.ToString();
            return true;
        }

        private int AsmId(Assembly a)
        {
            int id;
            if (asmIds.TryGetValue(a, out id))
                return id;
            id = asmIds.Count;
            asmIds[a] = id;
            asmLines.Add("A\t" + id + "\t" + Codec.Escape(a.GetName().Name) + "\t" + a.ManifestModule.ModuleVersionId.ToString("N"));
            return id;
        }

        internal int TypeId(Type t)
        {
            int id;
            if (typeIds.TryGetValue(t, out id))
                return id;
            string line;
            if (t.IsByRef)
                line = "b\t" + TypeId(t.GetElementType());
            else if (t.IsArray)
                line = "a\t" + TypeId(t.GetElementType()) + "\t" + t.GetArrayRank();
            else if (t.IsGenericType && !t.IsGenericTypeDefinition)
            {
                Type def = t.GetGenericTypeDefinition();
                CheckFound(def);
                Type[] args = t.GetGenericArguments();
                var argIds = new string[args.Length];
                for (int i = 0; i < args.Length; i++)
                    argIds[i] = TypeId(args[i]).ToString(CultureInfo.InvariantCulture);
                line = "g\t" + AsmId(def.Assembly) + "\t" + Codec.Escape(def.FullName) + "\t" + string.Join(",", argIds);
            }
            else
            {
                CheckFound(t);
                line = "n\t" + AsmId(t.Assembly) + "\t" + Codec.Escape(t.FullName);
            }
            id = typeIds.Count;
            typeIds[t] = id;
            typeLines.Add("T\t" + id + "\t" + line);
            return id;
        }

        // 0.8.0: the lookup GraphReader runs for an 'n' or 'g' type record (the first loaded assembly of the recorded
        // name, then SnapshotSchema.ResolveType), run when the type is recorded. A type the reader would not find, for
        // example a type of a mod that is neither the game, this mod, one of its prerequisites nor a mod following the
        // spec, makes the object holding it not storable now; up to 0.7.1 such a type (in a delegate, an enum value or a
        // Type value) passed the writer and failed the read-back check.
        private void CheckFound(Type t)
        {
            string message;
            if (notFound.TryGetValue(t, out message))
                throw new SnapshotTypeException(t, message);
            string name = t.FullName;
            string problem = null;
            Type found = null;
            try
            {
                string assembly = t.Assembly.GetName().Name;
                if (LoadedByName(assembly) != t.Assembly)
                    problem = "another loaded assembly has the name " + assembly;
                else
                    found = SnapshotSchema.ResolveType(t.Assembly, name, ctx.Adapters);
            }
            catch (Exception e)
            {
                problem = e.Message;
            }
            if (problem == null && found != t)
                problem = "it is found as " + (found == null ? "nothing" : found.AssemblyQualifiedName);
            if (problem == null)
                return;
            message = "type " + (name ?? t.Name) + " cannot be found when loading: " + problem;
            notFound[t] = message;
            throw new SnapshotTypeException(t, message);
        }

        private Assembly LoadedByName(string name)
        {
            if (loadedByName == null)
            {
                loadedByName = new Dictionary<string, Assembly>();
                foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string n = a.GetName().Name;
                    if (!loadedByName.ContainsKey(n))
                        loadedByName[n] = a;
                }
            }
            Assembly found;
            return loadedByName.TryGetValue(name, out found) ? found : null;
        }

        internal int FieldId(SnapshotField f)
        {
            int id;
            if (fieldIds.TryGetValue(f, out id))
                return id;
            int declaring = TypeId(f.DeclaringType);
            id = fieldIds.Count;
            fieldIds[f] = id;
            fieldLines.Add("F\t" + id + "\t" + declaring + "\t" + Codec.Escape(f.Name));
            return id;
        }

        private int MethodId(MethodInfo m)
        {
            int id;
            if (methodIds.TryGetValue(m, out id))
                return id;
            int declaring = TypeId(m.DeclaringType);
            ParameterInfo[] ps = m.GetParameters();
            var pTypes = new Type[ps.Length];
            var pIds = new string[ps.Length];
            for (int i = 0; i < ps.Length; i++)
            {
                pTypes[i] = ps[i].ParameterType;
                pIds[i] = TypeId(pTypes[i]).ToString(CultureInfo.InvariantCulture);
            }
            Type[] gargs = m.IsGenericMethod ? m.GetGenericArguments() : Type.EmptyTypes;
            var gIds = new string[gargs.Length];
            for (int i = 0; i < gargs.Length; i++)
                gIds[i] = TypeId(gargs[i]).ToString(CultureInfo.InvariantCulture);
            CheckFound(m, pTypes, gargs);
            id = methodIds.Count;
            methodIds[m] = id;
            methodLines.Add("M\t" + id + "\t" + declaring + "\t" + Codec.Escape(m.Name) + "\t" + string.Join(",", pIds) + "\t" + string.Join(",", gIds));
            return id;
        }

        // 0.8.0: the lookup GraphReader.ResolveMethod runs for an 'M' record, run when the method is recorded; the method
        // found must be declared in the same type, under the same name and with the same parameter types.
        private void CheckFound(MethodInfo m, Type[] parameters, Type[] genericArguments)
        {
            MethodInfo found;
            try
            {
                found = SnapshotSchema.ResolveMethod(m.DeclaringType, m.Name, parameters, genericArguments, ctx.Adapters);
            }
            catch (Exception e)
            {
                throw new SnapshotTypeException(m.DeclaringType, "method " + m.DeclaringType.FullName + "." + m.Name + " cannot be found when loading: " + e.Message);
            }
            if (found != m && (found == null || found.DeclaringType != m.DeclaringType || found.Name != m.Name))
                throw new SnapshotTypeException(m.DeclaringType, "method " + m.DeclaringType.FullName + "." + m.Name + " is found as another method when loading");
        }
    }

    internal sealed class GraphReader
    {
        internal readonly List<string> Errors = new List<string>();
        internal readonly List<string> Warnings = new List<string>();
        internal readonly List<string> ChangedAssemblies = new List<string>();
        // 0.8.0: the extra data of the snapshot, rebuilt with it and given back later (Snapshot.GiveExtraBack).
        internal readonly List<ExtraValue> Extras = new List<ExtraValue>();
        // 0.8.0: assemblies of the types that could not be read, so that a failed read-back check can name their mods.
        internal readonly HashSet<Assembly> FailedAssemblies = new HashSet<Assembly>();
        private readonly SnapshotContext ctx;
        private readonly bool dryRun;

        private readonly Dictionary<int, string[]> asmRecords = new Dictionary<int, string[]>();
        private readonly Dictionary<int, string[]> typeRecords = new Dictionary<int, string[]>();
        private readonly Dictionary<int, string[]> fieldRecords = new Dictionary<int, string[]>();
        private readonly Dictionary<int, string[]> methodRecords = new Dictionary<int, string[]>();
        private readonly SortedDictionary<int, string[]> objectRecords = new SortedDictionary<int, string[]>();
        private readonly List<string[]> extraRecords = new List<string[]>();
        private readonly Dictionary<string, string[]> rootRecords = new Dictionary<string, string[]>();

        private readonly Dictionary<int, Assembly> asms = new Dictionary<int, Assembly>();
        private readonly Dictionary<int, Type> types = new Dictionary<int, Type>();
        private readonly Dictionary<int, SnapshotField> fields = new Dictionary<int, SnapshotField>();
        private readonly Dictionary<int, MethodInfo> methods = new Dictionary<int, MethodInfo>();
        private readonly Dictionary<int, object> objs = new Dictionary<int, object>();
        private readonly HashSet<int> structsFilled = new HashSet<int>();
        private readonly HashSet<int> inProgress = new HashSet<int>();

        // The root in which F5 Quick Save Plus up to 0.7.1 stored the portraits of CorpPresence visitors. 0.8.0 no longer
        // supports CorpPresence (T-029, yzz 2026-11-12) and does not read a snapshot that has this root: its visitors
        // would come back without their portraits.
        private const string CorpPortraitsRoot = "corpPortraits";

        // dryRun: everything is resolved and built, but items stored with the game's encoding are not decoded
        // (the field gets null). Used to check a snapshot right after writing it.
        internal GraphReader(SnapshotContext ctx, bool dryRun)
        {
            this.ctx = ctx;
            this.dryRun = dryRun;
        }

        internal bool Read(string text)
        {
            try
            {
                Parse(text);
                if (Errors.Count > 0)
                    return false;
                if (rootRecords.ContainsKey(CorpPortraitsRoot))
                {
                    Errors.Add("the snapshot holds portraits of CorpPresence visitors (root " + CorpPortraitsRoot + ", written up to 0.7.1); CorpPresence is no longer supported");
                    return false;
                }
                ResolveTables();
                if (Errors.Count > 0)
                    return false;
                CreateObjects();
                if (Errors.Count > 0)
                    return false;
                BindReadonlyFields();
                if (Errors.Count > 0)
                    return false;
                foreach (KeyValuePair<int, string[]> r in objectRecords)
                {
                    if (r.Value[2] == "v")
                        GetDelegate(r.Key);
                }
                if (Errors.Count > 0)
                    return false;
                foreach (KeyValuePair<int, string[]> r in objectRecords)
                {
                    string cat = r.Value[2];
                    if (cat == "c")
                        FillFields(r.Key, r.Value, 4);
                    else if (cat == "P" && !dryRun)
                        FillFields(r.Key, r.Value, 5);
                    else if (cat == "s")
                        EnsureStruct(r.Key);
                    else if (cat == "I")
                        FillInstance(r.Key, r.Value);
                }
                if (Errors.Count > 0)
                    return false;
                foreach (KeyValuePair<int, string[]> r in objectRecords)
                {
                    string cat = r.Value[2];
                    if (cat == "a" || cat == "l" || cat == "h" || cat == "q" || cat == "k" || cat == "d")
                        FillCollection(r.Key, r.Value);
                }
                if (Errors.Count > 0)
                    return false;
                ReadExtras();
            }
            catch (Exception e)
            {
                Errors.Add("reading failed: " + e);
            }
            return Errors.Count == 0;
        }

        internal bool HasRoot(string name)
        {
            return rootRecords.ContainsKey(name);
        }

        private void Blame(Type t)
        {
            if (t != null)
                FailedAssemblies.Add(t.Assembly);
        }

        internal List<object> Root(string name)
        {
            var list = new List<object>();
            string[] rec;
            if (!rootRecords.TryGetValue(name, out rec))
                return list;
            for (int i = 2; i < rec.Length; i++)
                list.Add(Resolve(rec[i]));
            return list;
        }

        internal object RootValue(string name)
        {
            List<object> list = Root(name);
            return list.Count > 0 ? list[0] : null;
        }

        private void Parse(string text)
        {
            string[] lines = (text ?? "").Split('\n');
            if (lines.Length == 0 || !lines[0].StartsWith("H\t" + Codec.Magic + "\t", StringComparison.Ordinal))
            {
                Errors.Add("not a snapshot");
                return;
            }
            string[] header = lines[0].Split('\t');
            if (header.Length < 3 || !Codec.Accepts(header[2]))
            {
                Errors.Add("unknown snapshot format " + (header.Length > 2 ? header[2] : "?"));
                return;
            }
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Length == 0)
                    continue;
                string[] p = line.Split('\t');
                switch (p[0])
                {
                    case "A": asmRecords[Codec.ParseInt(p[1])] = p; break;
                    case "T": typeRecords[Codec.ParseInt(p[1])] = p; break;
                    case "F": fieldRecords[Codec.ParseInt(p[1])] = p; break;
                    case "M": methodRecords[Codec.ParseInt(p[1])] = p; break;
                    case "O": objectRecords[Codec.ParseInt(p[1])] = p; break;
                    case "E": extraRecords.Add(p); break;
                    case "R": rootRecords[p[1]] = p; break;
                    default: Errors.Add("unknown record " + p[0]); return;
                }
            }
        }

        private void ResolveTables()
        {
            var loaded = new Dictionary<string, Assembly>();
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                string n = a.GetName().Name;
                if (!loaded.ContainsKey(n))
                    loaded[n] = a;
            }
            foreach (KeyValuePair<int, string[]> r in asmRecords)
            {
                string name = Codec.Unescape(r.Value[2]);
                Assembly a;
                if (!loaded.TryGetValue(name, out a))
                {
                    Errors.Add("assembly '" + name + "' is not loaded");
                    continue;
                }
                if (!Codec.IsFrameworkAssembly(a) && a.ManifestModule.ModuleVersionId.ToString("N") != r.Value[3])
                    ChangedAssemblies.Add(name);
                asms[r.Key] = a;
            }
            if (ChangedAssemblies.Count > 0)
                Errors.Add("changed since the snapshot was made: " + string.Join(", ", ChangedAssemblies.ToArray()));
            if (Errors.Count > 0)
                return;
            foreach (int id in typeRecords.Keys)
                TypeOf(id);
            foreach (KeyValuePair<int, string[]> r in fieldRecords)
            {
                Type t = TypeOf(Codec.ParseInt(r.Value[2]));
                if (t == null)
                    continue;
                string name = Codec.Unescape(r.Value[3]);
                SnapshotField f;
                try
                {
                    f = SnapshotSchema.Field(t, name, ctx.Adapters);
                }
                catch (Exception e)
                {
                    Errors.Add("field " + t.FullName + "." + name + ": " + e.Message);
                    Blame(t);
                    continue;
                }
                if (f == null)
                {
                    Errors.Add("field " + t.FullName + "." + name + " not found");
                    Blame(t);
                }
                else
                    fields[r.Key] = f;
            }
            foreach (KeyValuePair<int, string[]> r in methodRecords)
                ResolveMethod(r.Key, r.Value);
        }

        private Type TypeOf(int id)
        {
            Type t;
            if (types.TryGetValue(id, out t))
                return t;
            string[] r;
            if (!typeRecords.TryGetValue(id, out r))
            {
                Errors.Add("type " + id + " missing");
                return null;
            }
            Assembly recorded = null;
            try
            {
                switch (r[2])
                {
                    case "n":
                        recorded = asms[Codec.ParseInt(r[3])];
                        t = SnapshotSchema.ResolveType(recorded, Codec.Unescape(r[4]), ctx.Adapters);
                        break;
                    case "g":
                        recorded = asms[Codec.ParseInt(r[3])];
                        Type def = SnapshotSchema.ResolveType(recorded, Codec.Unescape(r[4]), ctx.Adapters);
                        string[] argIds = r[5].Length > 0 ? r[5].Split(',') : new string[0];
                        var args = new Type[argIds.Length];
                        for (int i = 0; i < argIds.Length; i++)
                            args[i] = TypeOf(Codec.ParseInt(argIds[i]));
                        t = def != null && Array.IndexOf(args, null) < 0 ? def.MakeGenericType(args) : null;
                        break;
                    case "a":
                        Type element = TypeOf(Codec.ParseInt(r[3]));
                        int rank = Codec.ParseInt(r[4]);
                        t = element == null ? null : rank == 1 ? element.MakeArrayType() : element.MakeArrayType(rank);
                        break;
                    case "b":
                        Type inner = TypeOf(Codec.ParseInt(r[3]));
                        t = inner != null ? inner.MakeByRefType() : null;
                        break;
                }
            }
            catch (Exception e)
            {
                Errors.Add("type record " + id + ": " + e.Message);
                t = null;
            }
            if (t == null)
            {
                Errors.Add("type " + string.Join(" ", r) + " not found");
                if (recorded != null)
                    FailedAssemblies.Add(recorded);
            }
            types[id] = t;
            return t;
        }

        private void ResolveMethod(int id, string[] r)
        {
            Type t = TypeOf(Codec.ParseInt(r[2]));
            if (t == null)
                return;
            string name = Codec.Unescape(r[3]);
            string[] pIds = r[4].Length > 0 ? r[4].Split(',') : new string[0];
            string[] gIds = r[5].Length > 0 ? r[5].Split(',') : new string[0];
            var ps = new Type[pIds.Length];
            for (int i = 0; i < ps.Length; i++)
                ps[i] = TypeOf(Codec.ParseInt(pIds[i]));
            var gs = new Type[gIds.Length];
            for (int i = 0; i < gs.Length; i++)
                gs[i] = TypeOf(Codec.ParseInt(gIds[i]));
            try
            {
                methods[id] = SnapshotSchema.ResolveMethod(t, name, ps, gs, ctx.Adapters);
            }
            catch (Exception e)
            {
                Errors.Add("method " + t.FullName + "." + name + ": " + e.Message);
                Blame(t);
            }
        }

        private void CreateObjects()
        {
            foreach (KeyValuePair<int, string[]> r in objectRecords)
            {
                int id = r.Key;
                string[] p = r.Value;
                string cat = p[2];
                Type t = TypeOf(Codec.ParseInt(p[3]));
                if (t == null)
                    return;
                try
                {
                    switch (cat)
                    {
                        case "c":
                        case "s":
                            // The game's readonly members are initialized by its own constructors.
                            // RichTextBuilder's arrays are then filled through normal array operations.
                            if (SnapshotTuples.IsTuple(t)) objs[id] = SnapshotTuples.Access(t).Create();
                            else if (t == typeof(RichTextBuilder)) objs[id] = new RichTextBuilder();
                            // 0.8.0: a readonly instance field of the type or of any of its base types (the fields written,
                            // SnapshotContext.CopyFields). Up to 0.7.1, and in the 0.8.0 code of the first review on
                            // 2026-11-12, only the type's own fields counted, so that a base type's readonly collection
                            // stayed null and the read-back failed (Codex code review, 2026-11-12).
                            else if (ctx.CopyFields(t).Exists(f => f.IsReadOnly)) objs[id] = New(t);
                            else objs[id] = FormatterServices.GetUninitializedObject(t);
                            break;
                        case "a":
                            objs[id] = Array.CreateInstance(t.GetElementType(), Codec.ParseInt(p[4]));
                            break;
                        case "l":
                        case "q":
                        case "k":
                            objs[id] = Activator.CreateInstance(t);
                            break;
                        case "h":
                        case "d":
                            object comparer = Codec.Comparer(p[4]);
                            objs[id] = comparer == null ? Activator.CreateInstance(t) : Activator.CreateInstance(t, comparer);
                            break;
                        case "o":
                            objs[id] = new object();
                            break;
                        case "w":
                            objs[id] = new StringBuilder(Codec.Unescape(p.Length > 4 ? p[4] : ""));
                            break;
                        case "I":
                            objs[id] = new StoreClientInstance();
                            break;
                        case "x":
                        case "P":
                            string pathProblem;
                            object existing = ResolvePath(p[4], out pathProblem);
                            if (existing == null)
                                Errors.Add("object " + id + " (" + t.FullName + "): path " + p[4] + (pathProblem != null ? ": " + pathProblem : " is empty"));
                            objs[id] = existing;
                            break;
                        case "Y":
                            objs[id] = FindByKey(id, t, Codec.Unescape(p.Length > 4 ? p[4] : ""));
                            break;
                        case "g":
                            GameItem item = ctx.FindItem(Codec.ParseInt(p[4]));
                            if (item == null)
                                Errors.Add("item with unique ID " + p[4] + " not found");
                            objs[id] = item;
                            break;
                        case "G":
                            objs[id] = dryRun ? null : ctx.DecodeItem(Codec.Unescape(p[4]));
                            if (!dryRun && objs[id] == null)
                                Errors.Add("item " + id + " could not be decoded");
                            break;
                        case "u":
                            UnityEngine.Object asset = ctx.FindAsset(t, Codec.Unescape(p.Length > 4 ? p[4] : ""));
                            if (asset == null)
                                Warnings.Add(t.Name + " '" + (p.Length > 4 ? p[4] : "") + "' not found; left empty");
                            objs[id] = asset;
                            break;
                        case "v":
                            break;
                        default:
                            Errors.Add("unknown category " + cat);
                            break;
                    }
                }
                catch (Exception e)
                {
                    Errors.Add("creating object " + id + " (" + t.FullName + "): " + e.Message);
                    Blame(t);
                }
            }
        }

        // 0.8.0: a type of a mod that follows the spec with a readonly instance field is created through that mod's
        // create function, which runs its constructor with that mod's ModReflect scope. The game's types as before.
        private object New(Type t)
        {
            Adapter adapter = ctx.Adapters.For(t.Assembly);
            if (adapter == null)
                return ModReflect.New(t);
            object made = adapter.Create(t);
            if (made == null || made.GetType() != t)
                throw new NotSupportedException("the adapter of " + adapter.Mod + " created " + (made == null ? "nothing" : made.GetType().FullName));
            return made;
        }

        // 0.8.0, spec 3.5: the live object for a key written by the writer ('Y'). The read-back check at save time finds
        // the objects too; the catalogs and ledgers are live then. No object, an object of another type, or an exception
        // is an error, so the whole snapshot falls back.
        private object FindByKey(int id, Type t, string key)
        {
            Adapter adapter = ctx.Adapters.For(t.Assembly);
            if (adapter == null || adapter.Find == null)
            {
                Errors.Add("object " + id + " (" + t.FullName + "): no adapter finds it by key");
                Blame(t);
                return null;
            }
            object found = adapter.Find(t, key);
            if (found == null || found.GetType() != t)
            {
                Errors.Add("object " + id + " (" + t.FullName + "): key '" + key + "' found " + (found == null ? "nothing" : "a " + found.GetType().FullName));
                Blame(t);
                return null;
            }
            return found;
        }

        // 0.8.0, spec 3.6: E records. Each needs its provider assembly (checked with the other assemblies of the
        // snapshot), spec version 1, an adapter of the provider with loadExtra in this operation, and an owner that the
        // snapshot creates anew: a 'c' object of a game type, a StoreClientInstance ('I') or an item stored with the
        // game's encoding ('G'). The value is rebuilt now; it is given back on the first shutter update.
        private void ReadExtras()
        {
            Assembly game = typeof(StoreClient).Assembly;
            foreach (string[] p in extraRecords)
            {
                if (p.Length < 5)
                {
                    Errors.Add("extra data record with " + p.Length + " fields");
                    continue;
                }
                int owner = Codec.ParseInt(p[1]);
                string[] rec;
                Type ownerType = null;
                if (objectRecords.TryGetValue(owner, out rec) && (rec[2] == "c" || rec[2] == "I" || rec[2] == "G"))
                    types.TryGetValue(Codec.ParseInt(rec[3]), out ownerType);
                if (ownerType == null || ownerType.Assembly != game)
                {
                    Errors.Add("extra data for object " + owner + ", which is not a game object the snapshot creates");
                    continue;
                }
                Assembly provider;
                if (!asms.TryGetValue(Codec.ParseInt(p[2]), out provider))
                {
                    Errors.Add("extra data of an unknown assembly " + p[2]);
                    continue;
                }
                if (p[3] != SpecAdapters.Version)
                {
                    Errors.Add("extra data of " + provider.GetName().Name + " has spec version " + p[3]);
                    FailedAssemblies.Add(provider);
                    continue;
                }
                Adapter adapter = ctx.Adapters.For(provider);
                if (adapter == null || adapter.LoadExtra == null)
                {
                    Errors.Add("extra data of " + provider.GetName().Name + ": no adapter of that mod takes it back");
                    FailedAssemblies.Add(provider);
                    continue;
                }
                object ownerObject;
                objs.TryGetValue(owner, out ownerObject);
                if (ownerObject == null && !dryRun)
                {
                    Errors.Add("extra data for object " + owner + ", which was not created");
                    continue;
                }
                Extras.Add(new ExtraValue { Provider = provider, Owner = ownerObject, Value = Resolve(p[4]) });
            }
        }

        // Every step after the static field is a field id. Up to 0.7.1 paths into the state of CorpPresence also had the
        // steps .a<array index>, .i<list index> and .k<dictionary key>; 0.8.0 no longer supports CorpPresence (T-029) and
        // does not read them.
        private object ResolvePath(string path, out string problem)
        {
            problem = null;
            string[] steps = path.Split('.');
            if (steps.Length == 0 || steps[0].Length < 2 || steps[0][0] != 'S')
                return null;
            string[] head = steps[0].Substring(1).Split(':');
            SnapshotField holder;
            if (head.Length != 2 || !fields.TryGetValue(Codec.ParseInt(head[1]), out holder))
                return null;
            object cur = holder.GetValue(ctx, null);
            for (int i = 1; i < steps.Length && cur != null; i++)
            {
                int fieldId;
                if (!int.TryParse(steps[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out fieldId))
                {
                    problem = "the step '" + steps[i] + "' is not a field id (array, list and dictionary steps were written up to 0.7.1 for CorpPresence, which is no longer supported)";
                    return null;
                }
                SnapshotField f;
                if (!fields.TryGetValue(fieldId, out f))
                    return null;
                cur = f.GetValue(ctx, cur);
            }
            return cur;
        }

        private object GetDelegate(int id)
        {
            object existing;
            if (objs.TryGetValue(id, out existing))
                return existing;
            if (!inProgress.Add(id))
            {
                Errors.Add("delegate " + id + " refers to itself");
                return null;
            }
            string[] p = objectRecords[id];
            Type t = TypeOf(Codec.ParseInt(p[3]));
            Delegate result = null;
            for (int i = 4; i < p.Length; i++)
            {
                int at = p[i].IndexOf('@');
                MethodInfo m;
                if (at < 0 || !methods.TryGetValue(Codec.ParseInt(p[i].Substring(0, at)), out m))
                {
                    Errors.Add("delegate " + id + ": bad entry " + p[i]);
                    continue;
                }
                object target = Resolve(p[i].Substring(at + 1));
                Delegate d;
                try
                {
                    d = m.IsStatic && target == null
                        ? Delegate.CreateDelegate(t, m, false)
                        : Delegate.CreateDelegate(t, target, m, false);
                }
                catch (Exception e)
                {
                    Errors.Add("delegate " + id + " to " + m.DeclaringType.FullName + "." + m.Name + ": " + e.Message);
                    Blame(m.DeclaringType);
                    continue;
                }
                if (d == null)
                {
                    Errors.Add("delegate " + id + " to " + m.DeclaringType.FullName + "." + m.Name + " could not be bound");
                    Blame(m.DeclaringType);
                    continue;
                }
                result = result == null ? d : Delegate.Combine(result, d);
            }
            inProgress.Remove(id);
            objs[id] = result;
            return result;
        }

        private void BindReadonlyFields()
        {
            var bindings = new Dictionary<int, object>();
            foreach (KeyValuePair<int, string[]> record in objectRecords)
            {
                string[] p = record.Value;
                if (p[2] != "c") continue;
                object owner = objs[record.Key];
                for (int i = 4; i < p.Length; i++)
                {
                    int eq = p[i].IndexOf('=');
                    SnapshotField field;
                    if (eq < 0 || !fields.TryGetValue(Codec.ParseInt(p[i].Substring(0, eq)), out field) || !field.IsReadOnly) continue;
                    string value = p[i].Substring(eq + 1);
                    if (!value.StartsWith("r", StringComparison.Ordinal)) continue;
                    int id = Codec.ParseInt(value.Substring(1));
                    string[] target = objectRecords[id];
                    // Only a collection the constructor made is bound here. Any other object in a readonly field (found by
                    // key or by path, an item, a value, a delegate, a copied object) is compared after the constructor
                    // like every other readonly field (FillFields, SnapshotField.SetValue: the same object or an equal
                    // value, otherwise an error). Up to the third code review on 2026-11-12 such a field was refused
                    // here even when the constructor had set exactly the stored object.
                    if (target[2] != "a" && target[2] != "l" && target[2] != "h" && target[2] != "q" && target[2] != "k" && target[2] != "d")
                        continue;
                    object current = field.GetValue(ctx, owner), bound;
                    if (current == null || current.GetType() != objs[id].GetType()
                        || (current is Array && ((Array)current).Length != Codec.ParseInt(target[4])))
                    { Errors.Add("readonly collection shape changed: " + field.Name); Blame(owner.GetType()); continue; }
                    if (bindings.TryGetValue(id, out bound) && !ReferenceEquals(bound, current))
                    { Errors.Add("readonly collection sharing changed: " + field.Name); Blame(owner.GetType()); continue; }
                    if (!bindings.ContainsKey(id) && !(current is Array))
                    {
                        var list = current as IList;
                        var dictionary = current as IDictionary;
                        if (list != null) list.Clear();
                        else if (dictionary != null) dictionary.Clear();
                        else CollectionAccess.For(current.GetType()).Clear(current);
                    }
                    bindings[id] = current;
                    objs[id] = current;
                }
            }
        }

        private void FillFields(int id, string[] p, int start)
        {
            object o = objs[id];
            if (o == null)
                return;
            for (int i = start; i < p.Length; i++)
            {
                int eq = p[i].IndexOf('=');
                SnapshotField f;
                if (eq < 0 || !fields.TryGetValue(Codec.ParseInt(p[i].Substring(0, eq)), out f))
                {
                    Errors.Add("object " + id + ": bad field entry");
                    continue;
                }
                string token = p[i].Substring(eq + 1);
                // A readonly field is never set: the constructor's value must be exactly what was stored
                // (ReadonlyMismatch). Up to the fifth code review round on 2026-11-12, part of these fields were compared
                // in SnapshotField.SetValue with the Equals of the constructor's value.
                if (f.IsReadOnly)
                {
                    string problem;
                    try { problem = ReadonlyMismatch(f.GetValue(ctx, o), token); }
                    catch (Exception e) { problem = e.GetType().Name + ": " + e.Message; }
                    if (problem != null)
                    {
                        Errors.Add("object " + id + " field " + f.Name + ": constructor did not restore readonly field (" + problem + ")");
                        Blame(o.GetType());
                    }
                    continue;
                }
                object v = Resolve(token);
                try
                {
                    o = f.SetValue(ctx, o, v);
                    objs[id] = o;
                }
                catch (Exception e)
                {
                    Errors.Add("object " + id + " field " + f.Name + ": " + e.Message);
                    Blame(o.GetType());
                }
            }
        }

        private bool IsCollectionRecord(int id)
        {
            string[] rec;
            if (!objectRecords.TryGetValue(id, out rec))
                return false;
            string cat = rec[2];
            return cat == "a" || cat == "l" || cat == "h" || cat == "q" || cat == "k" || cat == "d";
        }

        // The constructor's value of a readonly field against its stored token; null when they agree. A collection was bound
        // to the constructor's own collection (BindReadonlyFields), so it must be that very object; any other stored object
        // is checked by StoredReadonlyMismatch; a stored scalar or null by ScalarMismatch.
        private string ReadonlyMismatch(object current, string token)
        {
            if (token.Length > 1 && token[0] == 'r')
            {
                int target = Codec.ParseInt(token.Substring(1));
                if (IsCollectionRecord(target))
                {
                    object bound;
                    objs.TryGetValue(target, out bound);
                    return bound != null && ReferenceEquals(current, bound) ? null : "not the collection the constructor made";
                }
                return StoredReadonlyMismatch(current, target, 0);
            }
            return ScalarMismatch(current, Resolve(token));
        }

        // 0.8.0 (code review 2026-11-12, fourth round): the constructor's value of a readonly field against the stored
        // object 'id'; null when they agree. A reference found again (by key, by path, an item by its unique ID, an asset)
        // must be the same object. A value must have the same type and agree field by field by these rules: the stored
        // fields of a copied value ('s'), every field of a value found by key. Scalars agree by their value; the type's
        // own Equals is never used, since a type may compare only part of its state. An object that reading always makes
        // anew (a copied object, an item decoded from the game's encoding, which the read-back check at save time leaves
        // null, a delegate, a StringBuilder, a client instance, an object made through a constructor) is never the
        // constructor's object, so a readonly field holding one cannot be read back. Up to the fourth round, the field
        // was compared with ReferenceEquals or Equals (SnapshotField.SetValue).
        private string StoredReadonlyMismatch(object current, int id, int depth)
        {
            if (depth > 16)
                return "values nested too deeply";
            string[] rec;
            if (!objectRecords.TryGetValue(id, out rec))
                return "object " + id + " missing";
            // The same object in any category.
            object stored = rec[2] == "s" ? null : Resolve("r" + id);
            if (stored != null && ReferenceEquals(current, stored))
                return null;
            switch (rec[2])
            {
                case "Y":
                case "x":
                case "P":
                case "g":
                case "u":
                    // A value found by key agrees by its fields.
                    if (stored != null && stored.GetType().IsValueType)
                        return ValueMismatch(current, stored, depth);
                    return "not the stored object " + id;
                case "s":
                {
                    Type t = TypeOf(Codec.ParseInt(rec[3]));
                    if (current == null || t == null || current.GetType() != t)
                        return "not the stored " + (t != null ? t.FullName : "value");
                    for (int i = 4; i < rec.Length; i++)
                    {
                        int eq = rec[i].IndexOf('=');
                        SnapshotField f;
                        if (eq < 0 || !fields.TryGetValue(Codec.ParseInt(rec[i].Substring(0, eq)), out f))
                            return "bad field entry in value " + id;
                        string token = rec[i].Substring(eq + 1);
                        object part = f.GetValue(ctx, current);
                        string problem = token.Length > 1 && token[0] == 'r'
                            ? StoredReadonlyMismatch(part, Codec.ParseInt(token.Substring(1)), depth + 1)
                            : ScalarMismatch(part, Resolve(token));
                        if (problem != null)
                            return f.Name + ": " + problem;
                    }
                    return null;
                }
                default:
                    return "a stored object of category '" + rec[2] + "' is made anew when reading, never the constructor's object";
            }
        }

        // A value found by key against the constructor's value: every instance field by the rules above.
        private string ValueMismatch(object current, object stored, int depth)
        {
            if (depth > 16)
                return "values nested too deeply";
            Type t = stored.GetType();
            if (current == null || current.GetType() != t)
                return "not a " + t.FullName + " value";
            if (IsScalar(t))
                return ScalarMismatch(current, stored);
            foreach (SnapshotField f in ctx.CopyFields(t))
            {
                object a = f.GetValue(ctx, current), b = f.GetValue(ctx, stored);
                string problem;
                if (a == null || b == null)
                    problem = a == null && b == null ? null : "null on one side";
                else if (IsScalar(b.GetType()))
                    problem = ScalarMismatch(a, b);
                else if (b.GetType().IsValueType)
                    problem = ValueMismatch(a, b, depth + 1);
                else
                    problem = ReferenceEquals(a, b) ? null : "not the same object";
                if (problem != null)
                    return f.Name + ": " + problem;
            }
            return null;
        }

        private static readonly Type RuntimeTypeType = typeof(object).GetType();

        // Primitives (pointers included), enums, strings, decimals and the runtime's own Type objects: values whose
        // equality no mod's type can change. A Type object of another class (a TypeDelegator, for example) is not one.
        private static bool IsScalar(Type t)
        {
            return t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) || t == RuntimeTypeType;
        }

        // 'current' is the constructor's value, 'stored' the value read from the snapshot (or the value found by key).
        // Both must be null, or of exactly the same scalar type and equal by the stored value's own Equals, which belongs
        // to the runtime for every scalar type; Type objects must be the same object.
        private static string ScalarMismatch(object current, object stored)
        {
            if (current == null || stored == null)
                return current == null && stored == null ? null : "null on one side";
            Type t = stored.GetType();
            if (current.GetType() != t || !IsScalar(t))
                return "not the stored " + t.FullName + " value";
            if (t == RuntimeTypeType)
                return ReferenceEquals(current, stored) ? null : "a different type";
            return stored.Equals(current) ? null : "a different value";
        }

        private void EnsureStruct(int id)
        {
            if (structsFilled.Contains(id))
                return;
            if (!inProgress.Add(id))
            {
                Errors.Add("struct " + id + " contains itself");
                return;
            }
            FillFields(id, objectRecords[id], 4);
            inProgress.Remove(id);
            structsFilled.Add(id);
        }

        private void FillInstance(int id, string[] p)
        {
            var instance = (StoreClientInstance)objs[id];
            object client = Resolve(p[4]);
            SnapshotField f = SnapshotSchema.Field(typeof(StoreClientInstance), "storeClient", ctx.Adapters);
            if (f == null)
                Errors.Add("StoreClientInstance.storeClient not found");
            else
                f.SetValue(ctx, instance, client);
        }

        private void FillCollection(int id, string[] p)
        {
            object o = objs[id];
            string cat = p[2];
            try
            {
                switch (cat)
                {
                    case "a":
                        var array = (Array)o;
                        for (int i = 5; i < p.Length; i++)
                            array.SetValue(Resolve(p[i]), i - 5);
                        break;
                    case "l":
                        var list = (IList)o;
                        for (int i = 4; i < p.Length; i++)
                            list.Add(Resolve(p[i]));
                        break;
                    case "h":
                    case "q":
                        ICollectionAccess add = CollectionAccess.For(o.GetType());
                        for (int i = 5 - (cat == "q" ? 1 : 0); i < p.Length; i++)
                            add.Add(o, Resolve(p[i]));
                        break;
                    case "k":
                        ICollectionAccess push = CollectionAccess.For(o.GetType());
                        for (int i = p.Length - 1; i >= 4; i--)
                            push.Add(o, Resolve(p[i]));
                        break;
                    case "d":
                        var dict = (IDictionary)o;
                        for (int i = 5; i + 1 < p.Length; i += 2)
                            dict.Add(Resolve(p[i]), Resolve(p[i + 1]));
                        break;
                }
            }
            catch (Exception e)
            {
                Errors.Add("collection " + id + " (" + o.GetType().FullName + "): " + e.Message);
            }
        }

        internal object Resolve(string token)
        {
            if (string.IsNullOrEmpty(token))
                return null;
            string body = token.Substring(1);
            switch (token[0])
            {
                case 'n': return null;
                case 'b': return body == "1";
                case 'c': return (char)Codec.ParseInt(body);
                case 'i':
                {
                    int colon = body.IndexOf(':');
                    Type t = TypeOf(Codec.ParseInt(body.Substring(0, colon)));
                    long v = Codec.ParseLong(body.Substring(colon + 1));
                    switch (Type.GetTypeCode(t))
                    {
                        case TypeCode.SByte: return (sbyte)v;
                        case TypeCode.Byte: return (byte)v;
                        case TypeCode.Int16: return (short)v;
                        case TypeCode.UInt16: return (ushort)v;
                        case TypeCode.Int32: return (int)v;
                        case TypeCode.UInt32: return (uint)v;
                        default: return v;
                    }
                }
                case 'u': return ulong.Parse(body, NumberStyles.Integer, CultureInfo.InvariantCulture);
                case 'f': return BitConverter.ToSingle(BitConverter.GetBytes(Codec.ParseInt(body)), 0);
                case 'd': return BitConverter.Int64BitsToDouble(Codec.ParseLong(body));
                case 'm': return decimal.Parse(body, NumberStyles.Number, CultureInfo.InvariantCulture);
                case 's': return Codec.Unescape(body);
                case 'e':
                {
                    int colon = body.IndexOf(':');
                    Type t = TypeOf(Codec.ParseInt(body.Substring(0, colon)));
                    return Enum.ToObject(t, Codec.ParseLong(body.Substring(colon + 1)));
                }
                case 'p':
                    return TypeOf(Codec.ParseInt(body)) == typeof(UIntPtr) ? (object)UIntPtr.Zero : IntPtr.Zero;
                case 'y': return TypeOf(Codec.ParseInt(body));
                case 'r':
                {
                    int id = Codec.ParseInt(body);
                    string[] rec;
                    if (!objectRecords.TryGetValue(id, out rec))
                    {
                        Errors.Add("object " + id + " missing");
                        return null;
                    }
                    if (rec[2] == "v")
                        return GetDelegate(id);
                    if (rec[2] == "s")
                        EnsureStruct(id);
                    object o;
                    objs.TryGetValue(id, out o);
                    return o;
                }
                default:
                    Errors.Add("bad value " + token);
                    return null;
            }
        }
    }
}
