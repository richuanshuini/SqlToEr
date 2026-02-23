using SqlToER.Model;

namespace SqlToER.Service
{
    /// <summary>
    /// 属性轨道避让算法 — 多扇区等比分配
    /// 
    /// 核心思想：不要去检测属性是否重叠，而是直接在物理空间上
    /// 计算出绝对安全的轨道区间（避开关系连线），然后把属性填进去。
    /// </summary>
    public static class AttrOrbitService
    {
        /// <summary>
        /// 可用弧段
        /// </summary>
        private class ArcSegment
        {
            public double Start { get; set; }
            public double End { get; set; }
            public double Length => End - Start;
            public int Count { get; set; }
        }

        /// <summary>
        /// 为所有实体的属性计算绝对坐标
        /// </summary>
        /// <param name="erDoc">ER 文档</param>
        /// <param name="entityCoords">实体坐标（Visio 英寸）</param>
        /// <param name="diamondCoords">菱形坐标（Visio 英寸）</param>
        /// <param name="attrW">属性椭圆宽度</param>
        /// <param name="attrRadius">最小轨道半径</param>
        /// <returns>key="EntityName.AttrName" → (X, Y)</returns>
        public static Dictionary<string, (double X, double Y)> ArrangeAllAttributes(
            ErDocument erDoc,
            Dictionary<string, (double X, double Y)> entityCoords,
            Dictionary<string, (double X, double Y)> diamondCoords,
            double attrW, double attrRadius)
        {
            var result = new Dictionary<string, (double X, double Y)>(
                StringComparer.OrdinalIgnoreCase);

            // 按实体分组属性
            var attrsByEntity = erDoc.Attributes
                .GroupBy(a => a.EntityName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            // 收集每个实体的关系连线角度
            var avoidAnglesMap = CollectAvoidAngles(erDoc, entityCoords, diamondCoords);

            foreach (var entity in erDoc.Entities)
            {
                if (!entityCoords.TryGetValue(entity.Name, out var center)) continue;
                if (!attrsByEntity.TryGetValue(entity.Name, out var attrs) || attrs.Count == 0)
                    continue;

                var avoidAngles = avoidAnglesMap.GetValueOrDefault(entity.Name, []);

                // 计算每个属性的位置
                var positions = ArrangeOrbit(
                    center.X, center.Y, attrs.Count, avoidAngles, attrW, attrRadius);

                for (int i = 0; i < attrs.Count && i < positions.Count; i++)
                {
                    string key = $"{entity.Name}.{attrs[i].Name}";
                    result[key] = positions[i];
                }
            }

            return result;
        }

        /// <summary>
        /// 收集每个实体发出的关系连线角度
        /// </summary>
        private static Dictionary<string, List<double>> CollectAvoidAngles(
            ErDocument erDoc,
            Dictionary<string, (double X, double Y)> entityCoords,
            Dictionary<string, (double X, double Y)> diamondCoords)
        {
            var result = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < erDoc.Relationships.Count; i++)
            {
                var rel = erDoc.Relationships[i];
                string dId = $"◇{rel.Name}_{i}";

                if (!diamondCoords.TryGetValue(dId, out var dPos)) continue;

                // Entity1 → Diamond 的角度
                if (entityCoords.TryGetValue(rel.Entity1, out var e1))
                {
                    if (!result.ContainsKey(rel.Entity1)) result[rel.Entity1] = [];
                    result[rel.Entity1].Add(Math.Atan2(dPos.Y - e1.Y, dPos.X - e1.X));
                }

                // Entity2 → Diamond 的角度
                if (entityCoords.TryGetValue(rel.Entity2, out var e2))
                {
                    if (!result.ContainsKey(rel.Entity2)) result[rel.Entity2] = [];
                    result[rel.Entity2].Add(Math.Atan2(dPos.Y - e2.Y, dPos.X - e2.X));
                }
            }

            return result;
        }

        /// <summary>
        /// 核心：单个实体的属性轨道分配
        /// </summary>
        private static List<(double X, double Y)> ArrangeOrbit(
            double cx, double cy, int attrCount,
            List<double> avoidAngles,
            double attrW, double attrRadius)
        {
            const double halfGap = 0.35; // 避让缓冲角（约 20°）
            var positions = new List<(double X, double Y)>();

            // ---- 步骤 1：找所有安全弧段 ----
            var segments = new List<ArcSegment>();

            if (avoidAngles.Count == 0)
            {
                // 独占 360°
                segments.Add(new ArcSegment { Start = 0, End = Math.PI * 2 });
            }
            else
            {
                var sorted = avoidAngles.Select(NormalizeAngle).OrderBy(a => a).ToList();

                for (int i = 0; i < sorted.Count; i++)
                {
                    double curr = sorted[i];
                    double next = (i == sorted.Count - 1)
                        ? sorted[0] + Math.PI * 2
                        : sorted[i + 1];

                    double start = curr + halfGap;
                    double end = next - halfGap;

                    if (end > start)
                        segments.Add(new ArcSegment { Start = start, End = end });
                }
            }

            // 兜底：连线太密无空间
            if (segments.Count == 0 || segments.Sum(s => s.Length) <= 0)
                segments = [new ArcSegment { Start = 0, End = Math.PI * 2 }];

            // ---- 步骤 2：按弧长比例分配属性数量 ----
            double totalFreeAngle = segments.Sum(s => s.Length);

            foreach (var seg in segments)
                seg.Count = Math.Max(0, (int)Math.Round(attrCount * (seg.Length / totalFreeAngle)));

            // 修正总数
            int allocated = segments.Sum(s => s.Count);
            while (allocated < attrCount)
            {
                segments.OrderByDescending(s => s.Length).First().Count++;
                allocated++;
            }
            while (allocated > attrCount)
            {
                var seg = segments.LastOrDefault(s => s.Count > 0);
                if (seg != null) { seg.Count--; allocated--; }
            }

            // ---- 步骤 3：计算动态半径 ----
            double neededArc = attrCount * attrW * 1.3;
            double dynR = Math.Max(attrRadius, neededArc / totalFreeAngle);

            // ---- 步骤 4：分配绝对坐标 ----
            foreach (var seg in segments)
            {
                if (seg.Count == 0) continue;
                double step = seg.Length / seg.Count;

                for (int i = 0; i < seg.Count; i++)
                {
                    double angle = NormalizeAngle(seg.Start + step * (i + 0.5));
                    positions.Add((
                        cx + dynR * Math.Cos(angle),
                        cy + dynR * Math.Sin(angle)
                    ));
                }
            }

            return positions;
        }

        private static double NormalizeAngle(double angle)
        {
            double a = angle % (Math.PI * 2);
            if (a < 0) a += Math.PI * 2;
            return a;
        }
    }
}
