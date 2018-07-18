// ----------------------------------------------------------------------------
// The MIT License
// Leopotam.GoogleDocs.Import https://github.com/Leopotam/googledocs-import
// Copyright (c) 2018 Leopotam <leopotam@gmail.com>
// ----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Leopotam.GoogleDocs.Import.UnityEditors {
    /// <summary>
    /// GoogleDocs data downloader.
    /// </summary>
    sealed class GoogleDocsDownloader : EditorWindow {
        const string StorePath = "{0}/../ProjectSettings/com.leopotam.googledocs.import.txt";
        const string Title = "GoogleDocs Downloader";
        const string UrlDefault = "http://localhost";
        const string ResDefault = "NewCsv.csv";
        static readonly Regex CsvMultilineFixRegex = new Regex ("\"([^\"]|\"\"|\\n)*\"");
        static readonly Regex CsvParseRegex = new Regex ("(?<=^|,)(\"(?:[^\"]|\"\")*\"|[^,]*)");
        static readonly List<string> _csvBuffer = new List<string> (32);
        List<RecordInfo> _items;
        Vector2 _scrollPos;
        string _newUrl;
        string _newAsset;
        JsonMode _newMode;

        [MenuItem ("Window/Leopotam/Download GoogleDocs data...")]
        static void OpenEditorWindow () {
            var win = GetWindow<GoogleDocsDownloader> (true);
            var pos = win.position;
            pos.width = 600f;
            pos.height = 300f;
            win.position = pos;
            win.titleContent.text = Title;
        }

        void OnEnable () {
            _scrollPos = Vector2.zero;
        }

        void OnGUI () {
            if (_items == null) {
                _items = LoadSettings ();
            }
            if (string.IsNullOrEmpty (_newUrl) || string.IsNullOrEmpty (_newAsset)) {
                _newUrl = UrlDefault;
                _newAsset = ResDefault;
                _newMode = JsonMode.None;
            }
            var needSave = false;
            if (_items.Count > 0) {
                EditorGUILayout.LabelField ("Resources", EditorStyles.boldLabel);
                _scrollPos = GUILayout.BeginScrollView (_scrollPos, false, true);
                for (var i = 0; i < _items.Count; i++) {
                    var item = _items[i];
                    GUILayout.BeginHorizontal (GUI.skin.box);
                    GUILayout.BeginVertical ();
                    GUILayout.BeginHorizontal ();
                    EditorGUILayout.LabelField ("External url path:", EditorStyles.label, GUILayout.Width (EditorGUIUtility.labelWidth));
                    EditorGUILayout.SelectableLabel (item.Url, EditorStyles.textField, GUILayout.Height (EditorGUIUtility.singleLineHeight));
                    GUILayout.EndHorizontal ();
                    GUILayout.BeginHorizontal ();
                    EditorGUILayout.LabelField ("Local resource path:", EditorStyles.label, GUILayout.Width (EditorGUIUtility.labelWidth));
                    EditorGUILayout.SelectableLabel (item.Asset, EditorStyles.textField, GUILayout.Height (EditorGUIUtility.singleLineHeight));
                    GUILayout.EndHorizontal ();
                    GUILayout.BeginHorizontal ();
                    EditorGUILayout.LabelField ("Convert to JSON:", EditorStyles.label, GUILayout.Width (EditorGUIUtility.labelWidth));
                    GUI.enabled = false;
                    EditorGUILayout.EnumPopup (item.Mode);
                    GUI.enabled = true;
                    GUILayout.EndHorizontal ();
                    GUILayout.EndVertical ();
                    if (GUILayout.Button ("Remove", GUILayout.Width (80f), GUILayout.Height (52f))) {
                        _items.Remove (item);
                        needSave = true;
                    }
                    GUILayout.EndHorizontal ();
                }
                GUILayout.EndScrollView ();
            }
            GUILayout.Space (16f);
            EditorGUILayout.LabelField ("New resource", EditorStyles.boldLabel);
            GUILayout.BeginHorizontal (GUI.skin.box);
            GUILayout.BeginVertical ();
            _newUrl = EditorGUILayout.TextField ("External Url path:", _newUrl).Trim ();
            _newAsset = EditorGUILayout.TextField ("Resource file:", _newAsset).Trim ();
            _newMode = (JsonMode) EditorGUILayout.EnumPopup ("Convert to JSON:", _newMode);
            GUILayout.EndVertical ();
            if (GUILayout.Button ("Add", GUILayout.Width (80f), GUILayout.Height (52f))) {
                var newItem = new RecordInfo ();
                newItem.Url = _newUrl;
                newItem.Asset = _newAsset;
                newItem.Mode = _newMode;
                _items.Add (newItem);
                _newUrl = UrlDefault;
                _newAsset = ResDefault;
                _newMode = JsonMode.None;
                needSave = true;
            }
            GUILayout.EndHorizontal ();
            if (needSave) {
                SaveSettings (_items);
            }
            GUILayout.Space (16f);
            GUI.enabled = _items.Count > 0;
            if (GUILayout.Button ("Download data", GUILayout.Height (30f))) {
                var res = Process (_items);
                EditorUtility.DisplayDialog (titleContent.text, res ?? "Success", "Close");
            }
            GUI.enabled = true;
        }

        static List<RecordInfo> LoadSettings () {
            List<RecordInfo> res;
            try {
                var assetPath = string.Format (StorePath, Application.dataPath);
                var savedData = JsonUtility.FromJson<SavedData> (File.ReadAllText (assetPath));
                if (savedData == null || savedData.Items == null) {
                    throw new Exception ();
                }
                res = savedData.Items;
            } catch {
                res = new List<RecordInfo> ();
            }
            return res;
        }

        static void SaveSettings (List<RecordInfo> items) {
            var assetPath = string.Format (StorePath, Application.dataPath);
            if (items != null && items.Count > 0) {
                var data = new SavedData ();
                data.Items = items;
                File.WriteAllText (assetPath, JsonUtility.ToJson (data));
            } else {
                if (File.Exists (assetPath)) {
                    File.Delete (assetPath);
                }
            }
        }

        static string ConvertToDictJson (string data) {
            var sb = new StringBuilder (data.Length * 2);
            var list = CsvToDict (data);
            if (list.Count < 2) {
                throw new Exception ("Invalid header data: first line should contains field names, second line - pair of wrapping chars.");
            }

            sb.Append ("{");
            var it = list.GetEnumerator ();

            // header.
            it.MoveNext ();
            var headerKey = it.Current.Key;
            var headerValue = it.Current.Value;

            // wrappers.
            it.MoveNext ();
            var wrapperKey = it.Current.Key;
            var wrapperValue = it.Current.Value;

            if (wrapperKey != "\"\"") {
                throw new Exception (string.Format ("Invalid wrapper data for \"{0}\" field: it should be wrapped with \"\".", headerKey));
            }

            for (var i = 0; i < wrapperValue.Length; i++) {
                if (!(
                        wrapperValue[i] == string.Empty ||
                        wrapperValue[i] == "[]" ||
                        wrapperValue[i] == "{}" ||
                        string.Compare (wrapperValue[i], "IGNORE", true) == 0 ||
                        wrapperValue[i] == "\"\"")) {
                    throw new Exception (string.Format ("Invalid wrapper data for \"{0}\" field.", headerValue[i]));
                }
            }

            var needObjectsComma = false;
            string itemValue;
            string wrapChars;
            while (it.MoveNext ()) {
                sb.AppendFormat ("{0}\"{1}\":{{", needObjectsComma ? "," : string.Empty, it.Current.Key);
                var needFieldsComma = false;
                for (var i = 0; i < headerValue.Length; i++) {
                    wrapChars = wrapperValue[i];
                    if (string.Compare (wrapChars, "IGNORE", true) == 0) {
                        continue;
                    }
                    itemValue = wrapChars.Length > 0 ?
                        string.Format ("{0}{1}{2}", wrapChars[0], it.Current.Value[i], wrapChars[1]) : it.Current.Value[i];
                    sb.AppendFormat ("{0}\"{1}\":{2}", needFieldsComma ? "," : string.Empty, headerValue[i], itemValue);
                    needFieldsComma = true;
                }
                sb.Append ("}");
                needObjectsComma = true;
            }

            sb.Append ("}");
            return sb.ToString ();
        }

        static string ConvertToArrayJson (string data) {
            var sb = new StringBuilder (data.Length * 2);
            var list = CsvToArray (data);
            if (list.Count < 2) {
                throw new Exception ("Invalid header data: first line should contains field names, second line - pair of wrapping chars.");
            }

            sb.Append ("[");
            var it = list.GetEnumerator ();

            // header.
            it.MoveNext ();
            var headerValue = it.Current;

            // wrappers.
            it.MoveNext ();
            var wrapperValue = it.Current;
            for (var i = 0; i < wrapperValue.Length; i++) {
                if (!(
                        wrapperValue[i] == string.Empty ||
                        wrapperValue[i] == "[]" ||
                        wrapperValue[i] == "{}" ||
                        string.Compare (wrapperValue[i], "IGNORE", true) == 0 ||
                        wrapperValue[i] == "\"\"")) {
                    throw new Exception (string.Format ("Invalid wrapper data for \"{0}\" field.", headerValue[i]));
                }
            }

            var needObjectsComma = false;
            string itemValue;
            string wrapChars;
            while (it.MoveNext ()) {
                sb.AppendFormat ("{0}{{", needObjectsComma ? "," : string.Empty);
                var needFieldsComma = false;
                for (var i = 0; i < headerValue.Length; i++) {
                    wrapChars = wrapperValue[i];
                    if (string.Compare (wrapChars, "IGNORE", true) == 0) {
                        continue;
                    }
                    itemValue = wrapChars.Length > 0 ?
                        string.Format ("{0}{1}{2}", wrapChars[0], it.Current[i], wrapChars[1]) : it.Current[i];
                    sb.AppendFormat ("{0}\"{1}\":{2}", needFieldsComma ? "," : string.Empty, headerValue[i], itemValue);
                    needFieldsComma = true;
                }
                sb.Append ("}");
                needObjectsComma = true;
            }

            sb.Append ("]");
            return sb.ToString ();
        }

        static void ParseCsvLine (string data) {
            _csvBuffer.Clear ();
            foreach (Match m in CsvParseRegex.Matches (data)) {
                var part = m.Value.Trim ();
                if (part.Length > 0) {
                    if (part[0] == '"' && part[part.Length - 1] == '"') {
                        part = part.Substring (1, part.Length - 2);
                    }
                    part = part.Replace ("\"\"", "\"");
                }
                _csvBuffer.Add (part);
            }
        }

        static Dictionary<string, string[]> CsvToDict (string data) {
            var list = new Dictionary<string, string[]> ();
            var headerLen = -1;
            string key;
            using (var reader = new StringReader (data)) {
                while (reader.Peek () != -1) {
                    ParseCsvLine (reader.ReadLine ());
                    if (_csvBuffer.Count > 0 && !string.IsNullOrEmpty (_csvBuffer[0])) {
                        if (headerLen == -1) {
                            headerLen = _csvBuffer.Count;
                        }
                        if (_csvBuffer.Count != headerLen) {
                            Debug.LogWarning ("Invalid csv line, skipping.");
                            continue;
                        }
                        key = _csvBuffer[0];
                        _csvBuffer.RemoveAt (0);
                        list[key] = _csvBuffer.ToArray ();
                    }
                }
            }
            return list;
        }

        static List<string[]> CsvToArray (string data) {
            var list = new List<string[]> ();
            var headerLen = -1;
            using (var reader = new StringReader (data)) {
                while (reader.Peek () != -1) {
                    ParseCsvLine (reader.ReadLine ());
                    if (_csvBuffer.Count > 0) {
                        if (headerLen == -1) {
                            headerLen = _csvBuffer.Count;
                        }
                        if (_csvBuffer.Count != headerLen) {
                            Debug.LogWarning ("Invalid csv line, skipping.");
                            continue;
                        }
                        list.Add (_csvBuffer.ToArray ());
                    }
                }
            }
            return list;
        }

        public static string Process (List<RecordInfo> items) {
            if (items == null || items.Count == 0) {
                return "No data";
            }
            try {
                ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
                using (var www = new WebClient ()) {
                    www.Encoding = Encoding.UTF8;
                    string data;
                    foreach (var item in items) {
                        if (!string.IsNullOrEmpty (item.Url) && !string.IsNullOrEmpty (item.Asset)) {
                            // dirty hack for url, because standard "publish to web" has huge lag up to 30 minutes.
                            try {
                                data = www.DownloadString (item.Url.Replace ("?", string.Empty).Replace ("/edit", "/export?format=csv&"));
                            } catch (Exception urlEx) {
                                throw new Exception (string.Format ("\"{0}\": {1}", item.Url, urlEx.Message));
                            }
                            var path = string.Format ("{0}/{1}", Application.dataPath, item.Asset);
                            var folder = Path.GetDirectoryName (path);
                            if (!Directory.Exists (folder)) {
                                Directory.CreateDirectory (folder);
                            }
                            // fix for multiline string.
                            data = CsvMultilineFixRegex.Replace (data, m => m.Value.Replace ("\n", "\\n"));
                            // json generation.
                            switch (item.Mode) {
                                case JsonMode.Array:
                                    data = ConvertToArrayJson (data);
                                    break;
                                case JsonMode.Dictionary:
                                    data = ConvertToDictJson (data);
                                    break;
                            }
                            File.WriteAllText (path, data, Encoding.UTF8);
                        }
                    }
                }
                AssetDatabase.Refresh ();
                return null;
            } catch (Exception ex) {
                AssetDatabase.Refresh ();
                return ex.Message;
            } finally {
                ServicePointManager.ServerCertificateValidationCallback = null;
            }
        }

        [Serializable]
        sealed class SavedData {
            public List<RecordInfo> Items;
        }

        [Serializable]
        public sealed class RecordInfo {
            public string Url = string.Empty;
            public string Asset = string.Empty;
            public JsonMode Mode = JsonMode.None;
        }

        public enum JsonMode {
            None = 0,
            Array = 1,
            Dictionary = 2
        }
    }
}