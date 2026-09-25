// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using SteamKit2;

namespace DepotDownloader
{
    class DepotManifestEntry
    {
        public uint AppId { get; set; }
        public uint DepotId { get; set; }
        public ulong ManifestId { get; set; } = ulong.MaxValue;
        public byte[] DepotKey { get; set; }
        public string ManifestFile { get; set; }
    }

    static class ManifestFileParser
    {
        static readonly Regex AddAppIdRegex = new(
            @"^\s*addappid\s*\(\s*(?<id>\d+)\s*(?:,\s*\d+\s*(?:,\s*""(?<key>[0-9a-fA-F]{64})""\s*)?)?\s*\)",
            RegexOptions.Compiled);

        static readonly Regex SetManifestIdRegex = new(
            @"^\s*setManifestid\s*\(\s*(?<id>\d+)\s*,\s*""(?<manifest>\d+)""",
            RegexOptions.Compiled);

        static readonly Regex QuotedDepotIdRegex = new(
            @"^\s*""(?<id>\d+)""\s*$",
            RegexOptions.Compiled);

        static readonly Regex DecryptionKeyRegex = new(
            @"""DecryptionKey""\s*""(?<key>[0-9a-fA-F]{64})""",
            RegexOptions.Compiled);

        public static List<DepotManifestEntry> ParseDirectory(string directory)
        {
            var entries = new Dictionary<uint, DepotManifestEntry>();

            if (!Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException(directory);
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                var extension = Path.GetExtension(file);

                if (extension.Equals(".manifest", StringComparison.OrdinalIgnoreCase))
                {
                    ParseManifestFile(file, entries);
                }
                else if (extension.Equals(".lua", StringComparison.OrdinalIgnoreCase))
                {
                    ParseLua(file, entries);
                }
                else if (extension.Equals(".vdf", StringComparison.OrdinalIgnoreCase))
                {
                    ParseVdf(file, entries);
                }
            }

            return [.. entries.Values];
        }

        static DepotManifestEntry GetOrAddEntry(Dictionary<uint, DepotManifestEntry> entries, uint depotId)
        {
            if (!entries.TryGetValue(depotId, out var entry))
            {
                entry = new DepotManifestEntry { DepotId = depotId };
                entries[depotId] = entry;
            }

            return entry;
        }

        static void ParseLua(string file, Dictionary<uint, DepotManifestEntry> entries)
        {
            var appId = 0u;
            if (uint.TryParse(Path.GetFileNameWithoutExtension(file), out var appIdFromFileName))
            {
                appId = appIdFromFileName;
            }

            foreach (var line in File.ReadLines(file))
            {
                var addAppIdMatch = AddAppIdRegex.Match(line);
                if (addAppIdMatch.Success)
                {
                    var id = uint.Parse(addAppIdMatch.Groups["id"].Value);

                    if (!addAppIdMatch.Groups["key"].Success)
                    {
                        appId = id;
                        continue;
                    }

                    var entry = GetOrAddEntry(entries, id);

                    if (appId != 0)
                    {
                        entry.AppId = appId;
                    }

                    entry.DepotKey = Util.DecodeHexString(addAppIdMatch.Groups["key"].Value);
                    continue;
                }

                var setManifestIdMatch = SetManifestIdRegex.Match(line);
                if (setManifestIdMatch.Success)
                {
                    var id = uint.Parse(setManifestIdMatch.Groups["id"].Value);
                    var entry = GetOrAddEntry(entries, id);

                    if (appId != 0)
                    {
                        entry.AppId = appId;
                    }

                    entry.ManifestId = ulong.Parse(setManifestIdMatch.Groups["manifest"].Value);
                }
            }
        }

        static void ParseVdf(string file, Dictionary<uint, DepotManifestEntry> entries)
        {
            var currentDepotId = 0u;

            foreach (var line in File.ReadLines(file))
            {
                var depotIdMatch = QuotedDepotIdRegex.Match(line);
                if (depotIdMatch.Success)
                {
                    currentDepotId = uint.Parse(depotIdMatch.Groups["id"].Value);
                    continue;
                }

                var decryptionKeyMatch = DecryptionKeyRegex.Match(line);
                if (decryptionKeyMatch.Success && currentDepotId != 0)
                {
                    var entry = GetOrAddEntry(entries, currentDepotId);
                    entry.DepotKey = Util.DecodeHexString(decryptionKeyMatch.Groups["key"].Value);
                }
            }
        }

        static void ParseManifestFile(string file, Dictionary<uint, DepotManifestEntry> entries)
        {
            DepotManifest manifest;

            try
            {
                manifest = DepotManifest.LoadFromFile(file);
            }
            catch (Exception)
            {
                return;
            }

            if (manifest == null)
            {
                return;
            }

            var entry = GetOrAddEntry(entries, manifest.DepotID);
            entry.ManifestId = manifest.ManifestGID;
            entry.ManifestFile = file;
        }
    }
}
