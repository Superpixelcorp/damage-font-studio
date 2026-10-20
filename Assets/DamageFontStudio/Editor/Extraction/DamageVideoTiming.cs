using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DamageFontStudio.Editor
{
    // Reads presentation times from ordinary (non-fragmented) MP4/MOV sample tables.
    // Unsupported containers/edit lists deliberately fall back to the decoder clock.
    internal static class DamageVideoTiming
    {
        struct Box { internal string type; internal long data, end; }
        static uint U32(BinaryReader r) { var b = r.ReadBytes(4); if (b.Length != 4) throw new EndOfStreamException(); return (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]); }
        static ulong U64(BinaryReader r) => ((ulong)U32(r) << 32) | U32(r);
        static List<Box> Children(BinaryReader r, long start, long end)
        {
            var result = new List<Box>(); long pos = start;
            while (pos + 8 <= end)
            {
                r.BaseStream.Position = pos; long size = U32(r); string type = Encoding.ASCII.GetString(r.ReadBytes(4)); long header = 8;
                if (size == 1) { size = checked((long)U64(r)); header = 16; }
                if (size == 0) size = end - pos;
                if (size < header || size > end - pos) throw new InvalidDataException("Malformed MP4 box.");
                result.Add(new Box { type = type, data = pos + header, end = pos + size }); pos += size;
            }
            return result;
        }
        static Box Child(BinaryReader r, Box parent, string type) => Children(r, parent.data, parent.end).FirstOrDefault(b => b.type == type);
        static uint Timescale(BinaryReader r, Box box)
        {
            r.BaseStream.Position = box.data; int version = r.ReadByte(); r.BaseStream.Position = box.data + (version == 1 ? 20 : 12); return U32(r);
        }
        internal static double[] TryRead(string path, int expectedFrames)
        {
            try
            {
                using (var stream = File.OpenRead(path)) using (var r = new BinaryReader(stream))
                {
                    var root = new Box { data = 0, end = stream.Length }; var moov = Child(r, root, "moov");
                    if (moov.type == null) return null;
                    var mvhd = Child(r, moov, "mvhd"); uint movieScale = Timescale(r, mvhd);
                    foreach (var track in Children(r, moov.data, moov.end).Where(b => b.type == "trak"))
                    {
                        var mdia = Child(r, track, "mdia"); if (mdia.type == null) continue;
                        var hdlr = Child(r, mdia, "hdlr"); r.BaseStream.Position = hdlr.data + 8;
                        if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "vide") continue;
                        uint scale = Timescale(r, Child(r, mdia, "mdhd")); if (scale == 0) return null;
                        var stbl = Child(r, Child(r, mdia, "minf"), "stbl"); var stts = Child(r, stbl, "stts");
                        if (stts.type == null) return null;
                        r.BaseStream.Position = stts.data + 4; uint entries = U32(r);
                        if (entries > expectedFrames + 1 || stts.data + 8 + entries * 8L > stts.end) return null;
                        var times = new List<double>(); long dts = 0;
                        for (int i = 0; i < entries; i++)
                        {
                            uint count = U32(r), delta = U32(r); if (times.Count + count > expectedFrames) return null;
                            for (int k = 0; k < count; k++) { times.Add(dts / (double)scale); dts += delta; }
                        }
                        if (times.Count != expectedFrames) return null;
                        var ctts = Child(r, stbl, "ctts");
                        if (ctts.type != null)
                        {
                            r.BaseStream.Position = ctts.data; int version = r.ReadByte(); r.BaseStream.Position = ctts.data + 4;
                            entries = U32(r); if (entries > expectedFrames + 1 || ctts.data + 8 + entries * 8L > ctts.end) return null;
                            int index = 0;
                            for (int i = 0; i < entries; i++)
                            {
                                uint count = U32(r), raw = U32(r); long offset = version == 1 ? unchecked((int)raw) : (long)raw;
                                if (index + count > expectedFrames) return null;
                                for (int k = 0; k < count; k++) times[index++] += offset / (double)scale;
                            }
                        }
                        double shift = 0;
                        var edts = Child(r, track, "edts"); var elst = edts.type == null ? default : Child(r, edts, "elst");
                        if (elst.type != null)
                        {
                            r.BaseStream.Position = elst.data; int version = r.ReadByte(); r.BaseStream.Position = elst.data + 4;
                            entries = U32(r); if (entries > 2 || movieScale == 0) return null;
                            bool mediaSeen = false;
                            for (int i = 0; i < entries; i++)
                            {
                                ulong duration = version == 1 ? U64(r) : U32(r);
                                long media = version == 1 ? unchecked((long)U64(r)) : unchecked((int)U32(r));
                                uint rate = U32(r); if (rate != 0x00010000 || mediaSeen) return null;
                                if (media < 0) shift += duration / (double)movieScale;
                                else { shift -= media / (double)scale; mediaSeen = true; }
                            }
                        }
                        return times.OrderBy(t => t).Select(t => t + shift).ToArray();
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is OverflowException || ex is ArgumentException) { }
            return null;
        }
    }
}
