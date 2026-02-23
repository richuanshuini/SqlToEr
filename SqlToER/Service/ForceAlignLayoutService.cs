using SqlToER.Model;

namespace SqlToER.Service
{
    /// <summary>
    /// 骨架强制对齐布局 — 移植自 sql_to_ER/js/layout/forceAlignLayout.js
    ///
    /// 流水线：
    /// ① BFS 最长路径 → ② 主链水平排列 → ③ 分支交替分侧 →
    /// ④ 扇区角度分配 → ⑤ 同侧均分 → ⑥ 重投影分支 →
    /// ⑦ 强制直线 → ⑧ 菱形中点 → ⑨ 属性环绕 →
    /// ⑩ 全局防重叠 → ⑪ 主链恢复
    /// </summary>
    public static class ForceAlignLayoutService
    {
        /// <summary>
        /// 计算所有节点坐标（实体+菱形+属性）
        /// </summary>
        public static Dictionary<string, (double X, double Y)> Layout(
            ErDocument erDoc,
            double entityW, double entityH,
            double attrW,
            double relW, double relH)
        {
            var result = new Dictionary<string, (double X, double Y)>(
                StringComparer.OrdinalIgnoreCase);
            if (erDoc.Entities.Count == 0) return result;

            // ---- 尺寸常量 ----
            double entityR = LayoutUtils.NodeRadius(entityW, entityH);
            double diamondR = LayoutUtils.NodeRadius(relW * 2, relH * 2);
            double attrR = attrW / 2.0;

            // 间距（英寸）
            double chainSpacing = Math.Max(3.0, entityR * 2 + diamondR * 2 + 0.8);
            double branchDist = entityR + diamondR + 0.6;
            double entityToDiamondGap = 0.6;

            // ---- 节点分类 ----
            var coreIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var entityIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var diamondIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 半径缓存
            var radii = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            foreach (var e in erDoc.Entities)
            {
                coreIds.Add(e.Name);
                entityIds.Add(e.Name);
                radii[e.Name] = entityR;
            }

            // 菱形ID映射
            var diamondRelMap = new Dictionary<string, (string E1, string E2)>(
                StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < erDoc.Relationships.Count; i++)
            {
                var rel = erDoc.Relationships[i];
                string dId = $"◇{rel.Name}_{i}";
                coreIds.Add(dId);
                diamondIds.Add(dId);
                radii[dId] = diamondR;
                diamondRelMap[dId] = (rel.Entity1, rel.Entity2);
            }

            // ---- 核心邻接表（仅实体+菱形）----
            var coreAdj = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in coreIds)
                coreAdj[id] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var kv in diamondRelMap)
            {
                string dId = kv.Key;
                string e1 = kv.Value.E1, e2 = kv.Value.E2;
                if (coreAdj.ContainsKey(e1) && coreAdj.ContainsKey(dId))
                {
                    coreAdj[e1].Add(dId);
                    coreAdj[dId].Add(e1);
                }
                if (coreAdj.ContainsKey(e2) && coreAdj.ContainsKey(dId))
                {
                    coreAdj[e2].Add(dId);
                    coreAdj[dId].Add(e2);
                }
            }

            // ---- 属性分组 ----
            var entityAttrs = erDoc.Attributes
                .GroupBy(a => a.EntityName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            // ---- 连通分量 ----
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var components = new List<List<string>>();
            foreach (var id in coreIds.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                if (visited.Contains(id)) continue;
                var stack = new Stack<string>();
                stack.Push(id);
                visited.Add(id);
                var comp = new List<string>();
                while (stack.Count > 0)
                {
                    var cur = stack.Pop();
                    comp.Add(cur);
                    foreach (var nb in coreAdj[cur])
                    {
                        if (!visited.Contains(nb))
                        {
                            visited.Add(nb);
                            stack.Push(nb);
                        }
                    }
                }
                components.Add(comp);
            }

            // ---- 对每个连通分量布局 ----
            var sideHint = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var allTargets = new Dictionary<string, (double X, double Y)>(StringComparer.OrdinalIgnoreCase);
            var allMainPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var componentLayouts = new List<(
                Dictionary<string, (double X, double Y)> Targets,
                (double MinX, double MaxX, double MinY, double MaxY) Bounds,
                HashSet<string> MainPathSet
            )>();

            foreach (var comp in components)
            {
                var layoutResult = LayoutComponent(
                    comp, coreAdj, entityIds, radii,
                    chainSpacing, branchDist, entityToDiamondGap,
                    sideHint);
                componentLayouts.Add(layoutResult);
            }

            // ---- 平铺多个连通分量 ----
            double componentGap = 4.0;
            double cursorX = componentGap;
            double cursorY = componentGap;
            double rowHeight = 0;
            double maxWidth = 30.0; // 最大行宽（英寸）

            foreach (var layout in componentLayouts)
            {
                var (minX, maxX, minY, maxY) = layout.Bounds;
                double width = maxX - minX + componentGap;
                double height = maxY - minY + componentGap;

                if (cursorX + width > maxWidth)
                {
                    cursorX = componentGap;
                    cursorY += rowHeight + componentGap;
                    rowHeight = 0;
                }

                double offX = cursorX - minX;
                double offY = cursorY - minY;

                foreach (var kv in layout.Targets)
                    allTargets[kv.Key] = (kv.Value.X + offX, kv.Value.Y + offY);
                foreach (var id in layout.MainPathSet)
                    allMainPaths.Add(id);

                cursorX += width;
                rowHeight = Math.Max(rowHeight, height);
            }

            // ---- 记录主链位置 ----
            var mainAnchorPos = new Dictionary<string, (double X, double Y)>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in allMainPaths)
                if (allTargets.TryGetValue(id, out var p))
                    mainAnchorPos[id] = p;

            // ---- ⑤ 同侧均分 ----
            EvenSideSpacing(allTargets, coreAdj, entityIds, radii, sideHint, allMainPaths);

            // ---- ⑥ 重投影分支 ----
            ReprojectBranches(allTargets, coreAdj, entityIds, radii, sideHint, allMainPaths);

            // ---- ⑦ 强制直线 ----
            EnforceLocalTriplets(allTargets, coreAdj, entityIds, diamondIds, radii, allMainPaths);

            // ---- ⑧ 菱形中点 ----
            AdjustRelationshipMidpoints(allTargets, coreAdj, entityIds, diamondIds, radii, allMainPaths);

            // ---- ⑨ 属性环绕 ----
            PlaceAttributes(allTargets, coreAdj, entityIds,
                entityAttrs, radii, attrR, entityR, erDoc);

            // ---- ⑩ 全局防重叠（仅核心节点）----
            ResolveCoreOverlaps(allTargets, coreIds.ToList(), radii, allMainPaths);

            // ---- ⑪ 恢复主链 ----
            foreach (var kv in mainAnchorPos)
                allTargets[kv.Key] = kv.Value;

            // 复制到结果
            foreach (var kv in allTargets)
                result[kv.Key] = kv.Value;

            return result;
        }

        // ============================================================
        // ① BFS 最远点
        // ============================================================
        private static (string Farthest, Dictionary<string, int> Dist, Dictionary<string, string?> Prev)
            BfsFarthest(string start, HashSet<string> allowed,
                Dictionary<string, HashSet<string>> adj)
        {
            var dist = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [start] = 0 };
            var prev = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>();
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                if (!adj.TryGetValue(cur, out var neighbors)) continue;
                foreach (var nb in neighbors)
                {
                    if (!allowed.Contains(nb) || dist.ContainsKey(nb)) continue;
                    dist[nb] = dist[cur] + 1;
                    prev[nb] = cur;
                    queue.Enqueue(nb);
                }
            }

            string farthest = start;
            int maxDist = 0;
            foreach (var kv in dist)
                if (kv.Value > maxDist) { maxDist = kv.Value; farthest = kv.Key; }

            return (farthest, dist, prev);
        }

        private static List<string> FindLongestPath(
            List<string> ids, Dictionary<string, HashSet<string>> adj)
        {
            var allowed = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
            var first = ids[0];
            var (endA, _, _) = BfsFarthest(first, allowed, adj);
            var (endB, _, prev) = BfsFarthest(endA, allowed, adj);

            var path = new List<string>();
            string? cur = endB;
            while (cur != null)
            {
                path.Insert(0, cur);
                cur = prev.TryGetValue(cur, out var p) ? p : null;
            }
            return path.Count > 0 ? path : [first];
        }

        // ============================================================
        // ②③④ 单连通分量布局
        // ============================================================
        private static (
            Dictionary<string, (double X, double Y)> Targets,
            (double MinX, double MaxX, double MinY, double MaxY) Bounds,
            HashSet<string> MainPathSet
        ) LayoutComponent(
            List<string> ids,
            Dictionary<string, HashSet<string>> coreAdj,
            HashSet<string> entityIds,
            Dictionary<string, double> radii,
            double chainSpacing, double branchDist, double gap,
            Dictionary<string, int> sideHint)
        {
            var targets = new Dictionary<string, (double X, double Y)>(StringComparer.OrdinalIgnoreCase);
            var mainPathSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int altSide = 1;

            // ② 主链水平排列
            var mainPath = FindLongestPath(ids, coreAdj);
            double startX = -((mainPath.Count - 1) * chainSpacing) / 2.0;
            for (int idx = 0; idx < mainPath.Count; idx++)
            {
                targets[mainPath[idx]] = (startX + idx * chainSpacing, 0);
                mainPathSet.Add(mainPath[idx]);
                if (entityIds.Contains(mainPath[idx]))
                    sideHint[mainPath[idx]] = 0;
            }

            // ③ 非主链节点分支分侧
            var nonMain = ids.Where(id => !mainPathSet.Contains(id)).ToList();
            var branchVisited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var id in nonMain)
            {
                if (branchVisited.Contains(id)) continue;
                var stack = new Stack<string>();
                stack.Push(id);
                branchVisited.Add(id);
                var comp = new List<string>();
                while (stack.Count > 0)
                {
                    var cur = stack.Pop();
                    comp.Add(cur);
                    foreach (var nb in coreAdj.GetValueOrDefault(cur, []))
                    {
                        if (!branchVisited.Contains(nb) && !mainPathSet.Contains(nb))
                        {
                            branchVisited.Add(nb);
                            stack.Push(nb);
                        }
                    }
                }

                // 确定分支侧向
                int compSign = 0;
                foreach (var nid in comp)
                {
                    if (sideHint.TryGetValue(nid, out int s) && s != 0)
                    { compSign = s; break; }
                }
                if (compSign == 0)
                {
                    // 从锚点继承
                    foreach (var nid in comp)
                    {
                        foreach (var nb in coreAdj.GetValueOrDefault(nid, []))
                        {
                            if (mainPathSet.Contains(nb) && sideHint.TryGetValue(nb, out int s2) && s2 != 0)
                            { compSign = s2; break; }
                        }
                        if (compSign != 0) break;
                    }
                }
                if (compSign == 0)
                {
                    compSign = altSide;
                    altSide = -altSide;
                }
                foreach (var nid in comp)
                    sideHint[nid] = compSign;
            }

            // ④ BFS 从主链实体向外放置未定位节点
            //    关键：按 JS L303-427 忠实移植，含关系子分量分组+分侧分配
            var queue = new Queue<string>(
                mainPath.Where(id => entityIds.Contains(id)));

            while (queue.Count > 0)
            {
                var eid = queue.Dequeue();
                if (!targets.TryGetValue(eid, out var entityPos)) continue;
                double eRadius = radii.GetValueOrDefault(eid, 0.8);
                int preferredSign = sideHint.GetValueOrDefault(eid, 0);
                int nextAltSign = preferredSign == 0 ? 1 : preferredSign;

                // 邻居菱形（对应 JS L312）
                var relNeighbors = coreAdj.GetValueOrDefault(eid, [])
                    .Where(id => !entityIds.Contains(id)).ToList();
                if (relNeighbors.Count == 0) continue;

                var anchorRels = relNeighbors.Where(targets.ContainsKey).ToList();
                var unplacedRels = relNeighbors.Where(r => !targets.ContainsKey(r)).ToList();

                // 已放置菱形的角度（JS L317-320）
                var anchorAngles = anchorRels.Select(rid =>
                {
                    var rPos = targets[rid];
                    return LayoutUtils.NormalizeAngle(
                        Math.Atan2(rPos.Y - entityPos.Y, rPos.X - entityPos.X));
                }).ToList();

                // ---- 关系子分量分组（JS L322-360）----
                // 每个未放置菱形的"另一端实体"列表
                var unplacedInfo = unplacedRels.Select(rid =>
                {
                    var others = coreAdj.GetValueOrDefault(rid, [])
                        .Where(id => entityIds.Contains(id) &&
                               !string.Equals(id, eid, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    return (Rid: rid, Others: others);
                }).ToList();

                // 构建关系邻接（共享目标实体或目标实体互连 → 同组）
                var relAdj = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                foreach (var info in unplacedInfo)
                    relAdj[info.Rid] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                for (int i = 0; i < unplacedInfo.Count; i++)
                {
                    for (int j = i + 1; j < unplacedInfo.Count; j++)
                    {
                        var a = unplacedInfo[i]; var b = unplacedInfo[j];
                        bool shared = a.Others.Any(x =>
                            b.Others.Any(y => string.Equals(x, y, StringComparison.OrdinalIgnoreCase)));
                        bool cross = shared || a.Others.Any(x =>
                            b.Others.Any(y => coreAdj.GetValueOrDefault(x, []).Contains(y)));
                        if (shared || cross)
                        {
                            relAdj[a.Rid].Add(b.Rid);
                            relAdj[b.Rid].Add(a.Rid);
                        }
                    }
                }

                // 找关系子分量（JS L342-360）
                var compVisited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var relComponents = new List<List<string>>();
                foreach (var rid in unplacedRels)
                {
                    if (compVisited.Contains(rid)) continue;
                    var stk = new Stack<string>();
                    stk.Push(rid); compVisited.Add(rid);
                    var comp = new List<string>();
                    while (stk.Count > 0)
                    {
                        var cur = stk.Pop();
                        comp.Add(cur);
                        foreach (var nb in relAdj.GetValueOrDefault(cur, []))
                        {
                            if (!compVisited.Contains(nb))
                            { compVisited.Add(nb); stk.Push(nb); }
                        }
                    }
                    relComponents.Add(comp);
                }

                // 锚点角度+侧向标记（JS L362-366）
                var anchorAnglesWithSign = anchorRels.Select((rid, idx) =>
                {
                    double ang = anchorAngles[idx];
                    int sign = sideHint.GetValueOrDefault(rid, 0);
                    if (sign == 0) sign = Math.Sign(Math.Sin(ang));
                    return (Ang: ang, Sign: sign);
                }).ToList();

                // ---- 对每个关系子分量分配侧向+角度（JS L368-406）----
                foreach (var relComp in relComponents)
                {
                    // 确定此子分量的侧向
                    int compSign = 0;
                    foreach (var rid in relComp)
                    {
                        int rs = sideHint.GetValueOrDefault(rid, 0);
                        if (rs != 0) { compSign = rs; break; }
                        var info = unplacedInfo.FirstOrDefault(x =>
                            string.Equals(x.Rid, rid, StringComparison.OrdinalIgnoreCase));
                        foreach (var oid in info.Others)
                        {
                            int es = sideHint.GetValueOrDefault(oid, 0);
                            if (es != 0) { compSign = es; break; }
                        }
                        if (compSign != 0) break;
                    }
                    if (compSign == 0)
                    {
                        compSign = nextAltSign;
                        nextAltSign = -nextAltSign;
                    }

                    // 按侧向过滤锚点角度（JS L389-391）
                    var anchorsForSide = anchorAnglesWithSign
                        .Where(a => compSign > 0 ? a.Sign >= 0 : a.Sign <= 0)
                        .Select(a => a.Ang).ToList();

                    // 计算此子分量的角度（JS L393）
                    var compAngles = ComputeExtraAngles(
                        anchorsForSide.Count > 0 ? anchorsForSide : anchorAngles,
                        relComp.Count, compSign);

                    // 放置此子分量中的菱形（JS L394-405）
                    var sortedComp = relComp.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
                    for (int idx = 0; idx < sortedComp.Count; idx++)
                    {
                        var rid = sortedComp[idx];
                        double rRadius = radii.GetValueOrDefault(rid, 0.5);
                        double angle = idx < compAngles.Count
                            ? compAngles[idx]
                            : LayoutUtils.NormalizeAngle(
                                Math.PI * (compSign > 0 ? 0.5 : 1.5) + idx * 0.2);
                        double dist = eRadius + rRadius + gap;
                        targets[rid] = (
                            entityPos.X + Math.Cos(angle) * dist,
                            entityPos.Y + Math.Sin(angle) * dist
                        );
                        int sign = Math.Sign(Math.Sin(angle));
                        if (sign == 0) sign = compSign;
                        if (sign == 0) sign = preferredSign != 0 ? preferredSign : 1;
                        sideHint.TryAdd(rid, sign);
                    }
                }

                // 从菱形继续向外放置实体（JS L408-427）
                foreach (var rid in relNeighbors)
                {
                    if (!targets.TryGetValue(rid, out var relPos)) continue;
                    double rRadius = radii.GetValueOrDefault(rid, 0.5);
                    double angle = Math.Atan2(relPos.Y - entityPos.Y, relPos.X - entityPos.X);

                    foreach (var otherId in coreAdj.GetValueOrDefault(rid, []))
                    {
                        if (!entityIds.Contains(otherId) || targets.ContainsKey(otherId)) continue;
                        double oRadius = radii.GetValueOrDefault(otherId, 0.8);
                        double d = eRadius + rRadius + oRadius + branchDist;
                        targets[otherId] = (
                            entityPos.X + Math.Cos(angle) * d,
                            entityPos.Y + Math.Sin(angle) * d
                        );
                        int sign = Math.Sign(Math.Sin(angle));
                        if (sign == 0) sign = sideHint.GetValueOrDefault(rid,
                            sideHint.GetValueOrDefault(eid, 1));
                        sideHint.TryAdd(otherId, sign);
                        queue.Enqueue(otherId);
                    }
                }
            }

            // 兜底：未放置节点
            foreach (var id in ids)
                targets.TryAdd(id, (0, 0));

            // 计算包围盒
            double bMinX = double.MaxValue, bMaxX = double.MinValue;
            double bMinY = double.MaxValue, bMaxY = double.MinValue;
            foreach (var kv in targets)
            {
                double r = radii.GetValueOrDefault(kv.Key, 0.5);
                bMinX = Math.Min(bMinX, kv.Value.X - r);
                bMaxX = Math.Max(bMaxX, kv.Value.X + r);
                bMinY = Math.Min(bMinY, kv.Value.Y - r);
                bMaxY = Math.Max(bMaxY, kv.Value.Y + r);
            }

            return (targets, (bMinX, bMaxX, bMinY, bMaxY), mainPathSet);
        }

        // ============================================================
        // ④ 角度扇区分配
        // ============================================================
        private static List<double> ComputeExtraAngles(
            List<double> anchors, int extraCount, int preferredSign = 0)
        {
            if (extraCount <= 0) return [];

            if (preferredSign != 0)
            {
                double halfStart = preferredSign > 0 ? 0 : Math.PI;
                double halfEnd = halfStart + Math.PI;

                var anchorInHalf = anchors
                    .Select(LayoutUtils.NormalizeAngle)
                    .Where(a => a >= halfStart && a < halfEnd)
                    .OrderBy(a => a).ToList();

                var points = new List<double> { halfStart };
                points.AddRange(anchorInHalf);
                points.Add(halfEnd);

                var arcs = new List<(double Start, double Length, int Extras, double Fraction)>();
                for (int i = 0; i < points.Count - 1; i++)
                    arcs.Add((points[i], points[i + 1] - points[i], 0, 0));

                double totalLen = arcs.Sum(a => a.Length);
                if (totalLen < 0.01) totalLen = Math.PI;

                int remaining = extraCount;
                var arcsMut = arcs.Select(a =>
                {
                    double ideal = (a.Length / totalLen) * extraCount;
                    int floor = (int)Math.Floor(ideal);
                    remaining -= floor;
                    return (a.Start, a.Length, Extras: floor, Fraction: ideal - floor);
                }).ToList();

                // 分配余数
                remaining = extraCount - arcsMut.Sum(a => a.Extras);
                var sorted = arcsMut.OrderByDescending(a => a.Fraction).ToList();
                for (int i = 0; i < remaining && i < sorted.Count; i++)
                {
                    var a = sorted[i];
                    sorted[i] = (a.Start, a.Length, a.Extras + 1, a.Fraction);
                }
                arcsMut = sorted.OrderBy(a => a.Start).ToList();

                var result = new List<double>();
                foreach (var arc in arcsMut)
                {
                    if (arc.Length < 1e-6 || arc.Extras <= 0) continue;
                    for (int k = 1; k <= arc.Extras; k++)
                    {
                        double ratio = (double)k / (arc.Extras + 1);
                        result.Add(LayoutUtils.NormalizeAngle(arc.Start + arc.Length * ratio));
                    }
                }
                if (result.Count == 0)
                {
                    double step = Math.PI / (extraCount + 1);
                    for (int i = 0; i < extraCount; i++)
                        result.Add(LayoutUtils.NormalizeAngle(halfStart + step * (i + 1)));
                }
                return result.OrderBy(a => a).ToList();
            }

            // 无偏好侧：全圆分配
            if (anchors.Count == 0)
            {
                double step = (Math.PI * 2) / extraCount;
                return Enumerable.Range(0, extraCount)
                    .Select(i => LayoutUtils.NormalizeAngle(step * i)).ToList();
            }

            // 有锚点：在锚点间隙中分配
            var sortedAnchors = anchors.OrderBy(a => a).ToList();
            var extended = sortedAnchors.Concat([sortedAnchors[0] + Math.PI * 2]).ToList();

            var arcs2 = new List<(double Start, double Length, int Extras, double Fraction)>();
            for (int i = 0; i < sortedAnchors.Count; i++)
                arcs2.Add((extended[i], extended[i + 1] - extended[i], 0, 0));

            double total2 = arcs2.Sum(a => a.Length);
            int rem2 = extraCount;
            var arcs2Mut = arcs2.Select(a =>
            {
                double ideal = (a.Length / total2) * extraCount;
                int floor = (int)Math.Floor(ideal);
                rem2 -= floor;
                return (a.Start, a.Length, Extras: floor, Fraction: ideal - floor);
            }).ToList();

            rem2 = extraCount - arcs2Mut.Sum(a => a.Extras);
            var sorted2 = arcs2Mut.OrderByDescending(a => a.Fraction).ToList();
            for (int i = 0; i < rem2 && i < sorted2.Count; i++)
            {
                var a = sorted2[i];
                sorted2[i] = (a.Start, a.Length, a.Extras + 1, a.Fraction);
            }
            arcs2Mut = sorted2.OrderBy(a => a.Start).ToList();

            var result2 = new List<double>();
            foreach (var arc in arcs2Mut)
            {
                for (int k = 1; k <= arc.Extras; k++)
                {
                    double ratio = (double)k / (arc.Extras + 1);
                    result2.Add(LayoutUtils.NormalizeAngle(arc.Start + arc.Length * ratio));
                }
            }
            return result2.OrderBy(a => a).ToList();
        }

        // ============================================================
        // ⑤ 同侧均分
        // ============================================================
        private static void EvenSideSpacing(
            Dictionary<string, (double X, double Y)> targets,
            Dictionary<string, HashSet<string>> coreAdj,
            HashSet<string> entityIds,
            Dictionary<string, double> radii,
            Dictionary<string, int> sideHint,
            HashSet<string> mainChainIds)
        {
            foreach (var eid in entityIds)
            {
                if (!targets.TryGetValue(eid, out var ePos)) continue;
                double eRadius = radii.GetValueOrDefault(eid, 0.8);

                var relNeighbors = coreAdj.GetValueOrDefault(eid, [])
                    .Where(id => !entityIds.Contains(id) && !mainChainIds.Contains(id))
                    .ToList();
                if (relNeighbors.Count == 0) continue;

                var up = new List<string>();
                var down = new List<string>();
                foreach (var rid in relNeighbors)
                {
                    int sign = sideHint.GetValueOrDefault(rid, 0);
                    if (sign == 0 && targets.TryGetValue(rid, out var rp))
                        sign = Math.Sign(rp.Y - ePos.Y);
                    if (sign == 0) sign = 1;
                    if (sign >= 0) up.Add(rid); else down.Add(rid);
                }

                PlaceSide(up, 1, eid, ePos, eRadius, targets, radii, sideHint);
                PlaceSide(down, -1, eid, ePos, eRadius, targets, radii, sideHint);
            }
        }

        private static void PlaceSide(
            List<string> list, int sign, string eid,
            (double X, double Y) ePos, double eRadius,
            Dictionary<string, (double X, double Y)> targets,
            Dictionary<string, double> radii,
            Dictionary<string, int> sideHint)
        {
            if (list.Count == 0) return;
            double jitter = ((LayoutUtils.DeterministicHash($"{eid}-{sign}") % 1000) / 1000.0) * 0.35 - 0.175;
            double start = (sign > 0 ? 0 : Math.PI) + jitter;
            double step = Math.PI / (list.Count + 1);
            double maxRelR = list.Max(rid => radii.GetValueOrDefault(rid, 0.5));
            double radius = eRadius + maxRelR + 0.6;

            var sorted = list.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            for (int idx = 0; idx < sorted.Count; idx++)
            {
                double ang = start + step * (idx + 1);
                targets[sorted[idx]] = (
                    ePos.X + Math.Cos(ang) * radius,
                    ePos.Y + Math.Sin(ang) * radius
                );
                sideHint[sorted[idx]] = sign;
            }
        }

        // ============================================================
        // ⑥ 重投影分支
        // ============================================================
        private static void ReprojectBranches(
            Dictionary<string, (double X, double Y)> targets,
            Dictionary<string, HashSet<string>> coreAdj,
            HashSet<string> entityIds,
            Dictionary<string, double> radii,
            Dictionary<string, int> sideHint,
            HashSet<string> mainChainIds)
        {
            var projected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var eid in entityIds)
            {
                if (!targets.TryGetValue(eid, out var ePos)) continue;
                double eRad = radii.GetValueOrDefault(eid, 0.8);

                foreach (var rid in coreAdj.GetValueOrDefault(eid, []))
                {
                    if (entityIds.Contains(rid) || mainChainIds.Contains(rid)) continue;
                    if (!targets.TryGetValue(rid, out var rPos)) continue;
                    double rRad = radii.GetValueOrDefault(rid, 0.5);
                    double ang = Math.Atan2(rPos.Y - ePos.Y, rPos.X - ePos.X);

                    foreach (var oid in coreAdj.GetValueOrDefault(rid, []))
                    {
                        if (!entityIds.Contains(oid) || mainChainIds.Contains(oid)) continue;
                        double oRad = radii.GetValueOrDefault(oid, 0.8);
                        double dist = eRad + rRad + oRad + 1.2;
                        var newPos = (
                            ePos.X + Math.Cos(ang) * dist,
                            ePos.Y + Math.Sin(ang) * dist
                        );
                        if (!targets.ContainsKey(oid) || projected.Contains(oid))
                            targets[oid] = newPos;
                        projected.Add(oid);
                        int sign = Math.Sign(Math.Sin(ang));
                        if (sign == 0) sign = sideHint.GetValueOrDefault(rid, 1);
                        sideHint[oid] = sign;
                    }
                }
            }
        }

        // ============================================================
        // ⑦ 强制直线
        // ============================================================
        private static void EnforceLocalTriplets(
            Dictionary<string, (double X, double Y)> targets,
            Dictionary<string, HashSet<string>> coreAdj,
            HashSet<string> entityIds,
            HashSet<string> diamondIds,
            Dictionary<string, double> radii,
            HashSet<string> mainChainIds)
        {
            foreach (var relId in diamondIds)
            {
                var entNeighbors = coreAdj.GetValueOrDefault(relId, [])
                    .Where(entityIds.Contains).ToList();
                if (entNeighbors.Count != 2) continue;
                string e1 = entNeighbors[0], e2 = entNeighbors[1];
                if (mainChainIds.Contains(e1) && mainChainIds.Contains(e2) && mainChainIds.Contains(relId))
                    continue;

                if (!targets.TryGetValue(relId, out var pR)) continue;
                if (!targets.TryGetValue(e1, out var p1)) continue;
                if (!targets.TryGetValue(e2, out var p2)) continue;

                double d1 = Math.Sqrt((pR.X - p1.X) * (pR.X - p1.X) + (pR.Y - p1.Y) * (pR.Y - p1.Y));
                double d2 = Math.Sqrt((pR.X - p2.X) * (pR.X - p2.X) + (pR.Y - p2.Y) * (pR.Y - p2.Y));
                var anchor = d1 <= d2 ? p1 : p2;
                string moveTarget = d1 <= d2 ? e2 : e1;
                if (mainChainIds.Contains(moveTarget)) continue;

                double dx = pR.X - anchor.X, dy = pR.Y - anchor.Y;
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 0.01) len = 0.01;
                double ux = dx / len, uy = dy / len;

                double moveRad = radii.GetValueOrDefault(moveTarget, 0.8);
                double relRad = radii.GetValueOrDefault(relId, 0.5);
                targets[moveTarget] = (
                    pR.X + ux * (moveRad + relRad + 0.3),
                    pR.Y + uy * (moveRad + relRad + 0.3)
                );
            }
        }

        // ============================================================
        // ⑧ 菱形中点
        // ============================================================
        private static void AdjustRelationshipMidpoints(
            Dictionary<string, (double X, double Y)> targets,
            Dictionary<string, HashSet<string>> coreAdj,
            HashSet<string> entityIds,
            HashSet<string> diamondIds,
            Dictionary<string, double> radii,
            HashSet<string> mainChainIds)
        {
            foreach (var relId in diamondIds)
            {
                if (mainChainIds.Contains(relId)) continue;
                var entNeighbors = coreAdj.GetValueOrDefault(relId, [])
                    .Where(entityIds.Contains).ToList();
                if (entNeighbors.Count != 2) continue;

                if (!targets.TryGetValue(entNeighbors[0], out var p1)) continue;
                if (!targets.TryGetValue(entNeighbors[1], out var p2)) continue;

                double dist = Math.Sqrt((p2.X - p1.X) * (p2.X - p1.X) + (p2.Y - p1.Y) * (p2.Y - p1.Y));
                if (dist < 0.1) continue;

                double r1 = radii.GetValueOrDefault(entNeighbors[0], 0.8);
                double r2 = radii.GetValueOrDefault(entNeighbors[1], 0.8);
                double rRel = radii.GetValueOrDefault(relId, 0.5);
                double minSpan = r1 + r2 + rRel * 2 + 0.6;
                if (dist < minSpan) continue;

                targets[relId] = ((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2);
            }
        }

        // ============================================================
        // ⑨ 属性环绕
        // ============================================================
        private static void PlaceAttributes(
            Dictionary<string, (double X, double Y)> targets,
            Dictionary<string, HashSet<string>> coreAdj,
            HashSet<string> entityIds,
            Dictionary<string, List<ErAttribute>> entityAttrs,
            Dictionary<string, double> radii,
            double attrR, double entityR,
            ErDocument erDoc)
        {
            foreach (var eid in entityIds)
            {
                if (!targets.TryGetValue(eid, out var center)) continue;
                if (!entityAttrs.TryGetValue(eid, out var attrs) || attrs.Count == 0)
                    continue;

                double baseRing = entityR + attrR + 0.12;

                // 收集关系连线角度
                var relNeighbors = coreAdj.GetValueOrDefault(eid, [])
                    .Where(id => !entityIds.Contains(id)).ToList();
                var relAngles = relNeighbors
                    .Where(rid => targets.ContainsKey(rid))
                    .Select(rid =>
                    {
                        var rp = targets[rid];
                        return LayoutUtils.NormalizeAngle(
                            Math.Atan2(rp.Y - center.Y, rp.X - center.X));
                    }).ToList();

                double step = (Math.PI * 2) / attrs.Count;
                var sortedAttrs = attrs.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();

                for (int idx = 0; idx < sortedAttrs.Count; idx++)
                {
                    double seed = (LayoutUtils.DeterministicHash(sortedAttrs[idx].Name, idx) % 1000) / 1000.0;
                    double angle = LayoutUtils.NormalizeAngle(step * idx + step * 0.35 + (seed - 0.5) * 0.2);

                    // 避开关系连线
                    double threshold = 0.12;
                    for (int t = 0; t < attrs.Count; t++)
                    {
                        double candidate = LayoutUtils.NormalizeAngle(angle + t * (step / (attrs.Count + 1)));
                        bool tooClose = relAngles.Any(ra =>
                        {
                            double diff = Math.Abs(candidate - ra);
                            double mind = Math.Min(diff, Math.PI * 2 - diff);
                            return mind < threshold;
                        });
                        if (!tooClose) { angle = candidate; break; }
                    }

                    string key = $"{eid}.{sortedAttrs[idx].Name}";
                    targets[key] = (
                        center.X + Math.Cos(angle) * baseRing,
                        center.Y + Math.Sin(angle) * baseRing
                    );
                }
            }
        }

        // ============================================================
        // ⑩ 全局防重叠
        // ============================================================
        private static void ResolveCoreOverlaps(
            Dictionary<string, (double X, double Y)> targets,
            List<string> coreIds,
            Dictionary<string, double> radii,
            HashSet<string> mainChainIds)
        {
            var meta = coreIds
                .Where(targets.ContainsKey)
                .Select(id => (Id: id, R: radii.GetValueOrDefault(id, 0.5)))
                .ToList();

            for (int iter = 0; iter < 120; iter++)
            {
                double maxMove = 0;
                for (int i = 0; i < meta.Count; i++)
                {
                    for (int j = i + 1; j < meta.Count; j++)
                    {
                        var a = meta[i]; var b = meta[j];
                        var pa = targets[a.Id]; var pb = targets[b.Id];
                        double dx = pb.X - pa.X, dy = pb.Y - pa.Y;
                        double dist = Math.Sqrt(dx * dx + dy * dy);
                        if (dist < 0.001) dist = 0.001;
                        double minDist = a.R + b.R + 0.2;
                        if (dist < minDist)
                        {
                            double overlap = minDist - dist;
                            double pushA = mainChainIds.Contains(a.Id) ? 0 : overlap / (mainChainIds.Contains(b.Id) ? 1 : 2);
                            double pushB = mainChainIds.Contains(b.Id) ? 0 : overlap / (mainChainIds.Contains(a.Id) ? 1 : 2);
                            double nx = dx / dist, ny = dy / dist;
                            targets[a.Id] = (pa.X - nx * pushA, pa.Y - ny * pushA);
                            targets[b.Id] = (pb.X + nx * pushB, pb.Y + ny * pushB);
                            maxMove = Math.Max(maxMove, Math.Max(pushA, pushB));
                        }
                    }
                }
                if (maxMove < 0.01) break;
            }
        }
    }
}
