using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace Moneylending
{
    // Compatibility with F5 Quick Save Plus (manifest ID F5SnapshotSave), spec v1. F5 Quick Save Plus writes the customers
    // in the shop into the save together with everything attached to them, and rebuilds them on load. For the types, fields
    // and methods of another mod it needs that mod's help: the game's ModReflect lets a mod reach the types of another mod
    // only when that mod is one of its running prerequisites (ModReflect.InScope), and F5 Quick Save Plus does not list this
    // one. So this mod hands it an adapter:
    // - the field table of this assembly (Data/f5-snapshot-fields.json, which build.ps1 writes from the built DLL); F5
    //   Quick Save Plus uses it only when the module version id in it is the one of the DLL the game loaded;
    // - functions written here for every access to this mod's fields, types and methods, in which ModReflect checks access
    //   for this mod (ModLoader.CallingModId takes the first frame of a mod on the call stack, the function's own frame);
    // - keys for the ledger records and borrower profiles the visitors capture (Key, Find), so that they are found again
    //   in the ledger instead of being copied.
    // The message and the adapter carry only types of the game and of .NET, so neither mod references the other's assembly
    // and neither is a prerequisite of the other. Without F5 Quick Save Plus the handler runs only for a message another
    // mod sends under its ID, and FromPlus returns false for it.
    internal static class F5SnapshotSpec
    {
        private const string PlusId = "F5SnapshotSave";

        // ModHelper.SendMessage calls the handlers registered under "<sender ID>:<channel>"; F5 Quick Save Plus sends on its
        // channel "snapshot.v1.adapters".
        private const string Channel = PlusId + ":snapshot.v1.adapters";

        // Names and flags of the fields of every type in this assembly, with the module version id of the DLL they were
        // read from (F5SnapshotSave/tools/generate_spec_fields.ps1). A mod cannot list the fields of its own
        // compiler-generated types (the closures of the visitors' dialogues) at run time without the reflection-external
        // capability, so the table is made at build time.
        private const string FieldTable = "Data/f5-snapshot-fields.json";

        private static bool tableProblemLogged;

        // Called from OnEnable, like the hooks: removed first so that a second OnEnable does not subscribe twice.
        internal static void Register()
        {
            ModHelper.OffMessage(MoneylendingMod.ModId, Channel, OnAdapters);
            ModHelper.OnMessage(MoneylendingMod.ModId, Channel, OnAdapters);
        }

        // Called from OnDisable. ModLoader.StopRunningMod also drops every message handler of a stopped mod
        // (ModHelper.ForgetModServices).
        internal static void Unregister()
        {
            ModHelper.OffMessage(MoneylendingMod.ModId, Channel, OnAdapters);
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
                MoneylendingMod.Warn("handing the adapter to F5 Quick Save Plus failed: " + e);
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
        // missing table is passed as null; F5 Quick Save Plus then rejects the adapter, writes the reason into its log and
        // treats the objects of this mod as ones it does not know.
        private static string ReadFieldTable()
        {
            string text = null;
            string problem = null;
            try
            {
                text = ModHelper.ReadModFileText(MoneylendingMod.ModId, FieldTable);
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
                MoneylendingMod.Warn(problem + "; F5 Quick Save Plus cannot use the adapter of this mod");
            }
            return text;
        }

        // The adapter (spec v1, section 6). The functions are methods of this file, so a frame of this assembly is on the
        // call stack whenever F5 Quick Save Plus calls them; a delegate made directly from a ModReflect method would check
        // access for F5 Quick Save Plus instead. A method group makes a new delegate with no target every time.
        private static Dictionary<string, object> Adapter(string fields)
        {
            return new Dictionary<string, object>
            {
                // The version of the spec this adapter follows.
                { "spec", 1 },
                // This mod's manifest ID; F5 Quick Save Plus checks it against ModLoader.ModIdOfAssembly of the assembly.
                { "mod", MoneylendingMod.ModId },
                // The assembly whose types the adapter describes.
                { "assembly", Own },
                // The field table as text, or null when it cannot be read.
                { "fields", fields },
                { "get", new Func<object, string, object>(Get) },
                { "set", new Action<object, string, object>(Set) },
                { "getStatic", new Func<Type, string, object>(GetStatic) },
                { "findType", new Func<string, Type>(FindType) },
                { "method", new Func<Type, string, Type[], MethodBase>(Method) },
                { "create", new Func<Type, object>(Create) },
                { "key", new Func<object, string>(Key) },
                { "find", new Func<Type, string, object>(Find) },
                // "saveExtra"/"loadExtra" are left out: the mod keeps its data in the ledger (PlayerStore.modData), which
                // the game saves, and keeps no data about game objects outside them.
            };
        }

        // The functions handle only this assembly's objects and types (spec v1, section 7.1) and refuse anything else with
        // an exception, so a mod that obtains them gets no access beyond what listing this mod as a prerequisite gives. The
        // spec has no function that writes a static field or calls a method.

        // Reads an instance field (declared in the object's type or one of its base types) of an object of this assembly.
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
        // delegates F5 Quick Save Plus rebuilds (the lambdas of the visitors' dialogues, Clients.Leave).
        private static MethodBase Method(Type type, string name, Type[] signature)
        {
            RequireOwnType(type);
            return ModReflect.Target(type, name, signature);
        }

        // Creates an object of a type of this assembly with its parameterless constructor; F5 Quick Save Plus uses it for
        // types with readonly instance fields, which ModReflect.Set cannot write.
        private static object Create(Type type)
        {
            RequireOwnType(type);
            return ModReflect.New(type, new object[0]);
        }

        // The visitors of this mod capture ledger records (Loan, Debt, today's Request) and borrower profiles. Written as
        // copies, a restored visitor would change the copy, and Book.Save, which writes the records of Book.S, would not
        // record a repayment, a part payment, a debt payment or an answered request. As keys they are found again on load,
        // so a restored visitor changes the record in the ledger. Loans and debts are keyed by their number while the ledger
        // has a record with it, profiles by their Id. Of the requests only today's borrower's is in the ledger
        // (State.request, one per day, keyed by its day); a request made for an offer at the counter is not in the ledger,
        // gets no key and is copied with its customer, as are the closures of the dialogues and every other object of this
        // mod.
        private static string Key(object o)
        {
            RequireOwnObject(o);
            var loan = o as Loan;
            if (loan != null)
                return Book.FindLoan(loan.id) != null ? Text.N(loan.id) : null;
            var debt = o as Debt;
            if (debt != null)
                return Book.FindDebt(debt.id) != null ? Text.N(debt.id) : null;
            var request = o as Request;
            if (request != null)
                return ReferenceEquals(request, Book.S.request) ? Text.N(request.day) : null;
            var profile = o as Profile;
            if (profile != null)
                return Profiles.Get(profile.Id) != null ? profile.Id : null;
            return null;
        }

        // The live object for a key. F5 Quick Save Plus calls this while it reads a snapshot: on load, in its handler of
        // ModHook.OnGameLoadedLate, after MoneylendingMod.OnGameRead has read the ledger of the loaded save in
        // ModHook.OnGameLoadedEarly; and in the check of a snapshot it has just written.
        // Book.S is not read again after that, so the objects returned here stay the ledger's. Null makes F5 Quick Save Plus
        // fall back for the whole snapshot.
        private static object Find(Type type, string key)
        {
            RequireOwnType(type);
            if (key == null)
                return null;
            if (type == typeof(Profile))
                return Profiles.Get(key);
            int n;
            if (!int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
                return null;
            if (type == typeof(Loan))
                return Book.FindLoan(n);
            if (type == typeof(Debt))
                return Book.FindDebt(n);
            if (type == typeof(Request))
            {
                Request today = Book.S.request;
                return today != null && today.day == n ? today : null;
            }
            return null;
        }

        private static Assembly Own
        {
            get { return typeof(F5SnapshotSpec).Assembly; }
        }

        private static void RequireOwnObject(object o)
        {
            if (o == null || o.GetType().Assembly != Own)
                throw new ArgumentException("not an object of " + MoneylendingMod.ModId + ": " + (o == null ? "null" : o.GetType().FullName));
        }

        private static void RequireOwnType(Type type)
        {
            if (type == null || type.Assembly != Own)
                throw new ArgumentException("not a type of " + MoneylendingMod.ModId + ": " + (type == null ? "null" : type.FullName));
        }
    }
}
