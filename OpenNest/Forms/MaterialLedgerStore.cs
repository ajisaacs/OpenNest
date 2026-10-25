using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using OpenNest.Data.MaterialLedger;

namespace OpenNest.Forms
{
    /// <summary>
    /// This PC's MaterialLedger connection, persisted to %APPDATA%\OpenNest\material-ledger.json
    /// with the API key protected by DPAPI for the current Windows user. The ledger is optional:
    /// nothing else in the app waits on it.
    /// </summary>
    public static class MaterialLedgerStore
    {
        // Distinguishes OpenNest's ledger key from other DPAPI blobs of the same user.
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("OpenNest.MaterialLedger.ApiKey");

        private static readonly HttpClient Http = new();

        private static MaterialLedgerSettings settings =
            MaterialLedgerSettings.Load(MaterialLedgerSettings.DefaultPath, Unprotect);

        /// <summary>Current settings, loaded once at process start.</summary>
        public static MaterialLedgerSettings Settings => settings;

        /// <summary>Persists the URL and key; the current settings stay unchanged if writing fails.</summary>
        public static void Save(string baseUrl, string apiKey)
        {
            var next = new MaterialLedgerSettings { BaseUrl = baseUrl ?? "", ApiKey = apiKey ?? "" };
            next.Save(MaterialLedgerSettings.DefaultPath, Protect);
            settings = next;
        }

        /// <summary>A client for <paramref name="connection"/>, or for the saved settings when null.</summary>
        public static MaterialLedgerClient CreateClient(MaterialLedgerSettings connection = null) =>
            new(Http, connection ?? settings);

        internal static string Protect(string plain) =>
            Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy,
                DataProtectionScope.CurrentUser));

        internal static string Unprotect(string protectedText) =>
            Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedText), Entropy,
                DataProtectionScope.CurrentUser));
    }
}
