using System;
using System.Collections.Generic;
using System.Linq;
using RedGame.OpenAI;
using Sirenix.OdinInspector;
using UnityEditor;
using UnityEditor.Localization;
using UnityEditor.Localization.UI;
using UnityEngine;

namespace RedGame.Framework.EditorTools
{
    public partial class LocalizeGptWindow
    {
        private const string ROOT_GROUP = "GPT Localization";
        private const string SETTINGS_GROUP = ROOT_GROUP + "/模型参数";
        private const string COLLECTION_GROUP = ROOT_GROUP + "/本地化表";
        private const string ENTRY_GROUP = ROOT_GROUP + "/待翻译条目";
        private const string OUTPUT_GROUP = ROOT_GROUP + "/输出";
        private const string BUSY_GROUP = ROOT_GROUP + "/翻译进度";

        enum OutputType
        {
            None, Prompt, Error, Info
        }
        
        private SimpleEditorTableView<TranslateRec> _tableView;
        private Vector2 _scrollPosition;
        private string _outputStr;
        private OutputType _outputType;
        private bool _isTestingConnection;

        [TitleGroup(ROOT_GROUP)]
        [BoxGroup(SETTINGS_GROUP, ShowLabel = true)]
        [ShowInInspector]
        [HideIf(nameof(IsBusy))]
        [LabelText("Base URL")]
        [OnValueChanged(nameof(SaveSettings))]
        [PropertyOrder(0)]
        private string BaseUrl
        {
            get => _baseUrl;
            set => _baseUrl = value;
        }

        [BoxGroup(SETTINGS_GROUP)]
        [ShowInInspector]
        [HideIf(nameof(IsBusy))]
        [LabelText("模型")]
        [ValueDropdown(nameof(GetModelDropdown), DropdownTitle = "选择模型", NumberOfItemsBeforeEnablingSearch = 8)]
        [OnValueChanged(nameof(SaveSettings))]
        [PropertyOrder(1)]
        private string Model
        {
            get => _model;
            set => _model = value;
        }

        [BoxGroup(SETTINGS_GROUP)]
        [ShowInInspector]
        [HideIf(nameof(IsBusy))]
        [LabelText("Temperature")]
        [PropertyRange(0f, 1f)]
        [OnValueChanged(nameof(SaveSettings))]
        [PropertyOrder(2)]
        private float Temperature
        {
            get => _temperature;
            set => _temperature = value;
        }

        [BoxGroup(SETTINGS_GROUP)]
        [OnInspectorGUI]
        [HideIf(nameof(IsBusy))]
        [PropertyOrder(3)]
        private void DrawApiKeyGUI()
        {
            EditorGUI.BeginChangeCheck();
            _apiKey = EditorGUILayout.PasswordField("API Key", _apiKey);
            if (EditorGUI.EndChangeCheck())
            {
                SaveSettings();
            }

            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                EditorGUILayout.HelpBox("请输入 API Key。Key 会保存在本机 EditorPrefs，不会写入项目资源。", MessageType.Warning);
                return;
            }

            if (!IsValidOpenAIKey(_apiKey))
            {
                EditorGUILayout.HelpBox("当前 Base URL 是 OpenAI 官方地址，API Key 应为 sk- 或 sk-proj- 开头。", MessageType.Error);
            }
        }

        [BoxGroup(SETTINGS_GROUP)]
        [HorizontalGroup(SETTINGS_GROUP + "/Actions")]
        [Button("测试连接", ButtonSizes.Medium)]
        [GUIColor(0.45f, 0.7f, 1f)]
        [HideIf(nameof(IsBusy))]
        [EnableIf(nameof(CanTestConnection))]
        [PropertyOrder(4)]
        private async void TestConnectionButton()
        {
            if (_isTestingConnection)
                return;

            SaveSettings();
            _isTestingConnection = true;
            Output("正在测试连接，请稍等...", OutputType.Info);
            Repaint();

            try
            {
                InitOpenAi();

                var response = await _openAi.CreateChatCompletion(new CreateChatCompletionRequest
                {
                    Model = _model,
                    Messages = new List<ChatMessage>
                    {
                        new()
                        {
                            Role = "system",
                            Content = "You are a connection test. Respond only with a valid JSON object."
                        },
                        new()
                        {
                            Role = "user",
                            Content = "Return exactly this JSON meaning: ok is true."
                        }
                    },
                    Temperature = 0,
                    MaxTokens = 32,
                    ResponseFormat = ResponseFormat.JsonObject
                });

                if (response.Error != null)
                {
                    Output(
                        $"连接失败。\nBase URL: {_baseUrl}\nModel: {_model}\nError Type: {response.Error.Type}\nError Message: {response.Error.Message}",
                        OutputType.Error);
                    return;
                }

                if (response.Choices == null || response.Choices.Count == 0)
                {
                    Output(
                        $"连接失败：接口有返回，但没有返回有效的 choices。\nBase URL: {_baseUrl}\nModel: {_model}",
                        OutputType.Error);
                    return;
                }

                string answer = response.Choices[0].Message.Content;
                Output(
                    $"连接成功。\nBase URL: {_baseUrl}\nModel: {_model}\nResponse: {answer}",
                    OutputType.Info);
            }
            catch (Exception e)
            {
                Output(
                    $"连接失败。\nBase URL: {_baseUrl}\nModel: {_model}\nException: {e.Message}",
                    OutputType.Error);
                Debug.LogException(e);
            }
            finally
            {
                _isTestingConnection = false;
                Repaint();
            }
        }

        [BoxGroup(COLLECTION_GROUP, ShowLabel = true)]
        [ShowInInspector]
        [HideIf(nameof(IsBusy))]
        [LabelText("String Table")]
        [ValueDropdown(nameof(GetCollectionDropdown), DropdownTitle = "选择本地化表", NumberOfItemsBeforeEnablingSearch = 10)]
        [OnValueChanged(nameof(OnCollectionChanged))]
        [PropertyOrder(10)]
        private StringTableCollection CurrentCollection
        {
            get
            {
                EnsureCollections();
                return _curCollection;
            }
            set => _curCollection = value;
        }

        [BoxGroup(COLLECTION_GROUP)]
        [OnInspectorGUI]
        [HideIf(nameof(IsBusy))]
        [ShowIf(nameof(HasNoCollections))]
        [PropertyOrder(10.5f)]
        private void DrawEmptyCollectionGUI()
        {
            EditorGUILayout.HelpBox("没有找到 StringTableCollection，请先创建 Unity Localization 表。", MessageType.Error);
            if (GUILayout.Button("创建本地化表"))
            {
                LocalizationTablesWindow.ShowWindow();
            }
        }

        [BoxGroup(COLLECTION_GROUP)]
        [HorizontalGroup(COLLECTION_GROUP + "/Actions")]
        [Button("刷新本地化表", ButtonSizes.Medium)]
        [HideIf(nameof(IsBusy))]
        [PropertyOrder(11)]
        private void RefreshCollectionsButton()
        {
            RefreshStringTableCollection();
            RefreshRecords();
            _tableView = null;
        }

        [HorizontalGroup(COLLECTION_GROUP + "/Actions")]
        [Button("打开表格编辑器", ButtonSizes.Medium)]
        [HideIf(nameof(IsBusy))]
        [EnableIf(nameof(HasCurrentCollection))]
        [PropertyOrder(12)]
        private void OpenTableEditor()
        {
            LocalizationTablesWindow.ShowWindow(_curCollection);
        }

        [BoxGroup(ENTRY_GROUP, ShowLabel = true)]
        [HorizontalGroup(ENTRY_GROUP + "/Actions")]
        [Button("全选", ButtonSizes.Medium)]
        [HideIf(nameof(IsBusy))]
        [EnableIf(nameof(HasRecords))]
        [PropertyOrder(20)]
        private void SelectAll()
        {
            foreach (var rec in _recs)
            {
                rec.selected = true;
            }
        }

        [HorizontalGroup(ENTRY_GROUP + "/Actions")]
        [Button("取消全选", ButtonSizes.Medium)]
        [HideIf(nameof(IsBusy))]
        [EnableIf(nameof(HasRecords))]
        [PropertyOrder(21)]
        private void DeselectAll()
        {
            foreach (var rec in _recs)
            {
                rec.selected = false;
            }
        }

        [HorizontalGroup(ENTRY_GROUP + "/Actions")]
        [Button("刷新条目", ButtonSizes.Medium)]
        [HideIf(nameof(IsBusy))]
        [EnableIf(nameof(HasCurrentCollection))]
        [PropertyOrder(22)]
        private void RefreshRecordButton()
        {
            RefreshRecords();
            _tableView = null;
        }

        [HorizontalGroup(ENTRY_GROUP + "/Actions")]
        [Button("翻译选中项", ButtonSizes.Medium)]
        [GUIColor(0.35f, 0.85f, 0.45f)]
        [HideIf(nameof(IsBusy))]
        [EnableIf(nameof(CanTranslateSelected))]
        [PropertyOrder(23)]
        private void TranslateSelectedButton()
        {
            TranslateSelectedRecs();
        }

        [HorizontalGroup(ENTRY_GROUP + "/Actions")]
        [Button("一键翻译全部缺失", ButtonSizes.Medium)]
        [GUIColor(0.45f, 0.75f, 1f)]
        [HideIf(nameof(IsBusy))]
        [EnableIf(nameof(CanTranslateAllMissing))]
        [PropertyOrder(24)]
        private void TranslateAllMissingButton()
        {
            TranslateAllMissingRecs();
        }

        [BoxGroup(ENTRY_GROUP)]
        [OnInspectorGUI]
        [HideIf(nameof(IsBusy))]
        [ShowIf(nameof(HasCurrentCollection))]
        [PropertyOrder(25)]
        private void DrawEntryListGUI()
        {
            OnEntryListGUI();
        }

        [BoxGroup(OUTPUT_GROUP, ShowLabel = true)]
        [OnInspectorGUI]
        [ShowIf(nameof(HasOutput))]
        [PropertyOrder(30)]
        private void DrawOutputBlockGUI()
        {
            OnOutputGUI();
        }

        [BoxGroup(BUSY_GROUP, ShowLabel = true)]
        [OnInspectorGUI]
        [ShowIf(nameof(IsBusy))]
        [PropertyOrder(40)]
        private void DrawBusyBlockGUI()
        {
            OnBusyGUI();
        }

        private SimpleEditorTableView<TranslateRec> CreateTable()
        {
            SimpleEditorTableView<TranslateRec> tableView = new SimpleEditorTableView<TranslateRec>();

            GUIStyle labelGUIStyle = new GUIStyle(GUI.skin.label)
            {
                padding = new RectOffset(left: 10, right: 10, top: 2, bottom: 2)
            };

            tableView.AddColumn("", 30, (rect, rec) =>
            {
                if (rec == null)
                    return;

                rec.selected = EditorGUI.Toggle(
                    position: rect,
                    value: rec.selected
                );
            }).SetMaxWidth(40).SetSorting((a, b) => a.selected.CompareTo(b.selected));

            tableView.AddColumn("Key", 80, (rect, rec) =>
            {
                if (rec == null)
                    return;

                EditorGUI.LabelField(
                    position: rect,
                    label: rec.key,
                    style: labelGUIStyle
                );
            }).SetAutoResize(true).SetSorting((a, b) => String.Compare(a.key, b.key, StringComparison.Ordinal));

            tableView.AddColumn("Src Locales", 100, (rect, rec) =>
            {
                if (rec == null)
                    return;

                EditorGUI.LabelField(
                    position: rect,
                    label: rec.srcLangNames,
                    style: labelGUIStyle
                );
            }).SetAllowToggleVisibility(true);

            tableView.AddColumn("Dst Locales", 100, (rect, rec) =>
            {
                if (rec == null)
                    return;

                EditorGUI.LabelField(
                    position: rect,
                    label: rec.dstLangNames,
                    style: labelGUIStyle
                );
            }).SetAllowToggleVisibility(true);

            tableView.AddColumn("Operation", 180, (rect, rec) =>
            {
                if (rec == null)
                    return;

                Rect rt1 = new Rect(rect.x, rect.y, rect.width / 2, rect.height);

                if (GUI.Button(rt1, "Show Prompt"))
                {
                    TranslateRec refreshedRec = RefreshRecord(rec.key);
                    if (refreshedRec == null)
                    {
                        Output($"条目 [{rec.key}] 已经没有需要翻译的目标语言。", OutputType.Info);
                    }
                    else
                    {
                        Output("System Prompt: \n" + refreshedRec.systemPrompt + "\n User Prompt:\n" + refreshedRec.prompt,
                            OutputType.Prompt);
                    }
                }

                Rect rt2 = new Rect(rect.x + rect.width / 2, rect.y, rect.width / 2, rect.height);

                if (GUI.Button(rt2, "Translate"))
                {
                    TranslateSingleRec(rec);
                }
            });
            return tableView;
        }

        private IEnumerable<ValueDropdownItem<string>> GetModelDropdown()
        {
            return s_validModels
                .Select(model => new ValueDropdownItem<string>(model, model));
        }

        private IEnumerable<ValueDropdownItem<StringTableCollection>> GetCollectionDropdown()
        {
            EnsureCollections();
            if (_collections == null)
                return Enumerable.Empty<ValueDropdownItem<StringTableCollection>>();

            return _collections
                .Where(collection => collection != null)
                .Select(collection => new ValueDropdownItem<StringTableCollection>(collection.TableCollectionName, collection));
        }

        private void EnsureCollections()
        {
            if (_collections == null)
            {
                RefreshStringTableCollection();
            }
        }

        private void OnCollectionChanged()
        {
            RefreshRecords();
            _tableView = null;
        }

        private bool HasCurrentCollection()
        {
            EnsureCollections();
            return _curCollection != null;
        }

        private bool HasNoCollections()
        {
            EnsureCollections();
            return _collections == null || _collections.Length == 0;
        }

        private bool HasRecords()
        {
            if (_recs == null && HasCurrentCollection())
            {
                RefreshRecords();
            }

            return _recs != null && _recs.Length > 0;
        }

        private bool HasOutput() => !string.IsNullOrEmpty(_outputStr) && _outputType != OutputType.None;

        private bool CanTranslateSelected()
        {
            return HasRecords() &&
                   _recs.Any(rec => rec != null && rec.selected) &&
                   !string.IsNullOrWhiteSpace(_apiKey) &&
                   IsValidOpenAIKey(_apiKey);
        }

        private bool CanTranslateAllMissing()
        {
            EnsureCollections();
            return _collections != null &&
                   _collections.Any(collection => collection != null) &&
                   !string.IsNullOrWhiteSpace(_baseUrl) &&
                   !string.IsNullOrWhiteSpace(_model) &&
                   !string.IsNullOrWhiteSpace(_apiKey) &&
                   IsValidOpenAIKey(_apiKey);
        }

        private bool CanTestConnection()
        {
            return !_isTestingConnection &&
                   !string.IsNullOrWhiteSpace(_baseUrl) &&
                   !string.IsNullOrWhiteSpace(_model) &&
                   !string.IsNullOrWhiteSpace(_apiKey) &&
                   IsValidOpenAIKey(_apiKey);
        }

        private void OnBusyGUI()
        {
            if (!TryGetCurrentPendingRec(out TranslateRec rec))
            {
                CancelTask();
                EditorGUILayout.HelpBox("翻译任务状态已经结束，列表正在刷新。", MessageType.Info);
                return;
            }

            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Translating ", EditorStyles.boldLabel);

            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();
            Rect progressRect = GUILayoutUtility.GetRect(100, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
            EditorGUI.ProgressBar(progressRect, GetProgress(),
                $"{rec.DisplayName}({_currentPendingRecIndex + 1}/{_pendingRecs.Length})");
            if (GUILayout.Button("Cancel", GUILayout.Width(100)))
            {
                CancelTask();
            }
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.BeginVertical("Box");
            EditorGUILayout.LabelField("System Prompt: ", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField(rec.systemPrompt, EditorStyles.wordWrappedLabel);
            EditorGUI.indentLevel--;
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("User Prompt: ", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField(rec.prompt, EditorStyles.wordWrappedLabel);
            EditorGUI.indentLevel--;
            EditorGUILayout.Space();
            EditorGUILayout.EndVertical();
        }

        private void OnOutputGUI()
        {
            if (!string.IsNullOrEmpty(_outputStr) && _outputType != OutputType.None)
            {
                EditorGUILayout.Space();
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(_outputType.ToString(), EditorStyles.boldLabel);
                if (GUILayout.Button("Clear", GUILayout.Width(100)))
                {
                    _outputStr = string.Empty;
                    _outputType = OutputType.None;
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space();
                if (_outputType == OutputType.Error)
                {
                    EditorGUILayout.HelpBox(_outputStr, MessageType.Error);
                } else
                {
                    _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(100));
                    EditorGUILayout.TextArea(_outputStr, GUILayout.ExpandHeight(true));
                    EditorGUILayout.EndScrollView();
                }
            }
        }

        private void OnEntryListGUI()
        {
            if (_recs == null)
                RefreshRecords();

            if (_collections == null || _collections.Length == 0)
            {
                EditorGUILayout.HelpBox("没有找到 StringTableCollection，请先创建 Unity Localization 表。", MessageType.Error);
                if (GUILayout.Button("创建本地化表"))
                {
                    LocalizationTablesWindow.ShowWindow();
                }

                return;
            }

            if (_recs == null || _recs.Length == 0)
            {
                EditorGUILayout.HelpBox("当前没有需要翻译的条目。\n只有部分语言缺失的 Entry 会出现在这里。",
                    MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField($"待翻译条目: {_recs.Length}", EditorStyles.boldLabel);

            EditorGUILayout.Space();
            _tableView ??= CreateTable();
            float tableHeight = Mathf.Clamp((_recs.Length + 2) * EditorGUIUtility.singleLineHeight, 120f, 420f);
            _tableView.DrawTableGUI(_recs, tableHeight);
        }


        private bool IsValidOpenAIKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return false;

            if (!IsOfficialOpenAiBaseUrl())
                return true;

            var apiKeyPattern = @"^sk-[A-Za-z0-9_-]{20,}$";
            return System.Text.RegularExpressions.Regex.IsMatch(key.Trim(), apiKeyPattern);
        }

        private bool IsOfficialOpenAiBaseUrl()
        {
            return string.IsNullOrWhiteSpace(_baseUrl) ||
                   _baseUrl.Trim().StartsWith(DEFAULT_BASE_URL, StringComparison.OrdinalIgnoreCase) ||
                   _baseUrl.Trim().StartsWith("https://api.openai.com", StringComparison.OrdinalIgnoreCase);
        }
    }
}
