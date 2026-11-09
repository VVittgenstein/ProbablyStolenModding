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
    //   R  <name> <values...>
    // Values: n null, b0/b1, c<char code>, i<type id>:<integer>, u<unsigned 64-bit>, f<float bits>, d<double bits>,
    // m<decimal>, s<string>, e<type id>:<enum value>, p<type id> (zero pointer), y<type id> (a Type), r<object id>.
    // Object categories: c class fields, s struct fields, P fields set on an existing object, a array, l List,
    // h HashSet, q Queue, k Stack, d Dictionary, v delegate, x existing object, g item by unique ID,
    // G item stored with the game's item encoding, u asset by name, w StringBuilder, o plain object,
    // I StoreClientInstance.
    // Existing objects are written as a path: S<type id>:<static field id> followed by .<instance field id> steps.
    internal static class Codec
    {
        internal const string Magic = "F5SnapshotSave";
        internal const int Format = 2;
        internal const BindingFlags DeclaredInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        internal const BindingFlags DeclaredStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        internal const BindingFlags DeclaredMethods = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private static readonly Dictionary<Type, List<SnapshotField>> fieldCache = new Dictionary<Type, List<SnapshotField>>();

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

        // Instance fields of a type and of its base types, constants excluded.
        internal static List<SnapshotField> InstanceFields(Type t)
        {
            List<SnapshotField> list;
            if (fieldCache.TryGetValue(t, out list))
                return list;
            list = new List<SnapshotField>();
            if (!SnapshotSchema.CanDescribe(t))
                throw new NotSupportedException("no snapshot field schema for " + t.FullName);
            for (Type cur = t; cur != null && cur != typeof(object) && cur != typeof(ValueType); cur = cur.BaseType)
            {
                if (!SnapshotSchema.CanDescribe(cur)) break;
                foreach (SnapshotField f in SnapshotSchema.DeclaredFields(cur))
                {
                    if (!f.IsStatic)
                        list.Add(f);
                }
            }
            var names = new HashSet<string>();
            foreach (SnapshotField field in list)
                if (!names.Add(field.Name)) throw new NotSupportedException("hidden field cannot be addressed through ModReflect: " + t.FullName + "." + field.Name);
            fieldCache[t] = list;
            return list;
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

    internal sealed class GraphWriter
    {
        internal readonly List<string> Errors = new List<string>();
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
        private readonly List<string> rootLines = new List<string>();

        private readonly Dictionary<object, int> ids = new Dictionary<object, int>(ReferenceComparer.Instance);
        private readonly List<object> objects = new List<object>();
        private readonly List<string> paths = new List<string>();
        private readonly Queue<int> pending = new Queue<int>();
        private int nextId;

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
                    tokens.Add(Value(v, name + "[" + i + "]"));
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
            string path;
            if (target == null || !TryPath(target, out path))
            {
                Errors.Add(name + ": no path to " + (target == null ? "null" : target.GetType().FullName));
                return;
            }
            int id = nextId++;
            objects.Add(target);
            paths.Add(name);
            var sb = new StringBuilder();
            sb.Append("O\t").Append(id).Append("\tP\t").Append(TypeId(target.GetType())).Append('\t').Append(path);
            foreach (SnapshotField f in fields)
                sb.Append('\t').Append(FieldId(f)).Append('=').Append(Value(f.GetValue(target), name + "." + f.Name));
            objectLines.Add(sb.ToString());
            Flush();
            rootLines.Add("R\t" + name + "\tr" + id);
        }

        internal string Finish()
        {
            Flush();
            var sb = new StringBuilder();
            sb.Append("H\t").Append(Codec.Magic).Append('\t').Append(Codec.Format).Append('\n');
            foreach (List<string> block in new[] { asmLines, typeLines, fieldLines, methodLines, objectLines, rootLines })
            {
                foreach (string line in block)
                    sb.Append(line).Append('\n');
            }
            return sb.ToString();
        }

        private void Flush()
        {
            while (pending.Count > 0)
            {
                int id = pending.Dequeue();
                WriteBody(id, objects[id], paths[id]);
            }
        }

        private void Count(Type t)
        {
            string n = t.FullName ?? t.Name;
            int c;
            TypeCounts.TryGetValue(n, out c);
            TypeCounts[n] = c + 1;
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
                        Errors.Add(path + ": native pointer");
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
            Type t = box.GetType();
            Count(t);
            var sb = new StringBuilder();
            sb.Append("O\t").Append(id).Append("\ts\t").Append(TypeId(t));
            AppendFields(sb, box, t, path);
            objectLines.Add(sb.ToString());
            return id;
        }

        private void AppendFields(StringBuilder sb, object o, Type t, string path)
        {
            foreach (SnapshotField f in Codec.InstanceFields(t))
            {
                if (ctx.SkipField(f))
                    continue;
                object fv;
                try
                {
                    fv = f.GetValue(o);
                }
                catch (Exception e)
                {
                    Errors.Add(path + "." + f.Name + ": " + e.GetType().Name);
                    continue;
                }
                sb.Append('\t').Append(FieldId(f)).Append('=').Append(Value(fv, path + "." + f.Name));
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
                        Errors.Add(path + ": delegate to a generated method");
                        continue;
                    }
                    sb.Append('\t').Append(MethodId(m)).Append('@').Append(Value(d.Target, path + ".Target"));
                }
                objectLines.Add(sb.ToString());
                return;
            }

            var item = o as GameItem;
            if (item != null)
            {
                string json;
                int uid;
                if (ctx.TryItemReference(item, out uid))
                    sb.Append("g\t").Append(TypeId(t)).Append('\t').Append(uid.ToString(CultureInfo.InvariantCulture));
                else if (ctx.TryEncodeItem(item, out json))
                    sb.Append("G\t").Append(TypeId(t)).Append('\t').Append(Codec.Escape(json));
                else
                {
                    Errors.Add(path + ": item '" + item.identifier + "' could not be stored");
                    return;
                }
                objectLines.Add(sb.ToString());
                return;
            }

            var instance = o as StoreClientInstance;
            if (instance != null)
            {
                sb.Append("I\t").Append(TypeId(t)).Append('\t').Append(Value(instance.GetClientBlueprint(), path + ".storeClient"));
                objectLines.Add(sb.ToString());
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
                    Errors.Add(path + ": scene object " + t.FullName);
                return;
            }

            if (t.IsArray)
            {
                var array = (Array)o;
                if (array.Rank != 1)
                {
                    Errors.Add(path + ": array of rank " + array.Rank);
                    return;
                }
                sb.Append("a\t").Append(TypeId(t)).Append('\t').Append(array.Length);
                for (int i = 0; i < array.Length; i++)
                    sb.Append('\t').Append(Value(array.GetValue(i), path + "[" + i + "]"));
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
                        Errors.Add(path + ": collection with a custom comparer");
                        return;
                    }
                    sb.Append('\t').Append(comparer);
                }
                int i = 0;
                if (kind == 'd')
                {
                    foreach (DictionaryEntry e in (IDictionary)o)
                    {
                        sb.Append('\t').Append(Value(e.Key, path + "{key" + i + "}"));
                        sb.Append('\t').Append(Value(e.Value, path + "{" + i + "}"));
                        i++;
                    }
                }
                else
                {
                    foreach (object e in (IEnumerable)o)
                    {
                        sb.Append('\t').Append(Value(e, path + "[" + i + "]"));
                        i++;
                    }
                }
                objectLines.Add(sb.ToString());
                return;
            }

            if (Codec.IsFrameworkAssembly(t.Assembly))
            {
                Errors.Add(path + ": unsupported type " + t.FullName);
                return;
            }

            sb.Append("c\t").Append(TypeId(t));
            AppendFields(sb, o, t, path);
            objectLines.Add(sb.ToString());
        }

        private bool TryPath(object o, out string path)
        {
            SnapshotField holder;
            List<SnapshotField> chain;
            int index;
            if (SnapshotSchema.FindArrayReference(o, out holder, out index))
            {
                path = "S" + TypeId(holder.DeclaringType) + ":" + FieldId(holder) + ".a" + index;
                return true;
            }
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
                Type[] args = t.GetGenericArguments();
                var argIds = new string[args.Length];
                for (int i = 0; i < args.Length; i++)
                    argIds[i] = TypeId(args[i]).ToString(CultureInfo.InvariantCulture);
                line = "g\t" + AsmId(def.Assembly) + "\t" + Codec.Escape(def.FullName) + "\t" + string.Join(",", argIds);
            }
            else
                line = "n\t" + AsmId(t.Assembly) + "\t" + Codec.Escape(t.FullName);
            id = typeIds.Count;
            typeIds[t] = id;
            typeLines.Add("T\t" + id + "\t" + line);
            return id;
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
            var pIds = new string[ps.Length];
            for (int i = 0; i < ps.Length; i++)
                pIds[i] = TypeId(ps[i].ParameterType).ToString(CultureInfo.InvariantCulture);
            Type[] gargs = m.IsGenericMethod ? m.GetGenericArguments() : Type.EmptyTypes;
            var gIds = new string[gargs.Length];
            for (int i = 0; i < gargs.Length; i++)
                gIds[i] = TypeId(gargs[i]).ToString(CultureInfo.InvariantCulture);
            id = methodIds.Count;
            methodIds[m] = id;
            methodLines.Add("M\t" + id + "\t" + declaring + "\t" + Codec.Escape(m.Name) + "\t" + string.Join(",", pIds) + "\t" + string.Join(",", gIds));
            return id;
        }
    }

    internal sealed class GraphReader
    {
        internal readonly List<string> Errors = new List<string>();
        internal readonly List<string> Warnings = new List<string>();
        internal readonly List<string> ChangedAssemblies = new List<string>();
        private readonly SnapshotContext ctx;
        private readonly bool dryRun;

        private readonly Dictionary<int, string[]> asmRecords = new Dictionary<int, string[]>();
        private readonly Dictionary<int, string[]> typeRecords = new Dictionary<int, string[]>();
        private readonly Dictionary<int, string[]> fieldRecords = new Dictionary<int, string[]>();
        private readonly Dictionary<int, string[]> methodRecords = new Dictionary<int, string[]>();
        private readonly SortedDictionary<int, string[]> objectRecords = new SortedDictionary<int, string[]>();
        private readonly Dictionary<string, string[]> rootRecords = new Dictionary<string, string[]>();

        private readonly Dictionary<int, Assembly> asms = new Dictionary<int, Assembly>();
        private readonly Dictionary<int, Type> types = new Dictionary<int, Type>();
        private readonly Dictionary<int, SnapshotField> fields = new Dictionary<int, SnapshotField>();
        private readonly Dictionary<int, MethodInfo> methods = new Dictionary<int, MethodInfo>();
        private readonly Dictionary<int, object> objs = new Dictionary<int, object>();
        private readonly HashSet<int> structsFilled = new HashSet<int>();
        private readonly HashSet<int> inProgress = new HashSet<int>();

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
            if (header.Length < 3 || (header[2] != "1" && header[2] != Codec.Format.ToString(CultureInfo.InvariantCulture)))
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
                SnapshotField f = SnapshotSchema.Field(t, name);
                if (f == null)
                    Errors.Add("field " + t.FullName + "." + name + " not found");
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
            try
            {
                switch (r[2])
                {
                    case "n":
                        t = SnapshotSchema.ResolveType(asms[Codec.ParseInt(r[3])], Codec.Unescape(r[4]));
                        break;
                    case "g":
                        Type def = SnapshotSchema.ResolveType(asms[Codec.ParseInt(r[3])], Codec.Unescape(r[4]));
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
                Errors.Add("type " + string.Join(" ", r) + " not found");
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
                MethodInfo m = (MethodInfo)ModReflect.Target(t, name, gs.Length == 0 ? ps : null);
                if (gs.Length > 0)
                {
                    if (!m.IsGenericMethodDefinition || m.GetGenericArguments().Length != gs.Length)
                        throw new NotSupportedException("generic delegate signature changed");
                    m = m.MakeGenericMethod(gs);
                }
                ParameterInfo[] actual = m.GetParameters();
                bool same = actual.Length == ps.Length;
                for (int i = 0; i < ps.Length && same; i++)
                    same = actual[i].ParameterType == ps[i];
                if (same)
                {
                    methods[id] = m;
                    return;
                }
            }
            catch (Exception e) { Errors.Add("method " + t.FullName + "." + name + ": " + e.Message); return; }
            Errors.Add("method " + t.FullName + "." + name + " not found");
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
                            else if (SnapshotSchema.DeclaredFields(t).Exists(f => !f.IsStatic && f.IsReadOnly)) objs[id] = ModReflect.New(t);
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
                            object existing = ResolvePath(p[4]);
                            if (existing == null)
                                Errors.Add("object " + id + " (" + t.FullName + "): path " + p[4] + " is empty");
                            objs[id] = existing;
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
                }
            }
        }

        private object ResolvePath(string path)
        {
            string[] steps = path.Split('.');
            if (steps.Length == 0 || steps[0].Length < 2 || steps[0][0] != 'S')
                return null;
            string[] head = steps[0].Substring(1).Split(':');
            SnapshotField holder;
            if (head.Length != 2 || !fields.TryGetValue(Codec.ParseInt(head[1]), out holder))
                return null;
            object cur = holder.GetValue(null);
            for (int i = 1; i < steps.Length && cur != null; i++)
            {
                if (steps[i].StartsWith("a", StringComparison.Ordinal))
                {
                    var array = cur as Array;
                    int index = Codec.ParseInt(steps[i].Substring(1));
                    if (array == null || array.Rank != 1 || index < 0 || index >= array.Length) return null;
                    cur = array.GetValue(index);
                    continue;
                }
                SnapshotField f;
                if (!fields.TryGetValue(Codec.ParseInt(steps[i]), out f))
                    return null;
                cur = f.GetValue(cur);
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
                    continue;
                }
                if (d == null)
                {
                    Errors.Add("delegate " + id + " to " + m.DeclaringType.FullName + "." + m.Name + " could not be bound");
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
                    object current = field.GetValue(owner), bound;
                    string[] target = objectRecords[id];
                    if (target[2] != "a" && target[2] != "l" && target[2] != "h" && target[2] != "q" && target[2] != "k" && target[2] != "d")
                    { Errors.Add("readonly field needs an unsupported constructor: " + field.Name); continue; }
                    if (current == null || current.GetType() != objs[id].GetType()
                        || (current is Array && ((Array)current).Length != Codec.ParseInt(target[4])))
                    { Errors.Add("readonly collection shape changed: " + field.Name); continue; }
                    if (bindings.TryGetValue(id, out bound) && !ReferenceEquals(bound, current))
                    { Errors.Add("readonly collection sharing changed: " + field.Name); continue; }
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
                object v = Resolve(p[i].Substring(eq + 1));
                try
                {
                    o = f.SetValue(o, v);
                    objs[id] = o;
                }
                catch (Exception e)
                {
                    Errors.Add("object " + id + " field " + f.Name + ": " + e.Message);
                }
            }
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
            SnapshotField f = SnapshotSchema.Field(typeof(StoreClientInstance), "storeClient");
            if (f == null)
                Errors.Add("StoreClientInstance.storeClient not found");
            else
                f.SetValue(instance, client);
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
