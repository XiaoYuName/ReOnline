using System;
using System.Collections.Generic;
using UnityEditor.Localization;
using UnityEngine.Localization.Tables;

namespace RedGame.Framework.EditorTools
{
    public partial class LocalizeGptWindow
    {
        private class TranslateRec
        {
            public bool selected;
            public StringTableCollection collection;
            public string key;
            public List<StringTable> srcTables;
            public List<StringTable> dstTables;
            public string srcLangNames;
            public string dstLangNames;
            public string prompt;
            public string systemPrompt;
            public string DisplayName => collection ? $"{collection.TableCollectionName}/{key}" : key;
        }
        
        private TranslateRec[] _recs;

        private TranslateRec RefreshRecord(string key)
        {
            if (_recs == null)
                return null;

            int index = Array.FindIndex(_recs, rec => rec != null && rec.key == key);
            if (index < 0)
                return null;

            bool selected = _recs[index].selected;
            TranslateRec updatedRec = GeneratePrompt(_curCollection, key);
            if (updatedRec != null)
            {
                updatedRec.selected = selected;
                _recs[index] = updatedRec;
                return updatedRec;
            }

            List<TranslateRec> recs = new List<TranslateRec>(_recs);
            recs.RemoveAt(index);
            _recs = recs.ToArray();
            return null;
        }

        private void RefreshRecords()
        {
            if (!_curCollection)
            {
                if (_recs == null || _recs.Length > 0)
                    _recs = Array.Empty<TranslateRec>();
                return;
            }

            Dictionary<string, bool> selectedKeys = new Dictionary<string, bool>();
            if (_recs != null)
            {
                foreach (var rec in _recs)
                {
                    if (rec == null)
                        continue;

                    selectedKeys[rec.key] = rec.selected;
                }
            }

            List<TranslateRec> recs = new List<TranslateRec>();
            foreach (var entry in _curCollection.SharedData.Entries)
            {
                var rec = GeneratePrompt(_curCollection, entry.Key);
                if (rec == null)
                    continue;
                if (selectedKeys.TryGetValue(entry.Key, out bool selected))
                {
                    rec.selected = selected;
                } else
                {
                    rec.selected = true;
                }

                recs.Add(rec);
            }

            _recs = recs.ToArray();
        }
    }
}
