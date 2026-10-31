// MainStay Sans — gerador de fonte.
// Lê glyphs.txt (traços retos), engrossa os traços com cantos em esquadria (WPF),
// une os contornos e grava TTF + WOFF em três pesos, além do logo em SVG e uma página de amostra.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace MainStayFont
{
    class Stroke { public List<Point> Pts = new List<Point>(); public bool Closed; }

    class GlyphDef
    {
        public int Code; public double Wd; public bool Upper; public double Sb = 1;
        public List<Stroke> Strokes = new List<Stroke>();
        public List<Point> Dots = new List<Point>();
        public bool IsSpace; public double SpaceAdv;
    }

    class AccentDef { public List<Stroke> Strokes = new List<Stroke>(); public List<Point> Dots = new List<Point>(); }

    class OutGlyph { public int Code; public List<List<Point>> Contours = new List<List<Point>>(); public int Advance; }

    class BE
    {
        MemoryStream s = new MemoryStream();
        public void U8(int v) { s.WriteByte((byte)v); }
        public void U16(int v) { s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v); }
        public void I16(int v) { U16(v & 0xFFFF); }
        public void U32(uint v) { U16((int)(v >> 16)); U16((int)(v & 0xFFFF)); }
        public void I64(long v) { U32((uint)(v >> 32)); U32((uint)(v & 0xFFFFFFFF)); }
        public void Tag(string t) { foreach (char c in t) U8(c); }
        public void Bytes(byte[] b) { s.Write(b, 0, b.Length); }
        public void Pad4() { while (s.Length % 4 != 0) U8(0); }
        public byte[] Data { get { return s.ToArray(); } }
        public int Len { get { return (int)s.Length; } }
    }

    static class Program
    {
        const string Family = "MainStay Sans";
        const string Ink = "#111827", Accent = "#2F6BFF", InkDark = "#F9FAFB", AccentDark = "#5B8CFF";
        const double Tol = 0.01;

        static Dictionary<int, GlyphDef> glyphs = new Dictionary<int, GlyphDef>();
        static List<int> order = new List<int>();
        static Dictionary<string, AccentDef> accents = new Dictionary<string, AccentDef>();

        // ---------- leitura do glyphs.txt ----------

        static double Num(string s) { return double.Parse(s, CultureInfo.InvariantCulture); }

        static int ParseKey(string k)
        {
            if (k == "space") return 32;
            if (k.StartsWith("u+")) return int.Parse(k.Substring(2), NumberStyles.HexNumber);
            if (k.Length == 1) return k[0];
            throw new Exception("chave inválida: " + k);
        }

        static void ParseStrokes(string[] parts, List<Stroke> strokes, List<Point> dots)
        {
            for (int i = 1; i < parts.Length; i++)
            {
                var toks = parts[i].Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (toks.Length == 0) continue;
                bool closed = toks[0] == "z", dot = toks[0] == "d";
                var pts = new List<Point>();
                for (int j = (closed || dot) ? 1 : 0; j < toks.Length; j++)
                {
                    if (toks[j].StartsWith("a:"))
                    {
                        // arco de elipse: a:cx,cy,rx,ry,ângulo inicial,ângulo final (graus)
                        var v = toks[j].Substring(2).Split(',').Select(Num).ToArray();
                        int steps = Math.Max(4, (int)Math.Ceiling(Math.Abs(v[5] - v[4]) / 6));
                        for (int k = 0; k <= steps; k++)
                        {
                            double t = (v[4] + (v[5] - v[4]) * k / steps) * Math.PI / 180;
                            pts.Add(new Point(v[0] + v[2] * Math.Cos(t), v[1] + v[3] * Math.Sin(t)));
                        }
                        continue;
                    }
                    var xy = toks[j].Split(',');
                    pts.Add(new Point(Num(xy[0]), Num(xy[1])));
                }
                if (dot) dots.AddRange(pts); else strokes.Add(new Stroke { Pts = pts, Closed = closed });
            }
        }

        static void Load(string path)
        {
            foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var parts = line.Split('|');
                var head = parts[0].Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (head[0] == "accent")
                {
                    var a = new AccentDef();
                    ParseStrokes(parts, a.Strokes, a.Dots);
                    accents[head[1] + "." + head[2]] = a;
                    continue;
                }
                int code = ParseKey(head[0]);
                GlyphDef g;
                if (head[0] == "space")
                {
                    g = new GlyphDef { Code = 32, IsSpace = true, SpaceAdv = Num(head[1]) };
                }
                else if (head.Length >= 5 && head[1] == "=")
                {
                    var b = glyphs[ParseKey(head[2])];
                    g = new GlyphDef { Code = code, Wd = b.Wd, Upper = b.Upper, Sb = b.Sb };
                    g.Strokes.AddRange(b.Strokes);
                    g.Dots.AddRange(b.Dots);
                    var acc = accents[head[4] + "." + (b.Upper ? "U" : "L")];
                    double dx = b.Wd / 2;
                    foreach (var s in acc.Strokes)
                        g.Strokes.Add(new Stroke { Closed = s.Closed, Pts = s.Pts.Select(p => new Point(p.X + dx, p.Y)).ToList() });
                    foreach (var p in acc.Dots) g.Dots.Add(new Point(p.X + dx, p.Y));
                }
                else
                {
                    g = new GlyphDef { Code = code, Wd = Num(head[1]), Upper = head[2] == "U" };
                    for (int i = 3; i < head.Length; i++)
                        if (head[i].StartsWith("s=")) g.Sb = Num(head[i].Substring(2));
                    ParseStrokes(parts, g.Strokes, g.Dots);
                }
                if (!glyphs.ContainsKey(code)) order.Add(code);
                glyphs[code] = g;
            }
        }

        // ---------- geometria ----------

        // Pena chata horizontal (nw largo × nh fino): verticais saem grossas, horizontais finas.
        // y do desenho é a borda externa nas linhas de referência; converte para o centro da pena.
        static double MapY(double y, double zone, double nh)
        {
            if (y < 0) return y + nh / 2;
            if (y <= zone) return nh / 2 + y * (zone - nh) / zone;
            return y - nh / 2;
        }

        static List<Point> Hull(List<Point> p)
        {
            var s = p.OrderBy(q => q.X).ThenBy(q => q.Y).ToList();
            var h = new List<Point>();
            for (int pass = 0; pass < 2; pass++)
            {
                int start = h.Count;
                foreach (var q in s)
                {
                    while (h.Count >= start + 2 && Cross(h[h.Count - 2], h[h.Count - 1], q) <= 0) h.RemoveAt(h.Count - 1);
                    h.Add(q);
                }
                h.RemoveAt(h.Count - 1);
                s.Reverse();
            }
            return h; // anti-horário
        }

        static double Cross(Point o, Point a, Point b) { return (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X); }

        static void AddPoly(PathGeometry pg, List<Point> poly)
        {
            var fig = new PathFigure { StartPoint = poly[0], IsClosed = true, IsFilled = true };
            fig.Segments.Add(new PolyLineSegment(poly.Skip(1), true));
            pg.Figures.Add(fig);
        }

        static List<Point> Nib(Point c, double nw, double nh)
        {
            return new List<Point> { new Point(c.X - nw / 2, c.Y - nh / 2), new Point(c.X + nw / 2, c.Y - nh / 2),
                                     new Point(c.X + nw / 2, c.Y + nh / 2), new Point(c.X - nw / 2, c.Y + nh / 2) };
        }

        static Geometry BuildGeometry(GlyphDef g, double nw, double nh, double lsb, IEnumerable<int> strokeIdx, bool includeDots)
        {
            double zone = g.Upper ? 700 : 500;
            var pg = new PathGeometry { FillRule = FillRule.Nonzero };
            foreach (int si in strokeIdx)
            {
                var pts = g.Strokes[si].Pts.Select(p => new Point(lsb + nw / 2 + p.X, MapY(p.Y, zone, nh))).ToList();
                if (pts.Count == 1) AddPoly(pg, Hull(Nib(pts[0], nw, nh)));
                for (int i = 1; i < pts.Count; i++)
                {
                    var both = Nib(pts[i - 1], nw, nh);
                    both.AddRange(Nib(pts[i], nw, nh));
                    AddPoly(pg, Hull(both));
                }
            }
            if (includeDots)
                foreach (var d in g.Dots)
                {
                    // ponto quadrado: em y=0 apoia na linha de base, na altura-x/versal encosta no topo
                    double cx = lsb + nw / 2 + d.X;
                    double cy = d.Y == 0 ? nw / 2 : (d.Y == zone ? zone - nw / 2 : d.Y);
                    AddPoly(pg, Hull(Nib(new Point(cx, cy), nw, nw)));
                }
            return pg.GetOutlinedPathGeometry(Tol, ToleranceType.Absolute);
        }

        static double Area(List<Point> p)
        {
            double a = 0;
            for (int i = 0; i < p.Count; i++) { var q = p[(i + 1) % p.Count]; a += p[i].X * q.Y - q.X * p[i].Y; }
            return a / 2;
        }

        static bool Inside(Point pt, List<Point> poly)
        {
            bool c = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                if ((poly[i].Y > pt.Y) != (poly[j].Y > pt.Y) &&
                    pt.X < (poly[j].X - poly[i].X) * (pt.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)
                    c = !c;
            return c;
        }

        static List<List<Point>> Contours(Geometry geo)
        {
            var res = new List<List<Point>>();
            var flat = geo.GetFlattenedPathGeometry(0.05, ToleranceType.Absolute);
            foreach (var f in flat.Figures)
            {
                var raw = new List<Point> { f.StartPoint };
                foreach (var seg in f.Segments)
                {
                    if (seg is LineSegment) raw.Add(((LineSegment)seg).Point);
                    else if (seg is PolyLineSegment) raw.AddRange(((PolyLineSegment)seg).Points);
                    else throw new Exception("segmento inesperado: " + seg.GetType());
                }
                var pts = new List<Point>();
                foreach (var p in raw)
                {
                    var q = new Point(Math.Round(p.X), Math.Round(p.Y));
                    if (pts.Count == 0 || pts[pts.Count - 1] != q) pts.Add(q);
                }
                if (pts.Count > 1 && pts[0] == pts[pts.Count - 1]) pts.RemoveAt(pts.Count - 1);
                bool changed = true;
                while (changed && pts.Count >= 3)
                {
                    changed = false;
                    for (int i = 0; i < pts.Count; i++)
                    {
                        var a = pts[(i + pts.Count - 1) % pts.Count]; var b = pts[i]; var c = pts[(i + 1) % pts.Count];
                        double cr = (b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X);
                        if (Math.Abs(cr) < 1e-9) { pts.RemoveAt(i); changed = true; break; }
                    }
                }
                if (pts.Count >= 3 && Math.Abs(Area(pts)) > 4) res.Add(pts);
            }
            // TrueType: contorno externo no sentido horário, furos no anti-horário
            for (int i = 0; i < res.Count; i++)
            {
                int depth = 0;
                for (int j = 0; j < res.Count; j++) if (i != j && Inside(res[i][0], res[j])) depth++;
                double a = Area(res[i]);
                bool wantCW = depth % 2 == 0;
                if ((wantCW && a > 0) || (!wantCW && a < 0)) res[i].Reverse();
            }
            return res;
        }

        static double SideBearing(double nw) { return 32 + 0.3 * nw; }

        static OutGlyph Build(GlyphDef g, double nw, double nh)
        {
            var o = new OutGlyph { Code = g.Code };
            if (g.IsSpace) { o.Advance = (int)Math.Round(g.SpaceAdv + nw * 0.5); return o; }
            double sb = SideBearing(nw) * g.Sb;
            o.Contours = Contours(BuildGeometry(g, nw, nh, sb, Enumerable.Range(0, g.Strokes.Count), true));
            o.Advance = (int)Math.Round(2 * sb + g.Wd + nw);
            return o;
        }

        // ---------- TTF / WOFF ----------

        static uint Checksum(byte[] d)
        {
            uint s = 0;
            for (int i = 0; i < d.Length; i += 4)
            {
                uint v = 0;
                for (int k = 0; k < 4; k++) { v <<= 8; if (i + k < d.Length) v |= d[i + k]; }
                s += v;
            }
            return s;
        }

        static byte[] BuildFont(List<OutGlyph> glyphsIn, double w, string style, int weightClass, out byte[] woff)
        {
            var gl = new List<OutGlyph>();
            var nd = new OutGlyph { Code = -1, Advance = 500 };
            nd.Contours.Add(new List<Point> { new Point(50, 0), new Point(50, 700), new Point(450, 700), new Point(450, 0) });
            nd.Contours.Add(new List<Point> { new Point(130, 80), new Point(370, 80), new Point(370, 620), new Point(130, 620) });
            gl.Add(nd);
            gl.AddRange(glyphsIn);
            int n = gl.Count;

            var glyf = new BE(); var loca = new BE(); var hmtx = new BE();
            int maxPts = 0, maxCont = 0, gxMin = int.MaxValue, gyMin = int.MaxValue, gxMax = int.MinValue, gyMax = int.MinValue;
            int advMax = 0, minLsb = int.MaxValue, minRsb = int.MaxValue, maxExt = int.MinValue;
            long advSum = 0; int advCount = 0;
            foreach (var g in gl)
            {
                loca.U32((uint)glyf.Len);
                advMax = Math.Max(advMax, g.Advance);
                if (g.Advance > 0) { advSum += g.Advance; advCount++; }
                if (g.Contours.Count == 0) { hmtx.U16(g.Advance); hmtx.I16(0); continue; }
                var all = g.Contours.SelectMany(c => c).ToList();
                int xMin = (int)all.Min(p => p.X), yMin = (int)all.Min(p => p.Y), xMax = (int)all.Max(p => p.X), yMax = (int)all.Max(p => p.Y);
                gxMin = Math.Min(gxMin, xMin); gyMin = Math.Min(gyMin, yMin); gxMax = Math.Max(gxMax, xMax); gyMax = Math.Max(gyMax, yMax);
                maxPts = Math.Max(maxPts, all.Count); maxCont = Math.Max(maxCont, g.Contours.Count);
                minLsb = Math.Min(minLsb, xMin); minRsb = Math.Min(minRsb, g.Advance - xMax); maxExt = Math.Max(maxExt, xMax);
                hmtx.U16(g.Advance); hmtx.I16(xMin);
                glyf.I16(g.Contours.Count); glyf.I16(xMin); glyf.I16(yMin); glyf.I16(xMax); glyf.I16(yMax);
                int end = -1;
                foreach (var c in g.Contours) { end += c.Count; glyf.U16(end); }
                glyf.U16(0);
                foreach (var p in all) glyf.U8(1);
                int prev = 0;
                foreach (var p in all) { glyf.I16((int)p.X - prev); prev = (int)p.X; }
                prev = 0;
                foreach (var p in all) { glyf.I16((int)p.Y - prev); prev = (int)p.Y; }
                glyf.Pad4();
            }
            loca.U32((uint)glyf.Len);

            bool bold = weightClass >= 700;
            long now = (long)(DateTime.UtcNow - new DateTime(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            var head = new BE();
            head.U32(0x00010000); head.U32(0x00010000); head.U32(0); head.U32(0x5F0F3CF5);
            head.U16(0x0009); head.U16(1000); head.I64(now); head.I64(now);
            head.I16(gxMin); head.I16(gyMin); head.I16(gxMax); head.I16(gyMax);
            head.U16(bold ? 1 : 0); head.U16(8); head.I16(2); head.I16(1); head.I16(0);

            const int asc = 950, desc = 250;
            var hhea = new BE();
            hhea.U32(0x00010000); hhea.I16(asc); hhea.I16(-desc); hhea.I16(0);
            hhea.U16(advMax); hhea.I16(minLsb); hhea.I16(minRsb); hhea.I16(maxExt);
            hhea.I16(1); hhea.I16(0); hhea.I16(0);
            for (int i = 0; i < 4; i++) hhea.I16(0);
            hhea.I16(0); hhea.U16(n);

            var maxp = new BE();
            maxp.U32(0x00010000); maxp.U16(n); maxp.U16(maxPts); maxp.U16(maxCont); maxp.U16(0); maxp.U16(0); maxp.U16(2);
            for (int i = 0; i < 8; i++) maxp.U16(0);

            var map = new SortedDictionary<int, int>();
            for (int i = 1; i < n; i++) map[gl[i].Code] = i;
            if (map.ContainsKey(32)) map[0xA0] = map[32];

            var os2 = new BE();
            os2.U16(4); os2.I16((int)(advSum / advCount)); os2.U16(weightClass); os2.U16(5); os2.U16(0);
            os2.I16(650); os2.I16(600); os2.I16(0); os2.I16(75);
            os2.I16(650); os2.I16(600); os2.I16(0); os2.I16(350);
            os2.I16((int)w); os2.I16(300); os2.I16(0);
            os2.U8(2); for (int i = 0; i < 9; i++) os2.U8(0);
            os2.U32(3); os2.U32(0); os2.U32(0); os2.U32(0);
            os2.Tag("MSTX");
            os2.U16(bold ? 0xA0 : 0xC0); os2.U16(map.Keys.First()); os2.U16(map.Keys.Last());
            os2.I16(asc); os2.I16(-desc); os2.I16(0);
            os2.U16(Math.Max(asc, gyMax)); os2.U16(Math.Max(desc, -gyMin));
            os2.U32(1); os2.U32(0);
            os2.I16(500); os2.I16(700); os2.U16(0); os2.U16(32); os2.U16(0);

            var cmapSub = new BE();
            int segCount = map.Count + 1;
            int es = (int)Math.Floor(Math.Log(segCount, 2)), sr = 2 * (1 << es);
            cmapSub.U16(4); cmapSub.U16(16 + segCount * 8); cmapSub.U16(0);
            cmapSub.U16(segCount * 2); cmapSub.U16(sr); cmapSub.U16(es); cmapSub.U16(segCount * 2 - sr);
            foreach (var kv in map) cmapSub.U16(kv.Key);
            cmapSub.U16(0xFFFF); cmapSub.U16(0);
            foreach (var kv in map) cmapSub.U16(kv.Key);
            cmapSub.U16(0xFFFF);
            foreach (var kv in map) cmapSub.U16((kv.Value - kv.Key) & 0xFFFF);
            cmapSub.U16(1);
            for (int i = 0; i < segCount; i++) cmapSub.U16(0);
            var cmap = new BE();
            cmap.U16(0); cmap.U16(2); cmap.U16(0); cmap.U16(3); cmap.U32(20); cmap.U16(3); cmap.U16(1); cmap.U32(20);
            cmap.Bytes(cmapSub.Data);

            var names = new List<KeyValuePair<int, string>>
            {
                new KeyValuePair<int, string>(1, Family),
                new KeyValuePair<int, string>(2, style),
                new KeyValuePair<int, string>(3, Family + " " + style + " 1.000"),
                new KeyValuePair<int, string>(4, Family + " " + style),
                new KeyValuePair<int, string>(5, "Version 1.000"),
                new KeyValuePair<int, string>(6, Family.Replace(" ", "") + "-" + style),
            };
            var name = new BE(); var store = new BE();
            name.U16(0); name.U16(names.Count); name.U16(6 + 12 * names.Count);
            foreach (var kv in names)
            {
                var b = Encoding.BigEndianUnicode.GetBytes(kv.Value);
                name.U16(3); name.U16(1); name.U16(0x409); name.U16(kv.Key); name.U16(b.Length); name.U16(store.Len);
                store.Bytes(b);
            }
            name.Bytes(store.Data);

            var post = new BE();
            post.U32(0x00030000); post.U32(0); post.I16(-120); post.I16((int)(w * 0.6));
            for (int i = 0; i < 5; i++) post.U32(0);

            var tabs = new List<KeyValuePair<string, byte[]>>
            {
                new KeyValuePair<string, byte[]>("OS/2", os2.Data), new KeyValuePair<string, byte[]>("cmap", cmap.Data),
                new KeyValuePair<string, byte[]>("glyf", glyf.Data), new KeyValuePair<string, byte[]>("head", head.Data),
                new KeyValuePair<string, byte[]>("hhea", hhea.Data), new KeyValuePair<string, byte[]>("hmtx", hmtx.Data),
                new KeyValuePair<string, byte[]>("loca", loca.Data), new KeyValuePair<string, byte[]>("maxp", maxp.Data),
                new KeyValuePair<string, byte[]>("name", name.Data), new KeyValuePair<string, byte[]>("post", post.Data),
            };
            return Assemble(tabs, out woff);
        }

        static byte[] Assemble(List<KeyValuePair<string, byte[]>> tabs, out byte[] woff)
        {
            tabs.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            int n = tabs.Count;
            int es = (int)Math.Floor(Math.Log(n, 2)), sr = (1 << es) * 16;
            var o = new BE();
            o.U32(0x00010000); o.U16(n); o.U16(sr); o.U16(es); o.U16(n * 16 - sr);
            int offset = 12 + 16 * n;
            var offsets = new int[n]; var sums = new uint[n];
            for (int i = 0; i < n; i++)
            {
                offsets[i] = offset; sums[i] = Checksum(tabs[i].Value);
                o.Tag(tabs[i].Key); o.U32(sums[i]); o.U32((uint)offset); o.U32((uint)tabs[i].Value.Length);
                offset += (tabs[i].Value.Length + 3) & ~3;
            }
            for (int i = 0; i < n; i++) { o.Bytes(tabs[i].Value); o.Pad4(); }
            byte[] font = o.Data;
            uint adj = 0xB1B0AFBA - Checksum(font);
            int hi = tabs.FindIndex(t => t.Key == "head");
            var headData = tabs[hi].Value;
            for (int k = 0; k < 4; k++)
            {
                byte b = (byte)(adj >> (24 - 8 * k));
                font[offsets[hi] + 8 + k] = b; headData[8 + k] = b;
            }
            woff = Woff(tabs, sums, font.Length);
            return font;
        }

        static byte[] Zlib(byte[] data)
        {
            var ms = new MemoryStream();
            ms.WriteByte(0x78); ms.WriteByte(0x9C);
            using (var ds = new DeflateStream(ms, CompressionLevel.Optimal, true)) ds.Write(data, 0, data.Length);
            uint a = 1, b = 0;
            foreach (byte x in data) { a = (a + x) % 65521; b = (b + a) % 65521; }
            uint ad = (b << 16) | a;
            for (int k = 0; k < 4; k++) ms.WriteByte((byte)(ad >> (24 - 8 * k)));
            return ms.ToArray();
        }

        static byte[] Woff(List<KeyValuePair<string, byte[]>> tabs, uint[] sums, int sfntSize)
        {
            int n = tabs.Count;
            var comp = new byte[n][]; var offs = new int[n];
            int off = 44 + 20 * n;
            for (int i = 0; i < n; i++)
            {
                var z = Zlib(tabs[i].Value);
                comp[i] = z.Length < tabs[i].Value.Length ? z : tabs[i].Value;
                offs[i] = off; off += (comp[i].Length + 3) & ~3;
            }
            var o = new BE();
            o.U32(0x774F4646); o.U32(0x00010000); o.U32((uint)off); o.U16(n); o.U16(0);
            o.U32((uint)sfntSize); o.U16(1); o.U16(0);
            for (int i = 0; i < 5; i++) o.U32(0);
            for (int i = 0; i < n; i++)
            {
                o.Tag(tabs[i].Key); o.U32((uint)offs[i]); o.U32((uint)comp[i].Length);
                o.U32((uint)tabs[i].Value.Length); o.U32(sums[i]);
            }
            for (int i = 0; i < n; i++) { o.Bytes(comp[i]); o.Pad4(); }
            return o.Data;
        }

        // ---------- logo e página de amostra ----------

        static string PathData(IEnumerable<List<Point>> contours, double dx)
        {
            var sb = new StringBuilder();
            foreach (var c in contours)
            {
                for (int i = 0; i < c.Count; i++)
                    sb.Append(i == 0 ? "M" : "L").Append((c[i].X + dx).ToString(CultureInfo.InvariantCulture))
                      .Append(' ').Append((-c[i].Y).ToString(CultureInfo.InvariantCulture));
                sb.Append('Z');
            }
            return sb.ToString();
        }

        static string TextPath(string text, Dictionary<int, OutGlyph> font, ref double x)
        {
            var sb = new StringBuilder();
            foreach (char ch in text) { var g = font[ch]; sb.Append(PathData(g.Contours, x)); x += g.Advance; }
            return sb.ToString();
        }

        static string Svg(double minX, double minY, double w, double h, string body)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"{0} {1} {2} {3}\">{4}</svg>", minX, minY, w, h, body);
        }

        static string[] MakeLogos(string logoDir, Dictionary<int, OutGlyph> textFont, double textNw, double nw, double nh)
        {
            var sym = glyphs[0xE000];
            string main = PathData(Contours(BuildGeometry(sym, nw, nh, 0, new[] { 0 }, false)), 0);
            string acc = PathData(Contours(BuildGeometry(sym, nw, nh, 0, new[] { 1 }, false)), 0);
            double symW = sym.Wd + nw, pad = 80;
            double x = symW + 300 - SideBearing(textNw);
            string word = TextPath("mainstay", textFont, ref x);
            string tail = TextPath("-x", textFont, ref x);
            double right = x - SideBearing(textNw);

            Func<string, string, string> logo = (ink, ac) => Svg(-pad, -(740 + pad), right + 2 * pad, 940 + 2 * pad,
                "<path fill=\"" + ink + "\" d=\"" + main + "\"/><path fill=\"" + ac + "\" d=\"" + acc + "\"/>" +
                "<path fill=\"" + ink + "\" d=\"" + word + "\"/><path fill=\"" + ac + "\" d=\"" + tail + "\"/>");
            string light = logo(Ink, Accent), dark = logo(InkDark, AccentDark);
            string symbol = Svg(-pad, -(700 + pad), symW + 2 * pad, 700 + 2 * pad,
                "<path fill=\"" + Ink + "\" d=\"" + main + "\"/><path fill=\"" + Accent + "\" d=\"" + acc + "\"/>");
            // ícone: quadrado, símbolo branco
            double s = 1400, c = 0, ox = (s - symW) / 2, oy = (s - 700) / 2;
            string iconBody = string.Format(CultureInfo.InvariantCulture,
                "<polygon fill=\"{0}\" points=\"{1},0 {2},0 {3},{1} {3},{2} {2},{3} {1},{3} 0,{2} 0,{1}\"/>" +
                "<g fill=\"#FFFFFF\" transform=\"translate({4} {5})\"><path d=\"{6}\"/><path d=\"{7}\"/></g>",
                Accent, c, s - c, s, ox, s - oy, main, acc);
            string icon = Svg(0, 0, s, s, iconBody);

            File.WriteAllText(Path.Combine(logoDir, "mainstay-x-logo.svg"), light);
            File.WriteAllText(Path.Combine(logoDir, "mainstay-x-logo-dark.svg"), dark);
            File.WriteAllText(Path.Combine(logoDir, "mainstay-x-symbol.svg"), symbol);
            File.WriteAllText(Path.Combine(logoDir, "mainstay-x-icon.svg"), icon);
            return new[] { light, dark, icon };
        }

        static string FontFace(string src, int weight)
        {
            return "@font-face {\n  font-family: 'MainStay Sans';\n  src: " + src + ";\n  font-weight: " + weight +
                   ";\n  font-style: normal;\n  font-display: swap;\n}\n";
        }

        static void WriteCss(string fontDir, string[] styles, int[] weights)
        {
            var sb = new StringBuilder("/* MainStay Sans — fonte própria da MainStay-x */\n");
            for (int i = 0; i < styles.Length; i++)
                sb.Append(FontFace("url('MainStaySans-" + styles[i] + ".woff') format('woff'), url('MainStaySans-" +
                                   styles[i] + ".ttf') format('truetype')", weights[i]));
            sb.Append("\n/* uso: body { font-family: 'MainStay Sans', system-ui, sans-serif; } */\n");
            File.WriteAllText(Path.Combine(fontDir, "mainstay-sans.css"), sb.ToString());
        }

        static void WriteSpecimen(string fontDir, string[] styles, int[] weights, Dictionary<string, string> b64, string[] logos)
        {
            var css = new StringBuilder();
            for (int i = 0; i < styles.Length; i++)
                css.Append(FontFace("url(data:font/woff;base64," + b64[styles[i]] + ") format('woff')", weights[i]));
            string page = @"<!doctype html>
<html lang=""pt-BR""><head><meta charset=""utf-8""><meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<title>MainStay Sans</title>
<style>
" + css + @"
:root { --bg:#F3F4F6; --card:#FFFFFF; --ink:#111827; --muted:#6B7280; --accent:#2F6BFF; --line:#E5E7EB; }
@media (prefers-color-scheme: dark) { :root:not([data-theme=""light""]) { --bg:#0B1220; --card:#111A2C; --ink:#F9FAFB; --muted:#9CA3AF; --accent:#5B8CFF; --line:#1F2A40; } }
* { box-sizing:border-box; }
body { margin:0; background:var(--bg); color:var(--ink); font-family:'MainStay Sans', system-ui, sans-serif; }
main { max-width:960px; margin:0 auto; padding:32px 16px 64px; display:grid; gap:16px; }
.card { background:var(--card); border:1px solid var(--line); padding:28px; }
.logos { display:grid; gap:16px; grid-template-columns:repeat(auto-fit,minmax(280px,1fr)); }
.logo { display:grid; place-items:center; padding:36px 24px; min-height:150px; }
.logo.l { background:#FFFFFF; } .logo.d { background:#0B1220; }
.logo svg { width:100%; max-width:420px; height:auto; }
.icon svg { width:96px; height:96px; }
h2 { margin:0 0 16px; font-size:13px; letter-spacing:.12em; text-transform:uppercase; color:var(--muted); font-weight:400; }
.big { font-size:clamp(44px,10vw,104px); line-height:1.05; margin:0; font-weight:300; }
.w { display:grid; gap:10px; font-size:clamp(22px,4vw,34px); }
.w span { color:var(--muted); font-size:13px; margin-right:12px; display:inline-block; width:70px; }
.chars { font-size:clamp(22px,4vw,32px); line-height:1.5; word-break:break-all; margin:0; }
.sizes p { margin:0 0 10px; }
.text { columns:2 260px; column-gap:32px; font-size:17px; line-height:1.6; }
.text p { margin:0 0 12px; }
code { font-family:Consolas, monospace; font-size:13px; background:var(--bg); padding:2px 6px; }
.btn { display:inline-block; background:var(--accent); color:#fff; padding:12px 22px; font-weight:700; font-size:16px; text-decoration:none; letter-spacing:.04em; }
</style></head><body><main>
<div class=""logos""><div class=""logo l"">" + logos[0] + @"</div><div class=""logo d"">" + logos[1] + @"</div></div>
<div class=""card""><h2>MainStay Sans</h2><p class=""big"">Moderna, leve,<br>precisa.</p></div>
<div class=""card w""><h2>Pesos</h2>
<div style=""font-weight:300""><span>Light</span>Estrutura que sustenta</div>
<div style=""font-weight:400""><span>Regular</span>Estrutura que sustenta</div>
<div style=""font-weight:700""><span>Bold</span>Estrutura que sustenta</div></div>
<div class=""card""><h2>Caracteres</h2><p class=""chars"">ABCDEFGHIJKLMNOPQRSTUVWXYZ<br>abcdefghijklmnopqrstuvwxyz<br>0123456789 &amp;@#%$€+=*/\&lt;&gt;<br>áàâãä éêè íî óôõö úü ç ñ ÁÂÃÉÊÍÓÔÕÚÇ<br>.,:;!?-–—_'""“”()[]|~^°ºª•</p></div>
<div class=""card sizes""><h2>Tamanhos</h2>
<p style=""font-size:48px;font-weight:700"">Gestão de ativos</p>
<p style=""font-size:32px"">Gestão de ativos com precisão</p>
<p style=""font-size:20px"">Gestão de ativos com precisão, do campo ao relatório.</p>
<p style=""font-size:14px"">Gestão de ativos com precisão, do campo ao relatório. Texto pequeno para legendas, tabelas e rodapés — 1º lugar, R$ 1.250,00.</p></div>
<div class=""card""><h2>Texto corrido</h2><div class=""text"">
<p>À noite, vovô Kowalsky vê o ímã cair no pé do pinguim queixoso e vovó põe açúcar no chá de tâmaras do jabuti feliz.</p>
<p>Uma fonte própria dá identidade ao site inteiro: títulos, menus, botões e textos falam com a mesma voz da marca. Hastes firmes contrastam com filetes finos e curvas geométricas, a mesma lógica do símbolo.</p>
<p>Números com largura fixa alinham bem em tabelas: 2026 · 18,40 · 1.000 · 07:30 · 99%.</p>
<p><a class=""btn"" href=""#"">Fale conosco</a></p></div></div>
<div class=""card""><h2>Ícone</h2><div class=""icon"">" + logos[2] + @"</div></div>
<div class=""card""><h2>Como usar no site</h2><p style=""line-height:1.7"">Inclua <code>&lt;link rel=""stylesheet"" href=""assets/fonts/mainstay-sans.css""&gt;</code> e use <code>font-family: 'MainStay Sans', system-ui, sans-serif;</code></p></div>
</main></body></html>";
            File.WriteAllText(Path.Combine(fontDir, "especime.html"), page, new UTF8Encoding(false));
        }

        [STAThread]
        static int Main(string[] args)
        {
            try
            {
                Load(args[0]);
                string fontDir = args[1], logoDir = args[2];
                var styles = new[] { "Light", "Regular", "Bold" };
                var nibW = new[] { 34.0, 60.0, 100.0 };   // espessura das verticais
                var nibH = new[] { 12.0, 26.0, 40.0 };    // espessura das horizontais (filetes)
                var weights = new[] { 300, 400, 700 };
                var b64 = new Dictionary<string, string>();
                Dictionary<int, OutGlyph> regularFont = null;
                for (int i = 0; i < styles.Length; i++)
                {
                    int wi = i;
                    var outs = order.Select(c => Build(glyphs[c], nibW[wi], nibH[wi])).ToList();
                    byte[] woff;
                    byte[] ttf = BuildFont(outs, nibH[i], styles[i], weights[i], out woff);
                    File.WriteAllBytes(Path.Combine(fontDir, "MainStaySans-" + styles[i] + ".ttf"), ttf);
                    File.WriteAllBytes(Path.Combine(fontDir, "MainStaySans-" + styles[i] + ".woff"), woff);
                    b64[styles[i]] = Convert.ToBase64String(woff);
                    if (styles[i] == "Regular") regularFont = outs.ToDictionary(o => o.Code);
                    Console.WriteLine("{0}: {1} glifos, ttf {2} KB, woff {3} KB", styles[i], outs.Count, ttf.Length / 1024, woff.Length / 1024);
                }
                var logos = MakeLogos(logoDir, regularFont, nibW[1], 80, 24);
                WriteCss(fontDir, styles, weights);
                WriteSpecimen(fontDir, styles, weights, b64, logos);
                return 0;
            }
            catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        }
    }
}
