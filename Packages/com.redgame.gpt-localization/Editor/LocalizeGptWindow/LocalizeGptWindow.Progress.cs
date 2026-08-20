using System.Linq;
using System.Threading.Tasks;
using RedGame.OpenAI;
using UnityEditor;
using UnityEngine;

namespace RedGame.Framework.EditorTools
{
    public partial class LocalizeGptWindow
    {
        private float _taskStartTime;
        private float _taskDuration = 3;
        private Task<CreateChatCompletionResponse> _task;
        private TranslateRec[] _pendingRecs;
        private int _currentPendingRecIndex;
        private bool _isTranslatingAllMissing;
        
        private void TranslateSingleRec(TranslateRec rec)
        {
            if (rec == null)
                return;

            TranslateRec pendingRec = GeneratePendingRec(rec);
            if (pendingRec == null)
            {
                Output($"条目 [{rec.key}] 已经没有需要翻译的目标语言。", OutputType.Info);
                RefreshRecords();
                return;
            }

            InitOpenAi();
            _isTranslatingAllMissing = false;
            _pendingRecs = new[] {pendingRec};
            _currentPendingRecIndex = 0;
            AskGpt(pendingRec);
            _taskStartTime = Time.realtimeSinceStartup;
        }
        
        private void TranslateSelectedRecs()
        {
            if (_recs == null)
                return;

            var recs = _recs
                .Where(rec => rec != null && rec.selected)
                .Select(GeneratePendingRec)
                .Where(rec => rec != null)
                .ToArray();

            if (recs.Length == 0)
            {
                Output("没有可翻译的选中条目。", OutputType.Info);
                RefreshRecords();
                return;
            }

            InitOpenAi();
            _isTranslatingAllMissing = false;
            _pendingRecs = recs;
            
            _currentPendingRecIndex = 0;
            AskGpt(_pendingRecs[0]);
            _taskStartTime = Time.realtimeSinceStartup;
        }

        private void TranslateAllMissingRecs()
        {
            RefreshStringTableCollection();
            TranslateRec[] recs = CollectAllMissingRecs();
            if (recs.Length == 0)
            {
                Output("所有本地化表都已经没有缺失翻译。", OutputType.Info);
                RefreshRecords();
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "一键翻译全部缺失项",
                    $"将自动翻译所有 String Table Collection 中缺失的条目，共 {recs.Length} 条。\n\n该操作会连续请求当前模型并消耗 API 额度，是否继续？",
                    "开始翻译",
                    "取消"))
            {
                RefreshRecords();
                return;
            }

            InitOpenAi();
            _isTranslatingAllMissing = true;
            _pendingRecs = recs;
            _currentPendingRecIndex = 0;
            AskGpt(_pendingRecs[0]);
            _taskStartTime = Time.realtimeSinceStartup;
            Output($"开始一键翻译全部缺失项，共 {recs.Length} 条。", OutputType.Info);
        }

        private TranslateRec[] CollectAllMissingRecs()
        {
            if (_collections == null || _collections.Length == 0)
                return System.Array.Empty<TranslateRec>();

            return _collections
                .Where(collection => collection != null)
                .SelectMany(collection => collection.SharedData.Entries
                    .Select(entry => GeneratePrompt(collection, entry.Key))
                    .Where(rec => rec != null))
                .ToArray();
        }

        private TranslateRec GeneratePendingRec(TranslateRec rec)
        {
            if (rec == null || string.IsNullOrEmpty(rec.key))
                return null;

            var collection = rec.collection ? rec.collection : _curCollection;
            if (!collection)
                return null;

            TranslateRec pendingRec = GeneratePrompt(collection, rec.key);
            if (pendingRec != null)
            {
                pendingRec.selected = rec.selected;
            }

            return pendingRec;
        }

        private void CancelTask()
        {
            _task = null;
            _pendingRecs = null;
            _currentPendingRecIndex = 0;
            _isTranslatingAllMissing = false;
        }
        
        private void UpdateTaskProgress()
        {
            if (_task == null)
                return;

            if (!_task.IsCompleted)
                return;

            if (!TryGetCurrentPendingRec(out TranslateRec rec))
            {
                CancelTask();
                return;
            }

            if (_pendingRecs != null && _currentPendingRecIndex < _pendingRecs.Length)
            {
                if (!OnTaskCompleted(_task, rec))
                {
                    CancelTask();
                    return;
                }
                _taskDuration = Time.realtimeSinceStartup - _taskStartTime;
                
                if (_currentPendingRecIndex < _pendingRecs.Length - 1)
                {
                    _currentPendingRecIndex++;
                    AskGpt(_pendingRecs[_currentPendingRecIndex]);
                    _taskStartTime = Time.realtimeSinceStartup;
                } else
                {
                    string completedKeys = string.Join("\n", _pendingRecs
                        .Where(rec => rec != null)
                        .Select(rec => rec.DisplayName));

                    Output((_isTranslatingAllMissing ? "全部缺失项翻译完成:\n" : "Translation completed:\n") + 
                           completedKeys, OutputType.Info);
                    CancelTask();
                    RefreshRecords();
                }
            }
        }
        
        private float GetProgress()
        {
            if (!HasActiveTranslationTask())
                return 0;
           
            float duration = Mathf.Max(_taskDuration, 0.01f);
            float curTaskProgress = Mathf.Clamp01((Time.realtimeSinceStartup - _taskStartTime) / duration);
            return (_currentPendingRecIndex + curTaskProgress) / _pendingRecs.Length;
        }

        private bool HasActiveTranslationTask()
        {
            return _task != null &&
                   _pendingRecs != null &&
                   _pendingRecs.Length > 0 &&
                   _currentPendingRecIndex >= 0 &&
                   _currentPendingRecIndex < _pendingRecs.Length;
        }

        private bool TryGetCurrentPendingRec(out TranslateRec rec)
        {
            rec = null;
            if (!HasActiveTranslationTask())
                return false;

            rec = _pendingRecs[_currentPendingRecIndex];
            return rec != null;
        }
    }
}
