using SqlToER.Model;

namespace SqlToER.Service
{
    /// <summary>
    /// 断连分量展开 — 移植自 sql_to_ER/js/layout/componentSpread.js
    /// 将互不相连的图分量环形分布在中心周围
    /// </summary>
    public static class ComponentSpreadService
    {
        public static Dictionary<string, (double X, double Y)> Spread(
            ErDocument erDoc,
            Dictionary<string, (double X, double Y)> inputCoords)
        {
            if (inputCoords.Count < 2) return inputCoords;

            // ---- 建立邻接表 ----
            var adj = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < erDoc.Relationships.Count; i++)
            {
                var rel = erDoc.Relationships[i];
                string dId = $"◇{rel.Name}_{i}";
                void Link(string a, string b)
                {
                    if (!adj.ContainsKey(a)) adj[a] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (!adj.ContainsKey(b)) adj[b] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    adj[a].Add(b); adj[b].Add(a);
                }
                Link(rel.Entity1, dId);
                Link(dId, rel.Entity2);
            }
            // 属性→实体
            foreach (var attr in erDoc.Attributes)
            {
                string key = $"{attr.EntityName}.{attr.Name}";
                if (!adj.ContainsKey(key)) adj[key] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!adj.ContainsKey(attr.EntityName)) adj[attr.EntityName] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                adj[key].Add(attr.EntityName);
                adj[attr.EntityName].Add(key);
            }

            // ---- DFS 找连通分量 ----
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var components = new List<List<string>>();
            foreach (var id in inputCoords.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                if (visited.Contains(id)) continue;
                var stack = new Stack<string>();
                stack.Push(id); visited.Add(id);
                var comp = new List<string>();
                while (stack.Count > 0)
                {
                    var cur = stack.Pop();
                    if (inputCoords.ContainsKey(cur)) comp.Add(cur);
                    foreach (var nb in adj.GetValueOrDefault(cur, []))
                    {
                        if (!visited.Contains(nb))
                        { visited.Add(nb); stack.Push(nb); }
                    }
                }
                if (comp.Count > 0) components.Add(comp);
            }

            if (components.Count < 2) return inputCoords;

            // ---- 计算每个分量的包围盒和中心 ----
            var compMeta = components.Select(comp =>
            {
                double minX = double.MaxValue, maxX = double.MinValue;
                double minY = double.MaxValue, maxY = double.MinValue;
                double cx = 0, cy = 0;
                foreach (var id in comp)
                {
                    var (x, y) = inputCoords[id];
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                    cx += x; cy += y;
                }
                double width = Math.Max(0.5, maxX - minX);
                double height = Math.Max(0.5, maxY - minY);
                double radius = Math.Sqrt(width * width + height * height) / 2 + 0.6;
                return (Comp: comp, Radius: radius,
                    Center: (X: cx / comp.Count, Y: cy / comp.Count));
            }).ToList();

            // ---- 环形分布 ----
            double gap = 0.7;
            double totalSpan = compMeta.Sum(c => c.Radius * 2 + gap);
            double orbitRadius = Math.Min(
                Math.Max(Math.Max(totalSpan / (2 * Math.PI), compMeta.Max(c => c.Radius) + gap + 3), 4.0),
                8.0);

            double diagCx = 0, diagCy = 0;
            foreach (var p in inputCoords.Values) { diagCx += p.X; diagCy += p.Y; }
            diagCx /= inputCoords.Count; diagCy /= inputCoords.Count;

            var result = new Dictionary<string, (double X, double Y)>(
                inputCoords, StringComparer.OrdinalIgnoreCase);

            double angleCursor = -Math.PI / 2;
            foreach (var meta in compMeta)
            {
                double angleSpan = ((meta.Radius * 2 + gap) / totalSpan) * Math.PI * 2;
                double midAngle = angleCursor + angleSpan / 2;

                double targetCx = diagCx + orbitRadius * Math.Cos(midAngle);
                double targetCy = diagCy + orbitRadius * Math.Sin(midAngle);

                double rotAngle = midAngle + Math.PI / 2;
                double cosA = Math.Cos(rotAngle), sinA = Math.Sin(rotAngle);

                foreach (var id in meta.Comp)
                {
                    var (ox, oy) = inputCoords[id];
                    double rx = ox - meta.Center.X, ry = oy - meta.Center.Y;
                    double rotX = rx * cosA - ry * sinA;
                    double rotY = rx * sinA + ry * cosA;
                    result[id] = (targetCx + rotX, targetCy + rotY);
                }

                angleCursor += angleSpan;
            }

            return result;
        }
    }
}
