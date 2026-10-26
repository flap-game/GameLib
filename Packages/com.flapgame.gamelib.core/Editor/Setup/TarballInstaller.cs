using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEditor.PackageManager;

namespace FlapGame.GameLib.Editor
{
    /// <summary>
    /// Installs an SDK Google ships only as a .tgz (Firebase): downloads it into <c>Packages/GoogleTgz/</c> and references it
    /// as <c>file:GoogleTgz/&lt;file&gt;.tgz</c>. Commit that folder - a CI build has no other way to get the SDK.
    /// </summary>
    public static class TarballInstaller
    {
        public const string Folder = "GoogleTgz";

        public static string FileName(SdkRequirement sdk) => Path.GetFileName(new Uri(sdk.Location).AbsolutePath);

        /// <summary>The manifest value, relative to the Packages folder.</summary>
        public static string ManifestValue(SdkRequirement sdk) => $"file:{Folder}/{FileName(sdk)}";

        /// <param name="registries">Scoped registries the tarball's dependencies resolve from (EDM on OpenUPM).</param>
        public static async Task InstallAsync(SdkRequirement sdk, IEnumerable<SdkRequirement> registries)
        {
            await DownloadAsync(sdk);

            var manifest = new ManifestManager();
            foreach (SdkRequirement registry in registries)
                manifest.EnsureScopedRegistry(registry.RegistryName, registry.RegistryUrl, registry.RegistryScopes);
            manifest.SetDependency(sdk.PackageName, ManifestValue(sdk));
            manifest.Save();
            Client.Resolve();
        }

        /// <summary>Downloads the tarball into <c>Packages/GoogleTgz/</c> unless it is there already. Does not touch the
        /// manifest. Continues off the main thread, so it can be waited on synchronously (batch mode).</summary>
        public static async Task DownloadAsync(SdkRequirement sdk)
        {
            string directory = Path.Combine(Path.GetDirectoryName(ManifestManager.ManifestPath)!, Folder);
            Directory.CreateDirectory(directory);
            string destination = Path.Combine(directory, FileName(sdk));

            if (!File.Exists(destination))
            {
                string temporary = destination + ".download";
                try
                {
                    using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
                    using HttpResponseMessage response = await http.GetAsync(sdk.Location, HttpCompletionOption.ResponseHeadersRead)
                                                                   .ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    using (FileStream file = File.Create(temporary))
                        await response.Content.CopyToAsync(file).ConfigureAwait(false);
                    File.Move(temporary, destination);
                }
                finally
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
            }
        }
    }
}
