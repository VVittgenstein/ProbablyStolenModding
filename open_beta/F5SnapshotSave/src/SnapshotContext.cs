using System;
using System.Collections.Generic;
using System.Reflection;

namespace F5SnapshotSave
{
    // What the graph codec needs from the game: objects that are referenced instead of copied (the store singletons,
    // their fields, and objects held in a static "instance" field), items found again by their unique ID, items stored
    // with the game's own item encoding, and assets found by name.
    internal sealed class SnapshotContext
    {
        private readonly Dictionary<object, KeyValuePair<SnapshotField, List<SnapshotField>>> knownPaths =
            new Dictionary<object, KeyValuePair<SnapshotField, List<SnapshotField>>>(ReferenceComparer.Instance);
        private readonly HashSet<object> placedItems = new HashSet<object>(ReferenceComparer.Instance);
        private readonly Dictionary<int, GameItem> itemsByUid = new Dictionary<int, GameItem>();
        private readonly HashSet<int> sharedUids = new HashSet<int>();
        private static readonly Dictionary<Type, List<SnapshotField>> staticHolders = new Dictionary<Type, List<SnapshotField>>();

        internal static SnapshotContext Create()
        {
            var ctx = new SnapshotContext();
            ctx.AddStoreRoots();
            ctx.CollectItems();
            return ctx;
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
                    SnapshotField managerField = SnapshotSchema.Field(typeof(PlayerStore), "storeClientManager");
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
            foreach (SnapshotField f in Codec.InstanceFields(root.GetType()))
            {
                object v;
                try
                {
                    v = f.GetValue(root);
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
        private static SnapshotField FindStaticHolder(object o)
        {
            Type t = o.GetType();
            bool unity = o is UnityEngine.Object;
            for (Type cur = t; cur != null && cur != typeof(object); cur = cur.BaseType)
            {
                if (!SnapshotSchema.CanDescribe(cur)) break;
                List<SnapshotField> candidates;
                if (!staticHolders.TryGetValue(cur, out candidates))
                {
                    candidates = new List<SnapshotField>();
                    foreach (SnapshotField f in SnapshotSchema.DeclaredFields(cur))
                    {
                        if (!f.IsStatic || f.IsValueType || f.FieldTypeName == "System.String")
                            continue;
                        candidates.Add(f);
                    }
                    staticHolders[cur] = candidates;
                }
                foreach (SnapshotField f in candidates)
                {
                    bool named = f.Name == "<>9" || f.Name.IndexOf("instance", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!named && !unity)
                        continue;
                    object v;
                    try
                    {
                        v = f.GetValue(null);
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
