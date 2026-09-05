# 国服资源解包、整理与 Unity 还原工作流

最后核对：2026-09-05

这份文档是国服资源工作的长期交接记忆。新对话只要先阅读本文，就应当能从当前状态继续，而不必重新猜测版本、路径、Spine 缩放或 Shader 参数来源。

本文把信息分成三类：

- **已验证**：已经在当前磁盘文件、报告或 Unity 6000.4.8f1 中核对。
- **工具行为**：由当前脚本实现决定；脚本改变后要同步更新本文。
- **待完成 / 推断**：不能当成已经完成或与原版完全一致。

## 1. 当前结论与不可忘记的约束

1. 当前完整整理基线是**简中服 Android `202608171854`**，不是日服。
2. 自包含的 AA 原包与清单位于 `D:\AssetsStudio\Rediv\CN_分类完成\_原始数据`。后续还原 Prefab、粒子、材质、Shader、Mesh、AnimationClip 或 NGUI 图集时，优先只使用这个整理目录，不依赖旧下载池。
3. 用户已明确要求重新下载时**跳过音频和视频**。因此 `_原始数据` 内没有 Sound/Movie；整理目录顶层现存的 `音频`、`视频` 是此前分类留下的旧快照，不能据此判断它们属于当前 AA 基线或已经更新。
4. 原游戏 Bundle 虽是 UnityFS，但头部 Unity 版本是 `0.0.0`。解包器的回退 Unity 版本必须使用 **`6000.0.58f2`**。
5. ReDiv Unity 工程使用 **Unity `6000.4.8f1`**。它与上一条的原游戏 Bundle 回退版本不是一回事。
6. 复杂分类时用户已经授权**直接移动**。当前分类完成，共移动 294,327 个文件，复核项 0，移动失败 0。
7. NGUI 上次批量生成的独立 `*_切割PNG` 已按要求删除；原始图集 PNG 和 `atlas_manifest.json` 保留。不要把批量切图重新塞回整理目录。
8. Spine 角色已经按正确基准重新完成 730 个 `sdnormal` 外观的 3.6 → 3.8.99 → 4.3.23 批处理。**骨架 Scale 必须在 3.8 导入阶段设为 `0.5`，且只应用一次；创建 3.8 和升级 4.3 时必须暂时隐藏图片，之后才恢复完整画布并导出。**
9. 怪物已经组装为 Spine 3.6 的“一怪物一文件夹”，但**尚未批量升级到 4.3**。当前 4.3 脚本只识别角色目录布局，不能直接声称也支持怪物。
10. `VariantCard.shader` 和通用材质一键还原工具已在 Unity 中工作；`still_unit_105831` 与背景 `bg_500020` 是已验证样例。工具会按原 Shader PathID 识别 VariantCard 主材质，不能假定所有背景都使用同一个 Shader。完整视觉还依赖正确的通用纹理、PathID 映射、导入设置及原始参数。
11. **还原出来的材质拿去做 UGUI 背景时，`renderQueue` 必须从原值 2000 改成 3000。** 2000 是原包 bin 里的值（还原验证要保留），但 UGUI 默认 3000、queue 小的先画 ⇒ 那张背景会**被所有 UI 元素盖住**，症状是「界面明明开着，别的界面元素还浮在上面」，看着像层级错了。2026-08-27 副本区域背景 `bg_500170_mat.mat` 踩过（已改成 3000），详见 9.3.1。工程里现在用着的三张（2026-08-27 逐个核对过）：立绘 `still_unit_105831` = **2000**（还原样例，没用在 UI 上，保持原值）；城镇背景 `bg_500020` = **2000**（**世界空间 `SpriteRenderer`**，2000 恰好让它比角色（Sprite 默认 3000）先画、也就是排在角色后面，正是想要的效果，所以别动它）；副本背景 `bg_500170` = **3000**（UI，2026-08-27 从 2000 改的）。
    换句话说这条只对**「把还原材质挂到 UGUI 上」**成立；世界空间那边 2000 反而是对的。

## 2. 路径总览与数据真相源

### 2.1 工作区

| 用途 | 绝对路径 |
|---|---|
| 资源工作区 | `D:\AssetsStudio\Rediv` |
| 下载及处理脚本 | `D:\AssetsStudio\Rediv\download` |
| 当前整理完成根目录 | `D:\AssetsStudio\Rediv\CN_分类完成` |
| Unity/Git 仓库 | `D:\GitLab\REDIV` |
| Unity 工程 | `D:\GitLab\REDIV\ReDiv_Online` |
| AnimeStudio CLI | `D:\Software\AssetStudio\AnimeStudio-net10\AnimeStudio-net10-7cfb26b9c158f6150c887f9157de97a3fa7672e1\AnimeStudio.CLI.exe` |
| Spine 启动器 | `D:\Software\SpineRuntime\Spine\Spine.com` |

### 2.2 自包含国服原始数据

```text
D:\AssetsStudio\Rediv\CN_分类完成\_原始数据\
├─ AssetBundles\Android\202608171854\a\
├─ Manifest\AssetBundles\Android\202608171854\
├─ 工具\
│  ├─ requirements.txt
│  └─ restore_ngui_atlas_manifests.py
└─ 数据说明.txt
```

已验证数量：

| 数据 | 文件数 | 总字节数 | 校验 |
|---|---:|---:|---|
| AssetBundles | 74,116 | 6,859,588,323 | 相对路径和尺寸：缺失 0、多余 0、不一致 0 |
| Manifest | 86 | 12,525,440 | 已保留 |

这组原始数据是恢复 Unity 对象依赖关系的首选来源。分类后的 `.png`、`.json`、`.bin`、`.bytes` 适合查找和使用，但不能代替 AA 原包中的对象表、PPtr/PathID 和依赖关系。

## 3. 版本发现与国服 AA 下载

主脚本：

```text
D:\AssetsStudio\Rediv\download\download_cn.py
```

### 3.1 自动获取版本的顺序

脚本 `get_latest()` 当前按以下顺序获取 `manifest_ver` 和 CDN：

1. 国服状态接口 `source_ini/get_maintenance_status`；请求头包含国服平台、资源版本及 Unity 版本信息。
2. `Expugn/priconne-database` 的 CN 版本缓存。
3. 两者都失败时使用脚本内置回退：版本 `202608171854`，CDN `l1-prod-patch-gzlj.bilibiligame.net/client_ob_771/`。

“脚本回退值”只能代表已知可用版本，不能当成永远最新。每次新补丁先运行清单模式并查看控制台打印的版本来源。

### 3.2 推荐命令

```powershell
Set-Location 'D:\AssetsStudio\Rediv\download'
& '.\.venv\Scripts\python.exe' '.\download_cn.py' --manifest-only
& '.\.venv\Scripts\python.exe' '.\download_cn.py' --threads 48
```

需要锁定版本时：

```powershell
& '.\.venv\Scripts\python.exe' '.\download_cn.py' --version 202608171854 --threads 48
```

脚本特性：

- 默认平台 Android，可用 `--platform iOS` 切换。
- 支持 HTTP Range 续传；已存在且尺寸一致的文件会跳过。
- 先下载根清单和全部子清单，再按内容哈希从 pool 下载。
- 同一逻辑路径优先保留完整质量记录，避免 `_s` 低清记录覆盖。
- 下载失败会写 `download_failed.txt`；未清零前不得宣称完整。

当前要求是只下载 AA，**不要运行** `download_cn_media.py`。旧的 `download.py` 和 `prd-priconne-redive.akamaized.net` 目录属于日服流程，不能用于国服整理。

### 3.3 下载池和整理快照的关系

`download_cn.py` 的下载池通常位于：

```text
D:\AssetsStudio\Rediv\download\cn\<国服CDN主机>\<CDN路径>\
```

当前整理快照已经把所需 AA 和 Manifest 复制进 `_原始数据` 并做过逐项校验。除非升级版本或修复损坏，不需要重新下载。

如果将来版本更新，建议先用**新的版本号暂存目录**下载、解包和核验，不要直接混入 `202608171854`，确认后再生成新的整理快照和报告。

## 4. AA 解包流程与保留规则

### 4.1 已采用的流程

当前分类报告记录的历史解包源为：

```text
D:\AssetsStudio\Rediv\unpacked_cn_202608171854_unitypy\bundles
D:\AssetsStudio\Rediv\unpacked_cn_202608171854_unitypy\exception_recovered_png
```

这两个是当时的 UnityPy 中间目录；现在不应假定它们仍存在。可重复使用的真相源是 `_原始数据` 和分类报告。

解包顺序：

1. 从 Manifest 确定逻辑 bundle 名和对应 AA 文件。
2. 以 `6000.0.58f2` 作为 Unity 版本回退解析 UnityFS。
3. 常规对象由 UnityPy 导出，保留资源容器路径。
4. UnityPy 异常或未生成 PNG 的 Texture2D 进入异常补导，历史补导 PNG 数为 397。
5. 不只导出最终 PNG：同时保留 JSON、原始 `.bin`、`.bytes`、Mesh、Shader、Material、Prefab 等后续还原所需数据。
6. 分类阶段通过 MasterData 建角色 ID → 中文名映射，再按资源路径和类型移动。

当前工作区里没有找到当时使用的完整“全量 UnityPy 解包/复杂分类”脚本，只有产物、索引和报告。因此未来升级版本前，应先把这两段流程重新固化为脚本；不要假装现有下载脚本会自动完成解包和分类。

### 4.2 AnimeStudio 的正确模式

AnimeStudio 用于异常补导、对象映射和精确 PathID 检查。当前国服包要使用 `--game Normal`；曾尝试 `--game UnityCN` 时没有正确得到资源。

典型的 Texture2D + AssetMap 命令形态：

```powershell
$anime = 'D:\Software\AssetStudio\AnimeStudio-net10\AnimeStudio-net10-7cfb26b9c158f6150c887f9157de97a3fa7672e1\AnimeStudio.CLI.exe'
$bundle = 'D:\AssetsStudio\Rediv\CN_分类完成\_原始数据\AssetBundles\Android\202608171854\a\bg_animationtexture.unity3d'
$out = 'D:\AssetsStudio\Rediv\_anime_output\bg_animationtexture'
& $anime $bundle $out --game Normal --unity_version 6000.0.58f2 --types Texture2D --export_type JSON --map_op AssetMap --map_type JSON --map_name animationtexture_asset_map
```

参数名以当前 CLI 的 `--help` 为准。已生成并实际使用的精确映射：

```text
D:\AssetsStudio\Rediv\CN_分类完成\场景与背景\背景\bg\animationtexture_bundle\animationtexture_asset_map.json
```

该映射来自 `bg_animationtexture.unity3d`，包含 84 个资源条目及 `Name`、`Container`、`Source`、`PathID`、`Type` 等字段。

### 4.3 不能丢弃的伴随数据

任何对象还原都至少要保留以下组合：

- PNG/TGA 等像素文件。
- 对应导出 JSON，包括 Sprite Rect、Border、Padding、Offset 等。
- 原始 `.bin` / `.bytes`，用于解析 Material、Prefab、Animation、CYSP 或未知对象。
- 来源 bundle 名、容器路径、PathID 和依赖 bundle。
- 原始 AA bundle 与 Manifest，作为无法从扁平导出恢复关系时的最终依据。

Prefab 和粒子不能仅凭一张 PNG 或单个 `.bin` 保证完整复原。它们通常依赖多对象 PPtr、Transform 层级、Material、Shader、Texture、Mesh、AnimationClip 和 MonoBehaviour 类型信息；要从 AA 的对象图逐项恢复。

## 5. 复杂分类目录与报告

当前根目录：

```text
D:\AssetsStudio\Rediv\CN_分类完成\
├─ _索引与报告\
├─ _原始数据\
├─ UI\
│  ├─ Common_通用\
│  ├─ 角色界面\
│  ├─ 剧情\
│  ├─ 活动\
│  └─ ...
├─ 角色\
│  └─ <中文名>_<unit_id>\
│     ├─ 头像\
│     ├─ 立绘\
│     ├─ 战斗小人\
│     ├─ Spine\
│     ├─ 视频\
│     └─ 语音\
├─ 敌人与Boss\
├─ 场景与背景\
├─ 动画与特效\
├─ Spine与剧情模型\
├─ 活动\
├─ 剧情\
├─ 漫画\
├─ 数据与配置\
├─ 探索与旅行\
├─ 通用资源\
├─ 小游戏\
├─ 字体\
├─ Shader\
├─ Spine组装完成_3.6\
└─ Spine导出完成_4.3\
```

历史分类共处理 294,327 个文件：常规资源 263,058、异常补导 PNG 397、视频 2,920、语音音频 27,952。所有文件状态均为 `moved`，未分类清单只有表头，`移动失败.json` 是空数组。

权威报告：

```text
D:\AssetsStudio\Rediv\CN_分类完成\_索引与报告\分类索引.csv
D:\AssetsStudio\Rediv\CN_分类完成\_索引与报告\分类统计.json
D:\AssetsStudio\Rediv\CN_分类完成\_索引与报告\角色ID名称映射.csv
D:\AssetsStudio\Rediv\CN_分类完成\_索引与报告\角色映射来源.json
D:\AssetsStudio\Rediv\CN_分类完成\_索引与报告\未分类清单.csv
D:\AssetsStudio\Rediv\CN_分类完成\_索引与报告\移动失败.json
```

`分类索引.csv` 保存来源路径、目标路径和分类原因，是追踪文件来源及未来重建分类器的最重要记录。当前角色映射从解包后的 `master.bytes` 取得，共 364 个条目。

### 5.1 战斗背景按场景组二次分类

2026-09-05 已把原先扁平混放的战斗背景和对应前景按“场景组”重新整理。目标目录：

```text
D:\AssetsStudio\Rediv\CN_分类完成\场景与背景\背景\bg\battle\background\bg_<场景组ID>\
```

原资源 ID 的**最后一位是同一场景组内的成员序号**，所以场景组 ID 的统一规则是
“去掉数字资源 ID 的最后一位”，不能按完整资源 ID 一文件夹：

- `bg_100011` ～ `bg_100019` → `bg_10001`。
- `bg_81000105`、`bg_81000106` → `bg_8100010`。
- `bg_81000110`、`bg_81000111` → `bg_8100011`。

每个场景组目录同时收纳主背景 PNG、Mask、Material bin 和原
`battle\foreground` 中能够按 ID 对应的 Front PNG；这样一组场景的组成资源无需再跨两个
扁平目录查找。文件名保持原样，没有重命名。

本次共移动 4,265 个文件（2,119,179,448 字节）到 410 个场景组：背景 PNG 1,300、
Front PNG 1,110、Mask 926、Material bin 929。逐文件尺寸与 SHA-256 复核错误 0、目标冲突 0。
`battle\foreground` 只剩 `icon_unit_314901.png`、`icon_unit_315001.png`、
`icon_unit_315002.png`；它们没有背景 ID，不能可靠推断所属场景，因此保留原位待复核。

可重复脚本和报告：

```text
D:\AssetsStudio\Rediv\download\regroup_cn_battle_backgrounds.py
D:\AssetsStudio\Rediv\CN_分类完成\_索引与报告\战斗背景场景重分类执行报告.json
D:\AssetsStudio\Rediv\CN_分类完成\_索引与报告\战斗背景场景重分类执行索引.csv
```

脚本默认只预演并写预演报告，确认没有冲突后加 `--apply` 才会移动。原
`分类索引.csv` 继续保存第一次全量分类时的来源与落点；二次分类后的当前路径以本节的执行索引为准，
两份索引共同构成完整追踪链，均不得删除。

## 6. NGUI 图集、JSON、Border 与 Padding

### 6.1 当前保存状态

原始图集 PNG 与恢复后的 `atlas_manifest.json` 保存在各分类目录旁。恢复工作基于 AA 原包完成：

- 图集作业：555
- Sprite：15,240
- 含 Border：1,165
- 含 Padding：6,855
- 状态：555 个 manifest 全部写入

报告：

```text
D:\AssetsStudio\Rediv\CN_分类完成\_索引与报告\NGUI图集JSON恢复报告.json
D:\AssetsStudio\Rediv\CN_分类完成\_索引与报告\NGUI图集JSON索引.csv
D:\AssetsStudio\Rediv\CN_分类完成\_索引与报告\NGUI图集切割统计.json
D:\AssetsStudio\Rediv\CN_分类完成\_索引与报告\NGUI图集切割索引.csv
```

批量独立切图已经删除。清理脚本保留在：

```text
D:\AssetsStudio\Rediv\CN_分类完成\清理批量切图.ps1
```

### 6.2 Unity 编辑器工具

脚本：

```text
D:\GitLab\REDIV\ReDiv_Online\Assets\Editor\UITools\NGUIAtlasSpriteImporterWindow.cs
```

菜单：

```text
Tools > Rediv > NGUI 图集切割与 Sprite 导入
```

当前工具支持：

- 拖入或选择当前工程 `Assets` 下的输出目录。
- 拖入原始图集 PNG 和对应 `atlas_manifest.json`。
- “解析并预览”。
- “切割并自动导入”：按 JSON 命名、坐标切成独立 PNG，并写 Unity Sprite Border。
- “复制图集并按 JSON 设置 Multiple Sprites”：不覆盖源 PNG，复制一份图集并写入多 Sprite Rect、名称、Border 及 Padding 元数据。

NGUI Padding 与 Unity Sprite 的表达方式不同：

- 独立 PNG 模式开启“还原透明 Padding”时，会扩展透明画布，并把 Padding 加入应用到 Unity 的 Border。
- Multiple Sprite 图集副本保持原像素不变，原始 Padding 写入 `SpriteRect.customData`。
- NGUI Border 含负数或超出 Rect 时，工具会钳制/按比例压缩成 Unity 可接受值；原始值仍保留在 `customData`。

不要再按“原 PNG 文件名必须唯一匹配某个 JSON Sprite”处理单图副本。当前实现的正确单图需求是：复制整张原图集为 Multiple Sprite 图集，由 JSON 一次性写入全部 Sprite 信息；如需独立文件，再用切割模式。

## 7. Spine：CYSP 组装为 3.6

### 7.1 组装脚本和原理

脚本：

```text
D:\AssetsStudio\Rediv\download\assemble_cn_spine.py
```

公主连结的 Spine 数据并非每个角色天然就是一个完整 skel。角色通常由装配模块、通用战斗动画、武器模块和角色特殊动画组合；脚本扫描 `.cysp.bytes`、按 MasterData/命名关系合并并生成可读的 Spine 3.6.39 骨架和 atlas。源目录不会修改，输出优先硬链接，失败时复制。

推荐命令：

```powershell
Set-Location 'D:\AssetsStudio\Rediv\download'
& '.\.venv\Scripts\python.exe' '.\assemble_cn_spine.py' --root 'D:\AssetsStudio\Rediv\CN_分类完成'
```

测试时可用 `--limit` 和 `--dry-run`；只跑一类可用 `--characters-only` 或 `--monsters-only`；需要完全独立物理副本时加 `--copy`。

输出布局：

```text
Spine组装完成_3.6\
├─ 角色\<角色名_ID>\外观\<资源ID_类型>\
├─ 怪物\<怪物名_ID>\
├─ _报告\
└─ README.json
```

当前有 332 个角色根目录、730 个已验证 `normal` 外观，以及 1,941 个怪物根目录。

### 7.2 3.6 组装与运行时验证结果

组装报告：

- 总数 3,401
- `ok` 3,400
- `not_spine` 1
- 组装阶段错误 0

官方 3.6 运行时验证使用 `spine-libgdx 3.6.53.1`：

| 类型 | 通过 | 错误/非 Spine | 处理原则 |
|---|---:|---:|---|
| 角色默认 normal | 730 | 0 | atlas 目录使用这一版 |
| 角色可选 full | 656 | 74 | 仅作可选研究，不要盲目替换 normal |
| 怪物 | 1,900 | 40 错误 + 1 非 Spine | 失败目录仍保留 CYSP 和 atlas，供自定义运行时研究 |

报告和验证工具：

```text
D:\AssetsStudio\Rediv\CN_分类完成\Spine组装完成_3.6\_报告\assembly_report.json
D:\AssetsStudio\Rediv\CN_分类完成\Spine组装完成_3.6\_报告\assembly_report.csv
D:\AssetsStudio\Rediv\CN_分类完成\Spine组装完成_3.6\_报告\runtime_validation_summary.json
D:\AssetsStudio\Rediv\CN_分类完成\Spine组装完成_3.6\_报告\runtime_validation_results.csv
D:\AssetsStudio\Rediv\download\Spine36BinaryValidator.java
D:\AssetsStudio\Rediv\download\merge_spine_validation.py
```

## 8. Spine：3.6 → 3.8.99 → 4.3.23

### 8.1 工具和版本

批处理脚本：

```text
D:\AssetsStudio\Rediv\download\batch_export_cn_spine.py
```

默认依赖：

- Spine CLI：`D:\Software\SpineRuntime\Spine\Spine.com`
- 3.6 二进制转 JSON：`D:\AssetsStudio\Rediv\Spine\SpineSkeletonDataConverter\build-ninja\SpineSkeletonDataConverter.exe`
- 中间 Spine：3.8.99
- 目标 Spine：4.3.23
- 长路径/中文路径中转：`D:\AssetsStudio\Rediv\SpineBatchLinks`、`D:\AssetsStudio\Rediv\SpineBatchSourceLinks`

Spine 4.3.23 曾因 NVIDIA 信息浮窗/游戏内覆盖导致启动异常。已关闭 NVIDIA App 的“信息浮窗”并重装后，用户确认 4.3 可以打开。若 CLI 又卡住，先验证桌面端 Spine 4.3.23 能正常进入编辑器。

### 8.2 必须按顺序执行的阶段

```powershell
Set-Location 'D:\AssetsStudio\Rediv\download'
& '.\.venv\Scripts\python.exe' '.\batch_export_cn_spine.py' status
& '.\.venv\Scripts\python.exe' '.\batch_export_cn_spine.py' prepare
& '.\.venv\Scripts\python.exe' '.\batch_export_cn_spine.py' project38
& '.\.venv\Scripts\python.exe' '.\batch_export_cn_spine.py' project43
& '.\.venv\Scripts\python.exe' '.\batch_export_cn_spine.py' unpack
& '.\.venv\Scripts\python.exe' '.\batch_export_cn_spine.py' export43
```

也可用 `all` 连续执行。单个测试用 `--match` 或 `--limit`，重做已有文件才加 `--force`。

各阶段含义：

1. `prepare`：3.6 二进制转 JSON。
2. `project38`：脚本临时隐藏 `images`，以 **Scale `0.5`** 导入 JSON，生成 3.8.99 `.spine` 工程；这样 Spine 不会按裁切图或完整画布尺寸重写 JSON 附件几何。
3. `project43`：脚本继续临时隐藏 `images`，用 4.3.23 打开 3.8 工程并保存 `.4.3.spine`；**此处不得再次缩放**。
4. `unpack`：工程生成后，使用 Spine 4.3 根据 atlas 的 `orig` 和 `offset` 恢复散图原始透明画布。
5. `export43`：放回完整画布后导出 4.3 JSON、`atlas.txt` 和 PNG。

此前“贴图全部堆叠、骨骼看似错位”的根因不是单纯骨骼缩放，而是散图丢失 atlas 的 `orig` 画布和 `offset`。进一步以用户确认正确的 `SpineScaleCheck\fixed_output\优衣_100201\外观\100211_sdnormal` 做字段级对照后确认：工程创建时也不能让 Spine 看到图片，否则会改写部分工程数据。最终正确顺序是“隐藏图片建 3.8 → 隐藏图片升 4.3 → 恢复完整画布 → 导出”。Scale 0.5 仍然是正确导入比例，但不能代替画布和步骤顺序修复。

`batch_export_cn_spine.py all --force` 已固化上述顺序。单独执行阶段时也应严格按本节顺序，不得把 `unpack` 提前到 `project38` 之前。

### 8.3 当前角色输出状态

输出：

```text
D:\AssetsStudio\Rediv\CN_分类完成\Spine导出完成_4.3\角色\<角色名_ID>\外观\<资源ID_sdnormal>\
```

每个外观包含：

```text
<id>.spine          # 3.8.99 工程
<id>.4.3.spine      # 4.3.23 工程，后续加事件优先打开此文件
<id>.json           # 4.3 导出数据
<id>.atlas.txt      # 4.3 图集描述
<id>.png            # 4.3 打包图集
images\             # 恢复透明画布后的散图
_intermediate\<id>.3.6.json
```

2026-08-24 20:44 已按正确基准强制重建并验证：730 个 3.8 工程、730 个 4.3 工程、730 个最终 JSON、730 个 atlas.txt，`images_full_canvas=730`，失败 0，staging/tmp 残留 0。图像文件总量包含 730 张打包图集和大量 `images` 散图。

正式优衣 `100211_sdnormal` 与正确基准做递归 JSON 对照，只剩 `skeleton.audio`、`skeleton.images` 的输出目录和由路径产生的 `skeleton.hash` 三项差异，骨架/动画无其他差异；最终 atlas 和 PNG 的 SHA-256 完全相同。修复报告：

```text
D:\AssetsStudio\Rediv\CN_分类完成\Spine导出完成_4.3\_画布与工程顺序修复验证.json
```

批处理状态：

```text
D:\AssetsStudio\Rediv\CN_分类完成\Spine导出完成_4.3\_批处理清单.json
D:\AssetsStudio\Rediv\CN_分类完成\Spine导出完成_4.3\_批处理进度.jsonl
```

日志保留了历史重试，所以能看到少量旧 `failed` / `blocked` 行；不能只数日志错误行判断最终状态。最终应同时检查 730 个 `.spine`、730 个 `.4.3.spine`、730 个最终 `.json` 和 730 个 `.atlas.txt` 是否存在且非空。

### 8.4 怪物 4.3 的当前缺口

`Spine组装完成_3.6\怪物` 已存在，且一怪物一个目录；`Spine导出完成_4.3\怪物` 目前不存在。

当前 `batch_export_cn_spine.py` 的发现逻辑写死为：

```text
*/外观/*_sdnormal/*.skel
```

因此它只支持角色外观。下一步要先扩展脚本的数据模型和 `discover()`，兼容怪物的一层目录与命名，再用少量怪物验证 atlas `orig/offset`、Scale 0.5 和 4.3 输出，最后全量执行。40 个 3.6 运行时错误怪物和 1 个非 Spine 文件应单独保留失败报告，不要强行标记成功。

## 9. VariantCard 背景 Shader 与材质参数复原

### 9.1 Unity 侧文件

```text
D:\GitLab\REDIV\ReDiv_Online\Assets\Shader\ReDiv\VariantCard.shader
D:\GitLab\REDIV\ReDiv_Online\Assets\Shader\ReDiv\Editor\ReDivVariantCardShaderGUI.cs
D:\GitLab\REDIV\ReDiv_Online\Assets\Shader\ReDiv\Editor\ReDivVariantCardMaterialRestorerWindow.cs
```

Shader 名：`Cygames/VariantCardShader`。当前实现是面向 URP 的还原版本。

通用一键工具菜单：

```text
Tools > ReDiv > VariantCard > 一键还原材质
```

工具流程：

1. 拖入整理目录外部的 `still_unit_xxxxxx` 或 `bg_xxxxxx` 文件夹。
2. 点击自动定位通用纹理与 `animationtexture_asset_map.json`，必要时手动选择。
3. 选择 Unity 工程 `Assets` 下输出目录。
4. “解析并检查”会按原 Shader PathID `8273635072764025099` 选择 VariantCard 主材质，并确认所有 Texture PathID 已匹配；同目录有其他 Shader 的材质不会被误选。
5. “一键还原”生成纹理副本、Material 和预览 Prefab；源目录不修改。

本地纹理名同时兼容立绘的 `still_unit_xxxxxx_mask.png`、常规背景的
`bg_xxxxxx_mask.png`、另一批背景的 `bg_mask_xxxxxx.png`，并以目录名和唯一语义匹配
处理 `bg_bg_xxxxxx_mat`、目录 ID 与材质名不一致等已发现变体。若材质的原 Shader
PathID 不同，工具会明确报告它不是当前 VariantCard，而不是强行套用。

解析保留 Unity 6000 Material 的 Shader PPtr、keywords、LightmapFlags、instancing、double-sided GI、render queue、tag map、disabled passes、TexEnv/PPtr/scale/offset、ints、floats、colors 等。工具的二进制布局已针对当前国服 `202608171854` / 原包回退 `6000.0.58f2` 验证，不能未经验证就宣称适用于所有 Unity 版本。

### 9.2 已验证样例 `still_unit_105831`

源数据：

```text
D:\AssetsStudio\Rediv\CN_分类完成\角色\佩可莉姆_105801\立绘\bg\still_unit_bundleroot\still_unit_105831
```

包括主图、mask、`offset_105831.json`、`still_unit_105831_mat.bin`、effect bin/png 和 prefab bin。OffsetY 为 -30。

已解析材质：

- keywords：`USE_BACK2`、`USE_BACK_1_2_FLASH`、`USE_FRONT_BACK_1_2_DISTORTION`
- render queue：2000
- Texture 槽：7
- float：77
- color：5
- `_MainTex`、`_MaskTex` 使用本地纹理。
- 其余五个通用纹理由 PathID 映射到 `tx_foil_flare`、`tx_foil_flare_dark`、`tx_foil_custom_distortion_06`、`tx_foil_dust_grain`、`tx_foil_light`。

Unity 输出：

```text
D:\GitLab\REDIV\ReDiv_Online\Assets\Shader\ReDiv\Restored\still_unit_105831\still_unit_105831_mat.mat
D:\GitLab\REDIV\ReDiv_Online\Assets\Shader\ReDiv\Restored\still_unit_105831\still_unit_105831_Preview.prefab
D:\GitLab\REDIV\ReDiv_Online\Assets\Shader\ReDiv\Restored\still_unit_105831\Textures\
```

已在 Unity 6000.4.8f1 核对：77 floats、5 colors、7 组 scale/offset、keywords 和 queue 均与解析值一致；Shader 编译错误 0、Console 错误 0。

### 9.3 已验证背景样例 `bg_500020`

源数据：

```text
D:\AssetsStudio\Rediv\CN_分类完成\场景与背景\背景\bg\main_bundleroot\bg_500020
```

原 `bg_500020_mat.bin` 的 Shader PPtr 为 `FileID 1 / PathID 8273635072764025099`，
与已验证立绘 VariantCard 材质相同。此前一键工具失败不是 Shader 不同，而是只尝试
`bg_500020_mask.png`，实际本地文件名为 `bg_mask_500020.png`。

已在 Unity 6000.4.8f1 重新生成并核对：

- 7 个纹理槽全部匹配；`_MaskTex` 精确指向 `bg_mask_500020`。
- 通用纹理为 `tx_foil_cloud`、`tx_foil_light`、`tx_foil_distortion`、`tx_shadow2`、`tx_foil_light`。
- 77 个 Float、5 个 Color、全部 Texture Scale/Offset 与原始 bin 字段级一致。
- Keyword 为 `USE_FRONT2`，Render Queue 为 `2000`，Shader 为 `Cygames/VariantCardShader`。
- Shader 编译错误 0。

Unity 输出：

```text
D:\GitLab\REDIV\ReDiv_Online\Assets\Shader\ReDiv\Restored\bg_500020\bg_500020_mat.mat
D:\GitLab\REDIV\ReDiv_Online\Assets\Shader\ReDiv\Restored\bg_500020\bg_500020_Preview.prefab
D:\GitLab\REDIV\ReDiv_Online\Assets\Shader\ReDiv\Restored\bg_500020\Textures\
```

附加回归：`bg_501470`（`bg_bg_...` 材质名）、`bg_530011`（同目录多个材质）、
`bg_710081`（目录 ID 与材质名不一致）均可完整解析；`bg_500114` 的 Shader PathID
为 `8992227300827615762`，工具会正确拒绝，不会误判为 VariantCard。

### 9.3.1 ⚠️ 还原出来的材质拿去做 UI 背景时要改 renderQueue

原包解析出来的 VariantCard 材质 `renderQueue` 是 **2000**，这是**原值，还原验证要保留**。
但如果把这张材质用在 **UGUI 的 `RawImage`** 上（副本区域背景就是这么用的），
2000 会让它**被所有 UI 元素盖住**（UI 默认 3000，queue 小的先画）——
症状是「界面明明开着，别的界面元素还浮在上面」，很难往回追（2026-08-27 踩过，查错了两轮）。

所以：**还原验证用 2000；真正拿去当 UI 背景的那份要改成 3000。**
`bg_500170_mat.mat` 已经改成 3000（它现在的用途就是副本区域背景）。
客户端那边 `PopDungeonUI.CheckRenderQueue` 会在挂背景时报错提醒，但不会自动改资产。
细节见 [../CLAUDE.md](../CLAUDE.md) 第 5 节「副本界面 → 区域背景」。

### 9.4 Mask 和“完全一致”的边界

当前 VariantCard 实现按通道使用 mask：R 控制前层、G 控制后层、B 控制扭曲影响。mask 是 Shader 输入纹理，不是简单透明裁剪图。

现有还原保留了可从 Material 二进制和编译 Shader 线索恢复的参数及纹理映射，并已验证 Unity 侧参数一致。但“与原版每个像素完全一致”仍需相同渲染管线、Blend/Color Space、纹理导入、时间参数和效果资源逐帧对比后才能下结论。文档和工具均不得把尚未做的逐帧对比写成已证明。

源游戏导出的编译 Shader 线索位于：

```text
D:\AssetsStudio\Rediv\CN_分类完成\Shader\shader\variantcardshader\variantcardshader.shader
```

它不是可直接编译的完整 Shader 源码，只能作为 Properties、keywords 和编译子程序线索。

## 10. Prefab、粒子、UB 与其他待还原内容

### 10.1 粒子和 Prefab

AA 原包已经自包含，因此原则上可以继续恢复粒子、材质、Mesh、AnimationClip 和 Prefab 结构。正确顺序是：

1. 从目标 Prefab/Material 的 bundle 对象表开始。
2. 递归解析 PPtr：先查同 bundle PathID，再查 dependency/fileID 指向的 bundle。
3. 导出并记录 Transform/Component 层级、Material、Texture、Mesh、AnimationClip、MonoBehaviour。
4. 在 Unity Editor 中生成资产和 Prefab，保留无法识别字段的原始 JSON/bin 旁证。
5. 用原版录屏或可运行客户端做视觉/行为对比。

仅依靠分类后的 JSON 或单个 AA 文件不一定足够；依赖 bundle、脚本类型定义和运行时 Shader 逻辑缺失时，只能部分复原。不要删除原始 `.bin` 和 AA。

### 10.2 UB 技能演出

当前分类历史中存在 `视频/技能演出`，说明至少部分技能演出包含视频资源；同时 AA 中也有动画与特效资源。因此不能用“全部透明视频”或“全部纯特效”概括所有 UB，应按每个技能的依赖和播放组件逐个判断。

因为当前自包含 `_原始数据` 按用户要求跳过 Movie/Sound，若下一步要精确追踪某个 UB 的视频部分，需单独下载该版本 Movie 清单/文件；这不影响 AA 内纯特效和 Prefab 的研究。

## 11. 新对话续接检查表

新对话开始时按顺序执行：

1. 阅读本文，不要重新使用日服脚本或把 Unity 工程版本误当 Bundle 回退版本。
2. 查看 `D:\GitLab\REDIV` 的 `git status`，保留用户已有修改；未经要求不要提交、回退或删除。
3. 查看 `_原始数据\数据说明.txt`、分类统计和目标子流程报告，确认当前版本与完成数。
4. 若继续角色 Spine，先运行 `batch_export_cn_spine.py status`；不要再次 Scale。
   `images_full_canvas` 必须等于 `items`，且项目创建/升级阶段必须隐藏图片。
5. 若继续怪物 Spine，先改脚本以支持怪物布局，抽样验证后再全量。
6. 若继续 Shader/Material，优先用通用一键工具和 PathID map，不再写死单个 105831。
7. 若继续 Prefab/粒子，从 AA 对象图和依赖关系出发，不能只看扁平 PNG/bin。
8. 若更新国服版本，先 `--manifest-only` 获取版本并建立新的暂存快照，完成下载、解包、异常补导、分类和逐项核验后再替换基线。

当前最明确的后续任务是：

- 扩展 Spine 4.3 批处理以支持怪物并输出一怪物一文件夹。
- 为任意 `still_unit_xxxxxx` 做更多 VariantCard 样本回归，验证纹理和参数映射覆盖率。
- 编写可重复的全量 UnityPy 解包与复杂分类脚本；当前只有结果和权威报告，脚本本身未保留。
- 选择一个粒子/Prefab 样本，实现从 AA PPtr 依赖到 Unity Prefab 的端到端恢复。
