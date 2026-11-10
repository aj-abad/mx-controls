using System.Globalization;

namespace MxControls.Svg;

public readonly record struct PathPoint(double X, double Y);

/// <summary>One cubic Bézier piece of a figure, from where the last one ended.</summary>
public readonly record struct CubicSegment(PathPoint Control1, PathPoint Control2, PathPoint End);

/// <summary>A run of connected segments from <see cref="Start"/>; lines and arcs come as cubics too.</summary>
public sealed record PathFigure(PathPoint Start, IReadOnlyList<CubicSegment> Segments, bool Closed);

/// <summary>
/// Reads SVG path data, every command included, into figures of cubic Béziers, which any 2D API can draw.
/// Rose icons arrive this way, so the overlay can draw them without an icon font.
/// </summary>
public static class SvgPath
{
    public static bool TryParse(string data, out IReadOnlyList<PathFigure> figures)
    {
        try
        {
            figures = Parse(data);
            return true;
        }
        catch (FormatException)
        {
            figures = [];
            return false;
        }
    }

    /// <exception cref="FormatException">The data isn't valid path data.</exception>
    public static IReadOnlyList<PathFigure> Parse(string data)
    {
        var figures = new List<PathFigure>();
        var reader = new Reader(data);
        var segments = new List<CubicSegment>();
        PathPoint start = default, current = default, lastControl = default;
        char previous = ' ';
        bool open = false;

        void Finish(bool closed)
        {
            if (open && (segments.Count > 0 || closed))
                figures.Add(new PathFigure(start, [.. segments], closed));
            segments.Clear();
            open = false;
        }

        void Begin(PathPoint at)
        {
            Finish(false);
            start = current = at;
            open = true;
        }

        void Line(PathPoint to)
        {
            if (!open)
                Begin(current);
            segments.Add(new CubicSegment(Lerp(current, to, 1 / 3.0), Lerp(current, to, 2 / 3.0), to));
            current = to;
        }

        void Cubic(PathPoint c1, PathPoint c2, PathPoint to)
        {
            if (!open)
                Begin(current);
            segments.Add(new CubicSegment(c1, c2, to));
            lastControl = c2;
            current = to;
        }

        void Quadratic(PathPoint control, PathPoint to)
        {
            Cubic(Lerp(current, control, 2 / 3.0), Lerp(to, control, 2 / 3.0), to);
            lastControl = control;
        }

        char command = ' ';
        while (reader.SkipSeparators())
        {
            if (reader.PeekCommand() is { } next)
            {
                command = next;
                reader.Advance();
            }
            else if (command is ' ' or 'Z' or 'z')
                throw new FormatException("Path data must start with a command.");

            bool relative = char.IsLower(command);
            PathPoint Point(PathPoint from) => relative
                ? new(from.X + reader.Number(), from.Y + reader.Number())
                : new(reader.Number(), reader.Number());
            PathPoint Reflected(char[] after) => after.Contains(previous)
                ? new(2 * current.X - lastControl.X, 2 * current.Y - lastControl.Y)
                : current;

            switch (char.ToUpperInvariant(command))
            {
                case 'M':
                    Begin(Point(current));
                    // More pairs after a move are lines.
                    command = relative ? 'l' : 'L';
                    break;
                case 'L':
                    Line(Point(current));
                    break;
                case 'H':
                    Line(new(relative ? current.X + reader.Number() : reader.Number(), current.Y));
                    break;
                case 'V':
                    Line(new(current.X, relative ? current.Y + reader.Number() : reader.Number()));
                    break;
                case 'C':
                {
                    var from = current;
                    var c1 = Point(from);
                    var c2 = Point(from);
                    Cubic(c1, c2, Point(from));
                    break;
                }
                case 'S':
                {
                    var from = current;
                    var c1 = Reflected(['C', 'c', 'S', 's']);
                    var c2 = Point(from);
                    Cubic(c1, c2, Point(from));
                    break;
                }
                case 'Q':
                {
                    var from = current;
                    var control = Point(from);
                    Quadratic(control, Point(from));
                    break;
                }
                case 'T':
                {
                    var control = Reflected(['Q', 'q', 'T', 't']);
                    Quadratic(control, Point(current));
                    break;
                }
                case 'A':
                {
                    double rx = Math.Abs(reader.Number()), ry = Math.Abs(reader.Number()), rotation = reader.Number();
                    bool large = reader.Flag(), sweep = reader.Flag();
                    var to = Point(current);
                    foreach (var segment in Arc(current, to, rx, ry, rotation, large, sweep))
                        Cubic(segment.Control1, segment.Control2, segment.End);
                    current = to;
                    break;
                }
                case 'Z':
                    Finish(true);
                    current = start;
                    break;
                default:
                    throw new FormatException($"Unknown path command '{command}'.");
            }
            previous = command;
        }
        Finish(false);
        return figures;
    }

    private static PathPoint Lerp(PathPoint a, PathPoint b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    /// <summary>An elliptical arc as cubics of at most a quarter turn each, by the SVG spec's endpoint conversion.</summary>
    private static IEnumerable<CubicSegment> Arc(PathPoint from, PathPoint to, double rx, double ry, double degrees, bool large, bool sweep)
    {
        if (from == to)
            yield break;
        if (rx == 0 || ry == 0)
        {
            yield return new CubicSegment(Lerp(from, to, 1 / 3.0), Lerp(from, to, 2 / 3.0), to);
            yield break;
        }
        double phi = degrees * Math.PI / 180, cos = Math.Cos(phi), sin = Math.Sin(phi);
        double dx = (from.X - to.X) / 2, dy = (from.Y - to.Y) / 2;
        double x1 = cos * dx + sin * dy, y1 = -sin * dx + cos * dy;
        double lambda = x1 * x1 / (rx * rx) + y1 * y1 / (ry * ry);
        if (lambda > 1)
        {
            rx *= Math.Sqrt(lambda);
            ry *= Math.Sqrt(lambda);
        }
        double numerator = rx * rx * ry * ry - rx * rx * y1 * y1 - ry * ry * x1 * x1;
        double denominator = rx * rx * y1 * y1 + ry * ry * x1 * x1;
        double factor = Math.Sqrt(Math.Max(0, numerator / denominator)) * (large == sweep ? -1 : 1);
        double cx1 = factor * rx * y1 / ry, cy1 = -factor * ry * x1 / rx;
        double cx = cos * cx1 - sin * cy1 + (from.X + to.X) / 2, cy = sin * cx1 + cos * cy1 + (from.Y + to.Y) / 2;

        double start = Math.Atan2((y1 - cy1) / ry, (x1 - cx1) / rx);
        double end = Math.Atan2((-y1 - cy1) / ry, (-x1 - cx1) / rx);
        double delta = end - start;
        if (sweep && delta < 0)
            delta += 2 * Math.PI;
        else if (!sweep && delta > 0)
            delta -= 2 * Math.PI;

        int pieces = Math.Max(1, (int)Math.Ceiling(Math.Abs(delta) / (Math.PI / 2) - 1e-9));
        double step = delta / pieces, k = 4 / 3.0 * Math.Tan(step / 4);
        PathPoint On(double angle) => new(
            cx + rx * Math.Cos(angle) * cos - ry * Math.Sin(angle) * sin,
            cy + rx * Math.Cos(angle) * sin + ry * Math.Sin(angle) * cos);
        PathPoint Tangent(double angle) => new(
            -rx * Math.Sin(angle) * cos - ry * Math.Cos(angle) * sin,
            -rx * Math.Sin(angle) * sin + ry * Math.Cos(angle) * cos);

        double a = start;
        var p0 = from;
        for (int i = 0; i < pieces; i++)
        {
            double b = a + step;
            var p3 = i == pieces - 1 ? to : On(b);
            var t0 = Tangent(a);
            var t1 = Tangent(b);
            yield return new CubicSegment(
                new(p0.X + k * t0.X, p0.Y + k * t0.Y), new(p3.X - k * t1.X, p3.Y - k * t1.Y), p3);
            p0 = p3;
            a = b;
        }
    }

    private sealed class Reader(string text)
    {
        private readonly string _text = text;
        private int _at;

        /// <summary>Skips spaces and commas; false at the end.</summary>
        public bool SkipSeparators()
        {
            while (_at < _text.Length && (char.IsWhiteSpace(_text[_at]) || _text[_at] == ','))
                _at++;
            return _at < _text.Length;
        }

        public char? PeekCommand()
        {
            char c = _text[_at];
            return char.IsLetter(c) && c is not ('e' or 'E') ? c : null;
        }

        public void Advance() => _at++;

        /// <summary>An arc flag, which may be written without a separator before the next value.</summary>
        public bool Flag()
        {
            SkipSeparators();
            if (_at >= _text.Length || _text[_at] is not ('0' or '1'))
                throw new FormatException("Expected an arc flag.");
            return _text[_at++] == '1';
        }

        public double Number()
        {
            SkipSeparators();
            int begin = _at;
            if (_at < _text.Length && _text[_at] is '+' or '-')
                _at++;
            bool dot = false, digits = false;
            while (_at < _text.Length)
            {
                char c = _text[_at];
                if (char.IsAsciiDigit(c))
                    digits = true;
                else if (c == '.' && !dot)
                    dot = true;
                else
                    break;
                _at++;
            }
            if (digits && _at < _text.Length && _text[_at] is 'e' or 'E')
            {
                _at++;
                if (_at < _text.Length && _text[_at] is '+' or '-')
                    _at++;
                while (_at < _text.Length && char.IsAsciiDigit(_text[_at]))
                    _at++;
            }
            if (!digits || !double.TryParse(_text.AsSpan(begin, _at - begin), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                throw new FormatException($"Expected a number at {begin}.");
            return value;
        }
    }
}
