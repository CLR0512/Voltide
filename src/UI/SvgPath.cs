using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace Voltide.UI
{
    /// <summary>
    /// Minimal SVG path-data parser: M L H V C S Q T A Z, absolute and relative.
    /// Enough for Illustrator's icon and artwork exports, and a lot safer than
    /// hand-transcribing bezier control points into code.
    /// </summary>
    public static class SvgPath
    {
        public static GraphicsPath Parse(string d)
        {
            GraphicsPath path = new GraphicsPath();
            if (string.IsNullOrEmpty(d)) return path;

            int i = 0;
            char cmd = '\0';
            PointF cur = PointF.Empty;
            PointF figStart = PointF.Empty;
            PointF lastCubic = PointF.Empty;
            PointF lastQuad = PointF.Empty;
            char prevCmd = '\0';

            while (true)
            {
                SkipSeparators(d, ref i);
                if (i >= d.Length) break;

                if (char.IsLetter(d[i]))
                {
                    cmd = d[i];
                    i++;
                }
                else if (cmd == '\0')
                {
                    throw new FormatException("SVG path data must start with a command letter");
                }

                bool rel = char.IsLower(cmd);
                char up = char.ToUpperInvariant(cmd);

                float x, y, a, b, c, e;

                switch (up)
                {
                    case 'M':
                    {
                        x = ReadNumber(d, ref i);
                        y = ReadNumber(d, ref i);
                        cur = rel ? new PointF(cur.X + x, cur.Y + y) : new PointF(x, y);
                        path.StartFigure();
                        figStart = cur;
                        // extra coordinate pairs after M are implicit linetos
                        cmd = rel ? 'l' : 'L';
                        prevCmd = 'M';
                        break;
                    }

                    case 'L':
                    {
                        x = ReadNumber(d, ref i);
                        y = ReadNumber(d, ref i);
                        PointF to = rel ? new PointF(cur.X + x, cur.Y + y) : new PointF(x, y);
                        path.AddLine(cur, to);
                        cur = to;
                        prevCmd = 'L';
                        break;
                    }

                    case 'H':
                    {
                        x = ReadNumber(d, ref i);
                        PointF to = new PointF(rel ? cur.X + x : x, cur.Y);
                        path.AddLine(cur, to);
                        cur = to;
                        prevCmd = 'H';
                        break;
                    }

                    case 'V':
                    {
                        y = ReadNumber(d, ref i);
                        PointF to = new PointF(cur.X, rel ? cur.Y + y : y);
                        path.AddLine(cur, to);
                        cur = to;
                        prevCmd = 'V';
                        break;
                    }

                    case 'C':
                    {
                        x1(ref i, d, cur, rel, out a, out b);
                        x1(ref i, d, cur, rel, out c, out e);
                        x = ReadNumber(d, ref i);
                        y = ReadNumber(d, ref i);
                        PointF p1 = new PointF(a, b);
                        PointF p2 = new PointF(c, e);
                        PointF to = rel ? new PointF(cur.X + x, cur.Y + y) : new PointF(x, y);
                        path.AddBezier(cur, p1, p2, to);
                        cur = to;
                        lastCubic = p2;
                        prevCmd = 'C';
                        break;
                    }

                    case 'S':
                    {
                        // x2 y2 x y  (first control point is the reflection)
                        x1(ref i, d, cur, rel, out c, out e);
                        x = ReadNumber(d, ref i);
                        y = ReadNumber(d, ref i);

                        PointF p1 = (prevCmd == 'C' || prevCmd == 'S')
                            ? new PointF(2f * cur.X - lastCubic.X, 2f * cur.Y - lastCubic.Y)
                            : cur;
                        PointF p2 = new PointF(c, e);
                        PointF to = rel ? new PointF(cur.X + x, cur.Y + y) : new PointF(x, y);

                        path.AddBezier(cur, p1, p2, to);
                        cur = to;
                        lastCubic = p2;
                        prevCmd = 'S';
                        break;
                    }

                    case 'Q':
                    {
                        x1(ref i, d, cur, rel, out a, out b);
                        x = ReadNumber(d, ref i);
                        y = ReadNumber(d, ref i);
                        PointF q = new PointF(a, b);
                        PointF to = rel ? new PointF(cur.X + x, cur.Y + y) : new PointF(x, y);
                        path.AddBezier(cur, q, q, to);
                        cur = to;
                        lastQuad = q;
                        prevCmd = 'Q';
                        break;
                    }

                    case 'T':
                    {
                        x = ReadNumber(d, ref i);
                        y = ReadNumber(d, ref i);
                        PointF q;
                        if (prevCmd == 'Q' || prevCmd == 'T')
                            q = new PointF(2f * cur.X - lastQuad.X, 2f * cur.Y - lastQuad.Y);
                        else
                            q = cur;
                        PointF to = rel ? new PointF(cur.X + x, cur.Y + y) : new PointF(x, y);
                        path.AddBezier(cur, q, q, to);
                        cur = to;
                        lastQuad = q;
                        prevCmd = 'T';
                        break;
                    }

                    case 'A':
                    {
                        float rx = ReadNumber(d, ref i);
                        float ry = ReadNumber(d, ref i);
                        float rot = ReadNumber(d, ref i);
                        bool large = ReadNumber(d, ref i) != 0f;
                        bool sweep = ReadNumber(d, ref i) != 0f;
                        x = ReadNumber(d, ref i);
                        y = ReadNumber(d, ref i);
                        PointF to = rel ? new PointF(cur.X + x, cur.Y + y) : new PointF(x, y);
                        ArcTo(path, cur, to, rx, ry, rot, large, sweep);
                        cur = to;
                        prevCmd = 'A';
                        break;
                    }

                    case 'Z':
                    {
                        path.CloseFigure();
                        cur = figStart;
                        prevCmd = 'Z';
                        break;
                    }

                    default:
                        throw new FormatException("Unsupported SVG path command: " + cmd);
                }
            }

            return path;
        }

        private static void x1(ref int i, string d, PointF cur, bool rel, out float x, out float y)
        {
            x = ReadNumber(d, ref i);
            y = ReadNumber(d, ref i);
            if (rel) { x += cur.X; y += cur.Y; }
        }

        private static void SkipSeparators(string s, ref int i)
        {
            while (i < s.Length && (char.IsWhiteSpace(s[i]) || s[i] == ',')) i++;
        }

        private static float ReadNumber(string s, ref int i)
        {
            SkipSeparators(s, ref i);
            int start = i;

            if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
            while (i < s.Length && char.IsDigit(s[i])) i++;
            if (i < s.Length && s[i] == '.')
            {
                i++;
                while (i < s.Length && char.IsDigit(s[i])) i++;
            }
            if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
            {
                int save = i;
                i++;
                if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
                if (i < s.Length && char.IsDigit(s[i]))
                {
                    while (i < s.Length && char.IsDigit(s[i])) i++;
                }
                else
                {
                    i = save;
                }
            }

            if (i == start) throw new FormatException("Expected a number at offset " + start);
            return float.Parse(s.Substring(start, i - start), CultureInfo.InvariantCulture);
        }

        /// <summary>Endpoint-parameterised arc, sampled into short segments.</summary>
        private static void ArcTo(GraphicsPath path, PointF from, PointF to,
                                  float rx, float ry, float rotationDeg,
                                  bool largeArc, bool sweep)
        {
            if (rx == 0f || ry == 0f) { path.AddLine(from, to); return; }

            double phi = rotationDeg * Math.PI / 180.0;
            double cosPhi = Math.Cos(phi), sinPhi = Math.Sin(phi);

            double dx2 = (from.X - to.X) / 2.0;
            double dy2 = (from.Y - to.Y) / 2.0;
            double x1p = cosPhi * dx2 + sinPhi * dy2;
            double y1p = -sinPhi * dx2 + cosPhi * dy2;

            double rx2 = rx * rx, ry2 = ry * ry;
            double lambda = (x1p * x1p) / rx2 + (y1p * y1p) / ry2;
            if (lambda > 1.0)
            {
                double s = Math.Sqrt(lambda);
                rx = (float)(rx * s);
                ry = (float)(ry * s);
                rx2 = rx * rx;
                ry2 = ry * ry;
            }

            double sign = largeArc == sweep ? -1.0 : 1.0;
            double num = rx2 * ry2 - rx2 * y1p * y1p - ry2 * x1p * x1p;
            double den = rx2 * y1p * y1p + ry2 * x1p * x1p;
            double coef = den == 0 ? 0 : sign * Math.Sqrt(Math.Max(0.0, num / den));

            double cxp = coef * (rx * y1p / ry);
            double cyp = coef * (-ry * x1p / rx);

            double cx = cosPhi * cxp - sinPhi * cyp + (from.X + to.X) / 2.0;
            double cy = sinPhi * cxp + cosPhi * cyp + (from.Y + to.Y) / 2.0;

            double theta1 = Angle(1, 0, (x1p - cxp) / rx, (y1p - cyp) / ry);
            double theta2 = Angle((x1p - cxp) / rx, (y1p - cyp) / ry,
                                  (-x1p - cxp) / rx, (-y1p - cyp) / ry);

            double delta = theta2;
            if (!sweep && delta > 0) delta -= 2 * Math.PI;
            else if (sweep && delta < 0) delta += 2 * Math.PI;

            int steps = Math.Max(8, (int)(Math.Abs(delta) / (Math.PI / 32)));
            PointF prev = from;

            for (int s = 1; s <= steps; s++)
            {
                double t = theta1 + delta * s / steps;
                double px = rx * Math.Cos(t);
                double py = ry * Math.Sin(t);
                float x = (float)(cosPhi * px - sinPhi * py + cx);
                float y = (float)(sinPhi * px + cosPhi * py + cy);
                PointF p = new PointF(x, y);
                path.AddLine(prev, p);
                prev = p;
            }
        }

        private static double Angle(double ux, double uy, double vx, double vy)
        {
            double dot = ux * vx + uy * vy;
            double len = Math.Sqrt((ux * ux + uy * uy) * (vx * vx + vy * vy));
            double a = Math.Acos(Math.Max(-1.0, Math.Min(1.0, dot / len)));
            if (ux * vy - uy * vx < 0) a = -a;
            return a;
        }
    }
}
