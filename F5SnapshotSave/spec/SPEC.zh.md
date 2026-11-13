# F5 存档加强版兼容规范 v1

适用版本：F5 存档加强版（F5 Quick Save Plus，manifest ID `F5SnapshotSave`，Steam 创意工坊 [3831061477](https://steamcommunity.com/sharedfiles/filedetails/?id=3831061477)）0.8.0 起。游戏：Probably Stolen 正式版自带的 Mod 系统。文中关于游戏接口的说明按游戏构建 Steam buildid 26525552（游戏版本 0.46D）核对。

## 1. 概要

F5 存档加强版（下称“加强版”）在玩家按存档键时，把柜台上的顾客、排队的顾客和化学品反馈顾客，连同当前对话、讲价状态和其他 Mod 加在顾客身上的东西，逐个对象、逐个字段写进存档；读档后重新做出这些对象，把顾客放回原处。化学品反馈顾客是游戏的化学品反馈名单（`StoreClientManager.chemicalFeedbackStack`）中的顾客：卖出化学品后回来投诉或再买的顾客。游戏只在开门时（`PlayerStore.OnShutterOpened`）向这个名单加入顾客，并在同一次调用中把名单中的每位顾客加入排队、清空名单，所以按存档键时这个名单是空的，除非有 Mod 向其中加入了顾客。这个名单中的顾客带着加强版存不下的东西时，这次不存档（第 3 节）。

其他 Mod 的类型中，加强版自己只能读写它前置清单中的 Mod 的类型：游戏的 `ModReflect` 只允许一个 Mod 访问游戏、它自己、它前置清单中正在运行的 Mod 以及 UI 命名空间的类型（`ModReflect.InScope`）。游戏的 Mod 扫描器又把“对运行时才取得的类型列出字段”计为 `reflection-external` 能力，所以一个 Mod 的字段名单要在构建时从 DLL 生成。

本规范让其他 Mod 把自己的字段表和一组写在自己 Mod 里的读写函数交给加强版。遵守规范的 Mod：

- 在本规范说明的范围内，加在顾客身上的东西由加强版原样存档、读档；
- 更新以后只需在构建时重新生成字段表，加强版不需要发新版本；
- 不需要引用加强版，不需要把加强版列为前置，不需要声明任何能力。消息与适配只用游戏与 .NET 的类型。没有装加强版时，加强版不发这条消息；只有另一个 Mod 以加强版的名义在这个频道上发消息时（第 5 节），处理函数才会执行，并在第 5 节的核对处返回，不交出适配。

没有在顾客、对话或柜台物品上加自己的对象或程序的 Mod，不需要做任何事。

## 2. 术语

| 术语 | 含义 |
|---|---|
| 加在顾客身上的东西 | 其他 Mod 加在顾客上的数据与代码。例：修枪顾客的订单（数据）；对话结束时、对话选项被点击后执行的程序（代码）。本规范处理其中属于这个 Mod 自己程序集的对象、类型与方法。 |
| 字段表 | 一个 JSON 文件，列出一个程序集中每种类型的字段（名字、是否静态、是否只读、是否值类型、字段类型），并记下这个 DLL 的模块版本标识。 |
| 模块版本标识（MVID） | DLL 的 `ModuleVersionId`。DLL 内容改变时它随之改变；确定性编译（`-deterministic+`）下，源码与引用相同时它相同。 |
| 适配 | 遵守规范的 Mod 交给加强版的一个字典：字段表与一组函数（第 6、7 节）。 |
| 登记函数 | 加强版为一次收集新建的 `Action<object>`，随消息发给各 Mod；Mod 调用它交出适配。 |
| 键 | 适配为某个对象给出的一段文字。加强版只记下类型与这段文字，读档时用它找回原物（第 8 节）。 |
| 附加数据 | 遵守规范的 Mod 记在顾客对象之外、以游戏对象为键的数据。加强版随存档写下，读档后交还这个 Mod（第 9 节）。 |
| 快照 | 加强版写进存档的这些对象的记录。 |
| 旧格式数据 | 加强版每次存档时同时写入的、F5 存档（普通版）格式的数据：天数、柜台物品，以及按顾客种类（ID）记录的排队名单，柜台上的顾客排第一。 |
| 重新生成 | 读档时按顾客种类（ID）新做一位顾客：对话从头开始，预算与物品重新随机。能生成的种类为游戏的顾客登记表中的种类（包括 Mod 登记的种类），以及加强版另外处理的几种。 |

## 3. 不遵守规范的 Mod

写快照时，有的对象存不下，例如：没有字段表的类型；字段表与载入的 DLL 不一致的类型；读档时找不到的类型或方法（例如指向某个 Mod 的方法的委托、它的枚举值或 `Type` 值）；场景中的物体。加强版按这个对象第一次被写到时所在的位置处理：

| 位置 | 处理 |
|---|---|
| 排队顾客 | 这位顾客不写进快照，存档照常进行。读档时按顾客种类重新生成，放回原来的位置；不能重新生成（第 2 节）的顾客读档后不出现。存档提示后面加一句“排队中 N 位顾客带着 F5 存档存不下的东西，读档后会重新生成，无法生成的不再出现”。 |
| 柜台上的顾客、当前对话、讲价状态、柜台物品的特征 | 这次不存档，提示等柜台上的顾客离开后再按存档键。 |
| 化学品反馈顾客 | 这次不存档，提示明天开门后再按存档键。这类顾客不在旧格式数据的排队名单中，不能重新生成。 |

- 一个对象被几位顾客共用时，归到第一个写到它的顾客；排除这位顾客后重写，直到没有存不下的对象。
- 写完后加强版照旧把快照读回检查一次；读回失败时逐位检查，读不回来的排队顾客同样排除。
- 每次启动游戏后，某个 Mod 第一次造成排除或拒绝时，加强版在存档提示之后弹出窗口，列出这个 Mod 的名称（manifest 中的 `Name`），并显示加强版创意工坊页面的网址。列出的 Mod 按造成存不下的那个类型所在程序集的 `ModLoader.ModIdOfAssembly` 判断：对象自己的类型，或者例如没有字段表的基类、委托所指方法的声明类型、枚举值的枚举类型、`Type` 值所代表的类型、集合比较器的类型；`saveExtra` 抛出异常时为这份适配的 Mod；读回检查失败时为读不回来的类型所属的 Mod。
- `Player.log` 中每位被排除的顾客有一行（以 `[F5SnapshotSave]` 开头，含 `left out of the snapshot`），写明排队位置、顾客种类（ID）与原因：写快照时排除的，原因中有路径，以及已知时的类型、程序集与所属 Mod；读回检查时排除的，原因为读回时的错误。这次存档涉及的 Mod 写在含 `could not be stored` 的一行。

加强版 0.7.1 及以前，以上情况都使整次存档失败。

另外：

- 加强版前置清单中的两个联动 Mod（Pen 的移液枪 `Pen_mod_pipette`、原版枪械拓展 `vanilla_gun_expansion`）没有遵守规范。加强版随包带着它们的字段表，按构建时安装的版本生成；玩家装的版本与之不同时，按上表处理，窗口中同样列出。公司增强（`CorpPresence`）在加强版 0.7.1 及以前也是这样的联动；加强版 0.8.0 起不再支持它，在公司增强遵守规范之前，它的对象与其他没有遵守规范的 Mod 一样按上表处理。
- 交出了适配、但适配没有被接受的 Mod（第 10 节），按不遵守规范处理。
- 游戏本身的代码与加强版随包的字段表不一致时（游戏更新后、加强版按新构建发版之前），整次存档失败，与 0.7.1 相同。

## 4. 频道与消息

在你的 Mod 的 `Init` 或 `OnEnable` 中监听：

```csharp
ModHelper.OnMessage("<你的 Mod ID>", "F5SnapshotSave:snapshot.v1.adapters", OnAdapters);

static void OnAdapters(string sender, object payload) { ... }   // Action<string, object>
```

加强版用 `ModHelper.SendMessage("F5SnapshotSave", "snapshot.v1.adapters", register)` 发送。游戏以“发送方 ID:频道”为键找出监听者，在 `SendMessage` 中依次调用；监听函数抛出的异常由游戏记入日志。消息内容 `register` 是一个 `Action<object>`，即登记函数。

加强版在以下时刻发这条消息（每次称为一次收集）：

1. 每次用存档键开始、并通过写快照之前的检查（例如玩家在店里、没有在通话、游戏本身的代码与加强版随包的字段表一致）的存档：一次收集，写快照与存档时的读回检查都用这一次收集到的适配。
2. 读档时，读快照之前。读快照在 `ModHook.OnGameLoadedLate` 中进行。
3. 快照中有附加数据时，在读档后第一次卷帘门更新、交还附加数据之前。

登记函数只在这一次 `SendMessage` 执行期间有效。`SendMessage` 返回后，加强版清空它引用的内容，之后调用它不起作用。

需要时可以用 `ModHelper.OffMessage` 移除监听；放贷在 `OnEnable` 中先 `OffMessage` 再 `OnMessage`，避免重复监听。

## 5. 核对登记函数

在 Mod 的 `Init`、`OnEnable` 期间，调用栈上没有属于已登记 Mod 的帧，这时游戏允许以任何尚未载入的 Mod 的 ID 发送消息（`ModHelper.RequireCaller`）。所以加强版载入之前，另一个 Mod 可以以 `F5SnapshotSave` 的名义在这个频道上发消息。交出适配之前，先核对登记函数属于加强版：

```csharp
var register = payload as Action<object>;
if (register == null || register.GetInvocationList().Length != 1 || register.Target is Delegate)
    return;
MethodInfo method = register.Method;
if (method == null || method.DeclaringType == null
    || typeof(Delegate).IsAssignableFrom(method.DeclaringType)
    || ModLoader.ModIdOfAssembly(method.DeclaringType.Assembly) != "F5SnapshotSave")
    return;
register(BuildAdapter());   // 每次新建一个字典
```

- `GetInvocationList().Length != 1`：多播委托的 `Method` 返回调用列表中最后一项的方法。
- `method.DeclaringType == null`：动态方法没有声明类型。
- 声明类型是委托类型，或 `Target` 是委托：某个委托类型的 `Invoke` 会执行这个委托里放进去的任何程序。
- `ModLoader.ModIdOfAssembly(...)`：游戏程序集返回 null，Mod 的程序集返回它的 manifest ID。

## 6. 适配

核对通过后，调用登记函数，交出一个新建的 `Dictionary<string, object>`（类型必须正是 `Dictionary<string, object>`）：

| 键 | 值的类型 | 必需 | 内容 |
|---|---|---|---|
| `spec` | `int` | 是 | 1 |
| `mod` | `string` | 是 | 你的 Mod 的 manifest ID，必须等于 `ModLoader.ModIdOfAssembly(assembly)` |
| `assembly` | `System.Reflection.Assembly` | 是 | 你的程序集（`typeof(你的某个类型).Assembly`）；不能是游戏或加强版的程序集 |
| `fields` | `string` | 是 | 字段表的文字（第 12 节）；读不到时传 null，加强版不接受这份适配并记下原因 |
| `get` | `Func<object, string, object>` | 是 | 按字段名读对象的实例字段 |
| `set` | `Action<object, string, object>` | 是 | 按字段名写对象的实例字段 |
| `getStatic` | `Func<Type, string, object>` | 是 | 按名字读你的类型的静态字段 |
| `findType` | `Func<string, Type>` | 是 | 按完整类型名找你的类型 |
| `method` | `Func<Type, string, Type[], System.Reflection.MethodBase>` | 是 | 按类型、方法名、参数类型找你的方法 |
| `create` | `Func<Type, object>` | 是 | 用无参构造函数创建你的类型的对象 |
| `key` | `Func<object, string>` | 否，与 `find` 成对 | 给出对象的键，或 null |
| `find` | `Func<Type, string, object>` | 否，与 `key` 成对 | 按类型与键找回对象 |
| `saveExtra` | `Func<object, object>` | 否，与 `loadExtra` 成对 | 给出一个游戏对象的附加数据，或 null |
| `loadExtra` | `Action<object, object>` | 否，与 `saveExtra` 成对 | 把附加数据交还给新建的游戏对象 |

加强版当场把这些值复制到自己的记录中，之后再改这个字典没有效果。成对的函数要么都给，要么都不给。同一次收集中，同一程序集只接受第一份合格的适配。

## 7. 函数的约定

### 7.1 所有函数

- 委托类型必须与上表完全相同（例如 `Func<object, string, object>`；自定义的委托类型不接受），并且是单个委托。
- 方法写在你的程序集中：`Method.DeclaringType.Assembly` 必须是你交出的 `assembly`。静态方法、实例方法与 lambda 都可以；方法不能是某个委托类型的 `Invoke`，`Target` 不能是委托。
- 原因：函数里调用的 `ModReflect` 按调用栈上第一个 Mod 的帧判断调用者（`ModLoader.CallingModId`）。函数写在你的 Mod 里，`ModReflect` 才按你的 Mod 检查访问范围；直接用 `ModReflect` 的方法做成的委托会按加强版检查。
- 类型限制：`get`、`set`、`key` 只接受实际类型属于你的程序集的对象；`getStatic`、`method`、`create`、`find` 只接受你的程序集的类型；`findType` 只返回你的程序集的类型，否则返回 null。其他输入抛出异常。这样，取得这些函数的 Mod 得不到“把你的 Mod 列为前置”之外的访问（第 14 节）。
- 写快照时，`get`、`findType`、`method`、`key` 或 `saveExtra` 抛出异常，加强版把正在写的对象记为存不下（第 3 节），窗口中会列出你的 Mod。写快照时 `getStatic` 抛出异常，加强版跳过这个静态字段；没有其他静态字段持有的对象随后在能按字段复制时按字段复制。读档时读快照出错，整份快照退回旧格式数据；存档时的读回检查出错时，加强版逐位检查顾客，读不回来的顾客按存不下处理（第 3 节）。
- `get`、`set` 按对象、按字段调用。一次存档中快照可能写多遍（排除顾客后重写，读回检查失败时逐位检查），所以同一对象的同一字段可能被读多次。

### 7.2 各函数

| 函数 | 加强版何时调用 | 约定 |
|---|---|---|
| `get(o, name)` | 写快照时，读实际类型属于你的程序集的对象的每个实例字段；读快照时，也用它读出只读字段，与存档时的值比较（第 11 节） | 返回字段值。字段名来自字段表，包括基类中声明的字段（基类可以是游戏的类型，或你的前置 Mod 的类型）。用 `ModReflect.Get(o, name)` 实现。 |
| `set(o, name, value)` | 读快照时（读档，以及存档时的读回检查），写重新做出的对象的实例字段，只读字段除外；只读字段用 `get` 读出后与存档时的值比较（第 11 节） | 用 `ModReflect.Set(o, name, value)` 实现。 |
| `getStatic(type, name)` | 写快照时，查看对象是否由对象自己的类型或其基类（直到第一个没有字段表的类型）中声明的某个静态字段持有：字段类型为 `string` 以外的引用类型，名字含 `instance`（不区分大小写）或为编译器生成的 lambda 类中的 `<>9`（对象继承 `UnityEngine.Object` 时不限名字）。由这样的字段持有时，加强版记下路径；读快照时（读档，以及存档时的读回检查），从这个字段找回同一个对象。只由其他类型的静态字段持有的对象，能按字段复制时按字段复制。 | 返回静态字段的值。加强版从不写你的类型的静态字段。 |
| `findType(fullName)` | 写快照时为每个类型做读档时同样的查找；读快照时解析类型 | 按完整类型名（嵌套类型用 `外层+内层`）返回你的类型，找不到或不属于你的程序集时返回 null。`ModReflect.FindType` 会先找游戏的程序集，也会返回 UI 命名空间中的引擎类型，所以要核对结果的程序集。 |
| `method(type, name, signature)` | 重建指向你的方法的委托（例如对话台词的结束动作）；写快照时也先查找一次 | 返回 `type` 自己声明的方法（`DeclaringType` 必须等于 `type`），参数类型与 `signature` 一致；泛型方法传入的 `signature` 为 null，返回泛型方法定义。用 `ModReflect.Target(type, name, signature)` 实现。 |
| `create(type)` | 读快照时（存档时的读回检查也会调用，创建的对象随后丢弃），为按字段复制、类型属于你的程序集、并有只读实例字段的每个对象创建对象；只读字段声明在这个类型自己或它的任一基类中都算，基类在另一个程序集中也算（加强版调用对象自己所在程序集的适配的 `create`） | 用无参构造函数创建，返回对象的类型必须正是 `type`。用 `ModReflect.New(type, new object[0])` 实现。 |
| `key(o)` | 写快照时，写你的程序集的每个对象之前（委托除外），包括你的结构的值（装箱后传入） | 返回键或 null。返回键时，加强版只记下类型与键，不复制对象；有键的结构值在每个出现的位置各写一条键记录。 |
| `find(type, key)` | 读快照时（读档，以及存档时的读回检查） | 返回这个键对应的现有对象，类型必须正是 `type`（结构为装箱后的值）。返回 null、返回其他类型的对象，或抛出异常：读档时整份快照退回旧格式数据；存档时这位顾客按存不下处理。 |
| `saveExtra(owner)` | 写快照时，对读档时会由快照新建的每个游戏对象调用（第 9 节） | 返回 null 或一个值。 |
| `loadExtra(owner, value)` | 读档后第一次卷帘门更新时，放回顾客之前（第 9 节） | 只把 `value` 放进以 `owner` 为键、对键弱引用的表。 |

## 8. 键（`key`、`find`）

对必须找回原物、不能复制的对象，适配给出键。例如目录中的定义（复制出来就不再是目录中的那一个）、账本中的记录（复制出来以后，账本更新时它不会跟着更新）、有只读字段且没有无参构造函数的类型、自己声明了只读实例字段的结构（第 11 节）。

- 加强版对它要写的你的程序集的每个对象（委托除外）都先问 `key`，包括你的结构的值（装箱后传入）。只为 `find` 能找回同一个对象的对象给出键；其他对象返回 null，由加强版按字段复制。
- 结构的值没有同一性：有键的值在每个出现的位置各写一条键记录，`find` 返回这个结构类型的值（装箱后），类型必须完全相同。
- 读档时，`find` 在加强版读快照时被调用，即在 `ModHook.OnGameLoadedLate` 中。键对应的对象在这时就要存在。各 Mod 的 `OnGameLoadedLate` 处理函数的先后取决于 Mod 的载入顺序；如果你的 Mod 在读档时重新载入这些对象，在 `ModHook.OnGameLoadedEarly` 中载入：游戏读入存档文件之后触发它，早于 `OnGameLoadedLate`。放贷在这里读入账本。
- 存档时的读回检查也调用 `find`，这时对象都在。

## 9. 附加数据（`saveExtra`、`loadExtra`）

附加数据用于你的 Mod 记在顾客对象之外、以游戏对象为键的数据，例如以订单对象 `GunOrderData` 为键的 `ConditionalWeakTable`。读档时快照会新建这些游戏对象，原来的键不再存在，表中的数据要随快照存下并交还。

- 范围：加强版只对读档时由快照新建的游戏对象（类型在游戏程序集 `Assembly-CSharp` 中）调用 `saveExtra`：按字段复制的对象（例如 `GunOrderData`）、`StoreClientInstance`、用游戏的物品编码写出的物品。按路径找回的现有对象、按唯一 ID 找回的物品、资源等不调用。
- 写出：`saveExtra(owner)` 返回的值按快照的规则写出，可以包含你的对象、游戏对象与键；共用的引用与循环保留。加强版记下提供它的程序集（连同模块版本标识，作为这份快照依赖的程序集）与规范版本 1。`saveExtra` 抛出异常时，`owner` 所在的顾客按存不下处理。
- 读档：提供者没有载入、模块版本标识与存档时不同、没有被接受的带 `loadExtra` 的适配，或规范版本不是 1，整份快照退回旧格式数据。
- 交还：读档后第一次卷帘门更新时，加强版重新收集一次适配，先对每份附加数据调用 `loadExtra(owner, value)`，之后才放回快照中的顾客、恢复柜台界面、重新生成被排除的顾客。存档时的读回检查不调用 `loadExtra`。
- `loadExtra` 的约定：只把 `value` 放进以 `owner` 为键、对键弱引用的表（例如 `System.Runtime.CompilerServices.ConditionalWeakTable`）；不得用强引用保存 `owner` 或从它可达的对象；不得枚举这样的表找出对象来使用；不得改动其他状态。原因：某一份 `loadExtra` 抛出异常、或提供者的适配不在时，加强版放弃这份快照，按旧格式数据重新生成一次顾客，并提示“这个存档里的顾客无法原样恢复”；此前已交还的附加数据只留在以被丢弃的对象为键的弱引用表中，游戏不再引用这些对象，按约定也没有代码找到它们。
- 建议先核对 `value` 的每一项，确认都能接受之后再改动表，这样不能接受的值不改动任何东西。

## 10. 加强版的核对与日志

每次收到适配，加强版当场核对：

- `spec` 是整数 1；`mod` 非空；`assembly` 为已载入的程序集（游戏与加强版的程序集除外），`ModLoader.ModIdOfAssembly(assembly)` 等于 `mod`。
- 每个函数类型正确，为单个委托，方法声明在 `assembly` 中，委托类型的 `Invoke` 与 `Target` 为委托的函数都不接受；成对的函数都有或都没有。
- `fields` 能解析为格式 2 的字段表，其中只有一个模块，名字与 `assembly` 的名字相同，模块版本标识与游戏载入的 `assembly` 相同；每个类型键都以 `<程序集名>:` 开头。

`Player.log` 中，每次游戏启动后的第一次收集（第 4 节）时，每份适配写一行（以 `[F5SnapshotSave]` 开头）：

- 接受：`spec adapter accepted: mod '<ID>', assembly <名字>, module <MVID>`，有键与附加数据时后面加 `, keys`、`, extra data`。
- 不接受：`spec adapter not accepted (mod '<ID>'): <原因>`，例如字段表属于另一次构建时为 `the field table belongs to module ..., the loaded ... is ... (made for another build)`。

## 11. 你的对象怎样被存取

- 对象的类型及其全部基类（直到 `System.Object` 或 `System.ValueType`）都要有字段表：你的类型用你的字段表，游戏的类型用加强版随包的字段表，另一个 Mod 的类型用那个 Mod 的适配（或加强版随包的联动字段表）。有一层没有时，这个对象存不下。基类在另一个 Mod 中时，那个 Mod 必须是你的前置（否则游戏扫描器对这个引用给硬性标记）。
- 继承链上的字段名不能重复（派生类中与基类同名的字段无法按名字访问），否则这个对象存不下。
- 只读实例字段：你的程序集中按字段复制、类型或其某个基类声明了只读实例字段的对象用 `create` 创建（第 7.2 节），构造函数因此会执行。加强版从不写入只读字段：
  - 类的对象中，构造函数放进只读字段的数组、`List<T>`、`HashSet<T>`、`Queue<T>`、`Stack<T>` 或 `Dictionary<TKey, TValue>`（类型与存档时相同，数组长度相同）被清空后填入存档时的内容。
  - 其他只读字段在构造函数之后必须正是存档时的值：按键或按路径找回的对象、按唯一 ID 找回的物品与资源须为同一个对象；按键找回的结构值须为同一类型，且每个字段一致；复制的结构值须为同一类型，且存下的每个字段一致；标量（`bool`、`char`、数字、指针、`decimal`、`string`、枚举值）须类型完全相同、值相等；`Type` 值须为同一个运行时类型对象；null 须为 null。引用只按同一性比较；这项比较从不调用你的类型自己定义的 `Equals`。
  - 委托、按字段复制的对象、`StringBuilder`、`new object()`、`StoreClientInstance` 与用游戏的物品编码写出的物品在读快照时总是新做出来，不会是构造函数做出的对象，所以存放它们的只读字段读不回来；复制的结构值中不为 null 的数组或集合同样如此。
  - 自己声明了只读实例字段的结构完全不能复制：加强版会用无参构造函数创建它，结构没有无参构造函数。这样的结构只能用键存下（第 8 节）。
  - 只读字段与存档时的值不一致时，这个对象读不回来：读档时整份快照退回旧格式数据；存档时读回检查会发现，这位顾客按存不下处理（排队顾客不写进快照；柜台上的顾客或化学品反馈顾客读不回来时，这次不存档）。这样的对象可以改用键（第 8 节）。
- 可以存的值：null；`bool`、`char`、整数与浮点数类型、`decimal`、`string`；枚举值；`Type` 值；有字段表的结构（自己声明了只读实例字段的结构，只能用它所属 Mod 的适配给出的键存下）与 `ValueTuple`；一维数组；`List<T>`、`HashSet<T>`、`Queue<T>`、`Stack<T>`、`Dictionary<TKey, TValue>`（`HashSet` 与 `Dictionary` 只支持默认比较器与 `StringComparer.Ordinal`、`OrdinalIgnoreCase`、`InvariantCulture`、`InvariantCultureIgnoreCase`）；`StringBuilder`；`new object()`；委托（调用列表中的每个方法在读档时都要找得到：游戏的方法、加强版前置 Mod 的方法，或遵守规范的 Mod 经 `method` 给出的方法）；游戏的物品；`StoreClientInstance`；Unity 资源（按名字找回）；有字段表的类的对象（按字段复制）。
- 存不下的值：其他框架类型（例如 `WeakReference<T>`、`ConditionalWeakTable`、`DateTime` 等没有字段表的结构）；场景中的物体；多维数组；不为零的原生指针；用其他比较器的集合；指向动态方法的委托。
- 写快照时，加强版对每个类型和方法都先做一次读档时同样的查找，找不到的当时就记为存不下。

## 12. 字段表与生成工具

字段表由我们提供的工具从构建出的 DLL 生成。工具与本文档一同发布在仓库中：`F5SnapshotSave/tools/generate_spec_fields.ps1`（[链接](https://github.com/VVittgenstein/ProbablyStolenModding/blob/main/F5SnapshotSave/tools/generate_spec_fields.ps1)）。

```powershell
& <仓库目录>\F5SnapshotSave\tools\generate_spec_fields.ps1 -Dll <构建出的 DLL> -Out <输出目录>\Data\f5-snapshot-fields.json -GameDir "<游戏安装目录>"
```

- 工具用游戏 `Probably Stolen_Data\Managed` 目录中的 `Unity.Burst.Cecil.dll` 读取 DLL 中的名字与标志，不载入、不运行这个 DLL。`-GameDir` 的默认值是我们本机的游戏目录，请传入你的游戏目录。
- 输出格式（与加强版随包的 `Data/snapshot-schema.json` 的格式 2 相同，只含一个程序集）：

```json
{"Format":2,"Modules":{"<程序集名>":"<MVID，32 位十六进制>"},"Fields":{"<程序集名>:<完整类型名，嵌套类型用 +>":[{"Name":"...","Static":false,"ReadOnly":false,"ValueType":false,"FieldType":"..."}]}}
```

- 常量字段与 `<Module>` 类型不写入。
- 每次构建后，对要发布的那个 DLL 文件重新生成，与 DLL 一起发布。字段表中的模块版本标识与游戏载入的 DLL 不一致时，加强版不接受这份适配，你的 Mod 按不遵守规范处理。
- 文件放在哪里由你的 Mod 决定，加强版只接收文字。我们的三个 Mod 都放在 `Data/f5-snapshot-fields.json`，收到消息时用 `ModHelper.ReadModFileText(<你的 Mod ID>, "Data/f5-snapshot-fields.json")` 读取。`ReadModFileText` 在你的 Mod 自己的 `Init`、`OnEnable` 期间不可用（游戏在它们返回之后才登记这个 Mod 的目录），所以不要在那时读。

## 13. 版本

- 本规范为 v1：`spec` 为 1，频道为 `snapshot.v1.adapters`。以后如有不兼容的修改，用新的频道（`snapshot.v2.adapters`），v1 保留。
- 附加数据的记录带有规范版本 1。
- 加强版 0.8.0 是第一个支持本规范的版本。0.7.1 及以前不发这条消息。
- 快照记下其中用到的每个非框架程序集（包括附加数据的提供者）的模块版本标识。你的 DLL 在存档与读档之间改变时（例如你的 Mod 更新了），读档时整份快照退回旧格式数据：柜台上的顾客排在队首、对话从头开始，排队顾客重新生成；不能重新生成（第 2 节）的顾客读档后不出现。

## 14. 安全范围

依据：游戏的前置声明不需要批准。`ModReflect.InScope` 对属于 Mod X 的成员，只核对调用方 manifest 的 `Prerequisites` 是否含有 X、X 是否在运行；任何 Mod 在自己的 manifest 中列出 X，就能用 `ModReflect` 读写 X 的字段、找到并调用 X 的方法。

- 加强版只用真实的适配：每个函数都写在所称的 Mod 中（第 10 节的核对），第三方无法让加强版把它的程序当作另一个 Mod 的适配使用。
- 适配的函数提供的访问不超过“把这个 Mod 列为前置”所得到的访问：函数只处理这个 Mod 自己程序集的类型（第 7.1 节）。规范中没有写静态字段、调用方法的函数。`ModReflect.Get`、`ModReflect.Set` 按名字在继承链上查找，也包括静态字段与属性，所以 `get`、`set` 也能读写你的类型的静态字段、执行其属性；这仍在上述范围内，加强版自己只传字段表中的实例字段名。
- 加强版只在一次存档或读档用到的对象里持有适配的函数，没有静态字段持有它们。这缩短了函数存在的时间；把加强版列为前置的 Mod 在一次收集期间仍能取得这些函数（见下一条）。
- 不在本规范范围内（游戏的设计）：把加强版列为前置的 Mod 可以读写加强版的对象，包括一次收集期间经登记函数的 `Target` 读到已经交出的适配；把你的 Mod 列为前置的 Mod 可以直接读写你的 Mod 的对象。

## 15. 完整示例：持枪抢劫与威胁议价（KeptShotgun）

持枪抢劫与威胁议价（manifest ID `KeptShotgun`）加在顾客身上的只有两个委托：被抢劫顾客的台词与拒绝威胁后离开的顾客的台词，结束动作分别指向它的静态方法 `Robbery.Finish` 与 `ThreatenPatch.LeaveNow`。从这两句台词出现到结束动作执行完，加强版不允许存档（抢劫在举枪时进行，举枪时也不能存档），所以实际游戏中这两个委托不会进入 F5 存档；快照中有这类委托时，加强版用 `findType` 与 `method` 找回其方法。它不需要键与附加数据。

在 `OnEnable` 中登记（`src/KeptShotgun.cs`，节选）：

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

适配全文（`src/F5SnapshotSpec.cs`，[链接](https://github.com/VVittgenstein/ProbablyStolenModding/blob/main/KeptShotgun/src/F5SnapshotSpec.cs)）。其中 `KeptShotgunMod.ModId` 为 `"KeptShotgun"`，`KeptShotgunMod.LogPrefix` 为 `"[KeptShotgun] "`。

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

构建步骤（`build.ps1`，节选，第 34 至 35 行与第 43 至 70 行，[链接](https://github.com/VVittgenstein/ProbablyStolenModding/blob/main/KeptShotgun/build.ps1)）：编译到临时目录，用生成工具从刚编译的 DLL 生成字段表，再把 DLL、字段表与 manifest 复制到输出目录；编译或生成字段表失败时不复制任何文件。编译用确定性编译（`-deterministic+`）。

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

## 16. 其他示例

- 枪匠拓展（`GunsmithExpansion/src/F5SnapshotSpec.cs`，[链接](https://github.com/VVittgenstein/ProbablyStolenModding/blob/main/GunsmithExpansion/src/F5SnapshotSpec.cs)）：键（目录中的枪 `GunDef` 按 Id，弹药族 `Ammo.Family`，复装模具 `Reloader.Die`），附加数据（以订单 `GunOrderData` 为键的三张 `ConditionalWeakTable`）。
- 放贷（`Moneylending/src/F5SnapshotSpec.cs`，[链接](https://github.com/VVittgenstein/ProbablyStolenModding/blob/main/Moneylending/src/F5SnapshotSpec.cs)）：键（账本中的借款、欠款与当天的借款请求，以及按 Id 的借款人档案），账本在 `ModHook.OnGameLoadedEarly` 中读入（`src/Moneylending.cs`）。

## 17. 检查清单

1. `Init` 或 `OnEnable` 中用 `ModHelper.OnMessage(<你的 Mod ID>, "F5SnapshotSave:snapshot.v1.adapters", handler)` 监听。
2. 收到消息时按第 5 节核对登记函数，不通过就返回。
3. 每次交出一个新建的 `Dictionary<string, object>`，必需的键都在，成对的键成对出现。
4. 每个函数写在你的程序集中，委托类型与第 6 节完全相同，并按第 7.1 节限制类型。
5. 构建后对要发布的 DLL 运行 `generate_spec_fields.ps1`，字段表与 DLL 一起发布；收到消息时读取字段表文字。
6. 用键的对象：`find` 能在加强版读快照时找回同一个对象（结构为类型完全相同的值）。
7. 用附加数据时：`loadExtra` 只写以 `owner` 为键的弱引用表。
8. 在店里用存档键存档后，`Player.log` 中有 `spec adapter accepted: mod '<你的 Mod ID>'` 一行；顾客带着你的东西时，含 `could not be stored` 的行中没有你的 Mod。
