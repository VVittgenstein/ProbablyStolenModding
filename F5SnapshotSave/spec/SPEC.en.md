# F5 Quick Save Plus compatibility spec v1

Applies to: F5 Quick Save Plus (manifest ID `F5SnapshotSave`, Steam Workshop [3831061477](https://steamcommunity.com/sharedfiles/filedetails/?id=3831061477)) 0.8.0 and later. Game: the built-in mod system of the released version of Probably Stolen. The statements about game interfaces were checked against Steam buildid 26525552 (game version 0.46D).

## 1. Overview

When the player presses the save key, F5 Quick Save Plus ("Plus" below) writes the customer at the counter, the waiting customers and the chemical feedback customers into the save, object by object and field by field, together with the current conversation, the haggling state and what other mods have added to the customers. After loading, it makes these objects again and puts the customers back where they were. The chemical feedback customers are the customers in the game's chemical feedback list (`StoreClientManager.chemicalFeedbackStack`): customers who come back after a chemical sale with a complaint or to buy again. The game adds customers to this list only when the shop opens (`PlayerStore.OnShutterOpened`), and in the same call it adds every customer in the list to the line and clears the list; so when the save key is pressed, the list is empty unless a mod has added customers to it. When a customer in this list carries something Plus cannot store, the save is refused (section 3).

Of the types of other mods, Plus itself can read and write only those of the mods in its prerequisite list: the game's `ModReflect` lets a mod reach only the types of the game, of itself, of the running mods in its prerequisite list and of the UI namespaces (`ModReflect.InScope`). The game's mod scanner counts "listing the fields of a type obtained at run time" as the `reflection-external` capability, so the field list of a mod has to be made from its DLL at build time.

With this spec, another mod hands Plus its own field table and a set of read and write functions written in that mod. A mod that follows the spec:

- has what it adds to customers saved and loaded by Plus as it is, within the limits this spec describes;
- after an update only makes its field table again at build time; Plus needs no new release;
- needs no reference to Plus, no prerequisite on Plus and no capability. The message and the adapter carry only game and .NET types. Without Plus installed, Plus sends no message; the handler then runs only when another mod sends on this channel under Plus's ID (section 5), and the check of section 5 returns without handing over an adapter.

A mod that adds none of its own objects or code to customers, the conversation or the counter items needs to do nothing.

## 2. Terms

| Term | Meaning |
|---|---|
| What a mod adds to customers | Data and code that another mod attaches to customers. Examples: the order of a gun repair customer (data); code run when a conversation ends or a dialogue choice is clicked (code). This spec deals with the objects, types and methods of that mod's own assembly. |
| Field table | A JSON file listing the fields of every type of one assembly (name, static, readonly, value type, field type) and the module version id of that DLL. |
| Module version id (MVID) | The `ModuleVersionId` of a DLL. It changes whenever the DLL's content changes; with deterministic builds (`-deterministic+`) the same sources and references give the same MVID. |
| Adapter | The dictionary a mod that follows the spec hands to Plus: its field table and a set of functions (sections 6 and 7). |
| Registration function | The `Action<object>` that Plus makes for one collection and sends with the message; a mod calls it to hand over its adapter. |
| Key | A text an adapter gives for an object. Plus records only the type and this text and finds the original object again with it on load (section 8). |
| Extra data | Data that a mod following the spec keeps outside the customer objects, keyed by game objects. Plus writes it with the save and gives it back to that mod after loading (section 9). |
| Snapshot | The record of these objects that Plus writes into the save. |
| Legacy data | The data in the format of F5 Quick Save (the regular version) that Plus writes with every save: the day, the counter items and the line of customers by customer type (ID), the customer at the counter first. |
| Created anew | On load, a customer is made new from their customer type (ID): the conversation starts over, budget and goods are rolled again. The types that can be created are those in the game's customer registry (including types that mods register) and a few that Plus handles itself. |

## 3. Mods that do not follow the spec

While the snapshot is written, some objects may not be storable, for example: a type without a field table; a type whose field table does not match the loaded DLL; a type or method that would not be found on load (for example a delegate to a method of a mod, an enum value or a `Type` value of it); a scene object. Plus handles such an object by the place where it was first reached:

| Place | Handling |
|---|---|
| Waiting customer | This customer is left out of the snapshot and the save goes on. On load the customer is created anew from their customer type and put back in the same place in line; a customer whose type cannot be created anew (section 2) does not come back. The save notice adds "<n> waiting customer(s) carry things an F5 save can't store and will be created anew after loading (those that can't be created won't come back)". |
| Customer at the counter, current conversation, haggling state, features of the counter items | This save is refused; the notice says to press the save key after the customer at the counter has left. |
| Chemical feedback customer | This save is refused; the notice says to press the save key after the shop opens tomorrow. These customers are not in the line of the legacy data and cannot be created anew. |

- An object shared by several customers belongs to the first customer that reached it; after leaving that customer out, Plus writes again until nothing fails.
- After writing, Plus reads the snapshot back once as before; if that fails, it checks the customers one by one and leaves out a waiting customer that cannot be read back too.
- The first time after each game start that a mod causes a customer to be left out or a save to be refused, Plus opens a window after the save notice that names the mod (the `Name` in its manifest) and shows the address of Plus's Workshop page. The mod named is `ModLoader.ModIdOfAssembly` of the assembly of the type that made the object not storable: the object's own type, or for example a base type without a field table, the type that declares a delegate's method, the enum type of an enum value, the type that a `Type` value names, or the type of a collection's comparer; when `saveExtra` throws, the mod of that adapter; when the read-back check fails, the mods of the types that could not be read back.
- `Player.log` has a line for every customer left out (starting with `[F5SnapshotSave]` and containing `left out of the snapshot`) with the place in line, the customer type (ID) and the reason. For a customer left out while the snapshot was written, the reason gives the path and, where known, the type, its assembly and its mod; for a customer left out by the read-back check, the reason gives the errors of reading it back. The mods involved in the save are named in a line containing `could not be stored`.

Up to Plus 0.7.1, each of these cases made the whole save fail.

Also:

- The two integrations in Plus's prerequisite list (Pen's Pipette `Pen_mod_pipette`, Vanilla Gun Expansion `vanilla_gun_expansion`) do not follow the spec. Plus ships their field tables, made from the versions installed when Plus was built; when the player's version differs, the table above applies and the window names them too. CorpPresence (`CorpPresence`) was a third such integration up to Plus 0.7.1; Plus 0.8.0 no longer supports it, and until CorpPresence follows the spec, its objects are handled by the table above like those of any mod that does not follow the spec.
- A mod that hands over an adapter that is not accepted (section 10) is handled like a mod that does not follow the spec.
- When the game's own code differs from the field table Plus ships (after a game update, before Plus is released for the new build), the whole save fails, as in 0.7.1.

## 4. Channel and message

Listen in your mod's `Init` or `OnEnable`:

```csharp
ModHelper.OnMessage("<your mod ID>", "F5SnapshotSave:snapshot.v1.adapters", OnAdapters);

static void OnAdapters(string sender, object payload) { ... }   // Action<string, object>
```

Plus sends with `ModHelper.SendMessage("F5SnapshotSave", "snapshot.v1.adapters", register)`. The game finds the listeners by the key "sender ID:channel" and calls them one after another inside `SendMessage`; an exception thrown by a listener is logged by the game. The payload `register` is an `Action<object>`, the registration function.

Plus sends this message at these moments (each one is called a collection):

1. Each save started with the save key, once it has passed the checks made before the snapshot is written (for example: the player is in the shop, no phone call is running, and the game's code matches the field table Plus ships): one collection; writing the snapshot and the read-back check of the save use the adapters of this collection.
2. On load, before the snapshot is read. The snapshot is read in `ModHook.OnGameLoadedLate`.
3. When the snapshot has extra data: at the first shutter update after loading, before the extra data is given back.

The registration function works only while this one `SendMessage` runs. When `SendMessage` returns, Plus clears what it references, and calling it later does nothing.

A listener can be removed with `ModHelper.OffMessage` where needed; Moneylending calls `OffMessage` before `OnMessage` in `OnEnable` so that it does not listen twice.

## 5. Checking the registration function

During a mod's `Init` and `OnEnable`, the call stack has no frame of a registered mod, and the game then lets code send under any mod ID that no loaded mod has (`ModHelper.RequireCaller`). So before Plus is loaded, another mod can send on this channel under the name `F5SnapshotSave`. Before handing over your adapter, check that the registration function belongs to Plus:

```csharp
var register = payload as Action<object>;
if (register == null || register.GetInvocationList().Length != 1 || register.Target is Delegate)
    return;
MethodInfo method = register.Method;
if (method == null || method.DeclaringType == null
    || typeof(Delegate).IsAssignableFrom(method.DeclaringType)
    || ModLoader.ModIdOfAssembly(method.DeclaringType.Assembly) != "F5SnapshotSave")
    return;
register(BuildAdapter());   // a new dictionary every time
```

- `GetInvocationList().Length != 1`: for a multicast delegate, `Method` returns the method of the last entry of its invocation list.
- `method.DeclaringType == null`: a dynamic method has no declaring type.
- A declaring type that is a delegate type, or a `Target` that is a delegate: the `Invoke` of a delegate type runs whatever was put into that delegate.
- `ModLoader.ModIdOfAssembly(...)`: null for the game's assembly, the manifest ID for a mod's assembly.

## 6. The adapter

After the check, call the registration function with a new `Dictionary<string, object>` (its type must be exactly `Dictionary<string, object>`):

| Key | Value type | Required | Content |
|---|---|---|---|
| `spec` | `int` | yes | 1 |
| `mod` | `string` | yes | your mod's manifest ID; must equal `ModLoader.ModIdOfAssembly(assembly)` |
| `assembly` | `System.Reflection.Assembly` | yes | your assembly (`typeof(SomeTypeOfYours).Assembly`); the game's and Plus's assemblies are refused |
| `fields` | `string` | yes | the text of your field table (section 12); pass null when it cannot be read: Plus then does not accept the adapter and logs the reason |
| `get` | `Func<object, string, object>` | yes | reads an instance field of an object by name |
| `set` | `Action<object, string, object>` | yes | writes an instance field of an object by name |
| `getStatic` | `Func<Type, string, object>` | yes | reads a static field of one of your types by name |
| `findType` | `Func<string, Type>` | yes | finds one of your types by full name |
| `method` | `Func<Type, string, Type[], System.Reflection.MethodBase>` | yes | finds one of your methods by type, name and parameter types |
| `create` | `Func<Type, object>` | yes | creates an object of one of your types with its parameterless constructor |
| `key` | `Func<object, string>` | no, pair with `find` | the key of an object, or null |
| `find` | `Func<Type, string, object>` | no, pair with `key` | finds an object again by type and key |
| `saveExtra` | `Func<object, object>` | no, pair with `loadExtra` | the extra data of a game object, or null |
| `loadExtra` | `Action<object, object>` | no, pair with `saveExtra` | gives extra data back with a newly created game object |

Plus copies these values into its own record at once; changing the dictionary afterwards has no effect. Paired functions are given both or neither. Within one collection, only the first valid adapter of an assembly is accepted.

## 7. Function contracts

### 7.1 All functions

- The delegate type must be exactly the one in the table (for example `Func<object, string, object>`; a custom delegate type is not accepted), and the delegate must be a single delegate.
- The method is written in your assembly: `Method.DeclaringType.Assembly` must be the `assembly` you hand over. Static methods, instance methods and lambdas all work; the method must not be the `Invoke` of a delegate type, and the `Target` must not be a delegate.
- Reason: `ModReflect` called inside the function identifies the caller by the first mod frame on the call stack (`ModLoader.CallingModId`). Only a function written in your mod makes `ModReflect` check access for your mod; a delegate made directly from a `ModReflect` method would be checked for Plus.
- Type restrictions: `get`, `set` and `key` accept only objects whose runtime type belongs to your assembly; `getStatic`, `method`, `create` and `find` accept only types of your assembly; `findType` returns only types of your assembly and null otherwise. Any other input throws an exception. A mod that obtains these functions then gets no access beyond what listing your mod as a prerequisite gives (section 14).
- When `get`, `findType`, `method`, `key` or `saveExtra` throws while the snapshot is written, Plus records the object being written as not storable (section 3), and the window names your mod. When `getStatic` throws while the snapshot is written, Plus skips that static field; an object that no other static field holds is then copied field by field when it can be. An error while a snapshot is read on load makes the whole snapshot fall back to the legacy data; when the read-back check of a save fails, Plus checks the customers one by one, and a customer that cannot be read back is handled as not storable (section 3).
- `get` and `set` are called per object and per field. Within one save the snapshot may be written several times (again after leaving a customer out, and per customer when the read-back check fails), so the same field of the same object may be read several times.

### 7.2 Each function

| Function | When Plus calls it | Contract |
|---|---|---|
| `get(o, name)` | While writing a snapshot, for each instance field of an object whose runtime type belongs to your assembly; while reading a snapshot, also to compare readonly fields with the saved values (section 11) | Returns the field's value. The names come from the field tables and include fields declared in base types (game types, or types of a mod that is your prerequisite). Implement it with `ModReflect.Get(o, name)`. |
| `set(o, name, value)` | While reading a snapshot (on load, and in the read-back check of a save), for each instance field of an object made again, readonly fields excepted; those are read with `get` and compared with the saved value (section 11) | Implement it with `ModReflect.Set(o, name, value)`. |
| `getStatic(type, name)` | While writing a snapshot, to find out whether an object is held in a static field declared in the object's own type or one of its base types (up to the first type without a field table) whose type is a reference type other than `string` and whose name contains `instance` in any letter case or is the `<>9` of a compiler-generated lambda class (for an object derived from `UnityEngine.Object`, any name). When such a field holds the object, Plus records the path; while reading the snapshot (on load, and in the read-back check of a save), it takes the same object again from that field. An object held only in a static field of another type is copied field by field when it can be. | Returns the static field's value. Plus never writes static fields of your types. |
| `findType(fullName)` | While writing a snapshot, Plus runs for every type the same lookup it will run on load; while reading, it resolves the types | Returns your type by full name (nested types as `Outer+Inner`), or null when it is not found or not in your assembly. `ModReflect.FindType` looks in the game's assemblies first and also returns engine types of the UI namespaces, so check the assembly of the result. |
| `method(type, name, signature)` | To make a delegate to one of your methods again (for example the end action of a dialogue line); also looked up once while writing | Returns the method declared by `type` itself (its `DeclaringType` must equal `type`) with the parameter types in `signature`; for a generic method `signature` is null and the generic method definition is returned. Implement it with `ModReflect.Target(type, name, signature)`. |
| `create(type)` | While reading a snapshot (also in the read-back check of a save; those objects are dropped afterwards), for each object copied field by field whose type belongs to your assembly and has a readonly instance field declared in the type itself or in any of its base types, also a base type in another assembly (Plus calls the `create` of the adapter of the object's own assembly) | Creates the object with its parameterless constructor; the returned object's type must be exactly `type`. Implement it with `ModReflect.New(type, new object[0])`. |
| `key(o)` | While writing a snapshot, before each object of your assembly is written (delegates excepted), values of your structs included (boxed) | Returns a key or null. With a key, Plus records only the type and the key and does not copy the object; a struct value with a key is written as a key record of its own at each place it occurs. |
| `find(type, key)` | While reading a snapshot (on load, and in the read-back check of a save) | Returns the existing object for the key; its type must be exactly `type` (for a struct, the value, boxed). Null, an object of another type, or an exception: on load the whole snapshot falls back to the legacy data; while saving, the customer is handled as not storable. |
| `saveExtra(owner)` | While writing a snapshot, for every game object that the snapshot will create anew on load (section 9) | Returns null or a value. |
| `loadExtra(owner, value)` | At the first shutter update after loading, before customers are put back (section 9) | Puts `value` only into a table keyed by `owner` that holds its keys weakly. |

## 8. Keys (`key`, `find`)

An adapter gives keys for objects that have to be found again as the same object, where a copy would not work. Examples: a definition of a catalog (a copy would no longer be the catalog's object); a record of a ledger (a copy would not follow later updates of the ledger); a type with readonly fields and no parameterless constructor; a struct that itself declares a readonly instance field (section 11).

- Plus asks `key` for every object of your assembly that it is about to write (delegates excepted), values of your structs included (boxed). Give a key only for an object that `find` returns again as the same object; return null for other objects, which Plus then copies field by field.
- A struct value has no identity: a value with a key is written as a key record of its own at each place it occurs, and `find` returns a value of exactly that struct type (boxed).
- On load, `find` is called when Plus reads the snapshot, in `ModHook.OnGameLoadedLate`. The objects for the keys must exist by then. The order of the mods' `OnGameLoadedLate` handlers depends on the order in which the mods were loaded; if your mod loads these objects again when a save is loaded, do it in `ModHook.OnGameLoadedEarly`, which the game fires after reading the save file and before `OnGameLoadedLate`. Moneylending reads its ledger there.
- The read-back check of a save also calls `find`; the objects all exist then.

## 9. Extra data (`saveExtra`, `loadExtra`)

Extra data is for data your mod keeps outside the customer objects, keyed by game objects, for example a `ConditionalWeakTable` keyed by the order object `GunOrderData`. On load the snapshot creates these game objects anew, the old keys no longer exist, and the data of the table has to be saved with the snapshot and given back.

- Scope: Plus calls `saveExtra` only for game objects (types of the game assembly `Assembly-CSharp`) that the snapshot creates anew on load: objects copied field by field (for example `GunOrderData`), `StoreClientInstance`, and items written with the game's item encoding. Existing objects found by path, items found by unique ID, assets and similar objects are not included.
- Writing: the value returned by `saveExtra(owner)` is written by the rules of the snapshot and can contain your objects, game objects and keys; shared references and cycles are kept. Plus records the assembly that provides it (with its MVID, as an assembly the snapshot depends on) and the spec version 1. If `saveExtra` throws, the customer that `owner` belongs to is handled as not storable.
- Reading: if the provider is not loaded, its MVID differs from the one at save time, it has no accepted adapter with `loadExtra`, or the spec version is not 1, the whole snapshot falls back to the legacy data.
- Giving back: at the first shutter update after loading, Plus collects the adapters again and first calls `loadExtra(owner, value)` for every record; only then does it put back the customers of the snapshot, restore the counter view and create the customers that were left out. The read-back check of a save never calls `loadExtra`.
- The `loadExtra` contract: put `value` only into a table keyed by `owner` that holds its keys weakly (for example `System.Runtime.CompilerServices.ConditionalWeakTable`); do not keep a strong reference to `owner` or to anything reachable from it; do not enumerate such tables to find objects to use; change nothing else. Reason: when one `loadExtra` throws, or the provider's adapter is missing, Plus gives up the snapshot, creates the customers once from the legacy data and shows the notice "The customers in this save can't be restored as they were". Extra data given back before that stays only in weak-keyed tables under the dropped objects; the game no longer references those objects, and by the contract no code finds them.
- We recommend checking every entry of `value` first and changing the tables only when all of them can be taken; then a value that cannot be taken changes nothing.

## 10. Checks and log lines of Plus

Plus checks every adapter when it receives it:

- `spec` is the int 1; `mod` is not empty; `assembly` is a loaded assembly, neither the game's nor Plus's, and `ModLoader.ModIdOfAssembly(assembly)` equals `mod`.
- Every function has the right type, is a single delegate, has its method declared in `assembly`, is not the `Invoke` of a delegate type and has no delegate as its `Target`; paired functions are both present or both absent.
- `fields` parses as a field table of format 2 with exactly one module, whose name is the name of `assembly` and whose MVID is the one of the `assembly` the game loaded; every type key starts with `<assembly name>:`.

In `Player.log`, each adapter gets one line per game start (starting with `[F5SnapshotSave]`), written at the first collection after the game starts (section 4):

- Accepted: `spec adapter accepted: mod '<ID>', assembly <name>, module <MVID>`, followed by `, keys` and `, extra data` when the adapter has them.
- Not accepted: `spec adapter not accepted (mod '<ID>'): <reason>`; for a field table of another build, for example `the field table belongs to module ..., the loaded ... is ... (made for another build)`.

## 11. How your objects are stored

- The object's type and all its base types (up to `System.Object` or `System.ValueType`) need a field table: your types use your field table, game types the table Plus ships, types of another mod that mod's adapter (or the integration tables Plus ships). If one level has none, the object cannot be stored. A base type in another mod requires that mod to be your prerequisite (otherwise the game's scanner gives the reference a hard flag).
- Field names must be unique along the inheritance chain (a field with the same name as one in a base type cannot be reached by name); otherwise the object cannot be stored.
- Readonly instance fields: an object of your assembly that is copied field by field and whose type or one of its base types declares a readonly instance field is created with `create` (section 7.2), so its constructor runs. Plus never writes a readonly field:
  - In an object of a class, the array, `List<T>`, `HashSet<T>`, `Queue<T>`, `Stack<T>` or `Dictionary<TKey, TValue>` that the constructor put into a readonly field (same type as saved, same array length) is cleared and filled with the saved content.
  - Every other readonly field must hold exactly the saved value after the constructor: an object found by key or by path, an item found by its unique ID or an asset must be the same object; a struct value found by key must have the same type and agree in every field; a copied struct value must have the same type and agree in every stored field; scalars (`bool`, `char`, numbers, pointers, `decimal`, `string`, enum values) must have exactly the same type and an equal value; a `Type` value must be the same runtime type object; null must be null. References agree only by identity; this comparison never calls an `Equals` that your types define.
  - Reading always makes delegates, objects copied field by field, `StringBuilder` objects, `new object()` objects, `StoreClientInstance` objects and items written with the game's item encoding anew. They are never the constructor's object, so a readonly field holding one of them cannot be read back; the same holds for an array or collection that is not null inside a copied struct value.
  - A struct that itself declares a readonly instance field cannot be copied at all: Plus would create it with a constructor without parameters, which structs do not have. Such a struct can be stored only through a key (section 8).
  - When a readonly field does not hold the saved value, the object cannot be read back: on load the whole snapshot falls back to the legacy data; at save time the read-back check finds this and the customer is handled as not storable (a waiting customer is left out of the snapshot; with the customer at the counter or a chemical feedback customer, the save is refused). Such objects can use a key instead (section 8).
- Values that can be stored: null; `bool`, `char`, the integer and floating point types, `decimal`, `string`; enum values; `Type` values; structs with a field table (a struct that itself declares a readonly instance field only through a key that the adapter of its own mod gives) and `ValueTuple`; one-dimensional arrays; `List<T>`, `HashSet<T>`, `Queue<T>`, `Stack<T>`, `Dictionary<TKey, TValue>` (`HashSet` and `Dictionary` only with the default comparer or `StringComparer.Ordinal`, `OrdinalIgnoreCase`, `InvariantCulture` or `InvariantCultureIgnoreCase`); `StringBuilder`; `new object()`; delegates (every method of the invocation list must be found on load: methods of the game, of Plus's prerequisites, or methods a mod following the spec gives through `method`); game items; `StoreClientInstance`; Unity assets (found again by name); objects of classes with a field table (copied field by field).
- Values that cannot be stored: other framework types (for example `WeakReference<T>`, `ConditionalWeakTable`, and structs without a field table such as `DateTime`); scene objects; arrays of more than one dimension; native pointers other than zero; collections with other comparers; delegates to dynamic methods.
- While writing the snapshot, Plus runs for every type and method the same lookup it will run on load, and records anything not found as not storable right away.

## 12. Field table and generator tool

The field table is made from the built DLL with the tool we provide. The tool is published in the repository together with this document: `F5SnapshotSave/tools/generate_spec_fields.ps1` ([link](https://github.com/VVittgenstein/ProbablyStolenModding/blob/main/F5SnapshotSave/tools/generate_spec_fields.ps1)).

```powershell
& <repository>\F5SnapshotSave\tools\generate_spec_fields.ps1 -Dll <built DLL> -Out <output folder>\Data\f5-snapshot-fields.json -GameDir "<game folder>"
```

- The tool reads names and flags from the DLL with `Unity.Burst.Cecil.dll` from the game's `Probably Stolen_Data\Managed` folder; it does not load or run the DLL. The default of `-GameDir` is the game folder on our machine; pass yours.
- Output (the same format 2 as `Data/snapshot-schema.json` that Plus ships, restricted to one assembly):

```json
{"Format":2,"Modules":{"<assembly name>":"<MVID, 32 hex digits>"},"Fields":{"<assembly name>:<full type name, nested types with +>":[{"Name":"...","Static":false,"ReadOnly":false,"ValueType":false,"FieldType":"..."}]}}
```

- Constant fields and the `<Module>` type are left out.
- After every build, make the table again from the DLL file you ship and ship it with the DLL. If the MVID in the table differs from the DLL the game loaded, Plus does not accept the adapter and your mod is handled like a mod that does not follow the spec.
- Where the file lives is up to your mod; Plus only receives the text. Our three mods keep it in `Data/f5-snapshot-fields.json` and read it with `ModHelper.ReadModFileText(<your mod ID>, "Data/f5-snapshot-fields.json")` when the message arrives. `ReadModFileText` does not work during your mod's own `Init` and `OnEnable` (the game registers the mod's folder only after they return), so do not read it then.

## 13. Versions

- This is spec v1: `spec` is 1 and the channel is `snapshot.v1.adapters`. An incompatible change will use a new channel (`snapshot.v2.adapters`); v1 stays.
- Extra data records carry the spec version 1.
- Plus 0.8.0 is the first version that supports this spec. Version 0.7.1 and earlier do not send the message.
- A snapshot records the MVID of every non-framework assembly it uses (including providers of extra data). When your DLL changes between saving and loading (for example after an update of your mod), the whole snapshot falls back to the legacy data on load: the customer at the counter waits first in line and starts over, and the waiting customers are created anew; a customer whose type cannot be created anew (section 2) does not come back.

## 14. Security scope

Basis: prerequisites in the game need no approval. For a member of mod X, `ModReflect.InScope` only checks that the caller's manifest lists X under `Prerequisites` and that X is running; any mod that lists X in its manifest can read and write X's fields and find and call X's methods with `ModReflect`.

- Plus uses only authentic adapters: every function is written in the mod it is handed over for (the checks of section 10), so no third party can make Plus use its code as another mod's adapter.
- An adapter's functions give no access beyond what listing that mod as a prerequisite gives: they handle only the types of that mod's own assembly (section 7.1). The spec has no function that writes static fields or calls methods. `ModReflect.Get` and `ModReflect.Set` look a name up along the inheritance chain including static fields and properties, so `get` and `set` can also read and write static fields of your types and run their property accessors; this stays within the bound above, and Plus itself passes only instance field names from the field tables.
- Plus holds the adapter functions only in the objects of one save or load; no static field holds them. This shortens how long the functions exist; a mod that lists Plus as a prerequisite can still reach them during a collection (next point).
- Outside this spec (the game's design): a mod that lists Plus as a prerequisite can read and write Plus's objects, including, during a collection, the adapters already handed over, through the registration function's `Target`; a mod that lists your mod as a prerequisite can read and write your mod's objects directly.

## 15. Complete example: Armed Robbery & Intimidation (KeptShotgun)

Armed Robbery & Intimidation (manifest ID `KeptShotgun`) adds only two delegates to customers: the end actions of the line of a customer being robbed and of the line of a customer who refuses a threat and leaves, which point at its static methods `Robbery.Finish` and `ThreatenPatch.LeaveNow`. From the moment either line appears until its end action has run, Plus does not allow saving (a robbery happens while aiming, and saving is not allowed while aiming either), so in play these delegates do not get into an F5 save; for a delegate of this kind in a snapshot, Plus finds the method again with `findType` and `method`. The mod needs no keys and no extra data.

Registration in `OnEnable` (`src/KeptShotgun.cs`, excerpt):

```csharp
        internal const string ModId = "KeptShotgun";
        internal const string PerkId = "keptshotgun_kept_shotgun";
        internal const string LogPrefix = "[KeptShotgun] ";
        ...
        public void OnEnable()
        {
            ModHelper.AddStartingPerk(ModId, PerkId, 1, StartingPerk.StartingPerkType.POSITIVE,
                Text.PerkName, Text.PerkDescription, null, new string[0], 0, true);
            PerkIcon.Register();
            // With the adapter (F5SnapshotSpec), F5 Quick Save Plus finds this mod's methods again for a dialogue line that
            // calls them; it does not allow saving while such a line is shown, so in play these lines do not reach a save.
            F5SnapshotSpec.Register();
        }
```

The whole adapter (`src/F5SnapshotSpec.cs`, [link](https://github.com/VVittgenstein/ProbablyStolenModding/blob/main/KeptShotgun/src/F5SnapshotSpec.cs)). `KeptShotgunMod.ModId` is `"KeptShotgun"` and `KeptShotgunMod.LogPrefix` is `"[KeptShotgun] "`.

```csharp
using System;
using System.Collections.Generic;
using System.Reflection;

namespace KeptShotgun
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
    // This mod attaches no objects of its own types to customers. It attaches delegates to two of its static methods as end
    // actions of game dialogue lines: Robbery.Finish (the line of a customer being robbed) and ThreatenPatch.LeaveNow (the
    // line of a customer who refuses a threat and leaves). F5 Quick Save Plus finds them again with "findType" and "method".
    // No object of this mod has to be found again instead of copied, and the mod keeps no data about game objects outside
    // them, so the optional pairs "key"/"find" and "saveExtra"/"loadExtra" are left out.
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

        private static bool tableProblemLogged;

        // Called from OnEnable. The game removes the subscription itself when the mod is stopped (ModLoader.StopRunningMod
        // calls ModHelper.ForgetModServices, which drops every message handler of the mod), so OnDisable has nothing to undo.
        internal static void Register()
        {
            try
            {
                ModHelper.OnMessage(KeptShotgunMod.ModId, Channel, OnAdapters);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(KeptShotgunMod.LogPrefix + "subscribing to F5 Quick Save Plus failed: " + e);
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
                UnityEngine.Debug.LogWarning(KeptShotgunMod.LogPrefix + "handing the adapter to F5 Quick Save Plus failed: " + e);
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
                text = ModHelper.ReadModFileText(KeptShotgunMod.ModId, FieldTable);
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
                UnityEngine.Debug.LogWarning(KeptShotgunMod.LogPrefix + problem + "; F5 Quick Save Plus cannot use the adapter of this mod");
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
                { "mod", KeptShotgunMod.ModId },
                // The assembly whose types the adapter describes.
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
        // delegates F5 Quick Save Plus rebuilds.
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

        private static Assembly Own
        {
            get { return typeof(F5SnapshotSpec).Assembly; }
        }

        private static void RequireOwnObject(object o)
        {
            if (o == null || o.GetType().Assembly != Own)
                throw new ArgumentException("not an object of " + KeptShotgunMod.ModId + ": " + (o == null ? "null" : o.GetType().FullName));
        }

        private static void RequireOwnType(Type type)
        {
            if (type == null || type.Assembly != Own)
                throw new ArgumentException("not a type of " + KeptShotgunMod.ModId + ": " + (type == null ? "null" : type.FullName));
        }
    }
}
```

Build step (`build.ps1`, excerpt, lines 34 to 35 and 43 to 70, [link](https://github.com/VVittgenstein/ProbablyStolenModding/blob/main/KeptShotgun/build.ps1)): compile into a temporary folder, make the field table from the DLL just compiled with the generator tool, then copy the DLL, the field table and the manifest to the output folder; a failure while compiling or while making the table copies nothing. The build is deterministic (`-deterministic+`).

```powershell
$specTool = Join-Path $PSScriptRoot '..\F5SnapshotSave\tools\generate_spec_fields.ps1'
if (-not (Test-Path -LiteralPath $specTool)) { throw "generate_spec_fields.ps1 not found at '$specTool'." }
...
$staging = Join-Path ([System.IO.Path]::GetTempPath()) ("$modName-build-" + [System.Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging | Out-Null
try {
    $dll = Join-Path $staging "$modName.dll"
    $lines = New-Object System.Collections.Generic.List[string]
    foreach ($opt in '-nologo', '-nostdlib+', '-target:library', '-langversion:7.3',
                     '-optimize+', '-deterministic+', '-debug-') { $lines.Add($opt) }
    $lines.Add("-out:`"$dll`"")
    Get-ChildItem -LiteralPath $managed -Filter '*.dll' | Sort-Object Name | ForEach-Object { $lines.Add("-r:`"$($_.FullName)`"") }
    Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | Sort-Object Name | ForEach-Object { $lines.Add("`"$($_.FullName)`"") }
    $rsp = Join-Path $staging 'csc.rsp'
    [System.IO.File]::WriteAllLines($rsp, $lines, (New-Object System.Text.UTF8Encoding($false)))

    & $dotnet $csc -noconfig "@$rsp"
    if ($LASTEXITCODE -ne 0) { throw "csc exited with code $LASTEXITCODE." }

    # The field table is made from the DLL just built, in the staging folder, before anything is copied: F5 Quick Save
    # Plus uses it only when the module version id recorded in it equals the one of the DLL the game loads. When
    # compiling or making the table fails, nothing is copied to -OutDir.
    $stagedTable = Join-Path $staging 'Data\f5-snapshot-fields.json'
    & $specTool -Dll $dll -Out $stagedTable -GameDir $GameDir
    if (-not (Test-Path -LiteralPath $stagedTable)) { throw "The field table was not written: $stagedTable" }

    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
    Copy-Item -LiteralPath $dll -Destination (Join-Path $OutDir "$modName.dll") -Force
    New-Item -ItemType Directory -Force -Path (Join-Path $OutDir 'Data') | Out-Null
    Copy-Item -LiteralPath $stagedTable -Destination (Join-Path $OutDir 'Data\f5-snapshot-fields.json') -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'manifest.xml') -Destination (Join-Path $OutDir 'manifest.xml') -Force
```

## 16. Other examples

- Gunsmith Expansion (`GunsmithExpansion/src/F5SnapshotSpec.cs`, [link](https://github.com/VVittgenstein/ProbablyStolenModding/blob/main/GunsmithExpansion/src/F5SnapshotSpec.cs)): keys (catalog guns `GunDef` by Id, ammunition families `Ammo.Family`, reloading dies `Reloader.Die`) and extra data (three `ConditionalWeakTable`s keyed by the order `GunOrderData`).
- Moneylending (`Moneylending/src/F5SnapshotSpec.cs`, [link](https://github.com/VVittgenstein/ProbablyStolenModding/blob/main/Moneylending/src/F5SnapshotSpec.cs)): keys (loans, debts and today's loan request in its ledger, and borrower profiles by Id); the ledger is read in `ModHook.OnGameLoadedEarly` (`src/Moneylending.cs`).

## 17. Checklist

1. Listen with `ModHelper.OnMessage(<your mod ID>, "F5SnapshotSave:snapshot.v1.adapters", handler)` in `Init` or `OnEnable`.
2. When the message arrives, check the registration function as in section 5 and return if the check fails.
3. Hand over a new `Dictionary<string, object>` every time, with all required keys and paired keys in pairs.
4. Write every function in your assembly, with exactly the delegate type of section 6, and restrict the types as in section 7.1.
5. After building, run `generate_spec_fields.ps1` on the DLL you ship and ship the table with it; read the table's text when the message arrives.
6. For objects with keys: `find` returns the same object (for a struct, a value of exactly that type) when Plus reads the snapshot.
7. With extra data: `loadExtra` writes only to weak-keyed tables keyed by `owner`.
8. After a save made with the save key in the shop, `Player.log` has a line `spec adapter accepted: mod '<your mod ID>'`; with customers carrying your objects, no line containing `could not be stored` names your mod.
