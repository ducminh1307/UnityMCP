using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace DucMinh.UnityMcp.Editor
{
    [Serializable] public sealed class LocalizationTableListInput { public int maxCollections = 256; }
    [Serializable] public sealed class LocalizationLocaleInfo { public string code; public string name; public string assetPath; }
    [Serializable] public sealed class LocalizationTableCollectionInfo
    {
        public string name;
        public string assetPath;
        public string sharedDataPath;
        public List<LocalizationLocaleInfo> locales = new List<LocalizationLocaleInfo>();
        public int totalEntries;
    }

    [Serializable] public sealed class LocalizationTableListOutput
    {
        public List<LocalizationLocaleInfo> projectLocales = new List<LocalizationLocaleInfo>();
        public List<LocalizationTableCollectionInfo> collections = new List<LocalizationTableCollectionInfo>();
        public bool truncated;
    }

    [Serializable] public sealed class LocalizationEntryGetInput
    {
        public string collection;
        public string key;
        /// <summary>Optional exact locale codes. Empty selects every table in the collection.</summary>
        public List<string> locales = new List<string>();
    }

    [Serializable] public sealed class LocalizationMetadataInfo { public string type; public string summary; }
    [Serializable] public sealed class LocalizationEntryValueInfo
    {
        public string locale;
        public bool exists;
        public string value;
        public List<LocalizationMetadataInfo> metadata = new List<LocalizationMetadataInfo>();
    }

    [Serializable] public sealed class LocalizationEntryGetOutput
    {
        public string collection;
        public string key;
        public string revision;
        public List<LocalizationEntryValueInfo> values = new List<LocalizationEntryValueInfo>();
    }

    [Serializable] public sealed class LocalizationEntrySetValue { public string locale; public string value; }
    [Serializable] public sealed class LocalizationEntrySetInput
    {
        public string collection;
        public string key;
        /// <summary>Revision returned by localization-entry-get for the same key and locales.</summary>
        public string expectedRevision;
        public List<LocalizationEntrySetValue> values = new List<LocalizationEntrySetValue>();
        public bool apply;
    }

    [Serializable] public sealed class LocalizationEntrySetOutput
    {
        public bool dryRun;
        public bool changed;
        public string collection;
        public string key;
        public string revisionBefore;
        public string revisionAfter;
        public bool rollbackSupported;
        public List<ChangeJournalEntry> journal = new List<ChangeJournalEntry>();
    }

    /// <summary>
    /// Optional Unity Localization integration. The code intentionally uses only reflection so
    /// com.unity.localization remains optional; RequiredType keeps these tools out of tools/list
    /// on projects where the package is not installed.
    /// </summary>
    public static class EditorLocalizationTools
    {
        private const int MaxCollections = 512;
        private const int MaxLocales = 128;
        private const int MaxKeyLength = 256;
        private const int MaxValueLength = 65536;
        private const int MaxMetadata = 32;
        private const int MaxMetadataSummaryLength = 1024;
        private static readonly Regex LocaleCode = new Regex("^[A-Za-z0-9]{1,8}(?:-[A-Za-z0-9]{1,8})*$", RegexOptions.Compiled);

        [UnityMcpTool("localization-table-list", Description = "List String Table collections and configured Localization locales.", Category = "project-extensions", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead, RequiredType = "UnityEditor.Localization.LocalizationEditorSettings")]
        public static LocalizationTableListOutput LocalizationTableList(LocalizationTableListInput input)
        {
            var limit = Math.Max(1, Math.Min(input?.maxCollections ?? 256, MaxCollections));
            var output = new LocalizationTableListOutput();
            foreach (var locale in GetProjectLocales()) output.projectLocales.Add(ToLocaleInfo(locale));
            foreach (var collection in GetStringTableCollections().OrderBy(CollectionName, StringComparer.Ordinal))
            {
                if (output.collections.Count >= limit) { output.truncated = true; break; }
                var info = new LocalizationTableCollectionInfo
                {
                    name = CollectionName(collection),
                    assetPath = AssetPath(collection),
                    sharedDataPath = AssetPath(ReadOptionalMember(collection, "SharedData"))
                };
                foreach (var table in Tables(collection))
                {
                    info.locales.Add(new LocalizationLocaleInfo { code = LocaleCodeOf(table), name = LocaleCodeOf(table), assetPath = AssetPath(table) });
                    info.totalEntries = checked(info.totalEntries + Count(ReadOptionalMember(table, "Values")));
                }
                output.collections.Add(info);
            }
            return output;
        }

        [UnityMcpTool("localization-entry-get", Description = "Read localized String Table values and entry metadata for an existing key.", Category = "project-extensions", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead, RequiredType = "UnityEditor.Localization.LocalizationEditorSettings")]
        public static LocalizationEntryGetOutput LocalizationEntryGet(LocalizationEntryGetInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            ValidateCollectionName(input.collection);
            ValidateKey(input.key);
            var collection = RequireCollection(input.collection);
            var tables = ResolveTables(collection, input.locales);
            var values = ReadValues(tables, input.key);
            if (!values.Any(value => value.exists)) throw new ArgumentException("The String Table collection does not contain key '" + input.key + "'.");
            return new LocalizationEntryGetOutput
            {
                collection = CollectionName(collection),
                key = input.key,
                revision = Revision(CollectionName(collection), input.key, values),
                values = values
            };
        }

        [UnityMcpTool("localization-entry-set", Description = "Update existing String Table values across explicit locales with optimistic revision checking; dry-run unless apply is true.", Category = "project-extensions", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true, RequiredType = "UnityEditor.Localization.LocalizationEditorSettings")]
        public static LocalizationEntrySetOutput LocalizationEntrySet(LocalizationEntrySetInput input, UnityMcpContext context)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (context == null) throw new ArgumentNullException(nameof(context));
            ValidateCollectionName(input.collection);
            ValidateKey(input.key);
            if (string.IsNullOrWhiteSpace(input.expectedRevision) || input.expectedRevision.Length != 64 || input.expectedRevision.Any(value => !Uri.IsHexDigit(value)))
                throw new ArgumentException("expectedRevision must be the 64-character revision returned by localization-entry-get.");
            var updates = ValidateUpdates(input.values);
            var collection = RequireCollection(input.collection);
            var tables = ResolveTables(collection, updates.Select(value => value.locale).ToList());
            var before = ReadValues(tables, input.key);
            if (before.Any(value => !value.exists))
                throw new InvalidOperationException("All requested locale tables must already contain the key. Create missing translations in Unity's Localization Tables window before using this bounded update tool.");
            var revisionBefore = Revision(CollectionName(collection), input.key, before);
            if (!string.Equals(revisionBefore, input.expectedRevision, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The requested localization entry is stale. Call localization-entry-get again before applying changes.");

            var planned = before.Select(value => new LocalizationEntryValueInfo
            {
                locale = value.locale,
                exists = true,
                value = updates.Single(update => string.Equals(update.locale, value.locale, StringComparison.Ordinal)).value,
                metadata = value.metadata
            }).ToList();
            var output = new LocalizationEntrySetOutput
            {
                dryRun = context.DryRun,
                changed = !context.DryRun && before.Any(value => !string.Equals(value.value, planned.Single(next => next.locale == value.locale).value, StringComparison.Ordinal)),
                collection = CollectionName(collection),
                key = input.key,
                revisionBefore = revisionBefore,
                revisionAfter = Revision(CollectionName(collection), input.key, planned),
                rollbackSupported = !context.DryRun,
                journal = updates.Select(update => new ChangeJournalEntry
                {
                    operation = "set-localized-string",
                    before = input.collection + "#" + input.key + "@" + update.locale,
                    after = input.collection + "#" + input.key + "@" + update.locale
                }).ToList()
            };
            if (context.DryRun || !output.changed) return output;

            var tableEntries = new List<KeyValuePair<object, object>>();
            for (var index = 0; index < tables.Count; index++) tableEntries.Add(new KeyValuePair<object, object>(tables[index], FindEntry(tables[index], input.key)));
            var sharedData = ReadOptionalMember(collection, "SharedData") as UnityEngine.Object;
            var undoTargets = tableEntries.Select(value => value.Key as UnityEngine.Object).Where(value => value != null).ToList();
            if (sharedData != null) undoTargets.Add(sharedData);
            Undo.RecordObjects(undoTargets.Distinct().ToArray(), "UnityMCP Set Localized String");
            try
            {
                foreach (var pair in tableEntries)
                {
                    var locale = LocaleCodeOf(pair.Key);
                    var update = updates.Single(value => string.Equals(value.locale, locale, StringComparison.Ordinal));
                    SetRequiredStringMember(pair.Value, "Value", update.value);
                    EditorUtility.SetDirty((UnityEngine.Object)pair.Key);
                }
                if (sharedData != null) EditorUtility.SetDirty(sharedData);
                foreach (var table in tables) AssetDatabase.SaveAssetIfDirty((UnityEngine.Object)table);
                if (sharedData != null) AssetDatabase.SaveAssetIfDirty(sharedData);
                return output;
            }
            catch (TargetInvocationException exception)
            {
                throw new InvalidOperationException("Unity Localization rejected the value update: " + (exception.InnerException?.Message ?? exception.Message), exception.InnerException ?? exception);
            }
        }

        private static List<object> GetStringTableCollections()
        {
            var settings = RequireType("UnityEditor.Localization.LocalizationEditorSettings");
            var method = settings.GetMethod("GetStringTableCollections", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            if (method == null) throw new InvalidOperationException("The installed Localization package does not expose LocalizationEditorSettings.GetStringTableCollections().");
            return Enumerate(method.Invoke(null, null));
        }

        private static List<object> GetProjectLocales()
        {
            var settings = RequireType("UnityEditor.Localization.LocalizationEditorSettings");
            var method = settings.GetMethod("GetLocales", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            if (method == null) throw new InvalidOperationException("The installed Localization package does not expose LocalizationEditorSettings.GetLocales().");
            return Enumerate(method.Invoke(null, null));
        }

        private static object RequireCollection(string name)
        {
            var matches = GetStringTableCollections().Where(collection => string.Equals(CollectionName(collection), name, StringComparison.Ordinal)).ToList();
            if (matches.Count == 0) throw new ArgumentException("String Table collection was not found: " + name);
            if (matches.Count != 1) throw new InvalidOperationException("More than one String Table collection has the requested exact name. Rename the duplicate collection before using this tool.");
            return matches[0];
        }

        private static List<object> Tables(object collection)
        {
            var tables = Enumerate(ReadRequiredMember(collection, "StringTables"));
            if (tables.Count == 0) throw new InvalidOperationException("The String Table collection contains no tables.");
            var duplicates = new HashSet<string>(StringComparer.Ordinal);
            foreach (var table in tables)
            {
                var locale = LocaleCodeOf(table);
                if (!duplicates.Add(locale)) throw new InvalidOperationException("The collection contains duplicate LocaleIdentifier '" + locale + "'.");
            }
            return tables;
        }

        private static List<object> ResolveTables(object collection, List<string> requestedLocales)
        {
            var all = Tables(collection);
            var requested = NormalizeLocales(requestedLocales);
            if (requested.Count == 0) return all.OrderBy(LocaleCodeOf, StringComparer.Ordinal).ToList();
            var byLocale = all.ToDictionary(LocaleCodeOf, StringComparer.Ordinal);
            var result = new List<object>();
            foreach (var locale in requested)
            {
                if (!byLocale.TryGetValue(locale, out var table))
                    throw new ArgumentException("The String Table collection does not contain locale '" + locale + "'.");
                result.Add(table);
            }
            return result;
        }

        private static List<string> NormalizeLocales(List<string> values)
        {
            var source = values ?? new List<string>();
            if (source.Count > MaxLocales) throw new ArgumentException("At most " + MaxLocales + " locales may be requested.");
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in source)
            {
                var locale = (raw ?? string.Empty).Trim();
                if (!LocaleCode.IsMatch(locale)) throw new ArgumentException("Locale codes must use BCP-47-like identifiers, such as en or pt-BR.");
                if (!seen.Add(locale)) throw new ArgumentException("Each locale may appear only once.");
                result.Add(locale);
            }
            return result;
        }

        private static List<LocalizationEntrySetValue> ValidateUpdates(List<LocalizationEntrySetValue> values)
        {
            if (values == null || values.Count == 0 || values.Count > MaxLocales)
                throw new ArgumentException("values must contain between 1 and " + MaxLocales + " explicit locale updates.");
            var result = new List<LocalizationEntrySetValue>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var update in values)
            {
                if (update == null) throw new ArgumentException("values may not contain null entries.");
                var locale = (update.locale ?? string.Empty).Trim();
                if (!LocaleCode.IsMatch(locale)) throw new ArgumentException("Every update locale must use a BCP-47-like identifier, such as en or pt-BR.");
                if (!seen.Add(locale)) throw new ArgumentException("Each update locale may appear only once.");
                if (update.value == null || update.value.Length > MaxValueLength || update.value.IndexOf('\0') >= 0)
                    throw new ArgumentException("Localized values must be non-null, contain no NUL characters, and be no longer than " + MaxValueLength + " characters.");
                result.Add(new LocalizationEntrySetValue { locale = locale, value = update.value });
            }
            return result;
        }

        private static List<LocalizationEntryValueInfo> ReadValues(List<object> tables, string key)
        {
            return tables.Select(table =>
            {
                var entry = FindEntry(table, key);
                return new LocalizationEntryValueInfo
                {
                    locale = LocaleCodeOf(table),
                    exists = entry != null,
                    value = entry == null ? null : ReadStringMember(entry, "Value"),
                    metadata = entry == null ? new List<LocalizationMetadataInfo>() : ReadMetadata(entry)
                };
            }).OrderBy(value => value.locale, StringComparer.Ordinal).ToList();
        }

        private static object FindEntry(object table, string key)
        {
            foreach (var entry in Enumerate(ReadOptionalMember(table, "Values")))
                if (string.Equals(ReadStringMember(entry, "Key"), key, StringComparison.Ordinal)) return entry;
            return null;
        }

        private static List<LocalizationMetadataInfo> ReadMetadata(object entry)
        {
            var result = new List<LocalizationMetadataInfo>();
            foreach (var metadata in Enumerate(ReadOptionalMember(entry, "MetadataEntries")))
            {
                if (metadata == null || result.Count >= MaxMetadata) break;
                string summary;
                try { summary = Convert.ToString(metadata, CultureInfo.InvariantCulture) ?? string.Empty; }
                catch { summary = "<metadata summary unavailable>"; }
                if (summary.Length > MaxMetadataSummaryLength) summary = summary.Substring(0, MaxMetadataSummaryLength) + "...";
                result.Add(new LocalizationMetadataInfo { type = metadata.GetType().FullName, summary = summary });
            }
            return result;
        }

        private static LocalizationLocaleInfo ToLocaleInfo(object locale)
        {
            var identifier = ReadOptionalMember(locale, "Identifier");
            var code = IdentifierCode(identifier);
            return new LocalizationLocaleInfo { code = code, name = ReadStringMember(locale, "LocaleName") ?? code, assetPath = AssetPath(locale) };
        }

        private static string LocaleCodeOf(object table) => IdentifierCode(ReadRequiredMember(table, "LocaleIdentifier"));

        private static string IdentifierCode(object identifier)
        {
            var code = ReadStringMember(identifier, "Code");
            if (string.IsNullOrWhiteSpace(code)) code = Convert.ToString(identifier, CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(code)) throw new InvalidOperationException("A Localization table has no readable LocaleIdentifier code.");
            return code;
        }

        private static string CollectionName(object collection)
        {
            var name = ReadStringMember(collection, "TableCollectionName");
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("A String Table collection has no readable TableCollectionName.");
            return name;
        }

        private static string AssetPath(object value) => value is UnityEngine.Object asset ? AssetDatabase.GetAssetPath(asset) : null;

        private static int Count(object values)
        {
            if (values == null) return 0;
            var property = values.GetType().GetProperty("Count", BindingFlags.Public | BindingFlags.Instance);
            return property == null ? Enumerate(values).Count : Convert.ToInt32(property.GetValue(values, null), CultureInfo.InvariantCulture);
        }

        private static string Revision(string collection, string key, List<LocalizationEntryValueInfo> values)
        {
            var builder = new StringBuilder();
            builder.Append(collection.Length).Append(':').Append(collection).Append('|').Append(key.Length).Append(':').Append(key).Append('|');
            foreach (var value in values.OrderBy(item => item.locale, StringComparer.Ordinal))
            {
                builder.Append(value.locale.Length).Append(':').Append(value.locale).Append('|').Append(value.exists ? '1' : '0').Append('|');
                var text = value.value ?? string.Empty;
                builder.Append(text.Length).Append(':').Append(text).Append('|');
            }
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(new UTF8Encoding(false).GetBytes(builder.ToString()))).Replace("-", string.Empty).ToLowerInvariant();
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
            throw new InvalidOperationException("The required Unity Localization type is unavailable: " + fullName);
        }

        private static object ReadRequiredMember(object target, string name)
        {
            var value = ReadOptionalMember(target, name);
            if (value == null) throw new InvalidOperationException(target.GetType().FullName + " does not expose readable member '" + name + "'.");
            return value;
        }

        private static object ReadOptionalMember(object target, string name)
        {
            if (target == null) return null;
            var type = target.GetType();
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property != null && property.CanRead && property.GetIndexParameters().Length == 0) return property.GetValue(target, null);
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            return field == null ? null : field.GetValue(target);
        }

        private static string ReadStringMember(object target, string name)
        {
            var value = ReadOptionalMember(target, name);
            return value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static void SetRequiredStringMember(object target, string name, string value)
        {
            var property = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property == null || !property.CanWrite || property.PropertyType != typeof(string))
                throw new InvalidOperationException(target.GetType().FullName + " does not expose writable string member '" + name + "'.");
            property.SetValue(target, value, null);
        }

        private static List<object> Enumerate(object value)
        {
            var result = new List<object>();
            if (!(value is IEnumerable values)) return result;
            foreach (var item in values) result.Add(item);
            return result;
        }

        private static void ValidateCollectionName(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 256 || value.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
                throw new ArgumentException("collection must be non-empty, at most 256 characters, and contain no control characters.");
        }

        private static void ValidateKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > MaxKeyLength || value.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
                throw new ArgumentException("key must be non-empty, at most " + MaxKeyLength + " characters, and contain no control characters.");
        }
    }
}
