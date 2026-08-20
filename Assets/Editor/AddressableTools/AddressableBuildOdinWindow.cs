using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;

namespace XFramework
{
    public class AddressableBuildOdinWindow : OdinEditorWindow
    {
        [TitleGroup("Addressable 一键打包")]
        [BoxGroup("Addressable 一键打包/配置", ShowLabel = false)]
        [InlineEditor(InlineEditorObjectFieldModes.Boxed)]
        [LabelText("构建配置")]
        [Required]
        [SerializeField]
        private BuildConfiguration config;

        [BoxGroup("Addressable 一键打包/操作")]
        [HorizontalGroup("Addressable 一键打包/操作/Buttons")]
        [Button("一键打标签", ButtonSizes.Large)]
        [GUIColor(0.35f, 0.85f, 0.45f)]
        private void BuildAddressables()
        {
            AddressableBuildReport report = AddressableBuild.Build(config);
            RefreshPreview();

            if (report.Success)
            {
                EditorUtility.DisplayDialog("Addressable 一键打包", report.Message, "确定");
                return;
            }

            EditorUtility.DisplayDialog("Addressable 一键打包失败", report.Message, "关闭");
        }

        [HorizontalGroup("Addressable 一键打包/操作/Buttons")]
        [Button("刷新预览", ButtonSizes.Large)]
        [GUIColor(0.45f, 0.7f, 1f)]
        private void RefreshPreview()
        {
            previewItems = AddressableBuild.PreviewAssetPaths(config)
                .Select(path => new AddressableBuildPreviewItem(config, path))
                .ToList();
        }

        [HorizontalGroup("Addressable 一键打包/操作/Buttons")]
        [Button("清空目标组", ButtonSizes.Large)]
        [GUIColor(1f, 0.72f, 0.35f)]
        private void ClearTargetGroup()
        {
            if (config == null)
            {
                EditorUtility.DisplayDialog("清空目标组", "构建配置为空。", "关闭");
                return;
            }

            string groupName = config.RemoteAssetSettings != null ? config.RemoteAssetSettings.Name : config.GroupName;
            if (!EditorUtility.DisplayDialog("清空目标组", $"确定清空 Addressable Group [{groupName}] 中的 Entry？", "清空", "取消"))
            {
                return;
            }

            int removedCount = AddressableBuild.ClearTargetGroup(config);
            EditorUtility.DisplayDialog("清空目标组", $"已移除 {removedCount} 个 Entry。", "确定");
        }

        [HorizontalGroup("Addressable 一键打包/操作/Buttons")]
        [Button("打开配置", ButtonSizes.Large)]
        private void PingConfig()
        {
            if (config == null)
            {
                LoadConfig();
            }

            EditorGUIUtility.PingObject(config);
            Selection.activeObject = config;
        }

        [BoxGroup("Addressable 一键打包/概览")]
        [ShowInInspector]
        [ReadOnly]
        [LabelText("资源数量")]
        private int PreviewCount => previewItems.Count;

        [BoxGroup("Addressable 一键打包/概览")]
        [ShowInInspector]
        [ReadOnly]
        [LabelText("当前路径")]
        private string CurrentPath => config == null ? string.Empty : config.RemoteAssetPath;

        [BoxGroup("Addressable 一键打包/概览")]
        [ShowInInspector]
        [ReadOnly]
        [LabelText("分组策略")]
        private string CurrentPackingMode => config == null ? string.Empty : config.GroupPackingMode.ToString();

        [BoxGroup("Addressable 一键打包/概览")]
        [ShowInInspector]
        [ReadOnly]
        [LabelText("Bundle 模式")]
        private string CurrentBundleMode => config == null ? string.Empty : config.BundleMode.ToString();

        [BoxGroup("Addressable 一键打包/资源预览")]
        [TableList(IsReadOnly = true, AlwaysExpanded = true)]
        [HideLabel]
        [SerializeField]
        private List<AddressableBuildPreviewItem> previewItems = new List<AddressableBuildPreviewItem>();

        [MenuItem("Tools/XFramework/Addressable 一键打包")]
        private static void Open()
        {
            AddressableBuildOdinWindow window = GetWindow<AddressableBuildOdinWindow>();
            window.titleContent = new GUIContent("Addressable Build");
            window.minSize = new Vector2(900, 620);
            window.Show();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            titleContent = new GUIContent("Addressable Build");
            minSize = new Vector2(900, 620);
            LoadConfig();
            RefreshPreview();
        }

        private void LoadConfig()
        {
            config = AddressableBuild.LoadConfig();
        }

        [Serializable]
        private class AddressableBuildPreviewItem
        {
            [TableColumnWidth(220)]
            [ReadOnly]
            [LabelText("资源名")]
            public string Name;

            [ReadOnly]
            [LabelText("路径")]
            public string Path;

            [TableColumnWidth(180)]
            [ReadOnly]
            [LabelText("目标 Group")]
            public string TargetGroup;

            [TableColumnWidth(90, false)]
            [Button("定位", ButtonSizes.Small)]
            private void Ping()
            {
                UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(Path);
                EditorGUIUtility.PingObject(asset);
                Selection.activeObject = asset;
            }

            public AddressableBuildPreviewItem(BuildConfiguration config, string path)
            {
                Path = path;
                TargetGroup = AddressableBuild.GetTargetGroupName(config, path);
                Name = System.IO.Path.GetFileNameWithoutExtension(path);
            }
        }
    }
}
