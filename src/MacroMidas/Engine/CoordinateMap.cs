using MacroMidas.Models;

namespace MacroMidas.Engine;

// Converte as coordenadas de todos os passos de um fluxo entre pixels e proporcional.
public static class CoordinateMap
{
    public static void Convert(IEnumerable<FlowStep> steps, Func<int, int, (int X, int Y)> point, Func<int, int, (int X, int Y)> delta)
    {
        foreach (var step in Flow.Walk(steps))
        {
            switch (step)
            {
                case MoveStep m when !m.Relative:
                    (m.X, m.Y) = point(m.X, m.Y);
                    break;
                case MoveStep m:
                    (m.X, m.Y) = delta(m.X, m.Y);
                    break;
                case ClickStep c when c.UsePosition:
                    (c.X, c.Y) = point(c.X, c.Y);
                    break;
                case MouseButtonStep b when b.UsePosition:
                    (b.X, b.Y) = point(b.X, b.Y);
                    break;
                case ScrollStep s when s.UsePosition:
                    (s.X, s.Y) = point(s.X, s.Y);
                    break;
                case DragStep d:
                    (d.FromX, d.FromY) = point(d.FromX, d.FromY);
                    (d.ToX, d.ToY) = point(d.ToX, d.ToY);
                    d.Path = MapPath(d.Path, point);
                    break;
                case MousePathStep p:
                    p.Path = MapPath(p.Path, point);
                    break;
                case MouseLookStep l:
                    l.Path = MapPath(l.Path, delta);
                    break;
                case ConditionalStepBase cs:
                    MapCondition(cs.Condition, point, delta);
                    break;
            }
        }
    }

    private static void MapCondition(Condition c, Func<int, int, (int X, int Y)> point, Func<int, int, (int X, int Y)> delta)
    {
        if (c.UsesPixel || c.UsesRegion)
        {
            (c.X, c.Y) = point(c.X, c.Y);
            if (c.UsesRegion) (c.Width, c.Height) = delta(c.Width, c.Height);
        }
    }

    // Caminho [espera, x, y, ...]: converte só os pares (x, y), mantendo as esperas.
    private static List<int> MapPath(List<int> path, Func<int, int, (int X, int Y)> map)
    {
        if (path.Count < 3) return path;
        var result = new List<int>(path.Count);
        for (var i = 0; i + 2 < path.Count; i += 3)
        {
            var (x, y) = map(path[i + 1], path[i + 2]);
            result.Add(path[i]);
            result.Add(x);
            result.Add(y);
        }
        return result;
    }
}
