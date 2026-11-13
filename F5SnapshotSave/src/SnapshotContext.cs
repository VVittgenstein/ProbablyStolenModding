using System;
using System.Collections.Generic;
using System.Reflection;

namespace F5SnapshotSave
{
    // What the graph codec needs from the game: objects that are referenced instead of copied (the store singletons,
    // their fields, and objects held in a static "instance" field), items found again by their unique ID, items stored
    // with the game's own item encoding, and assets found by name. Since 0.8.0 also the adapters of the mods that follow
    // the compatibility spec, collected for this one operation, and the field access that uses them.
    internal sealed class SnapshotContext
    {
        // The adapters of this operation (SpecAdapters.Collect). Only the objects of one save, snapshot read or put-back
        // step hold them, never a static field (plan 2.2 point 5).
        internal readonly AdapterSet Adapters;
        private readonly Dictionary<object, KeyValuePair<SnapshotField, List<SnapshotField>>> knownPaths =
            new Dictionary<object, KeyValuePair<SnapshotField, List<SnapshotField>>>(ReferenceComparer.Instance);
        private readonly HashSet<object> placedItems = new HashSet<object>(ReferenceComparer.Instance);
        private readonly Dictionary<int, GameItem> itemsByUid = new Dictionary<int, GameItem>();
        private readonly HashSet<int> sharedUids = new HashSet<int>();
        // Field lists of this operation, which depend on the adapters collected for it. The two lists of a type are kept
        // apart: the copy list is never taken from the path cache (plan 3.3).
        private readonly Dictionary<Type, List<SnapshotField>> copyFields = new Dictionary<Type, List<SnapshotField>>();
        private readonly Dictionary<Type, List<SnapshotField>> pathFields = new Dictionary<Type, List<SnapshotField>>();
        private readonly Dictionary<Type, List<SnapshotField>> staticHolders = new Dictionary<Type, List<SnapshotField>>();

        private SnapshotContext(AdapterSet adapters)
        {
            Adapters = adapters ?? new AdapterSet(null);
        }

        // Collects the adapters for this context alone.
        internal static SnapshotContext Create()
        {
            return Create(SpecAdapters.Collect());
        }

        internal static SnapshotContext Create(AdapterSet adapters)
        {
            var ctx = new SnapshotContext(adapters);
            ctx.AddStoreRoots();
            ctx.CollectItems();
            return ctx;
        }

        // Field access (0.8.0, spec 4.1). An instance field goes by the runtime type of the object: every instance field
        // of an object whose type belongs to a mod that follows the spec, also one declared in a base type of the game or
        // of another mod, is read and written through that mod's adapter. Its functions run ModReflect with that mod's
        // scope; a base type in another mod is a prerequisite of it (otherwise the game's scan flags the reference).
        // Other objects go through this mod's ModReflect, which checks the field's declaring type. A static field goes by
        // its declaring type; the static fields of such a mod are only read.
        internal object GetInstance(object target, string name)
        {
            Adapter adapter = Adapters.For(target.GetType().Assembly);
            return adapter != null ? adapter.Get(target, name) : ModReflect.Get(target, name);
        }

        internal void SetInstance(object target, string name, object value)
        {
            Adapter adapter = Adapters.For(target.GetType().Assembly);
            if (adapter != null)
                adapter.Set(target, name, value);
            else
                ModReflect.Set(target, name, value);
        }

        internal object GetStatic(Type declaring, string name)
        {
            Adapter adapter = Adapters.For(declaring.Assembly);
            return adapter != null ? adapter.GetStatic(declaring, name) : ModReflect.GetStatic(declaring, name);
        }

        internal void SetStatic(Type declaring, string name, object value)
        {
            if (Adapters.For(declaring.Assembly) != null)
                throw new NotSupportedException("static field " + declaring.FullName + "." + name + " of a mod that follows the spec is not written");
            ModReflect.SetStatic(declaring, name, value);
        }

        // The fields of an object that is copied ('c' and 's' objects when writing and reading, AddPartialRoot): the
        // instance fields of the type and of all its base types, constants excluded. Every type of the chain up to
        // System.Object or System.ValueType needs a field table (the shipped one or an adapter's); otherwise this throws and
        // the writer records the object as not storable. Up to 0.7.1 the list stopped at the first base type without a
        // table, so that base type's fields and those above it were not written and kept their defaults after loading.
        internal List<SnapshotField> CopyFields(Type t)
        {
            List<SnapshotField> list;
            if (copyFields.TryGetValue(t, out list))
                return list;
            list = new List<SnapshotField>();
            for (Type cur = t; cur != null && cur != typeof(object) && cur != typeof(ValueType); cur = cur.BaseType)
            {
                if (!SnapshotSchema.CanDescribe(cur, Adapters))
                    throw new SnapshotTypeException(cur, cur == t ? "no snapshot field schema for " + t.FullName
                        : "base type " + cur.FullName + " of " + t.FullName + " has no field table");
                AddInstanceFields(list, cur);
            }
            CheckNames(t, list);
            copyFields[t] = list;
            return list;
        }

        // The fields followed to find existing objects by path (AddStoreRoots, FindStaticHolder): the list
        // stops at the first base type without a field table, as up to 0.7.1. EmporiumEntry derives from MonoBehaviour,
        // which no table describes.
        internal List<SnapshotField> PathFields(Type t)
        {
            List<SnapshotField> list;
            if (pathFields.TryGetValue(t, out list))
                return list;
            list = new List<SnapshotField>();
            if (!SnapshotSchema.CanDescribe(t, Adapters))
                throw new SnapshotTypeException(t, "no snapshot field schema for " + t.FullName);
            for (Type cur = t; cur != null && cur != typeof(object) && cur != typeof(ValueType); cur = cur.BaseType)
            {
                if (!SnapshotSchema.CanDescribe(cur, Adapters))
                    break;
                AddInstanceFields(list, cur);
            }
            CheckNames(t, list);
            pathFields[t] = list;
            return list;
        }

        private void AddInstanceFields(List<SnapshotField> list, Type declaring)
        {
            foreach (SnapshotField f in SnapshotSchema.DeclaredFields(declaring, Adapters))
            {
                if (!f.IsStatic)
                    list.Add(f);
            }
        }

        private static void CheckNames(Type t, List<SnapshotField> list)
        {
            var names = new HashSet<string>();
            foreach (SnapshotField field in list)
                if (!names.Add(field.Name)) throw new SnapshotTypeException(t, "hidden field cannot be addressed through ModReflect: " + t.FullName + "." + field.Name);
        }

        // The store singletons and the objects in their fields are found again by path after loading; a closure that
        // captured, for example, the customer queue list keeps pointing at the live list.
        private void AddStoreRoots()
        {
            PlayerStore store = PlayerStore.HasInstance ? PlayerStore.Instance : null;
            if (store != null)
            {
                SnapshotField holder = FindStaticHolder(store);
                if (holder != null)
                {
                    AddRoot(store, holder, new List<SnapshotField>());
                    SnapshotField managerField = SnapshotSchema.Field(typeof(PlayerStore), "storeClientManager", Adapters);
                    if (managerField != null && store.storeClientManager != null)
                        AddRoot(store.storeClientManager, holder, new List<SnapshotField> { managerField });
                }
            }
            AddSingleton(StoreStation.Instance);
            AddSingleton(EmporiumEntry.Instance);
        }

        private void AddSingleton(object o)
        {
            if (o == null)
                return;
            SnapshotField holder = FindStaticHolder(o);
            if (holder != null)
                AddRoot(o, holder, new List<SnapshotField>());
        }

        private void AddRoot(object root, SnapshotField holder, List<SnapshotField> prefix)
        {
            if (!knownPaths.ContainsKey(root))
                knownPaths[root] = new KeyValuePair<SnapshotField, List<SnapshotField>>(holder, prefix);
            foreach (SnapshotField f in PathFields(root.GetType()))
            {
                object v;
                try
                {
                    v = f.GetValue(this, root);
                }
                catch (Exception)
                {
                    continue;
                }
                if (v == null || v is string || v.GetType().IsValueType || v is Delegate || v is GameItem || v is StoreClientInstance)
                    continue;
                if (knownPaths.ContainsKey(v))
                    continue;
                var chain = new List<SnapshotField>(prefix) { f };
                knownPaths[v] = new KeyValuePair<SnapshotField, List<SnapshotField>>(holder, chain);
            }
        }

        // A static field that currently holds the object: a field whose name contains "instance" (singletons and
        // auto-property backing fields), "<>9" (the instance of a compiler-generated lambda class), or any static
        // field for Unity objects.
        private SnapshotField FindStaticHolder(object o)
        {
            Type t = o.GetType();
            bool unity = o is UnityEngine.Object;
            for (Type cur = t; cur != null && cur != typeof(object); cur = cur.BaseType)
            {
                List<SnapshotField> candidates;
                try
                {
                    if (!SnapshotSchema.CanDescribe(cur, Adapters)) break;
                    candidates = StaticCandidates(cur);
                }
                catch (Exception)
                {
                    // 0.8.0: an integration whose module differs from the shipped table (SnapshotSchema.DeclaredFields
                    // throws) has no static holder; writing the object itself then records it as not storable. Up to
                    // 0.7.1 the exception ended the whole save ("snapshot schema is out of date for CorpPresence").
                    break;
                }
                foreach (SnapshotField f in candidates)
                {
                    bool named = f.Name == "<>9" || f.Name.IndexOf("instance", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!named && !unity)
                        continue;
                    object v;
                    try
                    {
                        v = f.GetValue(this, null);
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                    if (ReferenceEquals(v, o))
                        return f;
                }
            }
            return null;
        }

        private List<SnapshotField> StaticCandidates(Type declaring)
        {
            List<SnapshotField> candidates;
            if (staticHolders.TryGetValue(declaring, out candidates))
                return candidates;
            candidates = new List<SnapshotField>();
            foreach (SnapshotField f in SnapshotSchema.DeclaredFields(declaring, Adapters))
            {
                if (!f.IsStatic || f.IsValueType || f.FieldTypeName == "System.String")
                    continue;
                candidates.Add(f);
            }
            staticHolders[declaring] = candidates;
            return candidates;
        }

        internal bool TryFindPath(object o, out SnapshotField holder, out List<SnapshotField> chain)
        {
            KeyValuePair<SnapshotField, List<SnapshotField>> known;
            if (knownPaths.TryGetValue(o, out known))
            {
                holder = known.Key;
                chain = known.Value;
                return true;
            }
            holder = FindStaticHolder(o);
            chain = new List<SnapshotField>();
            return holder != null;
        }

        internal bool SkipField(SnapshotField f)
        {
            return false;
        }

        // Every item in the inventories that the game saves, plus the front counter (stored by this mod). Unique IDs
        // are kept by the game's item encoding, so these items are found again by ID after loading.
        private void CollectItems()
        {
            EmporiumEntry entry = EmporiumEntry.Instance;
            if (entry == null)
                return;
            var seen = new HashSet<object>(ReferenceComparer.Instance);
            try
            {
                foreach (GameItem item in entry.GetAllItems())
                    Walk(item, seen);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "listing the store items failed: " + e.Message);
            }
            Walk(entry.soldElement, seen);
            Walk(entry.hirelingInv, seen);
            Walk(entry.cassettePlayerElement, seen);
            Walk(entry.trashcan, seen);
            Walk(entry.dossier, seen);
            Walk(entry.frontInvinvElement, seen);
            Walk(entry.backInvinvElement, seen);
        }

        private void Walk(GraphNodeStorage node, HashSet<object> seen)
        {
            if (node == null)
                return;
            var stack = new Stack<GraphNodeStorage>();
            stack.Push(node);
            while (stack.Count > 0)
            {
                GraphNodeStorage cur = stack.Pop();
                if (cur == null || !seen.Add(cur))
                    continue;
                var item = cur as GameItem;
                if (item != null)
                {
                    placedItems.Add(item);
                    int uid = item.uniqueId;
                    if (uid != 0)
                    {
                        GameItem other;
                        if (itemsByUid.TryGetValue(uid, out other) && !ReferenceEquals(other, item))
                            sharedUids.Add(uid);
                        else
                            itemsByUid[uid] = item;
                    }
                }
                List<GraphNodeStorage> children;
                try
                {
                    children = cur.children;
                }
                catch (Exception)
                {
                    continue;
                }
                if (children == null)
                    continue;
                foreach (GraphNodeStorage child in children)
                    stack.Push(child);
            }
        }

        // An item placed in a saved inventory is written as its unique ID. An item that has no ID yet gets one from
        // the game (PlayerStore.currentUniqueId is saved), so this happens before the slot file is written.
        internal bool TryItemReference(GameItem item, out int uid)
        {
            uid = 0;
            if (!placedItems.Contains(item))
                return false;
            bool hadId = item.uniqueId != 0;
            uid = item.GetUniqueID();
            if (hadId && sharedUids.Contains(uid))
                return false;
            if (!hadId)
                itemsByUid[uid] = item;
            return true;
        }

        internal bool TryEncodeItem(GameItem item, out string json)
        {
            json = null;
            try
            {
                var state = new SaveState { saveItems = SaveManager.EncodeNodes(item) };
                json = UnityEngine.JsonUtility.ToJson(state);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "encoding the item '" + item.identifier + "' failed: " + e.Message);
            }
            return !string.IsNullOrEmpty(json);
        }

        internal GameItem FindItem(int uid)
        {
            if (sharedUids.Contains(uid))
                return null;
            GameItem item;
            return itemsByUid.TryGetValue(uid, out item) ? item : null;
        }

        internal GameItem DecodeItem(string json)
        {
            try
            {
                SaveState state = UnityEngine.JsonUtility.FromJson<SaveState>(json);
                return state != null && state.saveItems != null && state.saveItems.Count > 0 ? SaveManager.DecodeNodes(state.saveItems) : null;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "decoding a stored item failed: " + e.Message);
                return null;
            }
        }

        internal bool IsAsset(UnityEngine.Object o)
        {
            return o is UnityEngine.Sprite || o is UnityEngine.Texture || o is UnityEngine.AudioClip || o is UnityEngine.Material
                || o is UnityEngine.Font || o is UnityEngine.ScriptableObject;
        }

        internal UnityEngine.Object FindAsset(Type t, string name)
        {
            foreach (UnityEngine.Object o in UnityEngine.Resources.FindObjectsOfTypeAll(t))
            {
                if (o != null && o.name == name)
                    return o;
            }
            return null;
        }
    }
}
