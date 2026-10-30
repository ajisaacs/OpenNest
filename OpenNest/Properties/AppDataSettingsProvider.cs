using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.IO;
using System.Text.Json;

namespace OpenNest.Properties
{
    /// <summary>
    /// Stores the desktop app's user-scoped <see cref="Settings"/> in
    /// %APPDATA%\OpenNest\user-settings.json instead of .NET's default user.config.
    /// </summary>
    /// <remarks>
    /// The default provider writes to %LOCALAPPDATA%\OpenNest\&lt;exe&gt;_Url_&lt;hash&gt;\&lt;version&gt;\,
    /// which is inside the Velopack install folder (setup wipes it on reinstall) and
    /// starts empty at every assembly version. This file survives both. When it does
    /// not exist yet, values the default provider still holds for this copy are
    /// carried over once.
    /// </remarks>
    public sealed class AppDataSettingsProvider : SettingsProvider
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private readonly string _path;
        private readonly Func<SettingsProvider> _legacyProvider;

        public AppDataSettingsProvider()
            : this(DefaultPath, PathOverride == null ? () => new LocalFileSettingsProvider() : null) { }

        /// <param name="path">The settings file.</param>
        /// <param name="legacyProvider">Supplies values to carry over when <paramref name="path"/> does not exist; null for none.</param>
        public AppDataSettingsProvider(string path, Func<SettingsProvider> legacyProvider)
        {
            _path = path;
            _legacyProvider = legacyProvider;
        }

        /// <summary>Replaces <see cref="DefaultPath"/>, so test runs never write the user's settings.</summary>
        internal static string PathOverride { get; set; }

        /// <summary>%APPDATA%\OpenNest\user-settings.json.</summary>
        public static string DefaultPath => PathOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "OpenNest", "user-settings.json");

        public override string ApplicationName { get; set; } = "OpenNest";

        public override void Initialize(string name, NameValueCollection config)
            => base.Initialize(string.IsNullOrEmpty(name) ? nameof(AppDataSettingsProvider) : name, config);

        public override SettingsPropertyValueCollection GetPropertyValues(SettingsContext context, SettingsPropertyCollection collection)
        {
            var stored = File.Exists(_path) ? Load() : Migrate(context, collection);
            var values = new SettingsPropertyValueCollection();

            foreach (SettingsProperty property in collection)
            {
                var value = new SettingsPropertyValue(property);
                if (stored.TryGetValue(property.Name, out var serialized))
                {
                    value.SerializedValue = serialized;
                    value.IsDirty = false;
                }

                values.Add(value);
            }

            return values;
        }

        public override void SetPropertyValues(SettingsContext context, SettingsPropertyValueCollection collection)
        {
            // Keep entries this build does not know about, so older and newer copies share the file.
            var stored = Load();

            foreach (SettingsPropertyValue value in collection)
            {
                // Untouched defaults stay out of the file so a later default change still applies.
                if (!value.IsDirty && (value.UsingDefaultValue || value.SerializedValue is null))
                    continue;

                if (value.SerializedValue is string serialized)
                    stored[value.Property.Name] = serialized;
                else
                    stored.Remove(value.Property.Name);

                value.IsDirty = false;
            }

            Write(stored);
        }

        private Dictionary<string, string> Load()
        {
            try
            {
                return File.Exists(_path)
                    ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_path)) ?? new()
                    : new();
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                return new();
            }
        }

        private Dictionary<string, string> Migrate(SettingsContext context, SettingsPropertyCollection collection)
        {
            var stored = new Dictionary<string, string>();
            if (_legacyProvider is null)
                return stored;

            try
            {
                var legacy = _legacyProvider();
                legacy.Initialize(null, null);
                // user.config reports a missing setting as its default text; skip those.
                foreach (SettingsPropertyValue value in legacy.GetPropertyValues(context, collection))
                {
                    if (value.SerializedValue is string serialized
                        && serialized != value.Property.DefaultValue as string)
                        stored[value.Property.Name] = serialized;
                }
            }
            catch (Exception ex) when (ex is ConfigurationException or IOException or UnauthorizedAccessException)
            {
                // An unreadable user.config only means there is nothing to carry over.
                return new();
            }

            if (stored.Count > 0)
                Write(stored);

            return stored;
        }

        private void Write(Dictionary<string, string> stored)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path)));
                var temp = _path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(stored, JsonOptions));
                File.Move(temp, _path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Settings are a convenience: keep running with the in-memory values.
            }
        }
    }
}
