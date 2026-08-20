# RAFramework Package 转换指南

## 目标

将 RAFramework 转换为标准的 Unity Package Manager 包，方便在多个项目中复用。

## Package 结构规划

### 推荐的目录结构

```
Packages/com.yourcompany.raframework/
├── Runtime/
│   ├── Audio/
│   ├── Drama/
│   ├── Tutorial/
│   ├── SaveGame/
│   ├── UI/
│   ├── Scene/
│   ├── Inventory/
│   ├── Localization/
│   ├── Input/
│   ├── Resolution/
│   ├── Effects/
│   ├── Base/
│   ├── Common/
│   └── RAFramework.Runtime.asmdef
├── Editor/
│   ├── Tools/
│   └── RAFramework.Editor.asmdef
├── Samples~/
│   ├── ClawMachineExample/
│   └── GMCommandsExample/
├── Documentation~/
│   ├── index.md
│   ├── drama-system.md
│   ├── tutorial-system.md
│   └── save-system.md
├── package.json
├── README.md
├── CHANGELOG.md
└── LICENSE.md
```

## 第一步：创建 Assembly Definition 文件

### 1. Runtime Assembly Definition

**文件名**: `RAFramework.Runtime.asmdef`  
**位置**: `Runtime/RAFramework.Runtime.asmdef`

```json
{
  "name": "RAFramework.Runtime",
  "rootNamespace": "XFramework",
  "references": [
    "UniTask",
    "Unity.Addressables",
    "Unity.ResourceManager",
    "Unity.Localization",
    "Unity.TextMeshPro",
    "Unity.InputSystem"
  ],
  "includePlatforms": [],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": false,
  "precompiledReferences": [
    "DOTween.dll",
    "Sirenix.OdinInspector.dll",
    "DamageNumbersPro.dll"
  ],
  "autoReferenced": true,
  "defineConstraints": [],
  "versionDefines": [
    {
      "name": "com.unity.addressables",
      "expression": "1.21.0",
      "define": "ADDRESSABLES_1_21_OR_NEWER"
    }
  ],
  "noEngineReferences": false
}
```

### 2. Editor Assembly Definition

**文件名**: `RAFramework.Editor.asmdef`  
**位置**: `Editor/RAFramework.Editor.asmdef`

```json
{
  "name": "RAFramework.Editor",
  "rootNamespace": "XFramework.Editor",
  "references": [
    "RAFramework.Runtime",
    "Unity.Addressables.Editor",
    "Unity.Localization.Editor"
  ],
  "includePlatforms": [
    "Editor"
  ],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": false,
  "precompiledReferences": [],
  "autoReferenced": true,
  "defineConstraints": [],
  "versionDefines": [],
  "noEngineReferences": false
}
```

## 第二步：创建 package.json

**文件名**: `package.json`  
**位置**: 包根目录

```json
{
  "name": "com.yourcompany.raframework",
  "version": "1.0.0",
  "displayName": "RA Framework",
  "description": "通用游戏框架，包含剧情对话、新手引导、存档管理、UI系统等核心功能。适用于视觉小说、角色扮演等游戏类型。",
  "unity": "2021.3",
  "unityRelease": "0f1",
  "documentationUrl": "https://yourcompany.com/raframework/docs",
  "changelogUrl": "https://yourcompany.com/raframework/changelog",
  "licensesUrl": "https://yourcompany.com/raframework/license",
  "dependencies": {
    "com.unity.addressables": "1.21.0",
    "com.unity.localization": "1.4.0",
    "com.unity.textmeshpro": "3.0.0",
    "com.unity.inputsystem": "1.7.0"
  },
  "keywords": [
    "framework",
    "drama",
    "dialogue",
    "tutorial",
    "save",
    "ui",
    "localization",
    "visual novel"
  ],
  "author": {
    "name": "Your Company Name",
    "email": "support@yourcompany.com",
    "url": "https://yourcompany.com"
  },
  "hideInEditor": false,
  "samples": [
    {
      "displayName": "Claw Machine Example",
      "description": "娃娃机小游戏示例，展示如何使用引导系统",
      "path": "Samples~/ClawMachineExample"
    },
    {
      "displayName": "GM Commands Example",
      "description": "GM调试指令示例",
      "path": "Samples~/GMCommandsExample"
    }
  ]
}
```

## 第三步：文件迁移规划

### 保留在 Runtime（核心框架）

#### 必需系统
- ✅ `Drama/` - 剧情对话系统
- ✅ `Tutorial/` - 新手引导系统
- ✅ `SaveGame/` - 存档系统
- ✅ `UI/` - UI框架（UIBase, UISystem）
- ✅ `Audio/` - 音频系统
- ✅ `Scene/` - 场景管理（GameSceneManager, SceneController）
- ✅ `Inventory/` - 背包系统（通用部分）
- ✅ `Localization/` - 本地化系统
- ✅ `Input/` - 输入管理
- ✅ `Resolution/` - 分辨率管理
- ✅ `Effects/` - 特效管理
- ✅ `Base/` - 基础类（Singleton, GameBase等）
- ✅ `Common/` - 通用工具（AssetReleaser, LocStringEventExtensions等）

#### 通用工具
- ✅ `GameDataManager.cs` - 玩家数据管理
- ✅ `AssetsManager.cs` - 资源管理
- ✅ `LubanManager.cs` - Luban配置管理
- ✅ `GameManager.cs` - 游戏主管理器
- ✅ `GamePathTools.cs` - 路径工具

### 移动到 Samples~（示例/可选）

#### 游戏特定示例
- ⚠️ `CharacterManager.cs` → `Samples~/CharacterSystemExample/`
- ⚠️ `ShopManager.cs` → `Samples~/ShopSystemExample/`
- ⚠️ `ExhibitionManager.cs` → `Samples~/ExhibitionExample/`
- ⚠️ `GuideManager.cs` (娃娃机) → `Samples~/ClawMachineExample/`
- ⚠️ `ClawMachineSettingData.cs` → `Samples~/ClawMachineExample/`
- ⚠️ `GemSmartSlicerUI.cs` → `Samples~/MiniGamesExample/`
- ⚠️ `RacingCarSewingMachinesUI.cs` → `Samples~/MiniGamesExample/`
- ⚠️ `FactoryItemStubs.cs` → `Samples~/FactorySystemExample/`
- ⚠️ `SROptions.Gameplay.cs` → `Samples~/GMCommandsExample/`

### 需要删除或重构

#### 存根类型（改为可选扩展点）
- ❌ `CharacterStubs.cs` - 删除，在文档中说明如何扩展
- ❌ `MessageStubs.cs` - 删除，在文档中说明如何扩展
- ❌ `ItemIdSet.cs` - 移到示例项目

#### ScriptableObject 配置
- ⚠️ `GameSettingsDataManager.cs` - 保留，作为必需配置
- ⚠️ `PhotoAlbumDataManager.cs` - 改为可选，或移到示例
- ⚠️ `OnLineGameData.cs` - 改为可选，或移到示例

## 第四步：创建 README.md

```markdown
# RA Framework

通用游戏框架，专注于剧情对话、新手引导、存档管理等核心功能。

## 特性

- 🎭 **剧情系统** - 完整的对话、分支、历史记录
- 🎓 **引导系统** - 灵活的新手引导、锚点定位
- 💾 **存档系统** - 多槽位、自动存档、存档摘要
- 🎨 **UI框架** - 基于栈的UI管理、渐变效果
- 🔊 **音频系统** - 音乐、音效管理
- 🗺️ **场景管理** - 大地图、小场景、小游戏支持
- 🎒 **背包系统** - 物品管理、堆叠、解锁
- 🌍 **本地化** - 多语言支持
- ⌨️ **输入管理** - 统一输入处理
- 🖥️ **分辨率管理** - 窗口模式、全屏、无边框

## 安装

### 通过 Package Manager

1. 打开 Unity Package Manager (Window > Package Manager)
2. 点击 "+" 按钮
3. 选择 "Add package from git URL"
4. 输入: `https://github.com/yourcompany/raframework.git`

### 通过 manifest.json

在 `Packages/manifest.json` 中添加：

```json
{
  "dependencies": {
    "com.yourcompany.raframework": "https://github.com/yourcompany/raframework.git#1.0.0"
  }
}
```

## 依赖

- Unity 2021.3+
- Addressables 1.21.0+
- Localization 1.4.0+
- TextMeshPro 3.0.0+
- UniTask 2.3.0+
- DOTween (Asset Store)
- Odin Inspector (可选, Asset Store)

## 快速开始

查看完整文档: [Documentation](Documentation~/index.md)

## 示例

Package 包含以下示例：

- **Claw Machine Example** - 娃娃机小游戏示例
- **GM Commands Example** - GM调试指令示例

通过 Package Manager 导入示例到项目中。

## 支持

- 文档: https://yourcompany.com/raframework/docs
- 问题反馈: https://github.com/yourcompany/raframework/issues
- 邮件: support@yourcompany.com

## 许可证

[MIT License](LICENSE.md)
```

## 第五步：创建 CHANGELOG.md

```markdown
# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-08-14

### Added
- 剧情对话系统 (Drama System)
- 新手引导系统 (Tutorial System)
- 存档管理系统 (Save System)
- UI框架 (UI Framework)
- 音频系统 (Audio System)
- 场景管理 (Scene Management)
- 背包系统 (Inventory System)
- 本地化系统 (Localization)
- 输入管理 (Input Manager)
- 分辨率管理 (Resolution Manager)
- 特效系统 (Effects System)
- 娃娃机示例 (Claw Machine Example)
- GM指令示例 (GM Commands Example)

### Fixed
- 修复 SceneLoader ToUniTask 参数顺序问题
- 修复 LocalizeStringEvent 扩展方法缺失
- 修复 GamePathTools.CombinationScenePath 缺失

### Documentation
- 添加完整的 API 文档
- 添加快速开始指南
- 添加系统使用示例

## [Unreleased]
```

## 第六步：创建文档结构

### Documentation~/index.md

```markdown
# RA Framework 文档

欢迎使用 RA Framework！

## 目录

1. [快速开始](getting-started.md)
2. [核心系统](core-systems.md)
   - [剧情系统](drama-system.md)
   - [引导系统](tutorial-system.md)
   - [存档系统](save-system.md)
   - [UI系统](ui-system.md)
   - [音频系统](audio-system.md)
3. [扩展开发](extending.md)
4. [API 参考](api-reference.md)
5. [常见问题](faq.md)

## 核心概念

RAFramework 是一个模块化的游戏框架...
```

## 第七步：迁移步骤

### 1. 创建 Package 目录结构

```bash
mkdir -p Packages/com.yourcompany.raframework/Runtime
mkdir -p Packages/com.yourcompany.raframework/Editor
mkdir -p Packages/com.yourcompany.raframework/Samples~
mkdir -p Packages/com.yourcompany.raframework/Documentation~
```

### 2. 移动核心文件

```bash
# 移动 Runtime 文件
cp -r Assets/Scripts/XFramework/C#/Drama Packages/com.yourcompany.raframework/Runtime/
cp -r Assets/Scripts/XFramework/C#/Tutorial Packages/com.yourcompany.raframework/Runtime/
cp -r Assets/Scripts/XFramework/C#/SaveGame Packages/com.yourcompany.raframework/Runtime/
# ... 其他核心系统
```

### 3. 创建 Assembly Definition 文件

在 Unity 中：
1. 右键 Runtime 文件夹
2. Create > Assembly Definition
3. 命名为 `RAFramework.Runtime`
4. 配置引用和依赖

### 4. 移动示例到 Samples~

```bash
mkdir -p Packages/com.yourcompany.raframework/Samples~/ClawMachineExample
cp -r Assets/Scripts/XFramework/C#/Guide Packages/com.yourcompany.raframework/Samples~/ClawMachineExample/
# ... 其他示例
```

### 5. 测试 Package

1. 在项目中通过本地路径引用包
2. 测试所有核心功能
3. 确保依赖正确配置
4. 验证示例可以正常导入和运行

## 第八步：发布选项

### 选项 1: Git Repository

```bash
cd Packages/com.yourcompany.raframework
git init
git add .
git commit -m "Initial commit"
git remote add origin https://github.com/yourcompany/raframework.git
git push -u origin main
git tag 1.0.0
git push --tags
```

用户安装：
```
https://github.com/yourcompany/raframework.git#1.0.0
```

### 选项 2: OpenUPM

1. 注册 OpenUPM 账号
2. 提交 package 信息
3. 用户通过 OpenUPM CLI 安装

### 选项 3: Unity Asset Store

1. 准备 Asset Store 提交材料
2. 包含演示场景和文档
3. 通过 Unity Asset Store 审核

### 选项 4: 私有 NPM Registry

1. 搭建私有 Verdaccio 服务器
2. 发布包到私有 registry
3. 团队内部使用

## 注意事项

### 1. 依赖管理

⚠️ **第三方依赖处理**：
- DOTween - 需要用户自行从 Asset Store 安装
- Odin Inspector - 可选，需要用户自行安装
- 在文档中明确说明这些依赖

### 2. 命名空间

✅ **保持统一**：
- 核心框架使用 `XFramework` 命名空间
- 编辑器工具使用 `XFramework.Editor` 命名空间
- 示例代码可以使用独立命名空间

### 3. 版本控制

✅ **语义化版本**：
- 主版本号：不兼容的 API 变更
- 次版本号：向后兼容的功能新增
- 修订号：向后兼容的问题修复

### 4. 文档

✅ **完整文档**：
- API 文档
- 快速开始指南
- 各系统详细说明
- 示例代码
- 常见问题解答

## 推荐的迁移顺序

1. ✅ **第一阶段**：创建 Package 结构和 Assembly Definition
2. ✅ **第二阶段**：迁移核心系统到 Runtime
3. ✅ **第三阶段**：创建编辑器工具（如果有）
4. ✅ **第四阶段**：整理示例到 Samples~
5. ✅ **第五阶段**：编写文档
6. ✅ **第六阶段**：测试和验证
7. ✅ **第七阶段**：发布到 Git/OpenUPM

## 总结

将 RAFramework 转换为 Package Manager 包可以：
- ✅ 在多个项目间复用
- ✅ 版本控制更清晰
- ✅ 依赖管理更规范
- ✅ 更新和维护更方便
- ✅ 可以分享给其他开发者

---

**建议**: 先在本地完成迁移并充分测试，然后再发布到公开仓库。

**参考资料**:
- [Unity Package Manager 文档](https://docs.unity3d.com/Manual/Packages.html)
- [Custom Packages 指南](https://docs.unity3d.com/Manual/CustomPackages.html)
- [OpenUPM](https://openupm.com/)
