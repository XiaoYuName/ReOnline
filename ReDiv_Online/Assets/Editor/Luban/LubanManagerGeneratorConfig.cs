#if UNITY_EDITOR

using Sirenix.OdinInspector;
using UnityEngine;

namespace XFramework
{
    [CreateAssetMenu(
        fileName = "LubanManagerGeneratorConfig",
        menuName = "XFramework/Luban/Luban Manager Generator Config")]
    public class LubanManagerGeneratorConfig : ScriptableObject
    {
        [TitleGroup("路径配置")]
        [LabelText("Luban C#代码目录")]
        [FolderPath(RequireExistingPath = true)]
        [InfoBox("这里填写 Luban 生成的 C# 代码目录，目录中应该包含 TbGameSceneData.cs、TbItemData.cs 这类文件。")]
        public string lubanCodeDirectory = "Assets/Scripts/Game/Scripts/Luban";

        [LabelText("生成文件输出路径")]
        [FilePath(Extensions = "cs")]
        [InfoBox("这里是自动生成的 LubanManager.Generated.cs 路径。这个文件会被覆盖，请不要手动修改。")]
        public string outputPath = "Assets/Scripts/Game/Scripts/System/LubanManager.Generated.cs";

        [TitleGroup("命名空间配置")]
        [LabelText("生成代码是否使用命名空间")]
        public bool useNamespace = true;

        [LabelText("命名空间")]
        [ShowIf(nameof(useNamespace))]
        public string namespaceName = "XFramework";

        [TitleGroup("AssetKeys 配置")]
        [LabelText("AssetKeys 类名")]
        public string assetKeysClassName = "AssetKeys";

        [LabelText("AssetKey 命名规则")]
        [EnumToggleButtons]
        public AssetKeyNameMode assetKeyNameMode = AssetKeyNameMode.LowerCase;

        [LabelText("AssetKey 前缀")]
        [ShowIf(nameof(NeedCustomAssetKeyPrefixSuffix))]
        public string assetKeyPrefix = "";

        [LabelText("AssetKey 后缀")]
        [ShowIf(nameof(NeedCustomAssetKeyPrefixSuffix))]
        public string assetKeySuffix = "Path";

        [TitleGroup("释放配置")]
        [LabelText("ClearCache 时释放 Json 资源")]
        [InfoBox("开启后，生成的 ClearGeneratedCache 会调用 AssetsManager.Instance.FreeAsset(...)。")]
        public bool freeAssetOnClearCache = true;

        [TitleGroup("生成行为")]
        [LabelText("生成后自动刷新 AssetDatabase")]
        public bool refreshAssetDatabase = true;

        [LabelText("生成前自动扫描")]
        public bool autoScanBeforeGenerate = true;

        [LabelText("写入 UTF8 无 BOM")]
        public bool utf8WithoutBom = true;

        private bool NeedCustomAssetKeyPrefixSuffix()
        {
            return assetKeyNameMode == AssetKeyNameMode.CustomPrefixSuffix;
        }

        public enum AssetKeyNameMode
        {
            [LabelText("保持表类名：TbGameSceneDataPath")]
            KeepTableClassName,

            [LabelText("全部小写：tbgamescenedataPath")]
            LowerCase,

            [LabelText("首字母大写其余小写：TbgamescenedataPath")]
            FirstUpperRestLower,

            [LabelText("移除 Tb 前缀：GameSceneDataPath")]
            RemoveTbPrefix,

            [LabelText("自定义前后缀")]
            CustomPrefixSuffix
        }
    }
}

#endif