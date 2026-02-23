using SqlToER.Model;

namespace SqlToER.Service
{
    /// <summary>
    /// 弹簧力优化布局 — 忠实移植自 sql_to_ER/js/layout/arrangeLayout.js
    /// </summary>
    public static class ArrangeLayoutService
    {
        public static Dictionary<string, (double X, double Y)> Optimize(
            ErDocument erDoc,
            Dictionary<string, (double X, double Y)> inputCoords,
            double entityW, double entityH,
            double attrW,
            double relW, double relH)
        {
            if (erDoc.Entities.Count == 0) return inputCoords;

            double entityR = LayoutUtils.NodeRadius(entityW, entityH);
            double diamondR = LayoutUtils.NodeRadius(relW * 2, relH * 2);
            double attrR = attrW / 2.0;

            // ---- 建立拓扑 ----
            var entityPositions = new Dictionary<string, (double X, double Y)>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var e in erDoc.Entities)
                if (inputCoords.TryGetValue(e.Name, out var p))
                    entityPositions[e.Name] = p;

            var relConnections = new List<(string DId, string E1, string E2)>();
            for (int i = 0; i < erDoc.Relationships.Count; i++)
            {
                var rel = erDoc.Relationships[i];
                string dId = $"◇{rel.Name}_{i}";
                relConnections.Add((dId, rel.Entity1, rel.Entity2));
            }

            var attrsByEntity = erDoc.Attributes
                .GroupBy(a => a.EntityName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            // ---- 轨道半径（对齐 JS L102-120：maxSatelliteRadius 含菱形）----
            var baseRing = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var systemRadius = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            double maxSat = Math.Max(attrR, diamondR); // JS L104: max(所有卫星半径)

            foreach (var e in erDoc.Entities)
            {
                int nAttrs = attrsByEntity.GetValueOrDefault(e.Name, []).Count;
                int nRels = relConnections.Count(r =>
                    r.E1.Equals(e.Name, StringComparison.OrdinalIgnoreCase) ||
                    r.E2.Equals(e.Name, StringComparison.OrdinalIgnoreCase));
                int totalSatellites = nAttrs + nRels; // JS L111: satellites.length

                // JS L108: entityRadius + maxSatelliteRadius + 25px(≈0.35")
                double ringR = entityR + maxSat + 0.4;
                if (totalSatellites > 1)
                {
                    // JS L112-114: requiredArcLength = maxSatelliteRadius*2 + 18
                    double requiredArc = maxSat * 2 + 0.25;
                    double totalCirc = totalSatellites * requiredArc;
                    double requiredR = totalCirc / (2 * Math.PI);
                    ringR = Math.Max(ringR, requiredR);
                }
                baseRing[e.Name] = ringR;
                systemRadius[e.Name] = ringR + maxSat; // JS L119
            }

            // ---- ① 弹簧迭代 ----
            // safeGap 按实体数缩放（20表→1.1", 50表→1.5" capped）
            double safeGap = Math.Min(1.5, 0.7 + erDoc.Entities.Count * 0.02);
            int springIter = Math.Min(900, 300 + erDoc.Entities.Count * 30);

            for (int iter = 0; iter < springIter; iter++)
            {
                double maxMove = 0;

                foreach (var (dId, e1, e2) in relConnections)
                {
                    if (!entityPositions.TryGetValue(e1, out var posA)) continue;
                    if (!entityPositions.TryGetValue(e2, out var posB)) continue;

                    double dx = posB.X - posA.X, dy = posB.Y - posA.Y;
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    if (dist < 0.01) dist = 0.01;

                    double rA = systemRadius.GetValueOrDefault(e1, 1.5);
                    double rB = systemRadius.GetValueOrDefault(e2, 1.5);
                    double desired = rA + rB + safeGap;

                    double diff = desired - dist;
                    if (Math.Abs(diff) < 0.02) continue;

                    double nx = dx / dist, ny = dy / dist;
                    double move = (diff * 0.2) / 2;

                    entityPositions[e1] = (posA.X - nx * move, posA.Y - ny * move);
                    entityPositions[e2] = (posB.X + nx * move, posB.Y + ny * move);
                    maxMove = Math.Max(maxMove, Math.Abs(move));
                }

                var eIds = entityPositions.Keys.ToList();
                for (int i = 0; i < eIds.Count; i++)
                {
                    for (int j = i + 1; j < eIds.Count; j++)
                    {
                        var posA = entityPositions[eIds[i]];
                        var posB = entityPositions[eIds[j]];
                        double dx = posB.X - posA.X, dy = posB.Y - posA.Y;
                        double dist = Math.Sqrt(dx * dx + dy * dy);
                        if (dist < 0.01) dist = 0.01;

                        double rA = systemRadius.GetValueOrDefault(eIds[i], 1.5);
                        double rB = systemRadius.GetValueOrDefault(eIds[j], 1.5);
                        double minDist = rA + rB + safeGap;

                        if (dist < minDist)
                        {
                            double overlap = minDist - dist;
                            double nx = dx / dist, ny = dy / dist;
                            double move = overlap * 0.35;

                            entityPositions[eIds[i]] = (posA.X - nx * move, posA.Y - ny * move);
                            entityPositions[eIds[j]] = (posB.X + nx * move, posB.Y + ny * move);
                            maxMove = Math.Max(maxMove, move);
                        }
                    }
                }

                if (maxMove < 0.005) break;
            }

            // ---- ② 间距保障 ×3 ----
            for (int pass = 0; pass < 3; pass++)
            {
                foreach (var (dId, e1, e2) in relConnections)
                {
                    if (!entityPositions.TryGetValue(e1, out var posA)) continue;
                    if (!entityPositions.TryGetValue(e2, out var posB)) continue;

                    double dx = posB.X - posA.X, dy = posB.Y - posA.Y;
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    if (dist < 0.01) dist = 0.01;

                    double minHalf = Math.Max(
                        baseRing.GetValueOrDefault(e1, 1.0) + diamondR + 0.2,
                        baseRing.GetValueOrDefault(e2, 1.0) + diamondR + 0.2);
                    double required = minHalf * 2;
                    if (dist >= required) continue;

                    double missing = required - dist;
                    double nx = dx / dist, ny = dy / dist;
                    entityPositions[e1] = (posA.X - nx * missing / 2, posA.Y - ny * missing / 2);
                    entityPositions[e2] = (posB.X + nx * missing / 2, posB.Y + ny * missing / 2);
                }
            }

            // ---- ③ 卫星轨道分配（只分配属性）----
            var targets = new Dictionary<string, (double X, double Y)>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in entityPositions)
                targets[kv.Key] = kv.Value;

            var entityOrbitRadius = new Dictionary<string, double>(baseRing, StringComparer.OrdinalIgnoreCase);

            foreach (var e in erDoc.Entities)
            {
                if (!entityPositions.TryGetValue(e.Name, out var center)) continue;
                var attrs = attrsByEntity.GetValueOrDefault(e.Name, []);
                if (attrs.Count == 0) continue;

                double ringR = baseRing.GetValueOrDefault(e.Name, 1.0);

                var avoidAngles = new List<double>();
                foreach (var (dId2, e1, e2) in relConnections)
                {
                    if (!e1.Equals(e.Name, StringComparison.OrdinalIgnoreCase) &&
                        !e2.Equals(e.Name, StringComparison.OrdinalIgnoreCase)) continue;
                    string other = e1.Equals(e.Name, StringComparison.OrdinalIgnoreCase) ? e2 : e1;
                    if (entityPositions.TryGetValue(other, out var oPos))
                        avoidAngles.Add(LayoutUtils.NormalizeAngle(
                            Math.Atan2(oPos.Y - center.Y, oPos.X - center.X)));
                }

                double halfGap = 0.175;
                var segments = new List<(double Start, double End)>();

                if (avoidAngles.Count == 0)
                {
                    segments.Add((0, Math.PI * 2));
                }
                else
                {
                    var sorted = avoidAngles.OrderBy(a => a).ToList();
                    for (int i = 0; i < sorted.Count; i++)
                    {
                        double curr = sorted[i];
                        double next = i == sorted.Count - 1
                            ? sorted[0] + Math.PI * 2 : sorted[i + 1];
                        double s = curr + halfGap, en = next - halfGap;
                        if (en > s) segments.Add((s, en));
                    }
                }

                double totalFree = segments.Sum(s => s.End - s.Start);
                if (totalFree <= 0) segments = [(0, Math.PI * 2)];
                totalFree = segments.Sum(s => s.End - s.Start);

                int totalCount = attrs.Count;
                var segCounts = segments.Select(s =>
                    Math.Max(0, (int)Math.Round(totalCount * (s.End - s.Start) / totalFree))
                ).ToList();

                int allocated = segCounts.Sum();
                while (allocated < totalCount)
                {
                    int maxIdx = 0; double maxLen = -1;
                    for (int i = 0; i < segments.Count; i++)
                    {
                        double len = segments[i].End - segments[i].Start;
                        if (len > maxLen) { maxLen = len; maxIdx = i; }
                    }
                    segCounts[maxIdx]++;
                    allocated++;
                }
                while (allocated > totalCount)
                {
                    for (int i = segCounts.Count - 1; i >= 0; i--)
                    {
                        if (segCounts[i] > 0) { segCounts[i]--; allocated--; break; }
                    }
                }

                var sortedAttrs = attrs.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
                int nodeIdx = 0;
                for (int si = 0; si < segments.Count; si++)
                {
                    int count = segCounts[si];
                    if (count == 0) continue;
                    double step = (segments[si].End - segments[si].Start) / count;
                    for (int i = 0; i < count && nodeIdx < sortedAttrs.Count; i++)
                    {
                        double angle = LayoutUtils.NormalizeAngle(segments[si].Start + step * (i + 0.5));
                        string key = $"{e.Name}.{sortedAttrs[nodeIdx].Name}";
                        targets[key] = (
                            center.X + Math.Cos(angle) * ringR,
                            center.Y + Math.Sin(angle) * ringR
                        );
                        nodeIdx++;
                    }
                }
            }

            // ---- ④ 菱形中点定位 ----
            var relAnchors = new Dictionary<string, (double X, double Y)>(StringComparer.OrdinalIgnoreCase);
            var relRadii = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            foreach (var (dId, e1, e2) in relConnections)
            {
                if (!entityPositions.TryGetValue(e1, out var pA)) continue;
                if (!entityPositions.TryGetValue(e2, out var pB)) continue;
                var mid = ((pA.X + pB.X) / 2, (pA.Y + pB.Y) / 2);
                targets[dId] = mid;
                relAnchors[dId] = mid;
                relRadii[dId] = diamondR;
            }

            // ---- ⑤ 多菱形偏移 ----
            var grouped = relConnections
                .GroupBy(r =>
                {
                    string a = r.E1, b = r.E2;
                    return string.Compare(a, b, StringComparison.OrdinalIgnoreCase) < 0
                        ? $"{a}__{b}" : $"{b}__{a}";
                })
                .Where(g => g.Count() > 1);

            foreach (var group in grouped)
            {
                var list = group.ToList();
                var first = list[0];
                if (!entityPositions.TryGetValue(first.E1, out var posA)) continue;
                if (!entityPositions.TryGetValue(first.E2, out var posB)) continue;

                double dx = posB.X - posA.X, dy = posB.Y - posA.Y;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist < 0.01) dist = 0.01;
                double px = -dy / dist, py = dx / dist;

                var basePos = targets.GetValueOrDefault(first.DId, ((posA.X + posB.X) / 2, (posA.Y + posB.Y) / 2));
                double offsetStep = diamondR * 2 + 0.25;

                var sortedRels = list.OrderBy(r => r.DId, StringComparer.OrdinalIgnoreCase).ToList();
                double mid = (sortedRels.Count - 1) / 2.0;
                for (int idx = 0; idx < sortedRels.Count; idx++)
                {
                    double offsetIndex = idx - mid;
                    var newPos = (basePos.Item1 + px * offsetIndex * offsetStep,
                                  basePos.Item2 + py * offsetIndex * offsetStep);
                    targets[sortedRels[idx].DId] = newPos;
                    relAnchors[sortedRels[idx].DId] = newPos;
                }
            }

            // ---- ⑥ 菱形防碰撞 80 轮 ----
            var entityCollisionRadius = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in erDoc.Entities)
            {
                double ring = entityOrbitRadius.GetValueOrDefault(e.Name, baseRing.GetValueOrDefault(e.Name, 0.8));
                entityCollisionRadius[e.Name] = ring + 0.3;
            }

            var relPositions = relAnchors.ToDictionary(
                kv => kv.Key, kv => targets.GetValueOrDefault(kv.Key, kv.Value),
                StringComparer.OrdinalIgnoreCase);
            var relIds = relPositions.Keys.ToList();

            for (int iter = 0; iter < 80; iter++)
            {
                for (int i = 0; i < relIds.Count; i++)
                {
                    for (int j = i + 1; j < relIds.Count; j++)
                    {
                        var posA2 = relPositions[relIds[i]];
                        var posB2 = relPositions[relIds[j]];
                        double dx2 = posB2.X - posA2.X, dy2 = posB2.Y - posA2.Y;
                        double d2 = Math.Sqrt(dx2 * dx2 + dy2 * dy2);
                        if (d2 < 0.001) d2 = 0.001;
                        double rA2 = relRadii.GetValueOrDefault(relIds[i], diamondR);
                        double rB2 = relRadii.GetValueOrDefault(relIds[j], diamondR);
                        double minD = rA2 + rB2 + 0.2;
                        if (d2 < minD)
                        {
                            double push = (minD - d2) / 2;
                            double nx = dx2 / d2, ny = dy2 / d2;
                            relPositions[relIds[i]] = (posA2.X - nx * push, posA2.Y - ny * push);
                            relPositions[relIds[j]] = (posB2.X + nx * push, posB2.Y + ny * push);
                        }
                    }
                }

                foreach (var rid in relIds)
                {
                    var pos = relPositions[rid];
                    foreach (var conn in relConnections)
                    {
                        if (!string.Equals(conn.DId, rid, StringComparison.OrdinalIgnoreCase)) continue;
                        foreach (var eName in new[] { conn.E1, conn.E2 })
                        {
                            if (string.IsNullOrEmpty(eName)) continue;
                            if (!entityPositions.TryGetValue(eName, out var center)) continue;
                            double limit = entityCollisionRadius.GetValueOrDefault(eName, 1.0);
                            double dx3 = pos.X - center.X, dy3 = pos.Y - center.Y;
                            double d3 = Math.Sqrt(dx3 * dx3 + dy3 * dy3);
                            if (d3 < 0.01) d3 = 0.01;
                            if (d3 < limit)
                            {
                                double push = limit - d3;
                                pos = (pos.X + (dx3 / d3) * push, pos.Y + (dy3 / d3) * push);
                            }
                        }
                    }
                    relPositions[rid] = pos;
                }

                foreach (var rid in relIds)
                {
                    if (!relAnchors.TryGetValue(rid, out var anchor)) continue;
                    var pos = relPositions[rid];
                    relPositions[rid] = (pos.X * 0.85 + anchor.X * 0.15, pos.Y * 0.85 + anchor.Y * 0.15);
                }
            }

            foreach (var kv in relPositions)
                targets[kv.Key] = kv.Value;

            // ---- ⑦ 全局分离 400 轮 ----
            var allIds = targets.Keys.ToList();
            var allRadii = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in allIds)
            {
                if (erDoc.Entities.Any(e => e.Name.Equals(id, StringComparison.OrdinalIgnoreCase)))
                    allRadii[id] = entityR;
                else if (id.StartsWith("◇"))
                    allRadii[id] = diamondR;
                else
                    allRadii[id] = attrR;
            }

            for (int iter = 0; iter < 400; iter++)
            {
                double maxMove = 0;
                for (int i = 0; i < allIds.Count; i++)
                {
                    for (int j = i + 1; j < allIds.Count; j++)
                    {
                        var pa = targets[allIds[i]]; var pb = targets[allIds[j]];
                        double dx = pb.X - pa.X, dy = pb.Y - pa.Y;
                        double dist = Math.Sqrt(dx * dx + dy * dy);
                        if (dist < 0.001) dist = 0.001;
                        double minDist = allRadii.GetValueOrDefault(allIds[i], 0.5)
                                       + allRadii.GetValueOrDefault(allIds[j], 0.5) + 0.12;
                        if (dist < minDist)
                        {
                            double push = (minDist - dist) / 2;
                            double nx = dx / dist, ny = dy / dist;
                            targets[allIds[i]] = (pa.X - nx * push, pa.Y - ny * push);
                            targets[allIds[j]] = (pb.X + nx * push, pb.Y + ny * push);
                            maxMove = Math.Max(maxMove, push);
                        }
                    }
                }
                if (maxMove < 0.005) break;
            }

            return targets;
        }
    }
}
