using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using COMP2165.Clicker;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace COMP2165.Clicker.Editor
{
    public static class ClickerProjectBuilder
    {
        static readonly string[] SheetNames = { "Settings", "Currencies", "Buildings", "Upgrades", "Events", "Visuals", "Audio" };
        static string Root { get { return Directory.GetParent(Application.dataPath).FullName; } }
        static string ScenePath
        {
            get
            {
                foreach (string path in new[] { "Assets/Resources/Scenes/Clicker.unity", "Assets/Scenes/Clicker.unity", "Assets/Clicker/Scenes/Clicker.unity" })
                    if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null) return path;
                return "Assets/Scenes/Clicker.unity";
            }
        }

        [MenuItem("COMP2165/Prepare Clicker Scene")]
        public static void PrepareProject()
        {
            string scenePath = ScenePath;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) != null)
            {
                ClickerCanvasSceneBuilder.CreateEditableCanvas();
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scenePath, true) };
                return;
            }
            scenePath = "Assets/Scenes/Clicker.unity";
            Directory.CreateDirectory(Path.Combine(Application.dataPath, "Scenes"));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Camera", typeof(Camera));
            camera.tag = "MainCamera";
            camera.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor;
            camera.GetComponent<Camera>().backgroundColor = new Color(0.06f, 0.08f, 0.12f);
            new GameObject("CSV Clicker Game", typeof(ClickerGame));
            EditorSceneManager.SaveScene(scene, scenePath);
            ClickerCanvasSceneBuilder.CreateEditableCanvas();
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scenePath, true) };
            PlayerSettings.productName = "COMP2165 Clicker";
            PlayerSettings.companyName = "COMP2165 Game Studio";
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.runInBackground = true;
            AssetDatabase.SaveAssets();
            Debug.Log("Clicker scene prepared: " + scenePath);
        }

        [MenuItem("COMP2165/Export Workbook to CSV")]
        public static void ExportWorkbook()
        {
            string input = Path.Combine(Root, "Student Content", "Clicker Template.xlsx");
            if (!File.Exists(input)) throw new FileNotFoundException("Student workbook missing", input);
            string output = Path.Combine(Root, "Student Content");
            Directory.CreateDirectory(output);
            using (var file = ZipFile.OpenRead(input))
            {
                XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                XNamespace rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
                XNamespace packageRel = "http://schemas.openxmlformats.org/package/2006/relationships";
                var workbook = Xml(file, "xl/workbook.xml");
                var relationships = Xml(file, "xl/_rels/workbook.xml.rels");
                var relPaths = relationships.Descendants(packageRel + "Relationship").ToDictionary(x => (string)x.Attribute("Id"), x => (string)x.Attribute("Target"));
                var shared = new List<string>();
                if (file.GetEntry("xl/sharedStrings.xml") != null)
                {
                    var strings = Xml(file, "xl/sharedStrings.xml");
                    foreach (var si in strings.Descendants(main + "si")) shared.Add(string.Concat(si.Descendants(main + "t").Select(x => x.Value)));
                }
                var sheets = workbook.Descendants(main + "sheet").ToDictionary(x => (string)x.Attribute("name"), x => (string)x.Attribute(rel + "id"));
                foreach (string name in SheetNames)
                {
                    string id;
                    if (!sheets.TryGetValue(name, out id)) throw new InvalidDataException("Missing workbook sheet: " + name);
                    string target = relPaths[id].Replace('\\', '/').TrimStart('/');
                    if (!target.StartsWith("xl/")) target = "xl/" + target;
                    var document = Xml(file, target);
                    var rows = new List<List<string>>();
                    foreach (var row in document.Descendants(main + "sheetData").Elements(main + "row"))
                    {
                        int rowIndex = (int)row.Attribute("r") - 1;
                        while (rows.Count <= rowIndex) rows.Add(new List<string>());
                        foreach (var cell in row.Elements(main + "c"))
                        {
                            int column = ColumnIndex((string)cell.Attribute("r"));
                            while (rows[rowIndex].Count <= column) rows[rowIndex].Add("");
                            string type = (string)cell.Attribute("t");
                            string value = (string)cell.Element(main + "v") ?? "";
                            if (type == "s" && int.TryParse(value, out int stringIndex)) value = shared[stringIndex];
                            else if (type == "inlineStr") value = string.Concat(cell.Descendants(main + "t").Select(x => x.Value));
                            rows[rowIndex][column] = value;
                        }
                    }
                    if (rows.Count < 2 || rows[0].Count == 0) throw new InvalidDataException(name + " must contain a header and at least one data row.");
                    if ((name == "Buildings" || name == "Upgrades") && !rows[0].Contains("tooltip"))
                    {
                        string existing = Path.Combine(output, name + ".csv");
                        var saved = File.Exists(existing)
                            ? Csv.Read(File.ReadAllText(existing)).Where(row => !string.IsNullOrEmpty(Csv.Get(row, "id")))
                                .ToDictionary(row => Csv.Get(row, "id"), row => Csv.Get(row, "tooltip"), StringComparer.OrdinalIgnoreCase)
                            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        int idColumn = rows[0].IndexOf("id");
                        int descriptionColumn = rows[0].IndexOf("description");
                        if (idColumn < 0 || descriptionColumn < 0) throw new InvalidDataException(name + " needs id and description columns.");
                        rows[0].Add("tooltip");
                        for (int index = 1; index < rows.Count; index++)
                        {
                            string rowId = idColumn < rows[index].Count ? rows[index][idColumn] : "";
                            string description = descriptionColumn < rows[index].Count ? rows[index][descriptionColumn] : "";
                            rows[index].Add(saved.TryGetValue(rowId, out string tooltip) ? tooltip : description);
                        }
                    }
                    int width = rows[0].Count;
                    var csv = new StringBuilder();
                    foreach (var row in rows)
                    {
                        if (row.All(string.IsNullOrEmpty)) continue;
                        for (int column = 0; column < width; column++)
                        {
                            if (column > 0) csv.Append(',');
                            string value = column < row.Count ? row[column] : "";
                            csv.Append('"').Append(value.Replace("\"", "\"\"")).Append('"');
                        }
                        csv.Append("\r\n");
                    }
                    File.WriteAllText(Path.Combine(output, name + ".csv"), csv.ToString(), new UTF8Encoding(false));
                }
            }
            CopyStudentCsvs();
            AssetDatabase.Refresh();
            Debug.Log("Exported seven workbook sheets to Student Content and synced them to StreamingAssets/Data.");
        }

        static XDocument Xml(ZipArchive zip, string path)
        {
            var entry = zip.GetEntry(path);
            if (entry == null) throw new InvalidDataException("Workbook entry missing: " + path);
            using (var stream = entry.Open()) return XDocument.Load(stream);
        }

        static int ColumnIndex(string reference)
        {
            int value = 0;
            foreach (char character in reference)
            {
                if (!char.IsLetter(character)) break;
                value = value * 26 + char.ToUpperInvariant(character) - 'A' + 1;
            }
            return value - 1;
        }

        [MenuItem("COMP2165/Build WebGL ZIP")]
        public static void BuildWebGL()
        {
            CopyStudentCsvs();
            ValidateMediaPaths();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null) PrepareProject();
            else ClickerCanvasSceneBuilder.CreateEditableCanvas();
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL))
                throw new InvalidOperationException("WebGL Build Support is missing. Add it to Unity 6000.3.9f1 in Unity Hub.");
            PlayerSettings.productName = "Cookie Works";
            string output = Path.Combine(Root, "Builds", "WebGL");
            Directory.CreateDirectory(output);
            var options = new BuildPlayerOptions { scenes = new[] { ScenePath }, locationPathName = output, target = BuildTarget.WebGL, options = BuildOptions.None };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded) throw new Exception("WebGL build failed: " + report.summary.result);
            string indexPath = Path.Combine(output, "index.html");
            string index = File.ReadAllText(indexPath);
            string sizedIndex = System.Text.RegularExpressions.Regex.Replace(index,
                @"(<canvas\s+id=""unity-canvas""\s+width=)\d+(\s+height=)\d+",
                "${1}1280${2}720");
            if (sizedIndex == index) throw new InvalidDataException("Could not size the WebGL canvas to 1280 x 720.");
            string desktopIndex = System.Text.RegularExpressions.Regex.Replace(sizedIndex,
                @"canvas\.style\.width\s*=\s*""\d+px"";\s*canvas\.style\.height\s*=\s*""\d+px"";",
                "canvas.style.width = \"960px\";\n        canvas.style.height = \"540px\";");
            if (desktopIndex == sizedIndex) throw new InvalidDataException("Could not size the desktop WebGL canvas to 16:9.");
            File.WriteAllText(indexPath, desktopIndex);
            foreach (string launcher in new[] { "Play WebGL.bat", "Play-WebGL.ps1" })
            {
                string source = Path.Combine(Root, launcher);
                if (!File.Exists(source)) throw new FileNotFoundException("Local play launcher missing", source);
                File.Copy(source, Path.Combine(output, launcher), true);
            }
            string zip = Path.Combine(Root, "Builds", "COMP2165_Clicker_itchio.zip");
            if (File.Exists(zip)) File.Delete(zip);
            ZipFile.CreateFromDirectory(output, zip, System.IO.Compression.CompressionLevel.Optimal, false);
            Debug.Log("itch.io WebGL ZIP ready: " + zip);
        }

        [MenuItem("COMP2165/Export Student Unity Package")]
        public static void ExportStudentUnityPackage()
        {
            string[] folders = { "Assets/Resources", "Assets/StreamingAssets", "Assets/ThirdParty", "Assets/ClickerPackageSeed" };
            foreach (string folder in folders)
                if (!AssetDatabase.IsValidFolder(folder)) throw new DirectoryNotFoundException("Package folder is missing: " + folder);
            string output = Path.Combine(Root, "Builds", "COMP2165_Clicker_Student_Import.unitypackage");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            AssetDatabase.ExportPackage(folders, output, ExportPackageOptions.Recurse);
            Debug.Log("Student Unity package ready: " + output);
        }

        static void CopyStudentCsvs()
        {
            string source = Path.Combine(Root, "Student Content");
            string destination = Path.Combine(Application.streamingAssetsPath, "Data");
            Directory.CreateDirectory(destination);
            foreach (string name in SheetNames.Concat(new[] { "Palette" }))
            {
                string file = Path.Combine(source, name + ".csv");
                if (!File.Exists(file)) throw new FileNotFoundException("Student CSV missing", file);
                File.Copy(file, Path.Combine(destination, name + ".csv"), true);
            }
        }

        static void ValidateMediaPaths()
        {
            string mediaRoot = Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, "Media")) + Path.DirectorySeparatorChar;
            foreach (string table in new[] { "Visuals", "Audio" })
            {
                string csv = Path.Combine(Application.streamingAssetsPath, "Data", table + ".csv");
                foreach (var row in Csv.Read(File.ReadAllText(csv)))
                {
                    string mediaPath = Csv.Get(row, "path");
                    if (string.IsNullOrWhiteSpace(mediaPath)) continue;
                    if (mediaPath.Contains("..") || mediaPath.Contains(":") || Path.IsPathRooted(mediaPath))
                        throw new InvalidDataException(table + ".csv has an invalid media path: " + mediaPath);
                    string fullPath = Path.GetFullPath(Path.Combine(mediaRoot, mediaPath.Replace('/', Path.DirectorySeparatorChar)));
                    if (!fullPath.StartsWith(mediaRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
                        throw new FileNotFoundException(table + ".csv references missing or invalid media: " + mediaPath);
                }
            }
            Debug.Log("All Visuals and Audio media paths exist in Student Content/Media.");
        }
    }
}
