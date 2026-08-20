using System;
using System.Collections.Generic;
using System.Linq;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace RedGame.Framework.EditorTools
{
    public partial class LocalizeGptWindow : OdinEditorWindow
    {
        private const string DEFAULT_MODEL = "gpt-5.5";
        private string _model = DEFAULT_MODEL;
        private static readonly string[] s_legacyModels =
        {
            "gpt-3.5-turbo",
            "gpt-4-turbo-preview"
        };

        private static string[] s_validModels = 
        {
            "gpt-5.5",
            "gpt-5.2",
            "gpt-4.1",
            "gpt-4.1-mini",
            "gpt-4o",
            "gpt-4o-mini"
        };
        
        private float _temperature;
        private string _apiKey;
        private const string DEFAULT_BASE_URL = "https://api.openai.com/v1";
        private string _baseUrl = DEFAULT_BASE_URL;

        public static void AddModel(string modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName))
                return;

            modelName = modelName.Trim();
            if (s_validModels.Any(model => string.Equals(model, modelName, StringComparison.OrdinalIgnoreCase)))
                return;
            
            s_validModels = s_validModels.Append(modelName).ToArray();
        }
        
        [MenuItem("Tools/GPT Localization")]
        private static void ShowWindow()
        {
            var window = GetWindow<LocalizeGptWindow>();
            window.titleContent = new GUIContent("GPT Localization");
            window.minSize = new Vector2(860, 620);
            window.Show();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            LoadSettings();
            EditorApplication.update += UpdateFrame;
            CancelTask();
        }

        protected override void OnDisable()
        {
            EditorApplication.update -= UpdateFrame;
            CancelTask();
            base.OnDisable();
        }

        private void OnFocus()
        {
            if (!_curCollection)
            {
                RefreshStringTableCollection();
            }
            if (!IsBusy())
            {
                RefreshRecords();
            }
        }
        
        private void Output(string str, OutputType type)
        {
            _outputStr = str;
            _outputType = type;
            if (type == OutputType.Error)
                Debug.LogError(str);
        }

        private void UpdateFrame()
        {
            if (_task != null)
            {
                UpdateTaskProgress();
                Repaint();
            }
        }
        
        private bool IsBusy() => HasActiveTranslationTask();
        
        private void LoadSettings()
        {
            _model = EditorPrefs.GetString("LocalizeGptWindow.Model", _model);
            _temperature = EditorPrefs.GetFloat("LocalizeGptWindow.Temperature", _temperature);
            _baseUrl = EditorPrefs.GetString("LocalizeGptWindow.BaseUrl", _baseUrl);
            _apiKey = EditorPrefs.GetString("LocalizeGptWindow.ApiKey", _apiKey);
            NormalizeSettings();
        }
        
        private void SaveSettings()
        {
            NormalizeSettings();
            EditorPrefs.SetString("LocalizeGptWindow.Model", _model);
            EditorPrefs.SetFloat("LocalizeGptWindow.Temperature", _temperature);
            EditorPrefs.SetString("LocalizeGptWindow.BaseUrl", _baseUrl);
            EditorPrefs.SetString("LocalizeGptWindow.ApiKey", _apiKey);
        }

        private void NormalizeSettings()
        {
            if (string.IsNullOrWhiteSpace(_model) ||
                s_legacyModels.Any(model => string.Equals(model, _model, StringComparison.OrdinalIgnoreCase)))
            {
                _model = DEFAULT_MODEL;
            }
            else
            {
                _model = _model.Trim();
                AddModel(_model);
            }

            if (string.IsNullOrWhiteSpace(_baseUrl))
            {
                _baseUrl = DEFAULT_BASE_URL;
            }
            else
            {
                _baseUrl = _baseUrl.Trim().TrimEnd('/');
            }

            _temperature = Mathf.Clamp01(_temperature);
        }
    }
}
