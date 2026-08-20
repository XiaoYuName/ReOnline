# RAFramework 编译检查清单

## 最新修复（2026-08-14）

### 已修复的编译错误：

1. ✅ **WindowType 枚举缺失**
   - 文件：`Assets/Scripts/XFramework/C#/Resolution/WindowType.cs`
   - 定义了 Fullscreen、Borderless、Windowed 三种窗口模式

2. ✅ **AssetReleaser 及相关类缺失**
   - 已从 AFramework 拷贝：
     - `AssetReleaser.cs`
     - `IconLoadExtension.cs`
     - `IconReleaser.cs`
     - `AssetRefExtension.cs`
   - 位置：`Assets/Scripts/XFramework/C#/Common/Aa/`

3. ✅ **LocStringEventExtensions 缺失**
   - 文件：`Assets/Scripts/XFramework/C#/Common/LocStringEventExtensions.cs`
   - 添加了三个重载：
     - `SetText(LocalSelectedData data)`
     - `SetText(TbLocalzationKeyData data)`
     - `SetText(string table, string key)` ← 新增

## 编译前置检查清单

### 必需的 Unity Packages：
- [ ] Addressables
- [ ] Localization
- [ ] TextMeshPro
- [ ] Input System (New)

### 必需的第三方资源：
- [ ] DOTween (Asset Store)
- [ ] UniTask (Package Manager 或 Git URL)
- [ ] Odin Inspector (Asset Store)
- [ ] DamageNumbersPro (Asset Store，用于特效)
- [ ] PathologicalGames PoolManager (已包含在项目中)
- [ ] Febucci Text Animator (已包含在项目中)
- [ ] Live2D Cubism SDK (已包含在项目中)
- [ ] SRDebugger (已包含在项目中)

### 核心系统类检查：

#### ✅ 基础系统
- [x] GameBase.cs
- [x] MonoSingleton.cs
- [x] Singleton.cs
- [x] UIBase.cs
- [x] UISystem.cs

#### ✅ 管理器类
- [x] GameManager.cs
- [x] GameDataManager.cs
- [x] SaveGameManager.cs
- [x] InventoryManager.cs
- [x] AudioManager.cs
- [x] LanguageManager.cs
- [x] DramaManager.cs
- [x] TutorialManager.cs
- [x] GameSceneManager.cs
- [x] PlayerInputManager.cs
- [x] ResolutionManager.cs
- [x] EffectsManager.cs (新拷贝)
- [x] GuideManager.cs (新拷贝)

#### ✅ UI 相关
- [x] PopLoadingUI.cs (新拷贝)
- [x] MainUI.cs (简化版)
- [x] CommonUI.cs
- [x] CustomButton.cs
- [x] SelectedButton.cs
- [x] LongPressButton.cs

#### ✅ 场景相关
- [x] SceneController.cs (新拷贝)
- [x] SceneCharacterController.cs (存根)

#### ✅ 资源管理
- [x] AssetsManager.cs
- [x] AssetKeys.cs
- [x] AssetReleaser.cs (新拷贝)
- [x] IconLoadExtension.cs (新拷贝)

#### ✅ 本地化
- [x] LanguageManager.cs
- [x] LocalizationFontAsset.cs
- [x] LocStringEventExtensions.cs (新拷贝+扩展)

#### ✅ 存根类型
- [x] CharacterManager.cs (存根)
- [x] CharacterStubs.cs (CharacterBag, ClothingBag等)
- [x] ExhibitionManager.cs (存根)
- [x] ShopManager.cs (存根)
- [x] MessageStubs.cs (MessageData等)
- [x] FactoryItemStubs.cs (工厂系统存根)
- [x] ItemIdSet.cs
- [x] ValueNumberContent.cs (存根)

#### ✅ ScriptableObject 配置
- [x] GameSettingsDataManager.cs (新建)
- [x] PhotoAlbumDataManager.cs (新建)
- [x] OnLineGameData.cs (新建)
- [x] ClawMachineSettingData.cs (拷贝)

#### ✅ 枚举和数据结构
- [x] WindowType.cs (新建)
- [x] TimeSlot (在 GameDataManager.cs 中)
- [x] FadeLayer (在 Enums.cs 中)
- [x] MinGameSceneType (在 Enums.cs 中)
- [x] PropertyType (Luban 生成)
- [x] ItemType (Luban 生成)

## 已知存根系统（可选）

以下系统目前是存根实现，会输出警告日志但不会导致编译错误：

- ⚠️ **CharacterManager** - 角色系统存根
- ⚠️ **ExhibitionManager** - 展会系统存根
- ⚠️ **ShopManager** - 商店系统存根
- ⚠️ **FactoryItemStubs** - 工厂系统存根
- ⚠️ **SceneCharacterController** - 场景角色控制器存根
- ⚠️ **ValueNumberContent** - 数值显示组件存根

如果项目不需要这些系统，可以删除相关文件及引用。

## 编译后检查

### 运行时必需配置：

1. **创建 ScriptableObject 实例**：
   ```
   Assets/Create/Configs/GameSettingsDataManager
   Assets/Create/Configs/PhotoAlbumDataManager
   Assets/Create/Configs/OnLineGameData
   Assets/Create/Configs/MinGame/ClawMachineGuideUI
   ```

2. **配置 GameSettingsDataManager**：
   - 设置初始场景 ID (SceneID)
   - 配置初始背包物品列表 (StarItemBagList)

3. **配置 Addressables**：
   - 确保所有 AssetKeys 中的路径指向正确的资源
   - 检查 Addressables Groups 配置

4. **配置 Luban 数据表**：
   - 确保已生成配置表代码
   - 检查表格数据是否正确加载

### 场景设置：

确保初始场景包含以下管理器：
- GameManager
- UISystem
- AudioManager
- LanguageManager
- SaveGameManager
- GameDataManager
- GameSceneManager
- InventoryManager
- DramaManager
- TutorialManager
- PlayerInputManager
- ResolutionManager
- EffectsManager (新增)
- GuideManager (新增)

## 潜在运行时问题

### 1. 空引用异常
如果运行时出现空引用，检查：
- ScriptableObject 是否已创建并赋值
- Addressables 资源是否正确标记
- 单例初始化顺序

### 2. 资源加载失败
- 检查 AssetKeys 中的路径是否正确
- 确认 Addressables 资源已正确打包
- 检查资源标签和分组

### 3. 本地化不显示
- 检查 Localization Tables 是否已创建
- 确认语言包已导入
- 检查 LocalizeStringEvent 组件配置

## Pipeline 相关

如果项目使用自定义 Pipeline，确保：
- [ ] 所有脚本没有编译错误
- [ ] 没有缺失的类型引用
- [ ] 所有必需的 Package 已安装
- [ ] Addressables 配置正确
- [ ] AssetDatabase 刷新正常

## 最终验证步骤

1. **编译检查**：
   ```
   Unity 菜单 → Build Settings → Switch Platform
   查看 Console 是否有编译错误
   ```

2. **运行检查**：
   ```
   点击 Play
   观察 Console 是否有空引用或资源加载错误
   检查初始化流程是否正常
   ```

3. **存档测试**：
   ```
   创建新存档
   保存并重新加载
   确认数据正确持久化
   ```

4. **场景切换测试**：
   ```
   测试场景切换功能
   确认渐变效果正常
   检查资源正确释放
   ```

## 备份文件

以下文件已备份（.bak 后缀）：
- `ShopManager.cs.bak` - 原始的游戏特定商店管理器

如需恢复原始版本，可以参考这些备份文件。

## 总结

✅ **所有已知的编译错误已修复**

主要修复内容：
1. 添加缺失的枚举和数据类型
2. 从 AFramework 拷贝必要的通用系统
3. 创建 ScriptableObject 配置管理器
4. 实现必要的存根类型
5. 清理游戏特定代码
6. 添加本地化扩展方法

如果仍有编译错误，请提供具体的错误信息以便进一步修复。

---

**最后更新**：2026-08-14  
**版本**：1.1  
**状态**：✅ 编译就绪
