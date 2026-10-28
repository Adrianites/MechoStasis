using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace HeatmapTool
{
    public sealed class HeatmapSessionWriter : IDisposable
    {
        readonly FileStream stream;
        readonly BinaryWriter writer;

        public string FilePath { get; }

        public HeatmapSessionWriter(string path, HeatmapSession header)
        {
            FilePath = path;
            string folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

            stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 14);
            try
            {
                writer = new BinaryWriter(stream, new UTF8Encoding(false));
                writer.Write(HeatmapIO.Magic);
                writer.Write(HeatmapIO.FormatVersion);
                HeatmapIO.WriteHeader(writer, header);
                writer.Flush();
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }

        public void WriteEventName(int id, string name)
        {
            writer.Write(HeatmapIO.EventNameBlock);
            writer.Write((ushort)id);
            writer.Write(name ?? "");
        }

        public void WriteSamples(IReadOnlyList<HeatmapSample> samples)
        {
            writer.Write(HeatmapIO.SamplesBlock);
            writer.Write(samples.Count);
            for (int i = 0; i < samples.Count; i++)
            {
                HeatmapSample sample = samples[i];
                writer.Write(sample.t);
                WriteVector3(sample.position);
                writer.Write((byte)sample.state);
            }
        }

        public void WriteEvents(IReadOnlyList<HeatmapEvent> events)
        {
            writer.Write(HeatmapIO.EventsBlock);
            writer.Write(events.Count);
            for (int i = 0; i < events.Count; i++)
            {
                HeatmapEvent e = events[i];
                writer.Write(e.t);
                WriteVector3(e.position);
                writer.Write((ushort)e.type);
                writer.Write(e.value);
            }
        }

        public void Flush()
        {
            writer.Flush();
        }

        public void Dispose()
        {
            writer.Dispose();
        }

        void WriteVector3(Vector3 v)
        {
            writer.Write(v.x);
            writer.Write(v.y);
            writer.Write(v.z);
        }
    }
}
