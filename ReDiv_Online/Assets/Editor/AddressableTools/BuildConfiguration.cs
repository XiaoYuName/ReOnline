using Sirenix.OdinInspector;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

namespace XFramework
{
    /// <summary>
    /// 一键Addressable 包配置
    /// </summary>
    [CreateAssetMenu(fileName = "BuildConfig",menuName = "Configs/Project/BuildConfig")]
    public class BuildConfiguration : SerializedScriptableObject
    {
        [TitleGroup("Addressable 配置")]
        [BoxGroup("Addressable 配置/资源范围")]
        [FolderPath(RequireExistingPath = true)]
        [LabelText("远程资源路径")]
        public string RemoteAssetPath;

        [BoxGroup("Addressable 配置/资源范围")]
        [LabelText("包含子目录")]
        public bool IncludeSubFolders = true;

        [BoxGroup("Addressable 配置/Group")]
        [EnumToggleButtons]
        [LabelText("Group 分组策略")]
        public AddressableGroupPackingMode GroupPackingMode = AddressableGroupPackingMode.SingleGroup;

        [BoxGroup("Addressable 配置/Group")]
        [AssetsOnly]
        [LabelText("目标/模板 Group")]
        [InfoBox("整体打包时作为目标 Group；按文件夹打包时作为新建 Group 的 Schema 模板。")]
        public  AddressableAssetGroup RemoteAssetSettings;

        [BoxGroup("Addressable 配置/Group")]
        [LabelText("基础 Group 名称")]
        [InfoBox("整体打包时使用该名称；按文件夹打包时会生成“基础名称-文件夹名”的 Group。")]
        public string GroupName = "Remote";

        [BoxGroup("Addressable 配置/Group")]
        [LabelText("构建前清空目标 Group")]
        public bool ClearTargetGroupBeforeBuild = true;

        [BoxGroup("Addressable 配置/Bundle")]
        [EnumToggleButtons]
        [LabelText("Bundle 打包模式")]
        public BundledAssetGroupSchema.BundlePackingMode BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackTogether;

        [BoxGroup("Addressable 配置/Entry")]
        [LabelText("地址使用资源路径")]
        public bool UseAssetPathAsAddress = true;

        [BoxGroup("Addressable 配置/Entry")]
        [LabelText("标签")]
        public string Label;
    }

    public enum AddressableGroupPackingMode
    {
        [LabelText("整体进一个 Group")]
        SingleGroup,

        [LabelText("按一级文件夹拆 Group")]
        TopLevelFolderGroups
    }
}

