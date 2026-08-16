using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DucMinh.UnityMcp.Editor
{
    [Serializable] public sealed class TerrainHeightSetInput
    {
        public string terrainDataPath;
        public int x;
        public int y;
        public int width = 1;
        public int height = 1;
        public List<float> values = new List<float>();
        public bool apply;
    }

    [Serializable] public sealed class TerrainTexturePaintInput
    {
        public string terrainDataPath;
        public int layer;
        public int x;
        public int y;
        public int width = 1;
        public int height = 1;
        public List<float> values = new List<float>();
        public bool apply;
    }

    [Serializable] public sealed class SpriteSliceRectInput
    {
        public string name;
        public float x;
        public float y;
        public float width;
        public float height;
        public Vector2 pivot = new Vector2(0.5f, 0.5f);
        public string alignment;
    }

    [Serializable] public sealed class SpriteSliceInput
    {
        public string path;
        public List<SpriteSliceRectInput> sprites = new List<SpriteSliceRectInput>();
        public bool apply;
    }

    [Serializable] public sealed class TilemapCellWrite
    {
        public int x;
        public int y;
        public int z;
        /// <summary>Assets-relative TileBase path. Set null or empty to clear the cell.</summary>
        public string tilePath;
    }

    [Serializable] public sealed class TilemapSetTilesInput
    {
        public int tilemapInstanceId;
        public List<TilemapCellWrite> cells = new List<TilemapCellWrite>();
        public bool apply;
    }

    [Serializable] public sealed class UiToolkitScanInput { public string path; public int limit = 200; }
    [Serializable] public sealed class UiToolkitElementInfo
    {
        public string tag;
        public string name;
        public List<string> classes = new List<string>();
        public int depth;
    }

    [Serializable] public sealed class UiToolkitScanOutput
    {
        public string path;
        public string revision;
        public List<UiToolkitElementInfo> elements = new List<UiToolkitElementInfo>();
        public List<string> styleSheets = new List<string>();
        public bool truncated;
    }

    [Serializable] public sealed class UiToolkitTextEdit
    {
        public int startOffset;
        public int endOffset;
        public string newText;
    }

    [Serializable] public sealed class UiToolkitUxmlEditInput
    {
        public string path;
        public string expectedRevision;
        public List<UiToolkitTextEdit> edits = new List<UiToolkitTextEdit>();
        public bool apply;
    }

    [Serializable] public sealed class UiToolkitUssEditInput
    {
        public string path;
        public string expectedRevision;
        public List<UiToolkitTextEdit> edits = new List<UiToolkitTextEdit>();
        public bool apply;
    }

    [Serializable] public sealed class UiToolkitTextEditOutput
    {
        public bool dryRun;
        public bool changed;
        public string path;
        public string revisionBefore;
        public string revisionAfter;
        public bool rollbackSupported;
        public List<ChangeJournalEntry> journal = new List<ChangeJournalEntry>();
    }

    [Serializable] public sealed class UiToolkitControllerScaffoldInput
    {
        public string path;
        public string className;
        public string namespaceName;
        public string uxmlPath;
        public List<string> elementNames = new List<string>();
        public bool apply;
    }

    [Serializable] public sealed class UiToolkitControllerScaffoldOutput
    {
        public bool dryRun;
        public bool created;
        public string path;
        public string className;
        public string uxmlPath;
        public bool rollbackSupported;
        public List<ChangeJournalEntry> journal = new List<ChangeJournalEntry>();
    }

    /// <summary>
    /// Optional integration tools. No optional package assembly is referenced by this assembly:
    /// attributes use RequiredType and handlers resolve optional types only after the registry has
    /// verified that they are installed. This keeps an absent package out of tools/list entirely.
    /// </summary>
    public static class EditorPackageGatedTools
    {
        private const int MaxTerrainPatchCells = 16384;
        private const int MaxTilemapCells = 1024;
        private const int MaxSpriteRects = 256;
        private const int MaxTextBytes = 1024 * 1024;
        private const int MaxTextEdits = 256;
        private static readonly Regex CSharpIdentifier = new Regex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

        [UnityMcpTool("terrain-height-set", Description = "Apply a bounded normalized TerrainData height patch; dry-run unless apply is true.", Category = "project-extensions", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true, RequiredType = "UnityEngine.TerrainData")]
        public static ChangeOutput TerrainHeightSet(TerrainHeightSetInput input, UnityMcpContext context)
        {
            var terrainType = RequireType("UnityEngine.TerrainData");
            var terrainData = RequireAssetOfType(input.terrainDataPath, terrainType, "TerrainData");
            var resolution = ReadIntProperty(terrainData, "heightmapResolution");
            ValidatePatch(input.x, input.y, input.width, input.height, resolution, resolution, input.values, "height", 0f, 1f);
            var patch = ToHeightPatch(input.width, input.height, input.values);

            if (!context.DryRun)
            {
                Undo.RecordObject((UnityEngine.Object)terrainData, "UnityMCP Set Terrain Heights");
                InvokeExact(terrainData, "SetHeights", new[] { typeof(int), typeof(int), typeof(float[,]) }, input.x, input.y, patch);
                EditorUtility.SetDirty((UnityEngine.Object)terrainData);
            }
            return Change(context, "Set " + input.width + "x" + input.height + " TerrainData height patch at (" + input.x + ", " + input.y + ").", "set-terrain-heights", input.terrainDataPath);
        }

        [UnityMcpTool("terrain-texture-paint", Description = "Apply a bounded normalized TerrainData alphamap layer patch; dry-run unless apply is true.", Category = "project-extensions", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true, RequiredType = "UnityEngine.TerrainData")]
        public static ChangeOutput TerrainTexturePaint(TerrainTexturePaintInput input, UnityMcpContext context)
        {
            var terrainType = RequireType("UnityEngine.TerrainData");
            var terrainData = RequireAssetOfType(input.terrainDataPath, terrainType, "TerrainData");
            var widthLimit = ReadIntProperty(terrainData, "alphamapWidth");
            var heightLimit = ReadIntProperty(terrainData, "alphamapHeight");
            var layerCount = ReadIntProperty(terrainData, "alphamapLayers");
            if (input.layer < 0 || input.layer >= layerCount) throw new ArgumentException("layer must identify an existing TerrainData alphamap layer.");
            ValidatePatch(input.x, input.y, input.width, input.height, widthLimit, heightLimit, input.values, "alphamap weight", 0f, 1f);
            var alphamaps = (float[,,])InvokeExact(terrainData, "GetAlphamaps", new[] { typeof(int), typeof(int), typeof(int), typeof(int) }, input.x, input.y, input.width, input.height);
            ApplyLayerWeights(alphamaps, input.layer, input.values);

            if (!context.DryRun)
            {
                Undo.RecordObject((UnityEngine.Object)terrainData, "UnityMCP Paint Terrain Texture");
                InvokeExact(terrainData, "SetAlphamaps", new[] { typeof(int), typeof(int), typeof(float[,,]) }, input.x, input.y, alphamaps);
                EditorUtility.SetDirty((UnityEngine.Object)terrainData);
            }
            return Change(context, "Paint " + input.width + "x" + input.height + " TerrainData alphamap patch for layer " + input.layer + ".", "paint-terrain-texture", input.terrainDataPath);
        }

        [UnityMcpTool("sprite-slice", Description = "Update validated existing Sprite Editor rectangles on a TextureImporter; dry-run unless apply is true. New rectangles must first be created in Unity's Sprite Editor.", Category = "project-extensions", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true, RequiredType = "UnityEditor.U2D.Sprites.SpriteDataProviderFactories")]
        public static ChangeOutput SpriteSlice(SpriteSliceInput input, UnityMcpContext context)
        {
            var path = NormalizeExistingAssetPath(input.path, ".png");
            var importer = AssetImporter.GetAtPath(path);
            if (importer == null || !string.Equals(importer.GetType().FullName, "UnityEditor.TextureImporter", StringComparison.Ordinal))
                throw new ArgumentException("path must identify a TextureImporter-backed PNG asset.");
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null) throw new ArgumentException("The texture asset could not be loaded.");
            var requested = input.sprites ?? new List<SpriteSliceRectInput>();
            if (requested.Count == 0 || requested.Count > MaxSpriteRects) throw new ArgumentException("sprites must contain between 1 and " + MaxSpriteRects + " items.");
            if (requested.Any(value => value == null || string.IsNullOrWhiteSpace(value.name))) throw new ArgumentException("Every sprite rect must have a name.");
            if (requested.Select(value => value.name).Distinct(StringComparer.Ordinal).Count() != requested.Count) throw new ArgumentException("Sprite rect names must be unique.");

            var provider = GetSpriteDataProvider(importer);
            var existing = ReadSpriteRects(provider);
            if (existing.Count == 0)
                throw new InvalidOperationException("The texture has no existing Sprite Editor rectangles. Create initial slices in Unity's Sprite Editor before using this bounded update tool.");
            var byName = existing.ToDictionary(value => Convert.ToString(ReadMember(value, "name")) ?? string.Empty, StringComparer.Ordinal);
            foreach (var slice in requested)
            {
                ValidateSpriteRect(slice, texture.width, texture.height);
                if (!byName.TryGetValue(slice.name, out var rectangle))
                    throw new ArgumentException("Sprite rect '" + slice.name + "' does not exist. This tool intentionally does not create new sprite IDs.");
                SetMember(rectangle, "rect", new Rect(slice.x, slice.y, slice.width, slice.height));
                SetMember(rectangle, "pivot", slice.pivot);
                if (!string.IsNullOrWhiteSpace(slice.alignment)) SetEnumMember(rectangle, "alignment", slice.alignment);
            }

            if (!context.DryRun)
            {
                Undo.RecordObject(importer, "UnityMCP Update Sprite Slices");
                SetEnumMember(importer, "textureType", "Sprite");
                SetEnumMember(importer, "spriteImportMode", "Multiple");
                WriteSpriteRects(provider, existing);
                InvokeByName(provider, "Apply");
                var saveAndReimport = importer.GetType().GetMethod("SaveAndReimport", BindingFlags.Public | BindingFlags.Instance);
                if (saveAndReimport == null) throw new InvalidOperationException("This Unity TextureImporter does not expose SaveAndReimport.");
                saveAndReimport.Invoke(importer, null);
            }
            return Change(context, "Update " + requested.Count + " existing Sprite Editor rectangle(s) in '" + path + "'.", "set-sprite-rectangles", path);
        }

        [UnityMcpTool("tilemap-set-tiles", Description = "Set or clear a bounded set of Tilemap cells from stable TileBase asset paths; dry-run unless apply is true.", Category = "project-extensions", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true, RequiredType = "UnityEngine.Tilemaps.Tilemap")]
        public static ChangeOutput TilemapSetTiles(TilemapSetTilesInput input, UnityMcpContext context)
        {
            var tilemapType = RequireType("UnityEngine.Tilemaps.Tilemap");
            var tileBaseType = RequireType("UnityEngine.Tilemaps.TileBase");
            var target = EditorUtility.EntityIdToObject((EntityId)input.tilemapInstanceId);
            if (target == null || !tilemapType.IsInstanceOfType(target)) throw new ArgumentException("tilemapInstanceId must identify a loaded Tilemap component.");
            var cells = input.cells ?? new List<TilemapCellWrite>();
            if (cells.Count == 0 || cells.Count > MaxTilemapCells) throw new ArgumentException("cells must contain between 1 and " + MaxTilemapCells + " entries.");
            if (cells.Any(cell => cell == null)) throw new ArgumentException("cells may not contain null entries.");
            var duplicates = new HashSet<string>(StringComparer.Ordinal);
            var resolved = new List<KeyValuePair<Vector3Int, UnityEngine.Object>>();
            foreach (var cell in cells)
            {
                var key = cell.x + ":" + cell.y + ":" + cell.z;
                if (!duplicates.Add(key)) throw new ArgumentException("Each Tilemap coordinate may occur only once.");
                UnityEngine.Object tile = null;
                if (!string.IsNullOrWhiteSpace(cell.tilePath))
                {
                    var tilePath = NormalizeExistingAssetPath(cell.tilePath, null);
                    tile = AssetDatabase.LoadAssetAtPath(tilePath, tileBaseType);
                    if (tile == null) throw new ArgumentException("tilePath must identify a TileBase asset: " + cell.tilePath);
                }
                resolved.Add(new KeyValuePair<Vector3Int, UnityEngine.Object>(new Vector3Int(cell.x, cell.y, cell.z), tile));
            }
            var setTile = tilemapType.GetMethod("SetTile", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(Vector3Int), tileBaseType }, null);
            if (setTile == null) throw new InvalidOperationException("The installed Tilemap API does not expose SetTile(Vector3Int, TileBase).");

            if (!context.DryRun)
            {
                Undo.RecordObject(target, "UnityMCP Set Tilemap Tiles");
                foreach (var item in resolved) setTile.Invoke(target, new object[] { item.Key, item.Value });
                EditorUtility.SetDirty(target);
                if (target is Component component && component.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
            }
            return Change(context, "Set or clear " + resolved.Count + " Tilemap cell(s).", "set-tilemap-cells", input.tilemapInstanceId.ToString(CultureInfo.InvariantCulture));
        }

        [UnityMcpTool("uitoolkit-scan", Description = "Inspect a bounded UI Toolkit UXML document tree and its declared style sheet sources.", Category = "ui", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead, RequiredType = "UnityEngine.UIElements.VisualTreeAsset")]
        public static UiToolkitScanOutput UiToolkitScan(UiToolkitScanInput input)
        {
            var path = RequireUiToolkitAsset(input.path, ".uxml", "UnityEngine.UIElements.VisualTreeAsset");
            var source = ReadUtf8Source(ToFullPath(path));
            var output = ScanUxml(source.text, Math.Max(1, Math.Min(1000, input.limit)));
            output.path = path;
            output.revision = Revision(source.bytes);
            return output;
        }

        [UnityMcpTool("uitoolkit-uxml-edit", Description = "Apply revision-checked bounded text edits to a valid UXML asset; dry-run unless apply is true.", Category = "ui", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true, RequiredType = "UnityEngine.UIElements.VisualTreeAsset")]
        public static UiToolkitTextEditOutput UiToolkitUxmlEdit(UiToolkitUxmlEditInput input, UnityMcpContext context)
        {
            var path = RequireUiToolkitAsset(input.path, ".uxml", "UnityEngine.UIElements.VisualTreeAsset");
            return ApplyUiToolkitTextEdits(path, input.expectedRevision, input.edits, context, true);
        }

        [UnityMcpTool("uitoolkit-uss-edit", Description = "Apply revision-checked bounded text edits to a syntactically balanced USS asset; dry-run unless apply is true.", Category = "ui", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true, RequiredType = "UnityEngine.UIElements.StyleSheet")]
        public static UiToolkitTextEditOutput UiToolkitUssEdit(UiToolkitUssEditInput input, UnityMcpContext context)
        {
            var path = RequireUiToolkitAsset(input.path, ".uss", "UnityEngine.UIElements.StyleSheet");
            return ApplyUiToolkitTextEdits(path, input.expectedRevision, input.edits, context, false);
        }

        [UnityMcpTool("uitoolkit-controller-scaffold", Description = "Scaffold a bounded UI Toolkit MonoBehaviour controller with named VisualElement bindings; dry-run unless apply is true.", Category = "ui", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true, RequiredType = "UnityEngine.UIElements.VisualTreeAsset")]
        public static UiToolkitControllerScaffoldOutput UiToolkitControllerScaffold(UiToolkitControllerScaffoldInput input, UnityMcpContext context)
        {
            var uxmlPath = RequireUiToolkitAsset(input.uxmlPath, ".uxml", "UnityEngine.UIElements.VisualTreeAsset");
            if (string.IsNullOrWhiteSpace(input.className) || !CSharpIdentifier.IsMatch(input.className)) throw new ArgumentException("className must be a valid C# identifier.");
            if (!string.IsNullOrWhiteSpace(input.namespaceName)) ValidateNamespace(input.namespaceName);
            var bindings = input.elementNames ?? new List<string>();
            if (bindings.Count > 64 || bindings.Any(value => string.IsNullOrWhiteSpace(value) || !CSharpIdentifier.IsMatch(value)))
                throw new ArgumentException("elementNames may contain at most 64 non-empty C# identifiers.");
            if (bindings.Distinct(StringComparer.Ordinal).Count() != bindings.Count) throw new ArgumentException("elementNames must be unique.");
            var path = NormalizeCreatableAssetPath(input.path, ".cs");
            var fullPath = ToFullPath(path);
            if (File.Exists(fullPath) || AssetDatabase.LoadMainAssetAtPath(path) != null) throw new InvalidOperationException("The controller path already exists.");
            var parent = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent)) throw new ArgumentException("The controller target folder must already exist under Assets.");
            EnsureNoReparsePoint(AssetsRoot, parent);
            var contents = BuildControllerSource(input.className, input.namespaceName, bindings);

            if (!context.DryRun)
            {
                File.WriteAllBytes(fullPath, EncodeUtf8(contents, false));
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }
            return new UiToolkitControllerScaffoldOutput
            {
                dryRun = context.DryRun,
                created = !context.DryRun,
                path = path,
                className = input.className,
                uxmlPath = uxmlPath,
                rollbackSupported = false,
                journal = new List<ChangeJournalEntry> { new ChangeJournalEntry { operation = "create-uitoolkit-controller", after = path } }
            };
        }

        private static void ValidatePatch(int x, int y, int width, int height, int maxWidth, int maxHeight, List<float> values, string valueName, float minimum, float maximum)
        {
            if (x < 0 || y < 0 || width <= 0 || height <= 0 || x + width > maxWidth || y + height > maxHeight)
                throw new ArgumentException("The requested patch is outside the target terrain data dimensions.");
            var count = checked(width * height);
            if (count > MaxTerrainPatchCells) throw new ArgumentException("The requested patch exceeds the " + MaxTerrainPatchCells + " cell limit.");
            if (values == null || values.Count != count) throw new ArgumentException("values must contain exactly width * height entries in row-major order.");
            foreach (var value in values)
                if (float.IsNaN(value) || float.IsInfinity(value) || value < minimum || value > maximum)
                    throw new ArgumentException(valueName + " values must be finite and between " + minimum.ToString(CultureInfo.InvariantCulture) + " and " + maximum.ToString(CultureInfo.InvariantCulture) + ".");
        }

        private static float[,] ToHeightPatch(int width, int height, List<float> values)
        {
            var result = new float[height, width];
            for (var row = 0; row < height; row++)
                for (var column = 0; column < width; column++) result[row, column] = values[row * width + column];
            return result;
        }

        private static void ApplyLayerWeights(float[,,] alphamaps, int targetLayer, List<float> values)
        {
            var height = alphamaps.GetLength(0);
            var width = alphamaps.GetLength(1);
            var layers = alphamaps.GetLength(2);
            for (var row = 0; row < height; row++)
            {
                for (var column = 0; column < width; column++)
                {
                    var target = values[row * width + column];
                    if (layers == 1)
                    {
                        if (Math.Abs(target - 1f) > 0.0001f) throw new ArgumentException("A TerrainData with one alphamap layer requires all values to be 1.");
                        alphamaps[row, column, 0] = 1f;
                        continue;
                    }
                    var otherTotal = 0f;
                    for (var layer = 0; layer < layers; layer++) if (layer != targetLayer) otherTotal += alphamaps[row, column, layer];
                    alphamaps[row, column, targetLayer] = target;
                    var remaining = 1f - target;
                    if (otherTotal > 0.000001f)
                    {
                        for (var layer = 0; layer < layers; layer++) if (layer != targetLayer) alphamaps[row, column, layer] = alphamaps[row, column, layer] / otherTotal * remaining;
                    }
                    else
                    {
                        var firstOtherLayer = targetLayer == 0 ? 1 : 0;
                        for (var layer = 0; layer < layers; layer++) if (layer != targetLayer) alphamaps[row, column, layer] = layer == firstOtherLayer ? remaining : 0f;
                    }
                }
            }
        }

        private static object GetSpriteDataProvider(AssetImporter importer)
        {
            var factoryType = RequireType("UnityEditor.U2D.Sprites.SpriteDataProviderFactories");
            var factory = Activator.CreateInstance(factoryType);
            InvokeByName(factory, "Init");
            var provider = InvokeByName(factory, "GetSpriteEditorDataProviderFromObject", importer);
            if (provider == null) throw new InvalidOperationException("Unity could not create a Sprite Editor data provider for this importer.");
            InvokeByName(provider, "InitSpriteEditorDataProvider");
            return provider;
        }

        private static List<object> ReadSpriteRects(object provider)
        {
            var result = InvokeByName(provider, "GetSpriteRects") as IEnumerable;
            if (result == null) throw new InvalidOperationException("The Sprite Editor provider did not return sprite rectangles.");
            var values = new List<object>();
            foreach (var value in result) if (value != null) values.Add(value);
            return values;
        }

        private static void WriteSpriteRects(object provider, List<object> values)
        {
            if (values.Count == 0) throw new ArgumentException("At least one Sprite Editor rectangle is required.");
            var elementType = values[0].GetType();
            if (values.Any(value => value.GetType() != elementType)) throw new InvalidOperationException("The Sprite Editor provider returned inconsistent rectangle types.");
            var array = Array.CreateInstance(elementType, values.Count);
            for (var index = 0; index < values.Count; index++) array.SetValue(values[index], index);
            InvokeByName(provider, "SetSpriteRects", array);
        }

        private static void ValidateSpriteRect(SpriteSliceRectInput value, int textureWidth, int textureHeight)
        {
            if (value.x < 0f || value.y < 0f || value.width <= 0f || value.height <= 0f || value.x + value.width > textureWidth || value.y + value.height > textureHeight)
                throw new ArgumentException("Every sprite rect must stay within the texture bounds and have positive width and height.");
            if (float.IsNaN(value.x) || float.IsNaN(value.y) || float.IsNaN(value.width) || float.IsNaN(value.height) || float.IsInfinity(value.x) || float.IsInfinity(value.y) || float.IsInfinity(value.width) || float.IsInfinity(value.height))
                throw new ArgumentException("Sprite rect values must be finite.");
            if (value.pivot.x < 0f || value.pivot.x > 1f || value.pivot.y < 0f || value.pivot.y > 1f) throw new ArgumentException("Sprite pivots must be normalized between zero and one.");
        }

        private static UiToolkitTextEditOutput ApplyUiToolkitTextEdits(string path, string expectedRevision, List<UiToolkitTextEdit> edits, UnityMcpContext context, bool isUxml)
        {
            var source = ReadUtf8Source(ToFullPath(path));
            var before = Revision(source.bytes);
            if (string.IsNullOrWhiteSpace(expectedRevision) || !string.Equals(expectedRevision, before, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("expectedRevision does not match the current asset contents.");
            var after = ApplyTextEdits(source.text, edits);
            if (isUxml) ValidateUxml(after); else ValidateUss(after);
            var afterBytes = EncodeUtf8(after, source.hasBom);
            var changed = !source.bytes.SequenceEqual(afterBytes);
            if (!context.DryRun && changed)
            {
                var fullPath = ToFullPath(path);
                EnsureNoReparsePoint(AssetsRoot, fullPath);
                File.WriteAllBytes(fullPath, afterBytes);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }
            return new UiToolkitTextEditOutput
            {
                dryRun = context.DryRun,
                changed = changed && !context.DryRun,
                path = path,
                revisionBefore = before,
                revisionAfter = Revision(afterBytes),
                rollbackSupported = false,
                journal = new List<ChangeJournalEntry> { new ChangeJournalEntry { operation = isUxml ? "edit-uxml" : "edit-uss", before = path, after = path } }
            };
        }

        private static UiToolkitScanOutput ScanUxml(string text, int limit)
        {
            var output = new UiToolkitScanOutput();
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, IgnoreComments = true, IgnoreWhitespace = true, XmlResolver = null };
            try
            {
                using (var reader = XmlReader.Create(new StringReader(text), settings))
                {
                    while (reader.Read())
                    {
                        if (reader.NodeType != XmlNodeType.Element) continue;
                        if (string.Equals(reader.LocalName, "Style", StringComparison.OrdinalIgnoreCase))
                        {
                            var source = reader.GetAttribute("src");
                            if (!string.IsNullOrWhiteSpace(source) && !output.styleSheets.Contains(source)) output.styleSheets.Add(source);
                        }
                        if (output.elements.Count >= limit) { output.truncated = true; continue; }
                        var info = new UiToolkitElementInfo
                        {
                            tag = reader.LocalName,
                            name = reader.GetAttribute("name"),
                            depth = reader.Depth
                        };
                        var classes = reader.GetAttribute("class");
                        if (!string.IsNullOrWhiteSpace(classes)) info.classes.AddRange(classes.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
                        output.elements.Add(info);
                    }
                }
            }
            catch (XmlException exception)
            {
                throw new ArgumentException("The UXML source is not valid XML: " + exception.Message, exception);
            }
            return output;
        }

        private static void ValidateUxml(string text)
        {
            var scan = ScanUxml(text, 1);
            if (scan.elements.Count == 0) throw new ArgumentException("UXML must contain a document root element.");
        }

        private static void ValidateUss(string text)
        {
            if (text.IndexOf('\0') >= 0) throw new ArgumentException("USS source may not contain NUL characters.");
            var braces = 0;
            var inComment = false;
            var quote = '\0';
            for (var index = 0; index < text.Length; index++)
            {
                var current = text[index];
                var next = index + 1 < text.Length ? text[index + 1] : '\0';
                if (inComment)
                {
                    if (current == '*' && next == '/') { inComment = false; index++; }
                    continue;
                }
                if (quote != '\0')
                {
                    if (current == '\\') { index++; continue; }
                    if (current == quote) quote = '\0';
                    continue;
                }
                if (current == '/' && next == '*') { inComment = true; index++; continue; }
                if (current == '\'' || current == '"') { quote = current; continue; }
                if (current == '{') braces++;
                else if (current == '}' && --braces < 0) throw new ArgumentException("USS contains an unmatched closing brace.");
            }
            if (inComment) throw new ArgumentException("USS contains an unterminated comment.");
            if (quote != '\0') throw new ArgumentException("USS contains an unterminated string.");
            if (braces != 0) throw new ArgumentException("USS contains unbalanced braces.");
        }

        private static string ApplyTextEdits(string source, List<UiToolkitTextEdit> edits)
        {
            var values = edits ?? new List<UiToolkitTextEdit>();
            if (values.Count == 0 || values.Count > MaxTextEdits) throw new ArgumentException("edits must contain between 1 and " + MaxTextEdits + " entries.");
            UiToolkitTextEdit previous = null;
            var replacementCharacters = 0;
            foreach (var edit in values.OrderBy(value => value == null ? int.MinValue : value.startOffset).ThenBy(value => value == null ? int.MinValue : value.endOffset))
            {
                if (edit == null || edit.startOffset < 0 || edit.endOffset < edit.startOffset || edit.endOffset > source.Length || edit.newText == null)
                    throw new ArgumentException("Text edit offsets and replacement text are invalid.");
                replacementCharacters = checked(replacementCharacters + edit.newText.Length);
                if (replacementCharacters > MaxTextBytes) throw new ArgumentException("Combined replacement text exceeds the 1 MiB limit.");
                if (previous != null && (edit.startOffset < previous.endOffset || (edit.startOffset == previous.startOffset && edit.endOffset == previous.endOffset && edit.startOffset == edit.endOffset)))
                    throw new ArgumentException("Text edits may not overlap or insert at the same offset.");
                previous = edit;
            }
            var builder = new StringBuilder(source);
            foreach (var edit in values.OrderByDescending(value => value.startOffset).ThenByDescending(value => value.endOffset))
                builder.Remove(edit.startOffset, edit.endOffset - edit.startOffset).Insert(edit.startOffset, edit.newText);
            var result = builder.ToString();
            if (new UTF8Encoding(false).GetByteCount(result) > MaxTextBytes) throw new ArgumentException("The edited asset exceeds the 1 MiB limit.");
            return result;
        }

        private static string BuildControllerSource(string className, string namespaceName, List<string> bindings)
        {
            var builder = new StringBuilder();
            builder.AppendLine("using UnityEngine;");
            builder.AppendLine("using UnityEngine.UIElements;");
            builder.AppendLine();
            if (!string.IsNullOrWhiteSpace(namespaceName))
            {
                builder.Append("namespace ").Append(namespaceName).AppendLine();
                builder.AppendLine("{");
            }
            var indentation = string.IsNullOrWhiteSpace(namespaceName) ? string.Empty : "    ";
            builder.Append(indentation).AppendLine("[DisallowMultipleComponent]");
            builder.Append(indentation).Append("public sealed class ").Append(className).AppendLine(" : MonoBehaviour");
            builder.Append(indentation).AppendLine("{");
            builder.Append(indentation).AppendLine("    [SerializeField] private UIDocument document;");
            builder.Append(indentation).AppendLine("    private VisualElement root;");
            foreach (var binding in bindings) builder.Append(indentation).Append("    private VisualElement ").Append(binding).AppendLine(";");
            builder.AppendLine();
            builder.Append(indentation).AppendLine("    private void OnEnable()");
            builder.Append(indentation).AppendLine("    {");
            builder.Append(indentation).AppendLine("        if (document == null)");
            builder.Append(indentation).AppendLine("        {");
            builder.Append(indentation).AppendLine("            Debug.LogWarning(\"Assign a UIDocument before enabling this controller.\", this);");
            builder.Append(indentation).AppendLine("            return;");
            builder.Append(indentation).AppendLine("        }");
            builder.Append(indentation).AppendLine("        root = document.rootVisualElement;");
            foreach (var binding in bindings) builder.Append(indentation).Append("        ").Append(binding).Append(" = root.Q<VisualElement>(\"").Append(EscapeCSharpString(binding)).AppendLine("\");");
            builder.Append(indentation).AppendLine("    }");
            builder.Append(indentation).AppendLine("}");
            if (!string.IsNullOrWhiteSpace(namespaceName)) builder.AppendLine("}");
            return builder.ToString();
        }

        private static string EscapeCSharpString(string value) => (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");

        private static void ValidateNamespace(string namespaceName)
        {
            var segments = namespaceName.Split('.');
            if (segments.Length == 0 || segments.Any(segment => !CSharpIdentifier.IsMatch(segment))) throw new ArgumentException("namespaceName must contain dot-separated C# identifiers.");
        }

        private static string RequireUiToolkitAsset(string path, string extension, string typeName)
        {
            var assetPath = NormalizeExistingAssetPath(path, extension);
            var type = RequireType(typeName);
            if (AssetDatabase.LoadAssetAtPath(assetPath, type) == null) throw new ArgumentException("The requested asset is not a valid " + typeName + ": " + assetPath);
            return assetPath;
        }

        private static object RequireAssetOfType(string path, Type type, string displayName)
        {
            var assetPath = NormalizeExistingAssetPath(path, null);
            var asset = AssetDatabase.LoadAssetAtPath(assetPath, type);
            if (asset == null) throw new ArgumentException("path must identify a " + displayName + " asset.");
            return asset;
        }

        private static Type RequireType(string fullName)
        {
            var type = Type.GetType(fullName, false);
            if (type != null) return type;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    type = assembly.GetType(fullName, false);
                    if (type != null) return type;
                }
                catch { }
            }
            throw new InvalidOperationException("The required optional Unity type is not available: " + fullName);
        }

        private static int ReadIntProperty(object target, string name) => Convert.ToInt32(ReadMember(target, name), CultureInfo.InvariantCulture);

        private static object ReadMember(object target, string name)
        {
            var type = target.GetType();
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property != null && property.CanRead) return property.GetValue(target, null);
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (field != null) return field.GetValue(target);
            throw new InvalidOperationException(type.FullName + " does not expose readable member '" + name + "'.");
        }

        private static void SetMember(object target, string name, object value)
        {
            var type = target.GetType();
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property != null && property.CanWrite) { property.SetValue(target, value, null); return; }
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (field != null) { field.SetValue(target, value); return; }
            throw new InvalidOperationException(type.FullName + " does not expose writable member '" + name + "'.");
        }

        private static void SetEnumMember(object target, string name, string value)
        {
            var type = target.GetType();
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            var memberType = property?.PropertyType ?? type.GetField(name, BindingFlags.Public | BindingFlags.Instance)?.FieldType;
            if (memberType == null || !memberType.IsEnum) throw new InvalidOperationException(type.FullName + " does not expose enum member '" + name + "'.");
            object parsed;
            try { parsed = Enum.Parse(memberType, value, true); }
            catch (ArgumentException exception) { throw new ArgumentException("'" + value + "' is not a valid " + name + " value.", exception); }
            SetMember(target, name, parsed);
        }

        private static object InvokeExact(object target, string name, Type[] parameterTypes, params object[] arguments)
        {
            var method = target.GetType().GetMethod(name, BindingFlags.Public | BindingFlags.Instance, null, parameterTypes, null);
            if (method == null) throw new InvalidOperationException(target.GetType().FullName + " does not expose the expected " + name + " overload.");
            return method.Invoke(target, arguments);
        }

        private static object InvokeByName(object target, string name, params object[] arguments)
        {
            var candidates = target.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(method => string.Equals(method.Name, name, StringComparison.Ordinal) && method.GetParameters().Length == arguments.Length)
                .ToList();
            foreach (var method in candidates)
            {
                var parameters = method.GetParameters();
                var compatible = true;
                for (var index = 0; index < parameters.Length; index++)
                {
                    if (arguments[index] != null && !parameters[index].ParameterType.IsInstanceOfType(arguments[index])) { compatible = false; break; }
                }
                if (compatible) return method.Invoke(target, arguments);
            }
            throw new InvalidOperationException(target.GetType().FullName + " does not expose a compatible " + name + " method.");
        }

        private static ChangeOutput Change(UnityMcpContext context, string summary, string operation, string target)
        {
            return new ChangeOutput
            {
                dryRun = context.DryRun,
                changed = !context.DryRun,
                summary = summary,
                rollbackSupported = false,
                journal = new List<ChangeJournalEntry> { new ChangeJournalEntry { operation = operation, after = target } }
            };
        }

        private sealed class TextSource { public byte[] bytes; public string text; public bool hasBom; }

        private static TextSource ReadUtf8Source(string fullPath)
        {
            EnsureNoReparsePoint(AssetsRoot, fullPath);
            var bytes = File.ReadAllBytes(fullPath);
            if (bytes.Length > MaxTextBytes) throw new ArgumentException("Asset exceeds the 1 MiB tool limit.");
            var offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            if (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF))) throw new ArgumentException("Only UTF-8 assets are supported.");
            string text;
            try { text = new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset); }
            catch (DecoderFallbackException exception) { throw new ArgumentException("Asset is not valid UTF-8.", exception); }
            return new TextSource { bytes = bytes, text = text, hasBom = offset != 0 };
        }

        private static byte[] EncodeUtf8(string text, bool includeBom)
        {
            var body = new UTF8Encoding(false, true).GetBytes(text ?? string.Empty);
            if (!includeBom) return body;
            var preamble = Encoding.UTF8.GetPreamble();
            var result = new byte[preamble.Length + body.Length];
            Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
            Buffer.BlockCopy(body, 0, result, preamble.Length, body.Length);
            return result;
        }

        private static string Revision(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static string NormalizeExistingAssetPath(string path, string extension)
        {
            var result = NormalizeAssetPath(path, extension);
            var fullPath = ToFullPath(result);
            if (!File.Exists(fullPath)) throw new ArgumentException("Asset file does not exist: " + result);
            EnsureNoReparsePoint(AssetsRoot, fullPath);
            return result;
        }

        private static string NormalizeCreatableAssetPath(string path, string extension)
        {
            var result = NormalizeAssetPath(path, extension);
            var fullPath = ToFullPath(result);
            if (!IsContained(AssetsRoot, fullPath)) throw new ArgumentException("Asset path must remain within Assets.");
            return result;
        }

        private static string NormalizeAssetPath(string path, string extension)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A project-relative path under Assets is required.");
            var normalized = path.Trim().Replace('\\', '/');
            if (Path.IsPathRooted(normalized) || normalized.Contains(":")) throw new ArgumentException("Path must be project-relative under Assets/.");
            var parts = normalized.Split('/');
            if (parts.Length < 2 || !string.Equals(parts[0], "Assets", StringComparison.Ordinal) || parts.Any(part => string.IsNullOrEmpty(part) || part == "." || part == ".."))
                throw new ArgumentException("Path must be normalized under Assets/ and may not include traversal segments.");
            if (normalized.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Unity .meta files are not valid tool targets.");
            if (extension != null && !normalized.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Path must end with " + extension + ".");
            return normalized;
        }

        private static string AssetsRoot => Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        private static string ProjectRoot => Directory.GetParent(AssetsRoot).FullName;

        private static string ToFullPath(string assetPath)
        {
            var fullPath = Path.GetFullPath(Path.Combine(ProjectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsContained(AssetsRoot, fullPath)) throw new ArgumentException("Path escapes the project Assets directory.");
            return fullPath;
        }

        private static bool IsContained(string root, string fullPath)
        {
            var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalizedPath = Path.GetFullPath(fullPath);
            return string.Equals(normalizedRoot, normalizedPath, StringComparison.OrdinalIgnoreCase) || normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static void EnsureNoReparsePoint(string root, string fullPath)
        {
            if (!IsContained(root, fullPath)) throw new ArgumentException("Path must remain within the allowed root.");
            var current = Path.GetFullPath(root);
            if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("The Assets root may not be a symbolic link or junction.");
            var relative = fullPath.Substring(current.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            foreach (var part in relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, part);
                if (!File.Exists(current) && !Directory.Exists(current)) break;
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("Symbolic links and junctions are not permitted in tool paths.");
            }
        }
    }
}
