using System;
using System.Collections;
using System.Collections.Generic;

namespace F5SnapshotSave
{
    // One step of the path to an object that is found again after loading instead of being copied:
    // 'f' the value of a field, 'k' the value under a string key of a Dictionary, 'i' an element of a List,
    // 'a' an element of an array.
    internal struct PathStep
    {
        internal char Kind;
        internal SnapshotField Field;
        internal string Key;
        internal int Index;

        internal static PathStep OfField(SnapshotField field) { return new PathStep { Kind = 'f', Field = field }; }
        internal static PathStep OfKey(string key) { return new PathStep { Kind = 'k', Key = key }; }
        internal static PathStep OfIndex(char kind, int index) { return new PathStep { Kind = kind, Index = index }; }

        // Keys are written inside a path whose steps are separated by '.', in a record whose fields are separated
        // by tabs.
        internal static string EscapeKey(string key)
        {
            var sb = new System.Text.StringBuilder(key.Length + 4);
            foreach (char c in key)
            {
                switch (c)
                {
                    case '%': sb.Append("%25"); break;
                    case '.': sb.Append("%2E"); break;
                    case '\t': sb.Append("%09"); break;
                    case '\n': sb.Append("%0A"); break;
                    case '\r': sb.Append("%0D"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        internal static string UnescapeKey(string text)
        {
            if (text.IndexOf('%') < 0)
                return text;
            var sb = new System.Text.StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '%' && i + 2 < text.Length)
                {
                    sb.Append((char)Convert.ToInt32(text.Substring(i + 1, 2), 16));
                    i += 2;
                }
                else
                    sb.Append(text[i]);
            }
            return sb.ToString();
        }
    }

    // The workshop mod CorpPresence (公司增强, a declared integration of this mod) keeps its own state in
    // PlayerStore.modData and gives its visitors objects that are only valid for the state it has loaded:
    //   - CorpState.Current and Malice.MaliceState.Current and the objects inside them (the state of a character, of
    //     the envoy, of the rebels, ...). A visitor has to point at the objects CorpPresence itself uses, so these
    //     are written as paths from the two static fields and found again after loading, when CorpPresence has read
    //     its state from the save. The character definitions in Engine.All are written the same way, by index.
    //   - StoreSession, a struct holding the store, the state and a counter of CorpPresence. The actions of a visitor
    //     check with it that they still belong to the running game. It is written as "the current session" and
    //     taken again from CorpPresence after loading (StoreSession.Capture).
    //   - CharacterVisitSession and Choice, classes whose readonly fields are set by their constructors. They are
    //     created again through those constructors.
    //   - Portraits. CorpPresence adds the portrait of a visitor to the sprite dictionary of the running store when it
    //     creates the visitor (Portraits.Ensure(key, reference key)) and keeps the pair in Portraits.Known. A visitor
    //     rebuilt from a snapshot was not created in the store that is running after loading, so the pairs of the
    //     visitors of a snapshot are stored with it and given to Portraits.Ensure again before the visitors are put
    //     back. Without this the game shows a random portrait for a key it does not know (SpriteDict.GetSprite).
    // Up to 0.6.0 as published on 2026-11-10 the state objects were copied, which ended at the WeakReference fields of
    // CorpPresence's SaveBinding ("unsupported type"), so that F5 was refused while such a visitor was at the counter
    // or waiting in line.
    // CorpPresence is reached through the game's ModReflect only. Nothing here runs when CorpPresence is not running.
    internal static class CorpLink
    {
        private const string AssemblyName = "CorpPresence";
        private const string SessionType = "CorpPresence.StoreSession";
        private const string PortraitsType = "CorpPresence.Portraits";
        private const int MaxDepth = 8;
        private const int MaxObjects = 5000;

        // The static fields holding the state, and the method with which CorpPresence loads the state of the running
        // store when the field does not belong to it.
        private static readonly string[][] StateRoots =
        {
            new[] { "CorpPresence.CorpState", "<Current>k__BackingField", "Get" },
            new[] { "CorpPresence.Malice.MaliceState", "Current", "Get" },
        };

        // Static arrays of definitions that exist once per game process.
        private static readonly string[][] DefinitionArrays =
        {
            new[] { "CorpPresence.Engine", "All" },
        };

        // Classes whose readonly fields are set by a constructor: the fields passed to it, in the order of its
        // parameters. The other readonly fields are set by the constructor from CorpPresence's current state and are
        // compared with the stored values afterwards (SnapshotField.SetValue).
        private static readonly Dictionary<string, string[]> Constructors = new Dictionary<string, string[]>
        {
            { "CorpPresence.CharacterVisitSession", new[] { "_id", "_state", "_client" } },
            { "CorpPresence.Choice", new[] { "<Label>k__BackingField", "<Reply>k__BackingField", "<Then>k__BackingField" } },
        };

        private static readonly Dictionary<Type, string[]> constructorCache = new Dictionary<Type, string[]>();
        private static readonly Dictionary<Type, bool> sessionCache = new Dictionary<Type, bool>();
        private static bool problemLogged;

        private static bool IsCorpType(Type t)
        {
            return t.Assembly.GetName().Name == AssemblyName;
        }

        internal static bool IsSession(Type t)
        {
            bool result;
            if (sessionCache.TryGetValue(t, out result))
                return result;
            result = t.IsValueType && t.FullName == SessionType && IsCorpType(t);
            sessionCache[t] = result;
            return result;
        }

        // The static fields of StateRoots. CorpPresence sets them to null when PlayerStore.LoadGame returns
        // (Hooks.AfterLoadGame calls Hooks.ResetState) and loads the state again at its next use.
        internal static bool IsStateRoot(SnapshotField field)
        {
            if (field == null || !field.IsStatic || !IsCorpType(field.DeclaringType))
                return false;
            foreach (string[] root in StateRoots)
                if (field.DeclaringType.FullName == root[0] && field.Name == root[1])
                    return true;
            return false;
        }

        internal static string[] ConstructorFields(Type t)
        {
            string[] result;
            if (constructorCache.TryGetValue(t, out result))
                return result;
            result = null;
            if (t.FullName != null && Constructors.TryGetValue(t.FullName, out result) && !IsCorpType(t))
                result = null;
            constructorCache[t] = result;
            return result;
        }

        // Called for every snapshot that is written or read. Lets CorpPresence load the state of the running store,
        // as it does itself before every use, then registers the state objects and the definitions with their paths.
        internal static void Prepare(SnapshotContext ctx)
        {
            try
            {
                foreach (string[] root in StateRoots)
                {
                    Type type = ModReflect.FindType(root[0]);
                    if (type == null || !IsCorpType(type))
                        continue;
                    ModReflect.CallStatic(type, root[2], new object[0]);
                    SnapshotField holder = SnapshotSchema.Field(type, root[1]);
                    if (holder == null || !holder.IsStatic)
                        throw new NotSupportedException(root[0] + "." + root[1] + " is not in the snapshot schema");
                    object state = holder.GetValue(null);
                    if (state != null)
                        Walk(ctx, state, holder);
                }
                foreach (string[] array in DefinitionArrays)
                {
                    Type type = ModReflect.FindType(array[0]);
                    if (type == null || !IsCorpType(type))
                        continue;
                    SnapshotField holder = SnapshotSchema.Field(type, array[1]);
                    if (holder == null || !holder.IsStatic)
                        throw new NotSupportedException(array[0] + "." + array[1] + " is not in the snapshot schema");
                    var elements = holder.GetValue(null) as Array;
                    if (elements == null || elements.Rank != 1)
                        continue;
                    ctx.AddModPath(elements, holder, new PathStep[0]);
                    for (int i = 0; i < elements.Length; i++)
                    {
                        object element = elements.GetValue(i);
                        if (element != null && !element.GetType().IsValueType && !(element is string))
                            ctx.AddModPath(element, holder, new[] { PathStep.OfIndex('a', i) });
                    }
                }
            }
            catch (Exception e)
            {
                // Without the paths a CorpPresence visitor cannot be stored and F5 is refused while one is present,
                // as before; everything else is unaffected.
                if (!problemLogged)
                {
                    problemLogged = true;
                    UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "the state of CorpPresence could not be linked; its visitors cannot be stored: " + e.Message);
                }
            }
        }

        // Breadth first, so that an object reachable in several ways gets its shortest path.
        private static void Walk(SnapshotContext ctx, object root, SnapshotField holder)
        {
            var queue = new Queue<KeyValuePair<object, PathStep[]>>();
            queue.Enqueue(new KeyValuePair<object, PathStep[]>(root, new PathStep[0]));
            int count = 0;
            while (queue.Count > 0 && count < MaxObjects)
            {
                KeyValuePair<object, PathStep[]> cur = queue.Dequeue();
                object o = cur.Key;
                if (!ctx.AddModPath(o, holder, cur.Value))
                    continue;
                count++;
                if (cur.Value.Length >= MaxDepth)
                    continue;
                Type t = o.GetType();
                if (t.IsArray)
                {
                    var array = (Array)o;
                    if (array.Rank != 1 || t.GetElementType().IsValueType || t.GetElementType() == typeof(string))
                        continue;
                    for (int i = 0; i < array.Length; i++)
                        Enqueue(queue, array.GetValue(i), cur.Value, PathStep.OfIndex('a', i));
                    continue;
                }
                char kind = Codec.CollectionKind(t);
                if (kind == 'l')
                {
                    var list = (IList)o;
                    for (int i = 0; i < list.Count; i++)
                        Enqueue(queue, list[i], cur.Value, PathStep.OfIndex('i', i));
                    continue;
                }
                if (kind == 'd')
                {
                    if (t.GetGenericArguments()[0] != typeof(string))
                        continue;
                    foreach (DictionaryEntry entry in (IDictionary)o)
                        Enqueue(queue, entry.Value, cur.Value, PathStep.OfKey((string)entry.Key));
                    continue;
                }
                if (kind != '\0' || !IsCorpType(t))
                    continue;
                List<SnapshotField> fields;
                try
                {
                    fields = Codec.InstanceFields(t);
                }
                catch (NotSupportedException)
                {
                    continue;
                }
                foreach (SnapshotField f in fields)
                {
                    object v;
                    try
                    {
                        v = f.GetValue(o);
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                    Enqueue(queue, v, cur.Value, PathStep.OfField(f));
                }
            }
        }

        // State objects of CorpPresence and the collections that hold them; everything else in the state is data
        // that is copied with a visitor like any other value.
        private static void Enqueue(Queue<KeyValuePair<object, PathStep[]>> queue, object v, PathStep[] prefix, PathStep step)
        {
            if (v == null)
                return;
            Type t = v.GetType();
            if (t.IsValueType || v is string || v is Delegate || v is GameItem || v is StoreClient || v is StoreClientInstance || v is UnityEngine.Object)
                return;
            if (!t.IsArray && Codec.CollectionKind(t) == '\0' && !IsCorpType(t))
                return;
            var steps = new PathStep[prefix.Length + 1];
            Array.Copy(prefix, steps, prefix.Length);
            steps[prefix.Length] = step;
            queue.Enqueue(new KeyValuePair<object, PathStep[]>(v, steps));
        }

        // The portrait keys of these customers that CorpPresence has registered, each followed by its reference key.
        internal static List<string> PortraitPairs(IEnumerable<StoreClient> clients)
        {
            var pairs = new List<string>();
            try
            {
                Type type = ModReflect.FindType(PortraitsType);
                if (type == null || !IsCorpType(type))
                    return pairs;
                var known = ModReflect.GetStatic(type, "Known") as Dictionary<string, string>;
                if (known == null)
                    return pairs;
                var seen = new HashSet<string>();
                foreach (StoreClient client in clients)
                {
                    if (client == null || string.IsNullOrEmpty(client.spriteName) || !seen.Add(client.spriteName))
                        continue;
                    string reference;
                    if (!known.TryGetValue(client.spriteName, out reference))
                        continue;
                    pairs.Add(client.spriteName);
                    pairs.Add(reference ?? "");
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "the portraits of CorpPresence visitors could not be listed: " + e.Message);
            }
            return pairs;
        }

        // Has CorpPresence add the stored portraits to the sprite dictionary of the running store.
        internal static void EnsurePortraits(List<string> pairs)
        {
            if (pairs == null || pairs.Count < 2)
                return;
            try
            {
                Type type = ModReflect.FindType(PortraitsType);
                if (type == null || !IsCorpType(type))
                    return;
                for (int i = 0; i + 1 < pairs.Count; i += 2)
                    ModReflect.CallStatic(type, "Ensure", new object[] { pairs[i], pairs[i + 1] });
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "the portraits of CorpPresence visitors could not be prepared: " + e.Message);
            }
        }

        // Whether the actions of the visitor holding this session would run in the game as it is now.
        internal static bool SessionIsCurrent(object session)
        {
            return (bool)ModReflect.Get(session, "IsCurrent");
        }

        // The session of the running store, as CorpPresence gives it to a visitor it creates now.
        internal static object CaptureSession(Type type)
        {
            return ModReflect.CallStatic(type, "Capture", new object[0]);
        }
    }
}
