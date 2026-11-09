using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;

namespace F5SnapshotSave
{
    // Only metadata names are generated offline. Every game/mod field access goes through the official broker.
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

        internal object GetValue(object target)
        {
            if (SnapshotTuples.IsTuple(DeclaringType)) return SnapshotTuples.Access(DeclaringType).Get(target, Name);
            return IsStatic ? ModReflect.GetStatic(DeclaringType, Name) : ModReflect.Get(target, Name);
        }

        internal object SetValue(object target, object value)
        {
            if (SnapshotTuples.IsTuple(DeclaringType)) return SnapshotTuples.Access(DeclaringType).Set(target, Name, value);
            if (IsReadOnly)
            {
                object current = GetValue(target);
                if (!ReferenceEquals(current, value) && (current == null || !current.Equals(value)))
                    throw new InvalidOperationException("constructor did not restore readonly field " + DeclaringType.FullName + "." + Name);
                return target;
            }
            if (IsStatic) ModReflect.SetStatic(DeclaringType, Name, value);
            else ModReflect.Set(target, Name, value);
            return target;
        }
    }

    internal static class SnapshotSchema
    {
        private sealed class FieldRow
        {
            public string Name { get; set; }
            public bool Static { get; set; }
            public bool ReadOnly { get; set; }
            public bool ValueType { get; set; }
            public string FieldType { get; set; }
        }
        private sealed class Document
        {
            public int Format { get; set; }
            public Dictionary<string, string> Modules { get; set; }
            public Dictionary<string, FieldRow[]> Fields { get; set; }
        }
        private static Document document;
        private static readonly Dictionary<Type, List<SnapshotField>> cache = new Dictionary<Type, List<SnapshotField>>();

        private static Document Data
        {
            get
            {
                if (document != null) return document;
                string json = ModHelper.ReadModFileText(F5SnapshotSaveMod.ModId, "Data/snapshot-schema.json");
                if (string.IsNullOrEmpty(json)) throw new InvalidOperationException("snapshot field schema is missing; reinstall the complete mod");
                Document parsed = JsonConvert.DeserializeObject<Document>(json, new JsonSerializerSettings {
                    TypeNameHandling = TypeNameHandling.None, MetadataPropertyHandling = MetadataPropertyHandling.Ignore
                });
                string expected;
                if (parsed == null || parsed.Format != 2 || parsed.Modules == null || parsed.Fields == null
                    || !parsed.Modules.TryGetValue(typeof(StoreClient).Assembly.GetName().Name, out expected)
                    || expected != typeof(StoreClient).Module.ModuleVersionId.ToString("N"))
                    throw new InvalidOperationException("the game code differs from the snapshot schema; this mod needs an update");
                document = parsed;
                return parsed;
            }
        }

        private static string Key(Type type)
        {
            Type definition = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
            return definition.Assembly.GetName().Name + ":" + definition.FullName;
        }

        internal static bool CanDescribe(Type type) { return SnapshotTuples.IsTuple(type) || Data.Fields.ContainsKey(Key(type)); }

        internal static List<SnapshotField> DeclaredFields(Type type)
        {
            List<SnapshotField> result;
            if (cache.TryGetValue(type, out result)) return result;
            if (SnapshotTuples.IsTuple(type)) { result = SnapshotTuples.Fields(type); cache[type] = result; return result; }
            FieldRow[] rows;
            if (!Data.Fields.TryGetValue(Key(type), out rows))
                throw new NotSupportedException("no snapshot schema for " + type.FullName);
            string assembly = type.Assembly.GetName().Name;
            string expected;
            if (type.Assembly != typeof(SnapshotSchema).Assembly && Data.Modules.TryGetValue(assembly, out expected)
                && expected != type.Module.ModuleVersionId.ToString("N"))
                throw new InvalidOperationException("snapshot schema is out of date for " + assembly);
            result = new List<SnapshotField>();
            foreach (FieldRow row in rows)
                result.Add(new SnapshotField(type, row.Name, row.Static, row.ReadOnly, row.ValueType, row.FieldType));
            cache[type] = result;
            return result;
        }

        internal static SnapshotField Field(Type type, string name)
        {
            foreach (SnapshotField field in DeclaredFields(type)) if (field.Name == name) return field;
            return null;
        }

        // Immutable catalog definitions remain the existing objects in their declared mod's static arrays.
        private static readonly Dictionary<Type, List<SnapshotField>> arrayFields = new Dictionary<Type, List<SnapshotField>>();
        internal static bool FindArrayReference(object value, out SnapshotField holder, out int index)
        {
            holder = null; index = -1;
            Type type = value.GetType();
            string assembly = type.Assembly.GetName().Name;
            // GunDef is immutable configuration owned by the catalog. Do not generalize this to mutable static arrays.
            if (assembly != "GunsmithExpansion" || type.FullName != "GunsmithExpansion.GunDef") return false;
            if (!Data.Modules.ContainsKey(assembly)) return false;
            List<SnapshotField> candidates;
            if (!arrayFields.TryGetValue(type, out candidates))
            {
                candidates = new List<SnapshotField>();
                string prefix = assembly + ":", wanted = type.FullName + "[]";
                foreach (var row in Data.Fields)
                {
                    if (!row.Key.StartsWith(prefix, StringComparison.Ordinal)) continue;
                    foreach (FieldRow field in row.Value)
                    {
                        if (!field.Static || field.FieldType != wanted) continue;
                        Type owner = ModReflect.FindType(row.Key.Substring(prefix.Length));
                        if (owner != null) candidates.Add(Field(owner, field.Name));
                    }
                }
                arrayFields[type] = candidates;
            }
            foreach (SnapshotField candidate in candidates)
            {
                var array = candidate.GetValue(null) as Array;
                if (array == null || array.Rank != 1) continue;
                for (int i = 0; i < array.Length; i++)
                    if (ReferenceEquals(array.GetValue(i), value)) { holder = candidate; index = i; return true; }
            }
            return false;
        }

        internal static Type ResolveType(Assembly expectedAssembly, string name)
        {
            Type result;
            if (!KnownTypes.TryGetValue(name, out result)) result = ModReflect.FindType(name);
            if (result == null || result.Assembly != expectedAssembly)
                throw new NotSupportedException("snapshot type is not in its recorded assembly: " + name);
            return result;
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
