using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;

namespace F5SnapshotSave
{
    // Only metadata names are generated offline. Every game/mod field access goes through the official broker: this mod's
    // ModReflect, or, for an object of a mod that follows the compatibility spec, that mod's adapter (SnapshotContext).
    // A SnapshotField holds names and flags only, never a function of an adapter, so the field lists can be cached.
    internal sealed class SnapshotField
    {
        internal readonly Type DeclaringType;
        internal readonly string Name;
        internal readonly bool IsStatic;
        internal readonly bool IsReadOnly;
        internal readonly bool IsValueType;
        internal readonly string FieldTypeName;
        internal bool IsLiteral { get { return false; } }

        internal SnapshotField(Type owner, string name, bool isStatic, bool isReadOnly, bool isValueType, string fieldType)
        { DeclaringType = owner; Name = name; IsStatic = isStatic; IsReadOnly = isReadOnly; IsValueType = isValueType; FieldTypeName = fieldType; }

        // 0.8.0: through the context of the operation, which knows the adapters collected for it.
        internal object GetValue(SnapshotContext ctx, object target)
        {
            if (SnapshotTuples.IsTuple(DeclaringType)) return SnapshotTuples.Access(DeclaringType).Get(target, Name);
            return IsStatic ? ctx.GetStatic(DeclaringType, Name) : ctx.GetInstance(target, Name);
        }

        internal object SetValue(SnapshotContext ctx, object target, object value)
        {
            if (SnapshotTuples.IsTuple(DeclaringType)) return SnapshotTuples.Access(DeclaringType).Set(target, Name, value);
            if (IsReadOnly)
            {
                object current = GetValue(ctx, target);
                if (!ReferenceEquals(current, value) && (current == null || !current.Equals(value)))
                    throw new InvalidOperationException("constructor did not restore readonly field " + DeclaringType.FullName + "." + Name);
                return target;
            }
            if (IsStatic) ctx.SetStatic(DeclaringType, Name, value);
            else ctx.SetInstance(target, Name, value);
            return target;
        }
    }

    // 0.8.0: a type that makes an object not storable: no field table for it or for one of its base types, a table that
    // does not belong to the loaded module, or a type or method that would not be found again when the snapshot is read.
    // Culprit is the type to blame; its mod is the one named to the player (IncompatibleNotice).
    internal sealed class SnapshotTypeException : NotSupportedException
    {
        internal readonly Type Culprit;

        internal SnapshotTypeException(Type culprit, string message) : base(message)
        {
            Culprit = culprit;
        }
    }

    // A parsed field table (Format 2, see tools/generate_snapshot_schema.ps1): the table this mod ships
    // (Data/snapshot-schema.json), or, since 0.8.0, the table of one assembly that a mod following the spec hands over
    // (SpecAdapters). The SnapshotField lists made from it are cached per type; a table never changes once parsed.
    internal sealed class FieldTable
    {
        internal readonly Dictionary<string, string> Modules;
        private readonly Dictionary<string, SnapshotSchema.FieldRow[]> rows;
        private readonly Dictionary<Type, List<SnapshotField>> declared = new Dictionary<Type, List<SnapshotField>>();

        internal FieldTable(Dictionary<string, string> modules, Dictionary<string, SnapshotSchema.FieldRow[]> rows)
        {
            Modules = modules;
            this.rows = rows;
        }

        internal bool Has(Type type)
        {
            return rows.ContainsKey(SnapshotSchema.Key(type));
        }

        internal List<SnapshotField> DeclaredFields(Type type)
        {
            List<SnapshotField> result;
            if (declared.TryGetValue(type, out result)) return result;
            SnapshotSchema.FieldRow[] list;
            if (!rows.TryGetValue(SnapshotSchema.Key(type), out list) || list == null)
                throw new SnapshotTypeException(type, "no snapshot schema for " + type.FullName);
            result = new List<SnapshotField>();
            foreach (SnapshotSchema.FieldRow row in list)
            {
                if (row != null && !string.IsNullOrEmpty(row.Name))
                    result.Add(new SnapshotField(type, row.Name, row.Static, row.ReadOnly, row.ValueType, row.FieldType));
            }
            declared[type] = result;
            return result;
        }
    }

    internal static class SnapshotSchema
    {
        internal sealed class FieldRow
        {
            public string Name { get; set; }
            public bool Static { get; set; }
            public bool ReadOnly { get; set; }
            public bool ValueType { get; set; }
            public string FieldType { get; set; }
        }
        internal sealed class Document
        {
            public int Format { get; set; }
            public Dictionary<string, string> Modules { get; set; }
            public Dictionary<string, FieldRow[]> Fields { get; set; }
        }
        private static FieldTable shipped;
        private static readonly Dictionary<Type, List<SnapshotField>> tupleCache = new Dictionary<Type, List<SnapshotField>>();

        // The table this mod ships: the game, this mod and the kept integrations (Pen_mod_pipette, vanilla_gun_expansion)
        // as installed when it was built. Since 0.8.0 the mods that follow the spec (GunsmithExpansion, Moneylending,
        // KeptShotgun) are not in it; their adapters hand over their own tables. CorpPresence, an integration up to
        // 0.7.1, is not in it either (T-029, yzz 2026-11-12).
        private static FieldTable Shipped
        {
            get
            {
                if (shipped != null) return shipped;
                string json = ModHelper.ReadModFileText(F5SnapshotSaveMod.ModId, "Data/snapshot-schema.json");
                if (string.IsNullOrEmpty(json)) throw new InvalidOperationException("snapshot field schema is missing; reinstall the complete mod");
                Document parsed = Parse(json);
                string expected;
                if (parsed == null || parsed.Format != 2 || parsed.Modules == null || parsed.Fields == null
                    || !parsed.Modules.TryGetValue(typeof(StoreClient).Assembly.GetName().Name, out expected)
                    || expected != typeof(StoreClient).Module.ModuleVersionId.ToString("N"))
                    throw new InvalidOperationException("the game code differs from the snapshot schema; this mod needs an update");
                shipped = new FieldTable(parsed.Modules, parsed.Fields);
                return shipped;
            }
        }

        // 0.8.0: called before a snapshot is written. When the game's code differs from the shipped table (after a game
        // update and before this mod is built again, or on open_beta), the whole save fails as up to 0.7.1 (plan 3.14,
        // yzz 2026-11-10); the exception is not caught by the checks that leave single customers out.
        internal static void CheckGame()
        {
            if (Shipped == null) throw new InvalidOperationException("snapshot field schema is missing");
        }

        internal static Document Parse(string json)
        {
            return JsonConvert.DeserializeObject<Document>(json, new JsonSerializerSettings {
                TypeNameHandling = TypeNameHandling.None, MetadataPropertyHandling = MetadataPropertyHandling.Ignore
            });
        }

        internal static string Key(Type type)
        {
            Type definition = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
            return definition.Assembly.GetName().Name + ":" + definition.FullName;
        }

        // The table that describes the types of this assembly in the operation that holds these adapters: the adapter's
        // table for a mod that follows the spec, otherwise the shipped one.
        private static Adapter AdapterOf(Type type, AdapterSet adapters)
        {
            return adapters != null ? adapters.For(type.Assembly) : null;
        }

        internal static bool CanDescribe(Type type, AdapterSet adapters)
        {
            if (SnapshotTuples.IsTuple(type)) return true;
            Adapter adapter = AdapterOf(type, adapters);
            return adapter != null ? adapter.Table.Has(type) : Shipped.Has(type);
        }

        internal static List<SnapshotField> DeclaredFields(Type type, AdapterSet adapters)
        {
            if (SnapshotTuples.IsTuple(type))
            {
                List<SnapshotField> tuple;
                if (!tupleCache.TryGetValue(type, out tuple)) { tuple = SnapshotTuples.Fields(type); tupleCache[type] = tuple; }
                return tuple;
            }
            Adapter adapter = AdapterOf(type, adapters);
            if (adapter != null) return adapter.Table.DeclaredFields(type);
            FieldTable table = Shipped;
            if (!table.Has(type))
                throw new SnapshotTypeException(type, "no snapshot schema for " + type.FullName);
            string assembly = type.Assembly.GetName().Name;
            string expected;
            if (type.Assembly != typeof(SnapshotSchema).Assembly && table.Modules.TryGetValue(assembly, out expected)
                && expected != type.Module.ModuleVersionId.ToString("N"))
                throw new SnapshotTypeException(type, "snapshot schema is out of date for " + assembly);
            return table.DeclaredFields(type);
        }

        internal static SnapshotField Field(Type type, string name, AdapterSet adapters)
        {
            foreach (SnapshotField field in DeclaredFields(type, adapters)) if (field.Name == name) return field;
            return null;
        }

        // The shipped table only (the game, this mod and the kept integrations), for test code that checks it.
        internal static bool CanDescribe(Type type) { return CanDescribe(type, null); }
        internal static List<SnapshotField> DeclaredFields(Type type) { return DeclaredFields(type, null); }
        internal static SnapshotField Field(Type type, string name) { return Field(type, name, null); }

        // The type of a 'T' record when a snapshot is read, and since 0.8.0 the same lookup for every type the writer is
        // about to record (GraphWriter.TypeId): the framework types this mod knows, the type of a mod that follows the
        // spec through its adapter's findType, any other through ModReflect.FindType (the game, this mod and its running
        // prerequisites). A different Workshop mod may use the same namespace and type name, so the result must be in the
        // recorded assembly. Up to 0.7.1 GunsmithExpansion's types had a second lookup by its manifest ID here, and its
        // GunDef objects were written as paths into its static arrays (FindArrayReference); GunsmithExpansion now finds
        // them again by key (spec 3.5).
        internal static Type ResolveType(Assembly expectedAssembly, string name, AdapterSet adapters)
        {
            Type result;
            Adapter adapter = adapters != null ? adapters.For(expectedAssembly) : null;
            if (adapter != null) result = adapter.FindType(name);
            else if (!KnownTypes.TryGetValue(name, out result)) result = ModReflect.FindType(name);
            if (result == null || result.Assembly != expectedAssembly)
                throw new NotSupportedException("snapshot type is not in its recorded assembly: " + name);
            return result;
        }

        // The method of a delegate in a snapshot ('M' record), and since 0.8.0 the same lookup when the writer records it
        // (GraphWriter.MethodId): a method of a mod that follows the spec through its adapter's method function, any other
        // through ModReflect.Target. The parameter types must be the recorded ones.
        internal static MethodInfo ResolveMethod(Type type, string name, Type[] parameters, Type[] genericArguments, AdapterSet adapters)
        {
            Adapter adapter = adapters != null ? adapters.For(type.Assembly) : null;
            Type[] signature = genericArguments.Length == 0 ? parameters : null;
            MethodBase found = adapter != null ? adapter.Method(type, name, signature) : ModReflect.Target(type, name, signature);
            var method = found as MethodInfo;
            if (method == null)
                throw new NotSupportedException("method " + type.FullName + "." + name + " not found");
            if (adapter != null && method.DeclaringType != type)
                throw new NotSupportedException("the adapter of " + adapter.Mod + " gave a method of " + (method.DeclaringType == null ? "no type" : method.DeclaringType.FullName));
            if (genericArguments.Length > 0)
            {
                if (!method.IsGenericMethodDefinition || method.GetGenericArguments().Length != genericArguments.Length)
                    throw new NotSupportedException("generic delegate signature changed");
                method = method.MakeGenericMethod(genericArguments);
            }
            ParameterInfo[] actual = method.GetParameters();
            bool same = actual.Length == parameters.Length;
            for (int i = 0; i < parameters.Length && same; i++)
                same = actual[i].ParameterType == parameters[i];
            if (!same)
                throw new NotSupportedException("method " + type.FullName + "." + name + " not found");
            return method;
        }

        private static readonly Dictionary<string, Type> KnownTypes = MakeKnownTypes();
        private static Dictionary<string, Type> MakeKnownTypes()
        {
            var result = new Dictionary<string, Type>();
            Type[] types = {
                typeof(object), typeof(string), typeof(bool), typeof(char), typeof(byte), typeof(sbyte), typeof(short), typeof(ushort),
                typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal), typeof(IntPtr), typeof(UIntPtr),
                typeof(void), typeof(DateTime), typeof(TimeSpan), typeof(Guid), typeof(Type), typeof(System.Text.StringBuilder),
                typeof(List<>), typeof(Dictionary<,>), typeof(HashSet<>), typeof(Queue<>), typeof(Stack<>), typeof(KeyValuePair<,>), typeof(Nullable<>),
                typeof(Action), typeof(Action<>), typeof(Action<,>), typeof(Action<,,>), typeof(Action<,,,>), typeof(Action<,,,,>),
                typeof(Func<>), typeof(Func<,>), typeof(Func<,,>), typeof(Func<,,,>), typeof(Func<,,,,>), typeof(Func<,,,,,>),
                typeof(Predicate<>), typeof(Comparison<>), typeof(ValueTuple<>), typeof(ValueTuple<,>), typeof(ValueTuple<,,>), typeof(ValueTuple<,,,>),
                typeof(ValueTuple<,,,,>), typeof(ValueTuple<,,,,,>), typeof(ValueTuple<,,,,,,>), typeof(ValueTuple<,,,,,,,>),
                typeof(UnityEngine.Object), typeof(UnityEngine.GameObject), typeof(UnityEngine.Component), typeof(UnityEngine.Behaviour), typeof(UnityEngine.MonoBehaviour),
                typeof(UnityEngine.Transform), typeof(UnityEngine.RectTransform), typeof(UnityEngine.Sprite), typeof(UnityEngine.Texture), typeof(UnityEngine.Texture2D),
                typeof(UnityEngine.AudioClip), typeof(UnityEngine.Material), typeof(UnityEngine.Font), typeof(UnityEngine.ScriptableObject),
                typeof(UnityEngine.Vector2), typeof(UnityEngine.Vector3), typeof(UnityEngine.Vector4), typeof(UnityEngine.Vector2Int), typeof(UnityEngine.Vector3Int),
                typeof(UnityEngine.Color), typeof(UnityEngine.Color32), typeof(UnityEngine.Quaternion), typeof(UnityEngine.Rect), typeof(UnityEngine.Bounds), typeof(UnityEngine.KeyCode)
            };
            foreach (Type type in types) result[type.FullName] = type;
            return result;
        }
    }

    internal interface ICollectionAccess
    {
        object Comparer(object collection);
        object DefaultComparer { get; }
        void Add(object collection, object value);
        void Clear(object collection);
    }
    internal sealed class CollectionAccess<T> : ICollectionAccess
    {
        public object Comparer(object collection) { var set = collection as HashSet<T>; return set != null ? set.Comparer : null; }
        public object DefaultComparer { get { return EqualityComparer<T>.Default; } }
        public void Add(object collection, object value)
        {
            var set = collection as HashSet<T>; if (set != null) { set.Add((T)value); return; }
            var queue = collection as Queue<T>; if (queue != null) { queue.Enqueue((T)value); return; }
            var stack = collection as Stack<T>; if (stack != null) { stack.Push((T)value); return; }
            throw new NotSupportedException("unsupported snapshot collection");
        }
        public void Clear(object collection)
        {
            var set = collection as HashSet<T>; if (set != null) { set.Clear(); return; }
            var queue = collection as Queue<T>; if (queue != null) { queue.Clear(); return; }
            var stack = collection as Stack<T>; if (stack != null) { stack.Clear(); return; }
            throw new NotSupportedException("unsupported snapshot collection");
        }
    }
    internal sealed class DictionaryAccess<TKey, TValue> : ICollectionAccess
    {
        public object Comparer(object collection) { return ((Dictionary<TKey, TValue>)collection).Comparer; }
        public object DefaultComparer { get { return EqualityComparer<TKey>.Default; } }
        public void Add(object collection, object value) { throw new NotSupportedException(); }
        public void Clear(object collection) { ((Dictionary<TKey, TValue>)collection).Clear(); }
    }
    internal static class CollectionAccess
    {
        private static readonly Dictionary<Type, ICollectionAccess> cache = new Dictionary<Type, ICollectionAccess>();
        internal static ICollectionAccess For(Type type)
        {
            ICollectionAccess result;
            if (cache.TryGetValue(type, out result)) return result;
            Type[] args = type.GetGenericArguments();
            Type helper = type.GetGenericTypeDefinition() == typeof(Dictionary<,>)
                ? typeof(DictionaryAccess<,>).MakeGenericType(args) : typeof(CollectionAccess<>).MakeGenericType(args[0]);
            result = (ICollectionAccess)Activator.CreateInstance(helper);
            cache[type] = result;
            return result;
        }
    }
}
