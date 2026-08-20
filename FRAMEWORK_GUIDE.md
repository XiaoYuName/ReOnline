# RAFramework 使用指南

## 简介

RAFramework 是一个通用的 Unity 游戏框架，专注于剧情对话、新手引导、存档管理等通用功能。它从 AFramework 剥离而来，去除了游戏特定的业务逻辑，保留了可复用的核心系统。

## 核心系统介绍

### 1. 剧情系统（Drama System）

剧情系统负责剧情对话的播放、历史记录、跳过/自动播放等功能。

**主要组件**：
- `DramaManager` - 剧情管理器
- `DramaDirector` - 剧情导演，控制播放流程
- `DramaContext` - 剧情上下文
- `DramaHistory` - 剧情历史记录

**使用示例**：
```csharp
// 播放剧情
DramaManager.Instance.PlayDrama(dramaId);

// 检查剧情是否已播放
bool hasPlayed = DramaManager.Instance.HasDrama(dramaId);

// 开启/关闭自动播放
DramaManager.Instance.isAutoDrama = true;
```

### 2. 引导系统（Tutorial System）

新手引导系统，支持步骤式引导、锚点定位、条件判断等。

**主要组件**：
- `TutorialManager` - 引导管理器
- `TutorialConfig` - 引导配置
- `TutorialAnchor` - 引导锚点
- `TutorialDatabase` - 引导数据库

**使用示例**：
```csharp
// 播放引导
TutorialManager.Instance.PlayTutorial(tutorialId);

// 注册锚点
TutorialAnchorRegistry.Register("ButtonName", buttonTransform);

// 检查引导是否完成
bool isCompleted = TutorialManager.Instance.IsTutorialCompleted(tutorialId);
```

### 3. 存档系统（Save System）

灵活的存档系统，支持多存档槽位、自动存档、存档摘要等。

**主要组件**：
- `SaveGameManager` - 存档管理器
- `ISaveable` 接口 - 可存档对象接口
- `GameSaveData` - 存档数据结构

**使用示例**：
```csharp
// 实现 ISaveable 接口
public class MyManager : MonoSingleton<MyManager>, ISaveable
{
    public string GUID => "MyManager";

    public void SaveData(GameSaveData data)
    {
        // 保存数据
        data.MyCustomData = myData;
    }

    public void LoadData(GameSaveData data)
    {
        // 加载数据
        myData = data.MyCustomData;
    }

    private void Start()
    {
        ((ISaveable)this).RegisterSaveable();
    }
}

// 手动存档
SaveGameManager.Instance.Save();

// 加载存档
SaveGameManager.Instance.Load(saveSlotId);
```

### 4. UI 系统（UI System）

基于栈的UI管理系统，支持UI层级、渐变效果、UI生命周期管理。

**主要组件**：
- `UISystem` - UI系统管理器
- `UIBase` - UI基类
- `PopLoadingUI` - 加载/渐变界面

**使用示例**：
```csharp
// 创建自定义UI
public class MyUI : UIBase
{
    public override void Init()
    {
        // 初始化UI
    }

    public override void Open()
    {
        base.Open();
        // UI打开时的逻辑
    }

    public override void Close()
    {
        base.Close();
        // UI关闭时的逻辑
    }
}

// 打开UI
UISystem.Instance.OpenUI<MyUI>("MyUI");

// 关闭UI
UISystem.Instance.CloseUI("MyUI");

// 渐变效果
await UIUtility.FadeInAsync(0.3f);
await UIUtility.FadeOutAsync(0.3f);
```

### 5. 场景管理（Scene System）

场景加载、切换、小游戏场景管理。

**主要组件**：
- `GameSceneManager` - 场景管理器
- `SceneController` - 场景控制器
- `SceneData` - 场景数据

**使用示例**：
```csharp
// 进入场景
GameSceneManager.Instance.EnterGameScene(mapSceneID, sceneID);

// 切换场景
GameSceneManager.Instance.OptionGameScene(sceneID);

// 退出场景
GameSceneManager.Instance.QuitGameScene();

// 进入小游戏
GameSceneManager.Instance.EnterMinGameScene(MinGameSceneType.ClawMachineScene, () => {
    // 进入完成后的回调
});

// 退出小游戏
GameSceneManager.Instance.QuitMinGameScene();
```

### 6. 背包系统（Inventory System）

物品管理、堆叠、消耗、解锁等功能。

**主要组件**：
- `InventoryManager` - 背包管理器
- `ItemInfo` - 物品信息
- `RuntimeItemInfo` - 运行时物品信息

**使用示例**：
```csharp
// 添加物品
InventoryManager.Instance.AddItem(itemId, count);

// 消耗物品
InventoryManager.Instance.ConsumeItem(itemId, count);

// 使用物品
InventoryManager.Instance.UseItem(itemId);

// 获取物品数量
int count = InventoryManager.Instance.GetItemCount(itemId);

// 注册物品变化事件
InventoryManager.Instance.RegisterAllItemChange(OnItemChanged);

// 解锁物品
InventoryManager.Instance.UlockItem(itemId);

// 检查物品是否解锁
bool unlocked = InventoryManager.Instance.HasItemUnlock(itemId);
```

### 7. 音频系统（Audio System）

音乐、音效的播放和管理。

**主要组件**：
- `AudioManager` - 音频管理器

**使用示例**：
```csharp
// 播放背景音乐
AudioManager.Instance.PlayMusic(musicKey);

// 停止音乐
AudioManager.Instance.StopMusic();

// 播放音效
AudioManager.Instance.PlaySound(soundKey);

// 设置音量
AudioManager.Instance.SetMusicVolume(volume);
AudioManager.Instance.SetSoundVolume(volume);
```

### 8. 本地化系统（Localization System）

多语言支持，基于 Unity Localization Package。

**主要组件**：
- `LanguageManager` - 语言管理器
- `LocalizationFontAsset` - 本地化字体

**使用示例**：
```csharp
// 切换语言
LanguageManager.Instance.SetLanguage("zh-CN");

// 获取当前语言
string currentLanguage = LanguageManager.Instance.GetCurrentLanguage();

// 设置全局变量
LanguageManager.Instance.SetGlobalVariablesSource("global", "PlayerName", playerName);

// 注册语言变化事件
LanguageManager.Instance.AddOnLanguageChanged(OnLanguageChanged);
```

### 9. 输入系统（Input System）

统一的输入管理，支持取消输入、输入消费等。

**主要组件**：
- `PlayerInputManager` - 输入管理器

**使用示例**：
```csharp
// 注册右键/ESC未消费事件（用于返回上一级）
PlayerInputManager.Instance.OnRightClickUnconsumed += OnBackPressed;
PlayerInputManager.Instance.OnEscUnconsumed += OnBackPressed;

// 检查是否有输入
bool hasInput = PlayerInputManager.Instance.HasAnyInput();
```

### 10. 玩家数据系统（GameData System）

玩家属性、时间槽、货币等数据管理。

**主要组件**：
- `GameDataManager` - 游戏数据管理器
- `PlayerData` - 玩家数据
- `PropertyBag` - 属性背包

**使用示例**：
```csharp
// 添加属性
GameDataManager.Instance.AddProperty(PropertyType.Coin, 100);

// 扣除属性
GameDataManager.Instance.RemoveProperty(PropertyType.Coin, 50);

// 设置属性
GameDataManager.Instance.SetProperty(PropertyType.Coin, 1000);

// 获取属性
int coinCount = GameDataManager.Instance.GetProperty(PropertyType.Coin).Value;

// 注册属性变化事件
GameDataManager.Instance.RegisterPlayerDataChange(OnPlayerDataChanged);

// 休眠（推进时间）
GameDataManager.Instance.Sleep();
```

## 快速开始

### 1. 项目设置

1. 确保已安装以下 Unity Packages：
   - Addressables
   - Localization
   - TextMeshPro
   - DOTween
   - UniTask
   - Odin Inspector

2. 创建必要的 ScriptableObject 配置：
   ```
   Assets/Create/Configs/GameSettingsDataManager
   ```

3. 设置 Addressables 分组和资源路径。

### 2. 场景设置

在初始场景中创建以下管理器：

```
Scene Hierarchy:
├── GameManager (挂载 GameManager.cs)
│   ├── CommonUI (挂载 CommonUI.cs)
│   └── PopLoadingUI (挂载 PopLoadingUI.cs)
├── UISystem (挂载 UISystem.cs)
├── AudioManager (挂载 AudioManager.cs)
├── LanguageManager (挂载 LanguageManager.cs)
├── SaveGameManager (挂载 SaveGameManager.cs)
├── GameDataManager (挂载 GameDataManager.cs)
├── GameSceneManager (挂载 GameSceneManager.cs)
├── InventoryManager (挂载 InventoryManager.cs)
├── DramaManager (挂载 DramaManager.cs)
├── TutorialManager (挂载 TutorialManager.cs)
└── PlayerInputManager (挂载 PlayerInputManager.cs)
```

### 3. Luban 配置表

确保配置了以下 Luban 表：

- `TbItemData` - 物品表
- `TbPropertyData` - 属性表
- `TbGameSceneData` - 场景表
- `TbDramaData` - 剧情表
- `TbUIPageData` - UI页面表
- `TbLocalzationKeyData` - 本地化键表

### 4. 初始化流程

GameManager 的初始化顺序：

```csharp
public async UniTask Initialized()
{
    await Addressables.InitializeAsync();
    await PlayerInputManager.Instance.Initialized();
    await AudioManager.Instance.Initialized();
    await UISystem.Instance.Initialized();
    await EffectsManager.Instance.Initialized();
    await SaveGameManager.Instance.Initialized();
    await InventoryManager.Instance.Initialized();
    await GuideManager.Instance.Initialized();
    await TutorialManager.Instance.Initialized();

    // 首屏准备好了再把遮罩淡掉
    await _popLoadingUI.FadeOutAsync(0.3f);
}
```

## GM调试指令

通过 SRDebugger 提供的GM指令：

- **属性类**：添加玩家属性（金币、游戏币等）
- **存档类**：手动存档
- **物品类**：添加所有物品（测试用）

## 常见问题

### Q: 如何添加新的可存档对象？

A: 实现 `ISaveable` 接口并在 `Start` 中注册：

```csharp
public class MyManager : MonoSingleton<MyManager>, ISaveable
{
    public string GUID => "MyManager";

    private void Start()
    {
        ((ISaveable)this).RegisterSaveable();
    }

    public void SaveData(GameSaveData data)
    {
        // 在 GameSaveData 中添加你的字段
        data.MyData = myData;
    }

    public void LoadData(GameSaveData data)
    {
        if (data?.MyData != null)
        {
            myData = data.MyData;
        }
    }
}
```

### Q: 如何扩展物品类型？

A: 继承 `ItemInfo` 或 `RuntimeItemInfo`：

```csharp
[Serializable]
public class MyCustomItemInfo : RuntimeItemInfo
{
    public string CustomField;

    public MyCustomItemInfo(int count) : base(count)
    {
        // 初始化
    }
}
```

### Q: 如何添加新的剧情指令？

A: 在 Luban 的剧情表中配置，剧情系统会自动解析和执行。

### Q: 如何优化资源加载？

A: 使用 Addressables 的引用计数机制：

```csharp
// 加载
var asset = await AssetsManager.Instance.LoadAssetsUniTask<GameObject>(assetKey);

// 释放（引用计数-1）
AssetsManager.Instance.FreeAsset(assetKey);
```

## 性能优化建议

1. **使用对象池**：频繁创建销毁的对象使用 `PathologicalGames.PoolManager`
2. **异步加载**：使用 UniTask 进行异步操作
3. **资源管理**：及时释放不用的 Addressables 资源
4. **UI优化**：使用 Canvas 分组，避免频繁重建
5. **事件清理**：在 OnDestroy 中反注册所有事件

## 扩展系统

框架预留了以下扩展点：

1. **角色系统** - `CharacterManager` 存根
2. **商店系统** - `ShopManager` 存根
3. **展会系统** - `ExhibitionManager` 存根
4. **工厂系统** - `FactoryItemStubs` 存根

根据项目需求选择实现或删除。

## 更多资源

- [Unity Addressables 文档](https://docs.unity3d.com/Packages/com.unity.addressables@latest)
- [Unity Localization 文档](https://docs.unity3d.com/Packages/com.unity.localization@latest)
- [UniTask GitHub](https://github.com/Cysharp/UniTask)
- [DOTween 文档](http://dotween.demigiant.com/documentation.php)

---

**版本**: 1.0  
**日期**: 2026-08-14
