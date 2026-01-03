using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;
using System.IO;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;

namespace Tools.TMPConverter
{
    public class TMPConverterWindow : EditorWindow
    {
        private TMP_FontAsset targetFont;
        
        // Storage for references during the transition
        [SerializeField] private List<ReferenceRecord> savedReferences = new List<ReferenceRecord>();

        [System.Serializable]
        private struct ReferenceRecord
        {
            public MonoBehaviour sourceScript;
            public string fieldName;
            public GameObject targetGo;
        }

        [MenuItem("Tools/TMP Converter/Open Converter Window")]
        public static void ShowWindow()
        {
            GetWindow<TMPConverterWindow>("TMP Converter");
        }

        private void OnGUI()
        {
            GUILayout.Label("Legacy UI to TextMeshPro Converter", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            targetFont = (TMP_FontAsset)EditorGUILayout.ObjectField("Target Font Asset (SDF)", targetFont, typeof(TMP_FontAsset), false);

            EditorGUILayout.Space();
            DrawLine();

            // --- Step 0 ---
            EditorGUILayout.LabelField("Step 0: Backup References", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Select the Canvas or UI root object first.", MessageType.Info);
            if (GUILayout.Button("Scan & Backup References"))
            {
                ScanAndSaveReferences();
            }
            GUILayout.Label($"Backed up references: {savedReferences.Count}");

            EditorGUILayout.Space();
            DrawLine();

            // --- Step 1 ---
            EditorGUILayout.LabelField("Step 1: Upgrade Scripts", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Replaces Text/Input/Dropdown types in C# scripts with TMP types.", MessageType.Info);
            if (GUILayout.Button("Upgrade C# Scripts"))
            {
                DoUpgradeScriptsLogic();
            }

            // --- Step 2 ---
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Step 2: Convert Components", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Replaces components on GameObjects. Requires Target Font.", MessageType.Info);
            if (GUILayout.Button("Convert UI Components"))
            {
                if (targetFont == null)
                {
                    EditorUtility.DisplayDialog("Error", "Please assign a Target Font Asset first!", "OK");
                    return;
                }
                DoConvertComponentsLogic();
            }

            // --- Step 3 ---
            EditorGUILayout.Space();
            DrawLine();
            EditorGUILayout.LabelField("Step 3: Restore References", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Reconnects script variables to the new TMP components.", MessageType.Info);
            if (GUILayout.Button("Restore References"))
            {
                RestoreReferences();
            }
            
            EditorGUILayout.Space();
            if (GUILayout.Button("Clear Backup Data"))
            {
                savedReferences.Clear();
                Debug.Log("[TMP Converter] Backup data cleared.");
            }
        }

        private void DrawLine()
        {
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        }

        // =========================================================
        // Step 0: Scan & Backup
        // =========================================================
        private void ScanAndSaveReferences()
        {
            if (Selection.gameObjects.Length == 0)
            {
                EditorUtility.DisplayDialog("Warning", "Please select a UI Root GameObject in the Hierarchy.", "OK");
                return;
            }

            savedReferences.Clear();
            foreach (GameObject root in Selection.gameObjects)
            {
                MonoBehaviour[] scripts = root.GetComponentsInChildren<MonoBehaviour>(true);
                foreach (MonoBehaviour mb in scripts)
                {
                    if (mb == null) continue;

                    // Filter out internal UI references (e.g. Dropdown referencing its own Label)
                    // We only care about user scripts referencing UI elements.
                    if (mb is Text || mb is InputField || mb is Dropdown) continue;

                    SerializedObject so = new SerializedObject(mb);
                    SerializedProperty iter = so.GetIterator();
                    while (iter.NextVisible(true))
                    {
                        if (iter.propertyType == SerializedPropertyType.ObjectReference)
                        {
                            Component refComp = iter.objectReferenceValue as Component;
                            if (refComp != null)
                            {
                                bool isTarget = refComp is Text || refComp is InputField || refComp is Dropdown;
                                if (isTarget)
                                {
                                    savedReferences.Add(new ReferenceRecord {
                                        sourceScript = mb,
                                        fieldName = iter.propertyPath,
                                        targetGo = refComp.gameObject
                                    });
                                }
                            }
                        }
                    }
                }
            }
            Debug.Log($"[TMP Converter] Backup complete. {savedReferences.Count} valid user references found.");
        }

        // =========================================================
        // Step 1: Upgrade Scripts
        // =========================================================
        private void DoUpgradeScriptsLogic()
        {
            GameObject[] selectedObjects = Selection.gameObjects;
            HashSet<string> processedPaths = new HashSet<string>();
            int modifiedCount = 0;

            foreach (GameObject go in selectedObjects)
            {
                MonoBehaviour[] scripts = go.GetComponentsInChildren<MonoBehaviour>(true);
                foreach (MonoBehaviour mb in scripts)
                {
                    if (mb == null) continue;
                    MonoScript ms = MonoScript.FromMonoBehaviour(mb);
                    string path = AssetDatabase.GetAssetPath(ms);
                    
                    if (string.IsNullOrEmpty(path) || !path.EndsWith(".cs")) continue;
                    if (path.Contains("PackageCache") || path.Contains("/Plugins/")) continue;
                    if (processedPaths.Contains(path)) continue;

                    string originalCode = File.ReadAllText(path);
                    string code = originalCode;
                    bool isModified = false;

                    // 1. Add Namespace
                    if (code.Contains("using UnityEngine.UI;") && !code.Contains("using TMPro;")) {
                        code = code.Replace("using UnityEngine.UI;", "using UnityEngine.UI;\nusing TMPro;");
                        isModified = true;
                    }

                    // 2. Regex Replacements
                    code = ReplaceType(code, "Text", "TMP_Text", ref isModified);
                    code = ReplaceType(code, "InputField", "TMP_InputField", ref isModified);
                    code = ReplaceType(code, "Dropdown", "TMP_Dropdown", ref isModified);

                    if (isModified && code != originalCode) {
                        File.WriteAllText(path, code);
                        modifiedCount++;
                        Debug.Log($"[TMP Converter] Script Upgraded: {path}");
                    }
                    processedPaths.Add(path);
                }
            }
            
            if (modifiedCount > 0) 
            {
                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog("Success", $"{modifiedCount} scripts upgraded. Unity will now recompile.", "OK");
            }
            else 
            {
                Debug.Log("[TMP Converter] No scripts required upgrading.");
            }
        }

        private string ReplaceType(string code, string oldType, string newType, ref bool isModified)
        {
            // Generic: <Text> -> <TMP_Text>
            string p1 = $@"<\s*{oldType}\s*>";
            if (Regex.IsMatch(code, p1)) { code = Regex.Replace(code, p1, $"<{newType}>"); isModified = true; }
            
            // Full Namespace: UnityEngine.UI.Text
            if (code.Contains($"UnityEngine.UI.{oldType}")) { code = code.Replace($"UnityEngine.UI.{oldType}", newType); isModified = true; }
            
            // typeof(Text)
            string p2 = $@"typeof\s*\(\s*{oldType}\s*\)";
            if (Regex.IsMatch(code, p2)) { code = Regex.Replace(code, p2, $"typeof({newType})"); isModified = true; }
            
            // Field Declaration: public Text myText
            string p3 = $@"\b(public|private|protected|internal|SerializeField)\s+{oldType}\b";
            if (Regex.IsMatch(code, p3)) { code = Regex.Replace(code, p3, $"$1 {newType}"); isModified = true; }
            
            // Array: Text[]
            string p4 = $@"\b{oldType}\s*\[\s*\]";
            if (Regex.IsMatch(code, p4)) { code = Regex.Replace(code, p4, $"{newType}[]"); isModified = true; }

            return code;
        }

        // =========================================================
        // Step 2: Convert Components
        // =========================================================
        private void DoConvertComponentsLogic()
        {
            GameObject[] selectedObjects = Selection.gameObjects;
            int count = 0;
            foreach (GameObject go in selectedObjects)
            {
                Dropdown[] drops = go.GetComponentsInChildren<Dropdown>(true);
                foreach (var d in drops) { ConvertOneDropdown(d); count++; }

                InputField[] inputs = go.GetComponentsInChildren<InputField>(true);
                foreach (var i in inputs) { ConvertOneInputField(i); count++; }

                Text[] texts = go.GetComponentsInChildren<Text>(true);
                foreach (var t in texts) { if(t != null) { ConvertOneText(t); count++; } }
            }
            Debug.Log($"[TMP Converter] Conversion complete. Processed {count} components.");
        }

        private void ConvertOneDropdown(Dropdown legacy)
        {
            if (legacy == null) return;
            GameObject targetGo = legacy.gameObject;
            Undo.RegisterCompleteObjectUndo(targetGo, "Convert Dropdown");

            var interactable = legacy.interactable;
            var transition = legacy.transition;
            var targetGraphic = legacy.targetGraphic;
            var template = legacy.template;
            var captionText = legacy.captionText;
            var itemText = legacy.itemText;
            var value = legacy.value;
            var options = legacy.options;
            
            List<TMP_Dropdown.OptionData> newOptions = new List<TMP_Dropdown.OptionData>();
            foreach(var op in options) newOptions.Add(new TMP_Dropdown.OptionData(op.text, op.image, Color.white));

            legacy.captionText = null; legacy.itemText = null;
            DestroyImmediate(legacy);

            TextMeshProUGUI newCaption = null;
            TextMeshProUGUI newItem = null;
            if (captionText != null) newCaption = ConvertOneText(captionText);
            if (itemText != null) newItem = ConvertOneText(itemText);

            TMP_Dropdown tmpDrop = targetGo.AddComponent<TMP_Dropdown>();
            tmpDrop.interactable = interactable;
            tmpDrop.transition = transition;
            if (targetGraphic != null) tmpDrop.targetGraphic = targetGraphic;
            else if (targetGo.GetComponent<Image>() != null) tmpDrop.targetGraphic = targetGo.GetComponent<Image>();
            tmpDrop.template = template;
            tmpDrop.value = value;
            tmpDrop.options = newOptions; 
            if (newCaption != null) tmpDrop.captionText = newCaption;
            if (newItem != null) tmpDrop.itemText = newItem;
        }

        private void ConvertOneInputField(InputField legacy)
        {
            if (legacy == null) return;
            GameObject targetGo = legacy.gameObject;
            Undo.RegisterCompleteObjectUndo(targetGo, "Convert InputField");
            var interactable = legacy.interactable;
            var transition = legacy.transition;
            var targetGraphic = legacy.targetGraphic;
            var colors = legacy.colors;
            string text = ""; try { text = legacy.text; } catch {} 
            var charLimit = legacy.characterLimit;
            var contentType = legacy.contentType;
            var lineType = legacy.lineType;
            var inputType = legacy.inputType;
            var keyboardType = legacy.keyboardType;
            var charVal = legacy.characterValidation;
            Text legacyTextComp = legacy.textComponent;
            Graphic legacyPlaceholder = legacy.placeholder;
            Color caretColor = Color.black; 
            if (legacy.customCaretColor) caretColor = legacy.caretColor;
            else if (legacyTextComp != null) caretColor = legacyTextComp.color;
            var selectionColor = legacy.selectionColor;
            int caretWidth = legacy.caretWidth;
            legacy.textComponent = null; legacy.placeholder = null;
            DestroyImmediate(legacy);
            TextMeshProUGUI newTextComp = null;
            TextMeshProUGUI newPlaceholder = null;
            if (legacyTextComp != null) newTextComp = ConvertOneText(legacyTextComp);
            if (legacyPlaceholder != null && legacyPlaceholder is Text phText) newPlaceholder = ConvertOneText(phText);
            TMP_InputField tmpInput = targetGo.AddComponent<TMP_InputField>();
            tmpInput.interactable = interactable;
            tmpInput.transition = transition;
            if (targetGraphic != null) tmpInput.targetGraphic = targetGraphic;
            else if (targetGo.GetComponent<Image>() != null) tmpInput.targetGraphic = targetGo.GetComponent<Image>();
            tmpInput.colors = colors;
            tmpInput.text = text;
            tmpInput.characterLimit = charLimit;
            tmpInput.contentType = (TMP_InputField.ContentType)contentType;
            tmpInput.lineType = (TMP_InputField.LineType)lineType;
            tmpInput.inputType = (TMP_InputField.InputType)inputType;
            tmpInput.keyboardType = keyboardType;
            tmpInput.characterValidation = (TMP_InputField.CharacterValidation)charVal;
            tmpInput.caretColor = caretColor;
            tmpInput.selectionColor = selectionColor;
            tmpInput.caretWidth = caretWidth;
            if (newTextComp != null) { tmpInput.textComponent = newTextComp; tmpInput.textViewport = newTextComp.rectTransform; }
            if (newPlaceholder != null) { tmpInput.placeholder = newPlaceholder; }
        }

        private TextMeshProUGUI ConvertOneText(Text legacyText)
        {
            if (legacyText == null) return null;
            GameObject targetGo = legacyText.gameObject;
            string content = legacyText.text;
            float fontSize = legacyText.fontSize;
            Color color = legacyText.color;
            TextAnchor alignment = legacyText.alignment;
            bool raycastTarget = legacyText.raycastTarget;
            bool richText = legacyText.supportRichText;
            FontStyle fontStyle = legacyText.fontStyle;
            bool bestFit = legacyText.resizeTextForBestFit;
            int minSize = legacyText.resizeTextMinSize;
            int maxSize = legacyText.resizeTextMaxSize;
            try { DestroyImmediate(legacyText); } catch { return null; }
            TextMeshProUGUI tmp = targetGo.AddComponent<TextMeshProUGUI>();
            if (tmp == null) return null;
            tmp.text = content; tmp.font = targetFont; tmp.fontSize = fontSize; tmp.color = color;
            tmp.raycastTarget = raycastTarget; tmp.richText = richText; tmp.alignment = ConvertAlignment(alignment);
            if (fontStyle == FontStyle.Bold) tmp.fontStyle = FontStyles.Bold;
            else if (fontStyle == FontStyle.Italic) tmp.fontStyle = FontStyles.Italic;
            else if (fontStyle == FontStyle.BoldAndItalic) tmp.fontStyle = FontStyles.Bold | FontStyles.Italic;
            if (bestFit) { tmp.enableAutoSizing = true; tmp.fontSizeMin = minSize; tmp.fontSizeMax = maxSize; }
            return tmp;
        }
        
        private TextAlignmentOptions ConvertAlignment(TextAnchor anchor) {
            switch (anchor) {
                case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
                case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
                case TextAnchor.UpperRight: return TextAlignmentOptions.TopRight;
                case TextAnchor.MiddleLeft: return TextAlignmentOptions.Left;
                case TextAnchor.MiddleCenter: return TextAlignmentOptions.Center;
                case TextAnchor.MiddleRight: return TextAlignmentOptions.Right;
                case TextAnchor.LowerLeft: return TextAlignmentOptions.BottomLeft;
                case TextAnchor.LowerCenter: return TextAlignmentOptions.Bottom;
                case TextAnchor.LowerRight: return TextAlignmentOptions.BottomRight;
                default: return TextAlignmentOptions.Center;
            }
        }

        // =========================================================
        // Step 3: Restore References
        // =========================================================
        private void RestoreReferences()
        {
            int successCount = 0;
            int failCount = 0;
            Debug.Log("[TMP Converter] Restoring references...");

            foreach (var record in savedReferences)
            {
                if (record.sourceScript == null) continue;
                if (record.targetGo == null) continue;

                SerializedObject so = new SerializedObject(record.sourceScript);
                SerializedProperty prop = so.FindProperty(record.fieldName);
                if (prop == null) continue;

                Component newComponent = null;
                newComponent = record.targetGo.GetComponent<TMP_Dropdown>(); 
                if (newComponent == null) newComponent = record.targetGo.GetComponent<TMP_InputField>(); 
                if (newComponent == null) newComponent = record.targetGo.GetComponent<TMP_Text>(); 

                if (newComponent == null)
                {
                    Debug.LogWarning($"[Warning] No TMP Component found on '{record.targetGo.name}'. Variable: {record.fieldName}", record.sourceScript);
                    failCount++;
                    continue;
                }

                try 
                {
                    prop.objectReferenceValue = newComponent;
                    so.ApplyModifiedProperties();
                    
                    if (prop.objectReferenceValue == newComponent)
                    {
                        successCount++;
                    }
                    else
                    {
                        string scriptName = record.sourceScript.GetType().Name;
                        Debug.LogError($"[Type Mismatch] Failed to assign {newComponent.GetType().Name} to '{record.fieldName}'.\n" +
                                       $"Script: {scriptName}\n" +
                                       $"Expected Type: {prop.type}\n" +
                                       $"Action: Please check if the script variable was correctly upgraded to TMP types.", record.sourceScript);
                        failCount++;
                    }
                }
                catch { failCount++; }
            }
            Debug.Log($"[TMP Converter] Restore complete. Success: {successCount}, Failed: {failCount}");
            if (failCount == 0) savedReferences.Clear();
        }
    }
}