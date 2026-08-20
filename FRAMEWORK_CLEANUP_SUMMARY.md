# RAFramework 框架清理总结

## 概述
RAFramework 是从 AFramework 剥离出来的通用游戏框架，保留了对话、引导、存档等通用系统，移除了游戏特定的业务逻辑。

## 已完成的工作

### 1. 从 AFramework 拷贝的通用系统

#### 已拷贝文件：
- `PopLoadingUI.cs` - 通用加载/渐变界面
- `EffectsManager.cs` - 特效管理器
- `GuideManager.cs` - 引导管理器（娃娃机系统）
- `SceneController.cs` - 场景控制器
- `ClawMachineSettingData.cs` - 娃娃机配置数据

### 2. 创建的 ScriptableObject 配置管理器

#### 新建文件：
- `GameSettingsDataManager.cs` - 游戏设置数据管理器
  - 位置: `Assets/ScriptableObject/GameSettings/`
  - 包含初始场景ID、初始背包物品列表
  
- `PhotoAlbumDataManager.cs` - 相册数据管理器
  - 位置: `Assets/ScriptableObject/PhotoAlbum/`
  - 包含相册容量配置
  
- `OnLineGameData.cs` - 线上玩法配置数据
  - 位置: `Assets/ScriptableObject/OnLineGameData/`
  - 可选系统，包含启用开关

### 3. 创建的存根类型（Stub Types）

#### 角色系统存根 (`CharacterStubs.cs`):
- `CharacterBag` - 角色背包
- `ClothingBag` - 服装背包
- `ClothingAccessoriesBag` - 服装配件背包
- `CharacterSceneOverride` - 角色场景覆盖
- `NpcSpawnSaveData` - NPC生成保存数据

#### 消息系统存根 (`MessageStubs.cs`):
- `MessageData` - 消息数据
- `PriavateMessageBag` - 私信背包
- `ExhibitionPromotionBag` - 展会宣发背包

#### 角色管理器 (`CharacterManager.cs`):
- 完整的存根实现，包含基本的角色背包管理
- 提供场景NPC获取接口（返回空列表）
- 包含事件注册系统

#### 展会管理器 (`ExhibitionManager.cs`):
- 基础存根实现
- 提供展会准备接口（打印警告日志）

#### 工厂系统存根 (`FactoryItemStubs.cs`):
- `FactoryMerchandiseItemInfo` - 工厂周边物品信息
- `FactoryComposedItemInfoEt` - 工厂组合物品扩展方法

#### 物品ID集合 (`ItemIdSet.cs`):
- 常用物品ID常量定义

#### 场景相关存根:
- `SceneCharacterController.cs` - 场景角色控制器
- `ValueNumberContent.cs` - 数值显示组件

#### UI相关存根:
- `MainUI.cs` - 主界面UI简化版本

### 4. 清理的游戏特定代码

#### GameDataManager.cs:
- 移除了 `ExhibitionManager` 引用
- 注释掉展会相关逻辑

#### GameManager.cs:
- 保留了通用的初始化流程
- `MainUI` 改为使用简化版本

#### InventoryManager.cs:
- 注释掉 `CharacterManager` 相关的角色属性奖励逻辑
- 保留了基础的物品管理功能

#### SROptions.Gameplay.cs:
- 移除了大量游戏特定的GM指令
- 保留了基础的属性添加、存档功能
- 添加了通用的"添加所有物品"测试指令

#### ShopManager.cs:
- 原文件备份为 `ShopManager.cs.bak`
- 创建了简化的框架存根版本
- 移除了具体商店（布料店、超市、果蔬店等）的实现

## 保留的通用系统

### 核心系统（已有且完整）:
1. **Drama System** - 剧情对话系统
   - DramaManager, DramaDirector, DramaContext
   - 历史记录、存档点、本地化支持

2. **Tutorial System** - 新手引导系统
   - TutorialManager, TutorialConfig
   - 引导步骤、锚点注册、全局标记

3. **Save System** - 存档系统
   - SaveGameManager, ISaveable 接口
   - GameSaveData, 存档槽位管理

4. **UI System** - UI框架
   - UIBase, UISystem
   - UI栈管理、渐变效果

5. **Audio System** - 音频系统
   - AudioManager
   - 音乐、音效管理

6. **Scene System** - 场景管理
   - GameSceneManager
   - 场景切换、小游戏场景支持

7. **Inventory System** - 背包系统
   - InventoryManager
   - 物品增删改查、解锁系统

8. **Localization** - 本地化系统
   - LanguageManager
   - 多语言支持

9. **Input System** - 输入管理
   - PlayerInputManager

10. **Effects System** - 特效系统
    - EffectsManager（新拷贝）

11. **Guide System** - 引导系统
    - GuideManager（新拷贝，包含娃娃机）

## 需要根据项目实现的部分

### 1. CharacterManager（角色系统）
如果项目需要角色系统，需要实现：
- 角色属性管理
- 好感度系统
- 服装解锁逻辑
- 场景NPC生成规则

### 2. ShopManager（商店系统）
如果项目需要商店系统，需要实现：
- 商品列表管理
- 购买/出售逻辑
- 商店刷新机制

### 3. ExhibitionManager（展会系统）
如果项目需要特定的展会/活动系统，需要实现完整逻辑。

### 4. Factory System（工厂系统）
当前只有存根，如果需要类似的组合物品系统，需要完整实现。

### 5. OnLineGame System（线上玩法系统）
当前只有配置开关，如果需要社交/在线功能，需要完整实现。

## 使用建议

### 项目初始化步骤：

1. **创建配置资源**：
   - 在 Unity 中创建 `GameSettingsDataManager` ScriptableObject
   - 设置初始场景ID和初始背包物品

2. **删除不需要的系统**：
   - 如果不需要角色系统，删除 `CharacterManager.cs` 和 `CharacterStubs.cs`
   - 如果不需要商店系统，删除 `ShopManager.cs`
   - 如果不需要展会系统，删除 `ExhibitionManager.cs`
   - 如果不需要工厂系统，删除 `FactoryItemStubs.cs`

3. **实现必要的系统**：
   - 根据项目需求实现 `MainUI` 的完整功能
   - 实现 `SceneCharacterController` 的NPC表现逻辑
   - 实现 `ValueNumberContent` 的数值显示逻辑

4. **配置 Luban 表格**：
   - 确保配置了所需的数据表
   - ItemData, PropertyData, GameSceneData 等

5. **配置 Addressables**：
   - 确保 AssetKeys 中的路径与实际资源匹配

## 编译状态

经过以上修改，RAFramework 应该能够编译通过。所有缺失的类型都已创建存根或从 AFramework 拷贝。

## 注意事项

1. **存根类型**：标记为"存根"的类型只提供了最基本的结构，需要根据实际项目需求实现完整功能。

2. **ScriptableObject**：新创建的 ScriptableObject 需要在 Unity 编辑器中创建实例并赋值给相应的管理器。

3. **依赖关系**：某些系统之间有依赖关系，删除时需要同时清理引用。

4. **Luban配置**：部分类型依赖 Luban 生成的配置表，确保表格定义与代码匹配。

5. **备份文件**：游戏特定的原始文件被重命名为 `.bak` 后缀，可以作为参考。

## 推荐保留的通用系统

以下系统适合作为通用框架保留：
- ✅ Drama（剧情）
- ✅ Tutorial（引导）
- ✅ Save（存档）
- ✅ UI（界面框架）
- ✅ Audio（音频）
- ✅ Scene（场景管理）
- ✅ Inventory（背包，精简版）
- ✅ Localization（本地化）
- ✅ Input（输入）
- ✅ Effects（特效）
- ✅ Guide（引导系统，含娃娃机示例）

## 可选/根据项目需求的系统

以下系统是否保留取决于具体项目：
- ⚠️ Character（角色系统）- 当前为存根
- ⚠️ Shop（商店系统）- 当前为存根
- ⚠️ Exhibition（展会系统）- 当前为存根
- ⚠️ Factory（工厂系统）- 当前为存根
- ⚠️ OnLineGame（线上玩法）- 当前为存根

---

**日期**: 2026-08-14
**版本**: 1.0
