using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace GunsmithExpansion
{
    // Compatibility with F5 Quick Save Plus (manifest ID F5SnapshotSave), spec v1. F5 Quick Save Plus writes the customers
    // in the shop into the save together with everything attached to them, and rebuilds them on load. For the types, fields
    // and methods of another mod it needs that mod's help: the game's ModReflect lets a mod reach the types of another mod
    // only when that mod is one of its running prerequisites (ModReflect.InScope), and F5 Quick Save Plus does not list this
    // one. So this mod hands it an adapter: the field table of this assembly and functions written here, in which ModReflect
    // checks access for this mod (ModLoader.CallingModId takes the first frame of a mod on the call stack, which is the
    // function's own frame). The message and the adapter carry only types of the game and of .NET, so neither mod
    // references the other's assembly and neither is a prerequisite of the other. Without F5 Quick Save Plus the handler
    // runs only for a message another mod sends under its ID, and FromPlus returns false for it.
    //
    // What of this mod reaches customers: gun orders of its own types (RepairOrders.RepairOrder with its plan and the
    // customer's other guns, Reservations.PickupOrder with its reservation); the objects behind the actions it puts on
    // customers' lines (a contract or reservation offer's choice, the wares sellers bring after their stock, the repair
    // customer's next gun); definitions of its catalog that those reach (Key); and entries of its tables keyed by gun
    // orders of the game's type (SaveExtra). GunsmithExpansion2077 attaches nothing: its assembly holds only its IMod
    // class, and its guns are GunDefs of this mod (AddOn2077.Guns).
    internal static class F5SnapshotSpec
    {
        private const string PlusId = "F5SnapshotSave";

        // ModHelper.SendMessage calls the handlers registered under "<sender ID>:<channel>"; F5 Quick Save Plus sends on its
        // channel "snapshot.v1.adapters".
        private const string Channel = PlusId + ":snapshot.v1.adapters";

        // Names and flags of the fields of every type in this assembly, with the module version id of the DLL they were
        // read from, written by build.ps1 from the built DLL (F5SnapshotSave/tools/generate_spec_fields.ps1). A mod cannot
        // list the fields of its own compiler-generated types at run time without the reflection-external capability, so
        // the table is made at build time.
        private const string FieldTable = "Data/f5-snapshot-fields.json";

        // Tags of the entries of a gun order's extra data (SaveExtra), named after the table each one belongs to.
        private const string PlanTag = "ModOrders.plans";
        private const string CourierTag = "Contracts.couriers";
        private const string CollectionTag = "Collection.orders";

        private static bool tableProblemLogged;

        // Called from OnEnable. The game removes the subscription itself when the mod is stopped (ModLoader.StopRunningMod
        // calls ModHelper.ForgetModServices, which drops every message handler of the mod), so OnDisable has nothing to undo.
        internal static void Register()
        {
            try
            {
                ModHelper.OnMessage(GunsmithMod.ModId, Channel, OnAdapters);
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("subscribing to F5 Quick Save Plus failed: " + e);
            }
        }

        // F5 Quick Save Plus sends the message for each save it makes, before it reads a snapshot and before it gives extra
        // data back; the payload is a registration function it makes for that one collection. During its Init and OnEnable
        // a mod can send as any mod ID that no loaded mod has yet (ModHelper.RequireCaller), so a message on this channel
        // can also come from a mod that poses as F5 Quick Save Plus. The adapter therefore goes only to a single function
        // written in F5 Quick Save Plus's assembly, and every call gets a new dictionary.
        private static void OnAdapters(string sender, object payload)
        {
            try
            {
                var register = payload as Action<object>;
                if (!FromPlus(register))
                    return;
                register(Adapter(ReadFieldTable()));
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("handing the adapter to F5 Quick Save Plus failed: " + e);
            }
        }

        // Delegate.Method is the method of the last entry of a multicast delegate's invocation list, so the list is checked
        // first. A delegate over a dynamic method has no declaring type. The Invoke method of a delegate type declared in
        // F5 Quick Save Plus would run whatever another mod put into that delegate, so the function must be a method of
        // an ordinary type, and its target must not be a delegate.
        private static bool FromPlus(Action<object> register)
        {
            if (register == null || register.GetInvocationList().Length != 1 || register.Target is Delegate)
                return false;
            MethodInfo method = register.Method;
            return method != null && method.DeclaringType != null
                && !typeof(Delegate).IsAssignableFrom(method.DeclaringType)
                && ModLoader.ModIdOfAssembly(method.DeclaringType.Assembly) == PlusId;
        }

        // Read when the message arrives: the game finds a mod's folder only after the mod's Init and OnEnable have returned
        // (ModHelper.GetModFolder looks in the list of loaded mods), so ModHelper.ReadModFileText fails during them. A
        // missing table is passed as null; F5 Quick Save Plus then rejects the adapter and writes the reason into its log.
        private static string ReadFieldTable()
        {
            string text = null;
            string problem = null;
            try
            {
                text = ModHelper.ReadModFileText(GunsmithMod.ModId, FieldTable);
                if (text == null)
                    problem = FieldTable + " was not found";
            }
            catch (Exception e)
            {
                problem = "reading " + FieldTable + " failed: " + e.Message;
            }
            if (problem != null && !tableProblemLogged)
            {
                tableProblemLogged = true;
                GunsmithMod.Warn(problem + "; F5 Quick Save Plus cannot use the adapter of this mod");
            }
            return text;
        }

        // The adapter (spec v1, section 6). The functions are methods of this file, so a frame of this assembly is on the
        // call stack whenever F5 Quick Save Plus calls them, and ModReflect checks access for this mod; for a delegate made
        // directly from a ModReflect method it would check access for F5 Quick Save Plus. A method group makes a new
        // delegate with no target every time.
        private static Dictionary<string, object> Adapter(string fields)
        {
            return new Dictionary<string, object>
            {
                // The version of the spec this adapter follows.
                { "spec", 1 },
                // This mod's manifest ID; F5 Quick Save Plus checks it against ModLoader.ModIdOfAssembly of the assembly.
                { "mod", GunsmithMod.ModId },
                // The assembly whose types the adapter describes (VVittgenstein.GunsmithExpansion, shipped as GunsmithExpansion.dll).
                { "assembly", Own },
                // The field table as text, or null when it cannot be read. F5 Quick Save Plus uses the adapter only when
                // the module version id in the table equals the one of the assembly the game loaded.
                { "fields", fields },
                { "get", new Func<object, string, object>(Get) },
                { "set", new Action<object, string, object>(Set) },
                { "getStatic", new Func<Type, string, object>(GetStatic) },
                { "findType", new Func<string, Type>(FindType) },
                { "method", new Func<Type, string, Type[], MethodBase>(Method) },
                { "create", new Func<Type, object>(Create) },
                { "key", new Func<object, string>(Key) },
                { "find", new Func<Type, string, object>(Find) },
                { "saveExtra", new Func<object, object>(SaveExtra) },
                { "loadExtra", new Action<object, object>(LoadExtra) },
            };
        }

        // The functions handle only this assembly's objects and types (spec v1, section 7.1) and refuse anything else with
        // an exception, so a mod that obtains them gets no access beyond what listing this mod as a prerequisite gives; in
        // particular nothing of CorpPresence, which is this mod's prerequisite and so in the scope ModReflect checks here.
        // The spec has no function that writes a static field or calls a method. SaveExtra and LoadExtra take the game's
        // objects and touch only the tables named there.

        // Reads an instance field (declared in the object's type or one of its base types, as GunOrderData's fields of a
        // RepairOrder) of an object of this assembly.
        private static object Get(object o, string name)
        {
            RequireOwnObject(o);
            return ModReflect.Get(o, name);
        }

        // Writes an instance field of an object of this assembly.
        private static void Set(object o, string name, object value)
        {
            RequireOwnObject(o);
            ModReflect.Set(o, name, value);
        }

        // Reads a static field of a type of this assembly (for example the "<>9" instance of a compiler-generated lambda
        // class, which F5 Quick Save Plus finds again by that field).
        private static object GetStatic(Type type, string name)
        {
            RequireOwnType(type);
            return ModReflect.GetStatic(type, name);
        }

        // Finds a type of this assembly by its full name (a nested type as Outer+Inner); null for any other name.
        // ModReflect.FindType looks in the game's assemblies before this mod's and also returns engine types of the UI
        // namespaces, so its result is checked.
        private static Type FindType(string fullName)
        {
            Type type = ModReflect.FindType(fullName);
            return type != null && type.Assembly == Own ? type : null;
        }

        // Finds a method of a type of this assembly by name and parameter types (null for a generic method), for the
        // delegates F5 Quick Save Plus rebuilds.
        private static MethodBase Method(Type type, string name, Type[] signature)
        {
            RequireOwnType(type);
            return ModReflect.Target(type, name, signature);
        }

        // Creates an object of a type of this assembly with its parameterless constructor; F5 Quick Save Plus uses it for
        // types with readonly instance fields, which ModReflect.Set cannot write (RepairOrders.Party, whose list of the
        // customer's orders it then fills).
        private static object Create(Type type)
        {
            RequireOwnType(type);
            return ModReflect.New(type, new object[0]);
        }

        // Keys (spec v1, section 8): F5 Quick Save Plus writes only the key of a definition of the catalog that customers
        // reach, and on load takes the live definition for it. These are GunDef (the gun of a repair or modification order's
        // plan, the gun of a reservation offer's choice, the gun a thief, petty thief or scavenger brings after his stock),
        // Ammo.Family (the rounds the lower level gunsmith and thieves bring) and Reloader.Die (the die the retired gunsmith
        // brings). GunDef has readonly fields and no parameterless constructor, so F5 Quick Save Plus could not make a copy;
        // the 2077 Add-On's guns are GunDefs of AddOn2077.Guns, a list outside the static arrays. The key is the definition's
        // id, and it is given only for the object the same lookup finds again, so a definition outside the catalog is never
        // replaced by another one with its id. F5 Quick Save Plus copies the other objects of this mod (null).
        private static string Key(object o)
        {
            RequireOwnObject(o);
            var gun = o as GunDef;
            if (gun != null)
                return ReferenceEquals(RepairOrders.Find(gun.Id), gun) ? gun.Id : null;
            var family = o as Ammo.Family;
            if (family != null)
                return ReferenceEquals(FindFamily(family.Id), family) ? family.Id : null;
            var die = o as Reloader.Die;
            if (die != null)
                return ReferenceEquals(FindDie(die.Id), die) ? die.Id : null;
            return null;
        }

        // The live definition for a key, or null; F5 Quick Save Plus then falls back to the legacy queue. Guns are looked up
        // as this mod looks them up by id (RepairOrders.Find: the catalog, then the 2077 Add-On's guns read at start-up).
        private static object Find(Type type, string key)
        {
            RequireOwnType(type);
            if (key == null)
                return null;
            if (type == typeof(GunDef))
                return RepairOrders.Find(key);
            if (type == typeof(Ammo.Family))
                return FindFamily(key);
            if (type == typeof(Reloader.Die))
                return FindDie(key);
            return null;
        }

        private static Ammo.Family FindFamily(string id)
        {
            foreach (Ammo.Family f in Ammo.Families)
            {
                if (f.Id == id)
                    return f;
            }
            return null;
        }

        private static Reloader.Die FindDie(string id)
        {
            foreach (Reloader.Die d in Reloader.Dies)
            {
                if (d.Id == id)
                    return d;
            }
            return null;
        }

        // Extra data (spec v1, section 9). F5 Quick Save Plus asks for it for every game object its snapshot creates anew
        // on load, and gives it back with the new object at the first shutter update, before any customer is put back. Of the
        // tables of this mod keyed by game objects, three change what a restored customer does:
        //   - ModOrders.plans: when the game makes the service gun at the end of the greeting
        //     (StoreClientListGun.OnOrderGreetingEnd, GunOrderData.CreateServiceGun), ModOrders.OnServiceGunCreated puts the
        //     stat the customer names first; without the plan, a modification order saved before its gun was on the counter
        //     keeps the stat the game drew.
        //   - Contracts.couriers: the courier's pay stays the contract's unit price (ContractRewardPatch), and the delivery
        //     counts for the contract and for the workshop reputation (Contracts.OnOrderAccepted, Workshop.OnOrderAccepted;
        //     Contracts.cs returns early without the entry).
        //   - Collection.orders: the collector pays for the gun as the game's best orders do (CollectionRewardPatch).
        // The others are left out. RepairOrders.planned and Reservations.pickups hold what the orders' own fields hold
        // (RepairOrder.Plan, PickupOrder.Reservation), which the snapshot keeps with the order and which TryPlan and PickupOf
        // read first. NewCustomers.orders is read only while the game's buyer is being made (CustomerTypes.TryRetheme,
        // NewCustomers.Dress). Reservations.pleased is set before GunOrderData.OnOrderAccepted pays and read when the follow-up
        // line is made, both within one NegociationUIManager.OnOfferAcceptClick. Cabinets.states and DeliveryBoxes.insides
        // are keyed by items: SaveManager.DecodeNodes makes a stored item through DirectoryMaster.Item, whose factory
        // (Cabinets.Create, DeliveryBoxes.Create) adds the entry for the new item's own slots; the stored entry would point
        // at the old item's.
        //
        // The value is a list of tag and value pairs. The plan and the courier are this mod's objects, which F5 Quick Save
        // Plus writes like any other (the plan's gun as a key); the collector's theme is one of Collection.Themes, kept as its
        // index so that the same theme comes back. Null when the object has no entry.
        private static object SaveExtra(object owner)
        {
            var order = owner as GunOrderData;
            if (order == null)
                return null;
            List<object> entries = null;
            ModOrders.Plan plan = ModOrders.PlanOf(order);
            if (plan != null)
                AddEntry(ref entries, PlanTag, plan);
            Contracts.Courier courier = Contracts.CourierOf(order);
            if (courier != null)
                AddEntry(ref entries, CourierTag, courier);
            Collection.Theme theme = Collection.ThemeOf(order);
            if (theme != null)
            {
                int index = Array.IndexOf(Collection.Themes, theme);
                if (index < 0)
                    throw new InvalidOperationException("collection order with a theme that is not in Collection.Themes");
                AddEntry(ref entries, CollectionTag, index);
            }
            return entries;
        }

        private static void AddEntry(ref List<object> entries, string tag, object value)
        {
            if (entries == null)
                entries = new List<object>();
            entries.Add(tag);
            entries.Add(value);
        }

        // Puts the entries into this mod's tables for the order F5 Quick Save Plus has just made, and does nothing else: no
        // table is enumerated, and nothing keeps the order or what it reaches outside the tables, which hold their keys weakly
        // (ConditionalWeakTable). Every entry is checked before the first table changes, so a value this version cannot take
        // changes nothing; F5 Quick Save Plus then gives up the snapshot.
        private static void LoadExtra(object owner, object value)
        {
            var order = owner as GunOrderData;
            var entries = value as IList;
            if (order == null || entries == null || entries.Count == 0 || entries.Count % 2 != 0)
                throw new ArgumentException("extra data of " + GunsmithMod.ModId + " takes a gun order and tag and value pairs");
            ModOrders.Plan plan = null;
            Contracts.Courier courier = null;
            Collection.Theme theme = null;
            for (int i = 0; i < entries.Count; i += 2)
            {
                string tag = entries[i] as string;
                object entry = entries[i + 1];
                if (tag == PlanTag && plan == null && entry is ModOrders.Plan)
                    plan = (ModOrders.Plan)entry;
                else if (tag == CourierTag && courier == null && entry is Contracts.Courier)
                    courier = (Contracts.Courier)entry;
                else if (tag == CollectionTag && theme == null && entry is int && (int)entry >= 0 && (int)entry < Collection.Themes.Length)
                    theme = Collection.Themes[(int)entry];
                else
                    throw new ArgumentException("extra data of " + GunsmithMod.ModId + " has an entry it cannot take: " + (tag ?? "no tag"));
            }
            if (plan != null)
                ModOrders.PutPlan(order, plan);
            if (courier != null)
                Contracts.PutCourier(order, courier);
            if (theme != null)
                Collection.PutTheme(order, theme);
        }

        private static Assembly Own
        {
            get { return typeof(F5SnapshotSpec).Assembly; }
        }

        private static void RequireOwnObject(object o)
        {
            if (o == null || o.GetType().Assembly != Own)
                throw new ArgumentException("not an object of " + GunsmithMod.ModId + ": " + (o == null ? "null" : o.GetType().FullName));
        }

        private static void RequireOwnType(Type type)
        {
            if (type == null || type.Assembly != Own)
                throw new ArgumentException("not a type of " + GunsmithMod.ModId + ": " + (type == null ? "null" : type.FullName));
        }
    }
}
