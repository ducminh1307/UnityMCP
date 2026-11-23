#pragma warning disable UAC0005 // Editor tool reflection scans currently loaded optional package types.
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
using Newtonsoft.Json.Linq;
using Unity.Pipeline.Commands;
#if DM_TERRAIN_PRESENT || DM_2D_SPRITE_PRESENT || DM_TILEMAP_PRESENT
using Unity.Pipeline.Editor.Authoring;
#endif
#if DM_TILEMAP_PRESENT
using Unity.Pipeline.Models;
using UnityEditor.SceneManagement;
#endif
using UnityEditor;
using UnityEngine;

namespace DucMinh.UnityMcp.Editor
{
#if DM_2D_SPRITE_PRESENT
    [Serializable]
    public sealed class SpriteSliceRectInput
    {
        public string name;
        public float x;
        public float y;
        public float width;
        public float height;
        public Vector2 pivot = new Vector2(0.5f, 0.5f);
        public string alignment;
    }
#endif

#if DM_TILEMAP_PRESENT
    [Serializable]
    public sealed class TilemapCellWrite
    {
        public int x;
        public int y;
        public int z;
        /// <summary>Assets-relative TileBase path. Set null or empty to clear the cell.</summary>
        public string tilePath;
    }
#endif

    [Serializable]
    public sealed class UiToolkitElementInfo
    {
        public string tag;
        public string name;
        public List<string> classes = new List<string>();
        public int depth;
    }

    [Serializable]
    public sealed class UiToolkitScanOutput
    {
        public string path;
        public string revision;
        public List<UiToolkitElementInfo> elements = new List<UiToolkitElementInfo>();
        public List<string> styleSheets = new List<string>();
        public bool truncated;
    }

    [Serializable]
    public sealed class UiToolkitTextEdit
    {
        public int startOffset;
        public int endOffset;
        public string newText;
    }

    [Serializable]
    public sealed class UiToolkitTextEditOutput
    {
        public bool dryRun;
        public bool changed;
        public string path;
        public string revisionBefore;
        public string revisionAfter;
        public bool rollbackSupported;
        public List<ChangeJournalEntry> journal = new List<ChangeJournalEntry>();
    }

    [Serializable]
    public sealed class UiToolkitControllerScaffoldOutput
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
    /// Package-gated optional integration commands (Terrain, 2D Sprite, Tilemap) using reflection
    /// inside versionDefine guards, plus always-available UI Toolkit asset commands.
    /// </summary>
    public static class EditorPackageGatedTools
    {
        private const string UiToolkitGroup = "uitoolkit";
        private const int MaxTextBytes = 1024 * 1024;
        private const int MaxTextEdits = 256;
        private static readonly Regex CSharpIdentifier = new Regex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

#if DM_TERRAIN_PRESENT
        private const string TerrainGroup = "terrain";
        private const int MaxTerrainPatchCells = 16384;

        [CliGroupCommand(
            TerrainGroup,
            "Apply a bounded normalized TerrainData height patch.",
            Name = "set_height",
            Effect = CommandEffect.Idempotent,
            Tags = new[] { "terrain" })]
        internal static ExitCode TerrainHeightSet(
            CommandContext cmd,
            [CliArg("terrain-data-path", "Project-relative path to an existing TerrainData asset under Assets/.", Required = true)] string terrainDataPath,
            [CliArg("x", "Starting X sample coordinate.")] int x = 0,
            [CliArg("y", "Starting Y sample coordinate.")] int y = 0,
            [CliArg("width", "Patch width in samples.")] int width = 1,
            [CliArg("height", "Patch height in samples.")] int height = 1,
            [CliArg("values-json", "Row-major normalized heights [0..1] as a JSON array string.")] string valuesJson = null,
            [CliArg("values", "Row-major normalized heights [0..1] as a JSON array or comma-separated floats.")] string values = null,
            [CliArg("dry-run", "If true, validate bounds and heights without modifying TerrainData.")] bool dryRun = false)
        {
            return Execute(cmd, () => TerrainHeightSetCore(
                terrainDataPath, x, y, width, height, ParseFloatValues(valuesJson, values), dryRun));
        }

        [CliGroupCommand(
            TerrainGroup,
            "Apply a bounded normalized TerrainData alphamap layer patch.",
            Name = "paint_texture",
            Effect = CommandEffect.Idempotent,
            Tags = new[] { "terrain" })]
        internal static ExitCode TerrainTexturePaint(
            CommandContext cmd,
            [CliArg("terrain-data-path", "Project-relative path to an existing TerrainData asset under Assets/.", Required = true)] string terrainDataPath,
            [CliArg("layer", "Alphamap layer index to paint.")] int layer = 0,
            [CliArg("x", "Starting X alphamap coordinate.")] int x = 0,
            [CliArg("y", "Starting Y alphamap coordinate.")] int y = 0,
            [CliArg("width", "Patch width in alphamap samples.")] int width = 1,
            [CliArg("height", "Patch height in alphamap samples.")] int height = 1,
            [CliArg("values-json", "Row-major normalized layer weights [0..1] as a JSON array string.")] string valuesJson = null,
            [CliArg("values", "Row-major normalized layer weights [0..1] as a JSON array or comma-separated floats.")] string values = null,
            [CliArg("dry-run", "If true, validate bounds and weights without modifying TerrainData.")] bool dryRun = false)
        {
            return Execute(cmd, () => TerrainTexturePaintCore(
                terrainDataPath, layer, x, y, width, height, ParseFloatValues(valuesJson, values), dryRun));
        }

        internal static EditorChangeOutput TerrainHeightSetCore(
            string terrainDataPath,
            int x,
            int y,
            int width,
            int height,
            List<float> values,
            bool dryRun = false)
        {
            var terrainType = RequireType("UnityEngine.TerrainData");
            var terrainData = RequireAssetOfType(terrainDataPath, terrainType, "TerrainData", out var normalizedPath);
            var resolution = ReadIntProperty(terrainData, "heightmapResolution");
            ValidatePatch(x, y, width, height, resolution, resolution, values, "height", 0f, 1f);
            var patch = ToHeightPatch(width, height, values);

            if (!dryRun)
            {
                using (new AuthoringUndoScope("UnityMCP Set Terrain Heights"))
                {
                    Undo.RecordObject((UnityEngine.Object)terrainData, "UnityMCP Set Terrain Heights");
                    InvokeExact(terrainData, "SetHeights", new[] { typeof(int), typeof(int), typeof(float[,]) }, x, y, patch);
                    EditorUtility.SetDirty((UnityEngine.Object)terrainData);
                }
            }

            return Change(
                dryRun,
                "Set " + width + "x" + height + " TerrainData height patch at (" + x + ", " + y + ").",
                "set-terrain-heights",
                normalizedPath,
                normalizedPath);
        }

        internal static EditorChangeOutput TerrainTexturePaintCore(
            string terrainDataPath,
            int layer,
            int x,
            int y,
            int width,
            int height,
            List<float> values,
            bool dryRun = false)
        {
            var terrainType = RequireType("UnityEngine.TerrainData");
            var terrainData = RequireAssetOfType(terrainDataPath, terrainType, "TerrainData", out var normalizedPath);
            var widthLimit = ReadIntProperty(terrainData, "alphamapWidth");
            var heightLimit = ReadIntProperty(terrainData, "alphamapHeight");
            var layerCount = ReadIntProperty(terrainData, "alphamapLayers");
            if (layer < 0 || layer >= layerCount)
                throw new ArgumentException("layer must identify an existing TerrainData alphamap layer.");

            ValidatePatch(x, y, width, height, widthLimit, heightLimit, values, "alphamap weight", 0f, 1f);
            var alphamaps = (float[,,])InvokeExact(terrainData, "GetAlphamaps", new[] { typeof(int), typeof(int), typeof(int), typeof(int) }, x, y, width, height);
            ApplyLayerWeights(alphamaps, layer, values);

            if (!dryRun)
            {
                using (new AuthoringUndoScope("UnityMCP Paint Terrain Texture"))
                {
                    Undo.RecordObject((UnityEngine.Object)terrainData, "UnityMCP Paint Terrain Texture");
                    InvokeExact(terrainData, "SetAlphamaps", new[] { typeof(int), typeof(int), typeof(float[,,]) }, x, y, alphamaps);
                    EditorUtility.SetDirty((UnityEngine.Object)terrainData);
                }
            }

            return Change(
                dryRun,
                "Paint " + width + "x" + height + " TerrainData alphamap patch for layer " + layer + ".",
                "paint-terrain-texture",
                normalizedPath,
                normalizedPath);
        }

        internal static List<float> ParseFloatValues(string valuesJson, string values)
        {
            var hasJson = !string.IsNullOrWhiteSpace(valuesJson);
            var hasValues = !string.IsNullOrWhiteSpace(values);
            if (!hasJson && !hasValues)
                throw new ArgumentException("Either --values-json or --values is required.");
            if (hasJson && hasValues)
                throw new ArgumentException("Specify either --values-json or --values, not both.");

            var raw = (hasJson ? valuesJson : values).Trim();
            if (raw.StartsWith("[", StringComparison.Ordinal))
            {
                JArray array;
                try { array = JArray.Parse(raw); }
                catch (Exception ex) { throw new ArgumentException("values must be a valid JSON float array: " + ex.Message, ex); }

                var parsed = new List<float>(array.Count);
                foreach (var item in array)
                {
                    if (item.Type != JTokenType.Integer && item.Type != JTokenType.Float)
                        throw new ArgumentException("values JSON array entries must be numbers.");
                    parsed.Add(item.Value<float>());
                }

                return parsed;
            }

            var parts = raw.Split(',');
            var result = new List<float>(parts.Length);
            foreach (var part in parts)
            {
                var token = part.Trim();
                if (!float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                    throw new ArgumentException("values contains invalid float '" + token + "'.");
                result.Add(number);
            }

            return result;
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

        private static object RequireAssetOfType(string path, Type type, string displayName, out string normalizedPath)
        {
            normalizedPath = NormalizeExistingAssetPath(path, null);
            var asset = AssetDatabase.LoadAssetAtPath(normalizedPath, type);
            if (asset == null) throw new ArgumentException("terrain-data-path must identify a " + displayName + " asset.");
            return asset;
        }

        private static int ReadIntProperty(object target, string name) => Convert.ToInt32(ReadMember(target, name), CultureInfo.InvariantCulture);

        private static object InvokeExact(object target, string name, Type[] parameterTypes, params object[] arguments)
        {
            var method = target.GetType().GetMethod(name, BindingFlags.Public | BindingFlags.Instance, null, parameterTypes, null);
            if (method == null) throw new InvalidOperationException(target.GetType().FullName + " does not expose the expected " + name + " overload.");
            return method.Invoke(target, arguments);
        }
#endif

#if DM_2D_SPRITE_PRESENT
        private const string SpriteGroup = "sprite";
        private const int MaxSpriteRects = 256;

        [CliGroupCommand(
            SpriteGroup,
            "Update validated existing Sprite Editor rectangles on a TextureImporter. New rectangles must first be created in Unity's Sprite Editor.",
            Name = "slice",
            Effect = CommandEffect.Idempotent,
            Tags = new[] { "sprite", "2d" })]
        internal static ExitCode SpriteSlice(
            CommandContext cmd,
            [CliArg("path", "Project-relative path to an existing .png TextureImporter asset under Assets/.", Required = true)] string path,
            [CliArg("sprites-json", "JSON array of {name, x, y, width, height, pivot, alignment} sprite rect updates.", Required = true)] string spritesJson,
            [CliArg("dry-run", "If true, validate sprite rects without modifying the importer.")] bool dryRun = false)
        {
            return Execute(cmd, () => SpriteSliceCore(path, ParseSpriteRectsJson(spritesJson), dryRun));
        }

        internal static EditorChangeOutput SpriteSliceCore(
            string path,
            List<SpriteSliceRectInput> sprites,
            bool dryRun = false)
        {
            var normalizedPath = NormalizeExistingAssetPath(path, ".png");
            var importer = AssetImporter.GetAtPath(normalizedPath);
            if (importer == null || !string.Equals(importer.GetType().FullName, "UnityEditor.TextureImporter", StringComparison.Ordinal))
                throw new ArgumentException("path must identify a TextureImporter-backed PNG asset.");

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(normalizedPath);
            if (texture == null) throw new ArgumentException("The texture asset could not be loaded.");

            var requested = sprites ?? new List<SpriteSliceRectInput>();
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

            if (!dryRun)
            {
                using (new AuthoringUndoScope("UnityMCP Update Sprite Slices"))
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
            }

            return Change(
                dryRun,
                "Update " + requested.Count + " existing Sprite Editor rectangle(s) in '" + normalizedPath + "'.",
                "set-sprite-rectangles",
                normalizedPath,
                normalizedPath);
        }

        internal static List<SpriteSliceRectInput> ParseSpriteRectsJson(string spritesJson)
        {
            if (string.IsNullOrWhiteSpace(spritesJson))
                throw new ArgumentException("sprites-json is required.");

            JArray array;
            try
            {
                if (!(JToken.Parse(spritesJson) is JArray parsed))
                    throw new ArgumentException("sprites-json must be a JSON array of sprite rectangles.");
                array = parsed;
            }
            catch (ArgumentException) { throw; }
            catch (Exception ex)
            {
                throw new ArgumentException("sprites-json is not valid JSON: " + ex.Message, ex);
            }

            var result = new List<SpriteSliceRectInput>(array.Count);
            foreach (var item in array)
            {
                if (!(item is JObject obj))
                    throw new ArgumentException("sprites-json entries must be JSON objects.");

                result.Add(new SpriteSliceRectInput
                {
                    name = ReadJsonString(obj, "name"),
                    x = ReadJsonRequiredFloat(obj, "x"),
                    y = ReadJsonRequiredFloat(obj, "y"),
                    width = ReadJsonRequiredFloat(obj, "width"),
                    height = ReadJsonRequiredFloat(obj, "height"),
                    pivot = ReadJsonPivot(obj),
                    alignment = ReadJsonString(obj, "alignment")
                });
            }

            return result;
        }

        private static float ReadJsonRequiredFloat(JObject obj, string key)
        {
            var token = obj[key];
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float))
                throw new ArgumentException("sprites-json entry '" + key + "' must be a number.");
            return token.Value<float>();
        }

        private static Vector2 ReadJsonPivot(JObject obj)
        {
            var token = obj["pivot"];
            if (token == null || token.Type == JTokenType.Null)
                return new Vector2(0.5f, 0.5f);

            if (token is JArray array)
            {
                if (array.Count != 2 || (array[0].Type != JTokenType.Integer && array[0].Type != JTokenType.Float) || (array[1].Type != JTokenType.Integer && array[1].Type != JTokenType.Float))
                    throw new ArgumentException("pivot array must contain two numbers [x, y].");
                return new Vector2(array[0].Value<float>(), array[1].Value<float>());
            }

            if (token is JObject pivotObj)
            {
                var xToken = pivotObj["x"];
                var yToken = pivotObj["y"];
                if (xToken == null || yToken == null || (xToken.Type != JTokenType.Integer && xToken.Type != JTokenType.Float) || (yToken.Type != JTokenType.Integer && yToken.Type != JTokenType.Float))
                    throw new ArgumentException("pivot object must contain numeric x and y.");
                return new Vector2(xToken.Value<float>(), yToken.Value<float>());
            }

            throw new ArgumentException("pivot must be {x, y} or [x, y].");
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
            if (value.pivot.x < 0f || value.pivot.x > 1f || value.pivot.y < 0f || value.pivot.y > 1f)
                throw new ArgumentException("Sprite pivots must be normalized between zero and one.");
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
#endif

#if DM_TILEMAP_PRESENT
        private const string TilemapGroup = "tilemap";
        private const int MaxTilemapCells = 1024;

        [CliGroupCommand(
            TilemapGroup,
            "Set or clear a bounded set of Tilemap cells from stable TileBase asset paths.",
            Name = "set_tiles",
            Effect = CommandEffect.Idempotent,
            Tags = new[] { "tilemap", "2d" })]
        internal static ExitCode TilemapSetTiles(
            CommandContext cmd,
            [CliArg("tilemap-id", "Handle or instanceId of a loaded Tilemap component.", Required = true)] ObjectRef tilemapId,
            [CliArg("cells-json", "JSON array of {x, y, z, tilePath} cell writes (omit/empty tilePath to clear).", Required = true)] string cellsJson,
            [CliArg("dry-run", "If true, validate TileBase assets and coordinates without modifying the Tilemap.")] bool dryRun = false)
        {
            return Execute(cmd, () => TilemapSetTilesCore(tilemapId, ParseTilemapCellsJson(cellsJson), dryRun));
        }

        internal static EditorChangeOutput TilemapSetTilesCore(
            ObjectRef tilemapId,
            List<TilemapCellWrite> cells,
            bool dryRun = false)
        {
            var tilemapType = RequireType("UnityEngine.Tilemaps.Tilemap");
            var tileBaseType = RequireType("UnityEngine.Tilemaps.TileBase");
            var resolvedObj = ResolveObject(tilemapId, "tilemap-id");
            var target = tilemapType.IsInstanceOfType(resolvedObj)
                ? resolvedObj
                : (resolvedObj as GameObject)?.GetComponent(tilemapType);

            if (target == null || !tilemapType.IsInstanceOfType(target))
                throw new ArgumentException("tilemap-id must identify a loaded Tilemap component.");

            var cellList = cells ?? new List<TilemapCellWrite>();
            if (cellList.Count == 0 || cellList.Count > MaxTilemapCells)
                throw new ArgumentException("cells must contain between 1 and " + MaxTilemapCells + " entries.");
            if (cellList.Any(cell => cell == null))
                throw new ArgumentException("cells may not contain null entries.");

            var duplicates = new HashSet<string>(StringComparer.Ordinal);
            var resolved = new List<KeyValuePair<Vector3Int, UnityEngine.Object>>();
            foreach (var cell in cellList)
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

            if (!dryRun)
            {
                using (new AuthoringUndoScope("UnityMCP Set Tilemap Tiles"))
                {
                    Undo.RecordObject(target, "UnityMCP Set Tilemap Tiles");
                    foreach (var item in resolved) setTile.Invoke(target, new object[] { item.Key, item.Value });
                    EditorUtility.SetDirty(target);
                    if (target is Component component && component.gameObject.scene.IsValid())
                        EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
                }
            }

            var output = Change(
                dryRun,
                "Set or clear " + resolved.Count + " Tilemap cell(s).",
                "set-tilemap-cells",
                UnityMcpObjectId.Get(target).ToString(CultureInfo.InvariantCulture));
            output.instanceId = UnityMcpObjectId.Get(target);
            return output;
        }

        internal static List<TilemapCellWrite> ParseTilemapCellsJson(string cellsJson)
        {
            if (string.IsNullOrWhiteSpace(cellsJson))
                throw new ArgumentException("cells-json is required.");

            JArray array;
            try
            {
                if (!(JToken.Parse(cellsJson) is JArray parsed))
                    throw new ArgumentException("cells-json must be a JSON array of {x, y, z, tilePath} objects.");
                array = parsed;
            }
            catch (ArgumentException) { throw; }
            catch (Exception ex)
            {
                throw new ArgumentException("cells-json is not valid JSON: " + ex.Message, ex);
            }

            var result = new List<TilemapCellWrite>(array.Count);
            foreach (var item in array)
            {
                if (!(item is JObject obj))
                    throw new ArgumentException("cells-json entries must be JSON objects.");
                result.Add(new TilemapCellWrite
                {
                    x = ReadJsonRequiredInt(obj, "x"),
                    y = ReadJsonRequiredInt(obj, "y"),
                    z = ReadJsonOptionalInt(obj, "z"),
                    tilePath = ReadJsonString(obj, "tilePath")
                });
            }

            return result;
        }

        private static int ReadJsonOptionalInt(JObject obj, string key)
        {
            var token = obj[key];
            if (token == null || token.Type == JTokenType.Null) return 0;
            if (token.Type != JTokenType.Integer)
                throw new ArgumentException("cells-json entry '" + key + "' must be an integer.");
            return token.Value<int>();
        }

        private static UnityEngine.Object ResolveObject(ObjectRef handle, string paramName)
        {
            if (handle == null || handle.IsEmpty)
                throw new ArgumentException(paramName + " is required.");

            if (ObjectResolver.TryResolve(handle, out var obj, out var error) && obj != null)
                return obj;

            var text = handle.ToString();
            if (!string.IsNullOrEmpty(text) && text.StartsWith("instanceId:", StringComparison.Ordinal)
                && int.TryParse(text.Substring("instanceId:".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var legacyId))
            {
                var legacy = UnityMcpEditorObjectId.Resolve(legacyId);
                if (legacy != null) return legacy;
            }

            throw new ArgumentException(paramName + " could not be resolved: " + (error ?? "not found."));
        }
#endif

        [CliGroupCommand(
            UiToolkitGroup,
            "Inspect a bounded UI Toolkit UXML document tree and its declared style sheet sources.",
            Name = "scan",
            Effect = CommandEffect.ReadOnly,
            Tags = new[] { "ui", "uitoolkit" })]
        internal static ExitCode UiToolkitScan(
            CommandContext cmd,
            [CliArg("path", "Project-relative path to an existing .uxml asset under Assets/.", Required = true)] string path,
            [CliArg("limit", "Maximum number of elements to return (1-1000).")] int limit = 200)
        {
            return Execute(cmd, () => ScanCore(path, limit));
        }

        [CliGroupCommand(
            UiToolkitGroup,
            "Apply revision-checked bounded text edits to a valid UXML asset.",
            Name = "edit_uxml",
            Effect = CommandEffect.Idempotent,
            Tags = new[] { "ui", "uitoolkit" })]
        internal static ExitCode UiToolkitUxmlEdit(
            CommandContext cmd,
            [CliArg("path", "Project-relative path to an existing .uxml asset under Assets/.", Required = true)] string path,
            [CliArg("expected-revision", "SHA-256 revision returned by uitoolkit scan.", Required = true)] string expectedRevision,
            [CliArg("edits-json", "JSON array of {startOffset, endOffset, newText} text edits.", Required = true)] string editsJson,
            [CliArg("dry-run", "If true, validate revision, edits, and XML structure without writing the file.")] bool dryRun = false)
        {
            return Execute(cmd, () => EditUxmlCore(path, expectedRevision, ParseTextEditsJson(editsJson), dryRun));
        }

        [CliGroupCommand(
            UiToolkitGroup,
            "Apply revision-checked bounded text edits to a syntactically balanced USS asset.",
            Name = "edit_uss",
            Effect = CommandEffect.Idempotent,
            Tags = new[] { "ui", "uitoolkit" })]
        internal static ExitCode UiToolkitUssEdit(
            CommandContext cmd,
            [CliArg("path", "Project-relative path to an existing .uss asset under Assets/.", Required = true)] string path,
            [CliArg("expected-revision", "SHA-256 revision of the current USS asset contents.", Required = true)] string expectedRevision,
            [CliArg("edits-json", "JSON array of {startOffset, endOffset, newText} text edits.", Required = true)] string editsJson,
            [CliArg("dry-run", "If true, validate revision, edits, and USS brace balance without writing the file.")] bool dryRun = false)
        {
            return Execute(cmd, () => EditUssCore(path, expectedRevision, ParseTextEditsJson(editsJson), dryRun));
        }

        [CliGroupCommand(
            UiToolkitGroup,
            "Scaffold a bounded UI Toolkit MonoBehaviour controller with named VisualElement bindings.",
            Name = "scaffold_controller",
            Effect = CommandEffect.Additive,
            Tags = new[] { "ui", "uitoolkit" })]
        internal static ExitCode UiToolkitControllerScaffold(
            CommandContext cmd,
            [CliArg("path", "Project-relative .cs path under Assets/ for the new controller script.", Required = true)] string path,
            [CliArg("class-name", "C# class name for the MonoBehaviour controller.", Required = true)] string className,
            [CliArg("uxml-path", "Project-relative path to the target .uxml asset under Assets/.", Required = true)] string uxmlPath,
            [CliArg("namespace-name", "Optional C# namespace for the generated script.")] string namespaceName = null,
            [CliArg("element-names", "Optional VisualElement names to bind (JSON array or comma-separated C# identifiers).")] string elementNames = null,
            [CliArg("dry-run", "If true, validate inputs and preview without creating the script.")] bool dryRun = false)
        {
            return Execute(cmd, () => ScaffoldControllerCore(
                path, className, uxmlPath, namespaceName, ParseElementNames(elementNames), dryRun));
        }

        internal static UiToolkitScanOutput ScanCore(string path, int limit = 200)
        {
            var normalizedPath = RequireUiToolkitAsset(path, ".uxml", "UnityEngine.UIElements.VisualTreeAsset");
            var source = ReadUtf8Source(ToFullPath(normalizedPath));
            var output = ScanUxml(source.text, Math.Max(1, Math.Min(1000, limit)));
            output.path = normalizedPath;
            output.revision = Revision(source.bytes);
            return output;
        }

        internal static UiToolkitTextEditOutput EditUxmlCore(
            string path,
            string expectedRevision,
            List<UiToolkitTextEdit> edits,
            bool dryRun = false)
        {
            var normalizedPath = RequireUiToolkitAsset(path, ".uxml", "UnityEngine.UIElements.VisualTreeAsset");
            return ApplyUiToolkitTextEdits(normalizedPath, expectedRevision, edits, dryRun, true);
        }

        internal static UiToolkitTextEditOutput EditUssCore(
            string path,
            string expectedRevision,
            List<UiToolkitTextEdit> edits,
            bool dryRun = false)
        {
            var normalizedPath = RequireUiToolkitAsset(path, ".uss", "UnityEngine.UIElements.StyleSheet");
            return ApplyUiToolkitTextEdits(normalizedPath, expectedRevision, edits, dryRun, false);
        }

        internal static UiToolkitControllerScaffoldOutput ScaffoldControllerCore(
            string path,
            string className,
            string uxmlPath,
            string namespaceName = null,
            List<string> elementNames = null,
            bool dryRun = false)
        {
            var normalizedUxmlPath = RequireUiToolkitAsset(uxmlPath, ".uxml", "UnityEngine.UIElements.VisualTreeAsset");
            if (string.IsNullOrWhiteSpace(className) || !CSharpIdentifier.IsMatch(className))
                throw new ArgumentException("class-name must be a valid C# identifier.");
            if (!string.IsNullOrWhiteSpace(namespaceName))
                ValidateNamespace(namespaceName);

            var bindings = elementNames ?? new List<string>();
            if (bindings.Count > 64 || bindings.Any(value => string.IsNullOrWhiteSpace(value) || !CSharpIdentifier.IsMatch(value)))
                throw new ArgumentException("element-names may contain at most 64 non-empty C# identifiers.");
            if (bindings.Distinct(StringComparer.Ordinal).Count() != bindings.Count)
                throw new ArgumentException("element-names must be unique.");

            var normalizedPath = NormalizeCreatableAssetPath(path, ".cs");
            var fullPath = ToFullPath(normalizedPath);
            if (File.Exists(fullPath) || AssetDatabase.LoadMainAssetAtPath(normalizedPath) != null)
                throw new InvalidOperationException("The controller path already exists.");

            var parent = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
                throw new ArgumentException("The controller target folder must already exist under Assets.");

            EnsureNoReparsePoint(AssetsRoot, parent);
            var contents = BuildControllerSource(className, namespaceName, bindings);

            if (!dryRun)
            {
                File.WriteAllBytes(fullPath, EncodeUtf8(contents, false));
                AssetDatabase.ImportAsset(normalizedPath, ImportAssetOptions.ForceSynchronousImport);
            }

            return new UiToolkitControllerScaffoldOutput
            {
                dryRun = dryRun,
                created = !dryRun,
                path = normalizedPath,
                className = className,
                uxmlPath = normalizedUxmlPath,
                rollbackSupported = false,
                journal = new List<ChangeJournalEntry> { new ChangeJournalEntry { operation = "create-uitoolkit-controller", after = normalizedPath } }
            };
        }

        internal static List<UiToolkitTextEdit> ParseTextEditsJson(string editsJson)
        {
            if (string.IsNullOrWhiteSpace(editsJson))
                throw new ArgumentException("edits-json is required.");

            JArray array;
            try
            {
                if (!(JToken.Parse(editsJson) is JArray parsed))
                    throw new ArgumentException("edits-json must be a JSON array of {startOffset, endOffset, newText} objects.");
                array = parsed;
            }
            catch (ArgumentException) { throw; }
            catch (Exception ex)
            {
                throw new ArgumentException("edits-json is not valid JSON: " + ex.Message, ex);
            }

            var result = new List<UiToolkitTextEdit>(array.Count);
            foreach (var item in array)
            {
                if (!(item is JObject obj))
                    throw new ArgumentException("edits-json entries must be JSON objects.");

                var startOffset = ReadJsonRequiredInt(obj, "startOffset");
                var endOffset = ReadJsonRequiredInt(obj, "endOffset");
                var newTextToken = obj["newText"];
                if (newTextToken == null || newTextToken.Type != JTokenType.String)
                    throw new ArgumentException("edits-json entry 'newText' must be a string.");

                result.Add(new UiToolkitTextEdit
                {
                    startOffset = startOffset,
                    endOffset = endOffset,
                    newText = newTextToken.Value<string>()
                });
            }

            return result;
        }

        internal static List<string> ParseElementNames(string elementNames)
        {
            if (string.IsNullOrWhiteSpace(elementNames))
                return new List<string>();

            var trimmed = elementNames.Trim();
            if (trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                JArray array;
                try { array = JArray.Parse(trimmed); }
                catch (Exception ex) { throw new ArgumentException("element-names must be a comma-separated list or valid JSON string array: " + ex.Message, ex); }

                var parsed = new List<string>(array.Count);
                foreach (var item in array)
                {
                    if (item.Type != JTokenType.String)
                        throw new ArgumentException("element-names JSON array entries must be strings.");
                    parsed.Add(item.Value<string>());
                }

                return parsed;
            }

            return trimmed.Split(',').Select(part => part.Trim()).ToList();
        }

        private static ExitCode Execute(CommandContext cmd, Func<object> action)
        {
            try
            {
                cmd.Result.Write(action());
                return ExitCode.Success;
            }
            catch (TargetInvocationException ex)
            {
                var inner = ex.InnerException ?? ex;
                cmd.Log.Error(inner.Message);
                return inner is ArgumentException ? ExitCode.InvalidUsage : ExitCode.Failed;
            }
            catch (ArgumentException ex)
            {
                cmd.Log.Error(ex.Message);
                return ExitCode.InvalidUsage;
            }
            catch (Exception ex)
            {
                cmd.Log.Error(ex.Message);
                return ExitCode.Failed;
            }
        }

        private static UiToolkitTextEditOutput ApplyUiToolkitTextEdits(
            string path,
            string expectedRevision,
            List<UiToolkitTextEdit> edits,
            bool dryRun,
            bool isUxml)
        {
            var source = ReadUtf8Source(ToFullPath(path));
            var before = Revision(source.bytes);
            if (string.IsNullOrWhiteSpace(expectedRevision) || !string.Equals(expectedRevision, before, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("expected-revision does not match the current asset contents.");

            var after = ApplyTextEdits(source.text, edits);
            if (isUxml) ValidateUxml(after); else ValidateUss(after);

            var afterBytes = EncodeUtf8(after, source.hasBom);
            var changed = !source.bytes.SequenceEqual(afterBytes);
            if (!dryRun && changed)
            {
                var fullPath = ToFullPath(path);
                EnsureNoReparsePoint(AssetsRoot, fullPath);
                File.WriteAllBytes(fullPath, afterBytes);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }

            return new UiToolkitTextEditOutput
            {
                dryRun = dryRun,
                changed = changed && !dryRun,
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
            if (segments.Length == 0 || segments.Any(segment => !CSharpIdentifier.IsMatch(segment)))
                throw new ArgumentException("namespace-name must contain dot-separated C# identifiers.");
        }

        private static string RequireUiToolkitAsset(string path, string extension, string typeName)
        {
            var assetPath = NormalizeExistingAssetPath(path, extension);
            var type = RequireType(typeName);
            if (AssetDatabase.LoadAssetAtPath(assetPath, type) == null)
                throw new ArgumentException("The requested asset is not a valid " + typeName + ": " + assetPath);
            return assetPath;
        }

        private static Type RequireType(string fullName)
        {
            return EditorCommandHelpers.TryResolveOptionalType(fullName)
                ?? throw new InvalidOperationException("The required optional Unity type is not available: " + fullName);
        }

#if DM_TERRAIN_PRESENT || DM_2D_SPRITE_PRESENT
        private static object ReadMember(object target, string name)
        {
            var type = target.GetType();
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property != null && property.CanRead) return property.GetValue(target, null);
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (field != null) return field.GetValue(target);
            throw new InvalidOperationException(type.FullName + " does not expose readable member '" + name + "'.");
        }
#endif

#if DM_2D_SPRITE_PRESENT || DM_TILEMAP_PRESENT
        private static string ReadJsonString(JObject obj, string key)
        {
            var token = obj[key];
            return token == null || token.Type == JTokenType.Null ? null : token.Value<string>();
        }
#endif

        private static int ReadJsonRequiredInt(JObject obj, string key)
        {
            var token = obj[key];
            if (token == null || token.Type != JTokenType.Integer)
                throw new ArgumentException("JSON entry '" + key + "' must be an integer.");
            return token.Value<int>();
        }

#if DM_TERRAIN_PRESENT || DM_2D_SPRITE_PRESENT || DM_TILEMAP_PRESENT
        private static EditorChangeOutput Change(bool dryRun, string summary, string operation, string target, string path = null)
        {
            return new EditorChangeOutput
            {
                dryRun = dryRun,
                changed = !dryRun,
                summary = summary,
                path = path,
                rollbackSupported = false,
                journal = new List<ChangeJournalEntry> { new ChangeJournalEntry { operation = operation, after = target } }
            };
        }
#endif

        private sealed class TextSource { public byte[] bytes; public string text; public bool hasBom; }

        private static TextSource ReadUtf8Source(string fullPath)
        {
            EnsureNoReparsePoint(AssetsRoot, fullPath);
            var bytes = File.ReadAllBytes(fullPath);
            if (bytes.Length > MaxTextBytes) throw new ArgumentException("Asset exceeds the 1 MiB tool limit.");
            var offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            if (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF)))
                throw new ArgumentException("Only UTF-8 assets are supported.");
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
            return string.Equals(normalizedRoot, normalizedPath, StringComparison.OrdinalIgnoreCase)
                || normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static void EnsureNoReparsePoint(string root, string fullPath)
        {
            if (!IsContained(root, fullPath)) throw new ArgumentException("Path must remain within the allowed root.");
            var current = Path.GetFullPath(root);
            if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("The Assets root may not be a symbolic link or junction.");
            var relative = fullPath.Substring(current.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            foreach (var part in relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, part);
                if (!File.Exists(current) && !Directory.Exists(current)) break;
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new ArgumentException("Symbolic links and junctions are not permitted in tool paths.");
            }
        }
    }
}
