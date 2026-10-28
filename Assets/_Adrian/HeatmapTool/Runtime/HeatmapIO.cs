using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

namespace HeatmapTool
{
    public static class HeatmapIO
    {
        public const string BinaryExtension = ".heatmap";
        public const string JsonExtension = ".json";
        public const string CsvExtension = ".csv";

        internal const uint Magic = 0x50414D48;
        internal const int FormatVersion = 1;
        internal const byte SamplesBlock = 1;
        internal const byte EventsBlock = 2;
        internal const byte EventNameBlock = 3;

        const string JsonFormatId = "heatmap";
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        static readonly char[] CsvSpecialChars = { ',', '"', '\n', '\r' };

        public const string FolderName = "Heatmaps";

        public static string DefaultFolder => Path.Combine(Application.persistentDataPath, FolderName);

        public static string SaveFolder
        {
            get
            {
                HeatmapSettings settings = HeatmapSettings.Instance;
                if (!Application.isEditor && settings != null && settings.saveBuildsNextToExecutable
                    && TryGetExecutableFolder(out string folder))
                    return Path.Combine(folder, FolderName);
                return DefaultFolder;
            }
        }

        public static bool TryGetExecutableFolder(out string folder)
        {
            folder = null;
            switch (Application.platform)
            {
                case RuntimePlatform.WindowsPlayer:
                case RuntimePlatform.WindowsServer:
                case RuntimePlatform.LinuxPlayer:
                case RuntimePlatform.LinuxServer:
                    folder = Path.GetDirectoryName(Application.dataPath);
                    break;
                case RuntimePlatform.OSXPlayer:
                case RuntimePlatform.OSXServer:
                    folder = Path.GetDirectoryName(Path.GetDirectoryName(Application.dataPath));
                    break;
            }
            return !string.IsNullOrEmpty(folder);
        }

        public static bool IsSupportedFile(string path)
        {
            string extension = Path.GetExtension(path);
            return extension.Equals(BinaryExtension, StringComparison.OrdinalIgnoreCase)
                || extension.Equals(JsonExtension, StringComparison.OrdinalIgnoreCase);
        }

        public static List<string> FindFiles(string folder, bool includeSubfolders = true)
        {
            var files = new List<string>();
            if (!Directory.Exists(folder)) return files;

            var option = includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            foreach (string file in Directory.EnumerateFiles(folder, "*", option))
                if (IsSupportedFile(file)) files.Add(Path.GetFullPath(file));
            files.Sort(StringComparer.OrdinalIgnoreCase);
            return files;
        }

        public static List<HeatmapSession> Load(string path)
        {
            if (Path.GetExtension(path).Equals(JsonExtension, StringComparison.OrdinalIgnoreCase))
                return LoadJson(path);
            return new List<HeatmapSession> { LoadBinary(path) };
        }

        #region Binary

        public static HeatmapSession LoadBinary(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16);
            using var reader = new BinaryReader(stream, Utf8);

            if (stream.Length < 8 || reader.ReadUInt32() != Magic)
                throw new InvalidDataException($"{Path.GetFileName(path)} is not a heatmap file.");
            int version = reader.ReadInt32();
            if (version > FormatVersion)
                throw new InvalidDataException(
                    $"{Path.GetFileName(path)} was written by a newer version of the heatmap tool (format {version}).");

            HeatmapSession session = ReadHeader(reader);
            var samples = new List<HeatmapSample>(Mathf.Max(64, (int)(stream.Length / 17)));
            var events = new List<HeatmapEvent>();

            try
            {
                while (stream.Position < stream.Length)
                {
                    byte block = reader.ReadByte();
                    if (block == SamplesBlock)
                    {
                        int count = reader.ReadInt32();
                        if (count < 0) break;
                        for (int i = 0; i < count; i++)
                        {
                            samples.Add(new HeatmapSample
                            {
                                t = reader.ReadSingle(),
                                position = ReadVector3(reader),
                                state = (HeatmapMoveState)reader.ReadByte(),
                            });
                        }
                    }
                    else if (block == EventsBlock)
                    {
                        int count = reader.ReadInt32();
                        if (count < 0) break;
                        for (int i = 0; i < count; i++)
                        {
                            events.Add(new HeatmapEvent
                            {
                                t = reader.ReadSingle(),
                                position = ReadVector3(reader),
                                type = reader.ReadUInt16(),
                                value = reader.ReadSingle(),
                            });
                        }
                    }
                    else if (block == EventNameBlock)
                    {
                        int id = reader.ReadUInt16();
                        string name = reader.ReadString();
                        while (session.eventNames.Count <= id) session.eventNames.Add("");
                        session.eventNames[id] = name;
                    }
                    else
                    {
                        break;
                    }
                }
            }
            catch (EndOfStreamException)
            {
            }

            session.samples = samples.ToArray();
            session.events = events.ToArray();
            session.Repair();
            return session;
        }

        public static void SaveBinary(HeatmapSession session, string path)
        {
            using var writer = new HeatmapSessionWriter(path, session);
            for (int i = 0; i < session.eventNames.Count; i++)
                writer.WriteEventName(i, session.eventNames[i]);
            if (session.samples.Length > 0) writer.WriteSamples(session.samples);
            if (session.events.Length > 0) writer.WriteEvents(session.events);
        }

        internal static void WriteHeader(BinaryWriter writer, HeatmapSession session)
        {
            writer.Write(session.sessionId ?? "");
            writer.Write(session.sceneName ?? "");
            writer.Write(session.scenePath ?? "");
            writer.Write(session.playerLabel ?? "");
            writer.Write(session.startTicksUtc);
            writer.Write(session.appVersion ?? "");
            writer.Write(session.unityVersion ?? "");
            writer.Write(session.platform ?? "");
            writer.Write(session.fromEditor);
            writer.Write(session.sampleInterval);
        }

        static HeatmapSession ReadHeader(BinaryReader reader)
        {
            return new HeatmapSession
            {
                sessionId = reader.ReadString(),
                sceneName = reader.ReadString(),
                scenePath = reader.ReadString(),
                playerLabel = reader.ReadString(),
                startTicksUtc = reader.ReadInt64(),
                appVersion = reader.ReadString(),
                unityVersion = reader.ReadString(),
                platform = reader.ReadString(),
                fromEditor = reader.ReadBoolean(),
                sampleInterval = reader.ReadSingle(),
            };
        }

        static Vector3 ReadVector3(BinaryReader reader)
        {
            return new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        }

        #endregion

        #region JSON

        [Serializable]
        internal sealed class JsonFile
        {
            public string format;
            public int version;
            public List<HeatmapSession> sessions;
        }

        public static void SaveJson(IEnumerable<HeatmapSession> sessions, string path, bool prettyPrint = false)
        {
            var file = new JsonFile
            {
                format = JsonFormatId,
                version = FormatVersion,
                sessions = new List<HeatmapSession>(sessions),
            };
            EnsureFolderFor(path);
            File.WriteAllText(path, JsonUtility.ToJson(file, prettyPrint), Utf8);
        }

        public static List<HeatmapSession> LoadJson(string path)
        {
            string json = File.ReadAllText(path, Utf8);

            var file = JsonUtility.FromJson<JsonFile>(json);
            if (file != null && file.format == JsonFormatId && file.sessions != null)
            {
                foreach (HeatmapSession session in file.sessions) session.Repair();
                return file.sessions;
            }

            var single = JsonUtility.FromJson<HeatmapSession>(json);
            if (single != null && !string.IsNullOrEmpty(single.sessionId) && single.samples != null)
            {
                single.Repair();
                return new List<HeatmapSession> { single };
            }

            throw new InvalidDataException($"{Path.GetFileName(path)} is not a heatmap JSON file.");
        }

        #endregion

        #region CSV

        public static void SaveCsv(IEnumerable<HeatmapSession> sessions, string path)
        {
            EnsureFolderFor(path);
            CultureInfo ci = CultureInfo.InvariantCulture;
            using var writer = new StreamWriter(path, false, Utf8);
            writer.WriteLine("session,scene,player,source,kind,time,x,y,z,name,value");
            foreach (HeatmapSession s in sessions)
            {
                string prefix = string.Join(",", Csv(s.sessionId), Csv(s.sceneName), Csv(s.playerLabel),
                    s.fromEditor ? "Editor" : "Build");
                foreach (HeatmapSample p in s.samples)
                {
                    writer.WriteLine(string.Format(ci, "{0},sample,{1:0.###},{2:0.###},{3:0.###},{4:0.###},{5},",
                        prefix, p.t, p.position.x, p.position.y, p.position.z, p.state));
                }
                foreach (HeatmapEvent e in s.events)
                {
                    writer.WriteLine(string.Format(ci, "{0},event,{1:0.###},{2:0.###},{3:0.###},{4:0.###},{5},{6:0.###}",
                        prefix, e.t, e.position.x, e.position.y, e.position.z, Csv(s.GetEventName(e.type)), e.value));
                }
            }
        }

        public static void SaveCellsCsv(HeatmapGrid grid, string path)
        {
            EnsureFolderFor(path);
            CultureInfo ci = CultureInfo.InvariantCulture;
            using var writer = new StreamWriter(path, false, Utf8);
            writer.WriteLine(grid.Source == HeatmapSource.TimeSpent ? "x,y,z,seconds" : "x,y,z,count");
            foreach (HeatmapCell cell in grid.Cells)
            {
                writer.WriteLine(string.Format(ci, "{0:0.###},{1:0.###},{2:0.###},{3:0.###}",
                    cell.center.x, cell.center.y, cell.center.z, cell.value));
            }
        }

        public static void SaveMapBoundsCsv(HeatmapMapInfo map, string path)
        {
            EnsureFolderFor(path);
            CultureInfo ci = CultureInfo.InvariantCulture;
            using var writer = new StreamWriter(path, false, Utf8);
            writer.WriteLine("image,scene,width,height,pixels_per_metre,min_x,max_x,min_z,max_z,height_image,min_y,max_y");
            string heights = string.IsNullOrEmpty(map.heightImage)
                ? ",,"
                : string.Format(ci, "{0},{1:0.####},{2:0.####}", Csv(map.heightImage), map.minY, map.maxY);
            writer.WriteLine(string.Format(ci, "{0},{1},{2},{3},{4:0.######},{5:0.###},{6:0.###},{7:0.###},{8:0.###},{9}",
                Csv(map.image), Csv(map.scene), map.width, map.height, map.pixelsPerMetre, map.minX, map.maxX, map.minZ, map.maxZ,
                heights));
        }

        public static byte[] EncodeGray16Png(ushort[] pixels, int width, int height)
        {
            if (width <= 0 || height <= 0 || pixels.Length != width * height)
                throw new ArgumentException("The pixel count doesn't match the image size.");

            int stride = width * 2 + 1;
            var filtered = new byte[stride * height];
            for (int y = 0; y < height; y++)
            {
                int row = y * stride;
                filtered[row] = 1;
                int previous = 0;
                for (int x = 0; x < width; x++)
                {
                    int value = pixels[y * width + x];
                    filtered[row + 1 + x * 2] = (byte)((value >> 8) - (previous >> 8));
                    filtered[row + 2 + x * 2] = (byte)(value - previous);
                    previous = value;
                }
            }

            var header = new byte[13];
            WriteBigEndian(header, 0, (uint)width);
            WriteBigEndian(header, 4, (uint)height);
            header[8] = 16;

            using var png = new MemoryStream();
            png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
            WritePngChunk(png, "IHDR", header);
            WritePngChunk(png, "IDAT", Zlib(filtered));
            WritePngChunk(png, "IEND", Array.Empty<byte>());
            return png.ToArray();
        }

        static byte[] Zlib(byte[] data)
        {
            using var output = new MemoryStream();
            output.WriteByte(0x78);
            output.WriteByte(0x9C);
            using (var deflate = new DeflateStream(output, System.IO.Compression.CompressionLevel.Optimal, true)) deflate.Write(data, 0, data.Length);

            uint a = 1, b = 0;
            foreach (byte value in data)
            {
                a = (a + value) % 65521;
                b = (b + a) % 65521;
            }
            var adler = new byte[4];
            WriteBigEndian(adler, 0, (b << 16) | a);
            output.Write(adler, 0, 4);
            return output.ToArray();
        }

        static void WritePngChunk(Stream stream, string type, byte[] data)
        {
            var bytes = new byte[12 + data.Length];
            WriteBigEndian(bytes, 0, (uint)data.Length);
            Encoding.ASCII.GetBytes(type, 0, 4, bytes, 4);
            Buffer.BlockCopy(data, 0, bytes, 8, data.Length);
            WriteBigEndian(bytes, 8 + data.Length, Crc32(bytes, 4, 4 + data.Length));
            stream.Write(bytes, 0, bytes.Length);
        }

        static uint[] crcTable;

        static uint Crc32(byte[] data, int start, int count)
        {
            if (crcTable == null)
            {
                crcTable = new uint[256];
                for (uint n = 0; n < 256; n++)
                {
                    uint c = n;
                    for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                    crcTable[n] = c;
                }
            }

            uint crc = 0xFFFFFFFFu;
            for (int i = start; i < start + count; i++) crc = crcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }

        static void WriteBigEndian(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }

        static string Csv(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.IndexOfAny(CsvSpecialChars) < 0) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        #endregion

        internal static void EnsureFolderFor(string path)
        {
            string folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
        }
    }
}
