using SqlToER.Model;

namespace SqlToER.Service
{
    /// <summary>
    /// 强制对齐布局 — 忠实移植自 sql_to_ER/js/layout/forceAlignLayout.js
    /// 核心：找最长路径为主链→水平排列→BFS向外放置分支→属性环绕→全局防重叠
    /// 仅处理实体+菱形的骨架，属性由后续 ArrangeLayout 精调
    /// </summary>
    public static class ForceAlignLayoutService
    {
        public static Dictionary<string, (double X, double Y)> Layout(
            ErDocument erDoc,
            double entityW, double entityH,
            double attrW,
            double relW, double relH,
            Dictionary<string, (double X, double Y)>? inputCoords = null)
        {
            var result = new Dictionary<string, (double X, double Y)>(StringComparer.OrdinalIgnoreCase);
            if (erDoc.Entities.Count == 0) return result;

            double entityR = LayoutUtils.NodeRadius(entityW, entityH);
            double diamondR = LayoutUtils.NodeRadius(relW * 2, relH * 2);
            double attrR = attrW / 2.0;

            // ---- 构建核心邻接表（实体+菱形）----
            var coreAdj = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var nodeTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var nodeRadii = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            foreach (var e in erDoc.Entities)
            {
                nodeTypes[e.Name] = "entity";
                nodeRadii[e.Name] = entityR;
                if (!coreAdj.ContainsKey(e.Name))
                    coreAdj[e.Name] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            for (int i = 0; i < erDoc.Relationships.Count; i++)
            {
                var rel = erDoc.Relationships[i];
                string dId = $"◇{rel.Name}_{i}";
                nodeTypes[dId] = "relationship";
                nodeRadii[dId] = diamondR;
                if (!coreAdj.ContainsKey(dId))
                    coreAdj[dId] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Entity1 ↔ Diamond
                if (!coreAdj.ContainsKey(rel.Entity1))
                    coreAdj[rel.Entity1] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                coreAdj[rel.Entity1].Add(dId);
                coreAdj[dId].Add(rel.Entity1);

                // Diamond ↔ Entity2
                if (!coreAdj.ContainsKey(rel.Entity2))
                    coreAdj[rel.Entity2] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                coreAdj[rel.Entity2].Add(dId);
                coreAdj[dId].Add(rel.Entity2);
            }

            // ---- 划分连通分量（JS L78-99）----
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var components = new List<List<string>>();

            foreach (var id in coreAdj.Keys)
            {
                if (visited.Contains(id)) continue;
                var stack = new Stack<string>();
                var comp = new List<string>();
                stack.Push(id);
                visited.Add(id);
                while (stack.Count > 0)
                {
                    var cur = stack.Pop();
                    comp.Add(cur);
                    foreach (var nb in coreAdj.GetValueOrDefault(cur, []))
                    {
                        if (!visited.Contains(nb)) { visited.Add(nb); stack.Push(nb); }
                    }
                }
                components.Add(comp);
            }

            // ---- 对每个分量布局（JS L138-447）----
            var sideHint = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            // 用 MSAGL 全节点坐标预填 sideHint：
            // 每个分量中节点相对于分量中心的 Y 方向决定上下侧
            if (inputCoords != null)
            {
                foreach (var compIds in components)
                {
                    // 算分量中心 Y
                    double sumY = 0;
                    int cnt = 0;
                    foreach (var id in compIds)
                    {
                        if (inputCoords.TryGetValue(id, out var pos))
                        { sumY += pos.Y; cnt++; }
                    }
                    double centerY = cnt > 0 ? sumY / cnt : 0;

                    foreach (var id in compIds)
                    {
                        if (inputCoords.TryGetValue(id, out var pos))
                        {
                            int sign = pos.Y > centerY + 0.01 ? 1 : (pos.Y < centerY - 0.01 ? -1 : 0);
                            if (sign != 0) sideHint[id] = sign;
                        }
                    }
                }
            }

            var globalTargets = new Dictionary<string, (double X, double Y)>(StringComparer.OrdinalIgnoreCase);
            var mainChainIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (inputCoords != null)
            {
                // ---- 有 MSAGL 坐标：保持 2D 拓扑，不做水平主链 ----
                // 只用 BFS 标记主链，不覆盖位置
                foreach (var compIds in components)
                {
                    var mainPath = FindLongestPath(compIds, coreAdj);
                    foreach (var id in mainPath)
                        mainChainIds.Add(id);
                }

                // 用 MSAGL 坐标作为骨架初始位置
                foreach (var id in coreAdj.Keys)
                {
                    if (inputCoords.TryGetValue(id, out var pos))
                        globalTargets[id] = pos;
                    else
                        globalTargets[id] = (0, 0);
                }
            }
            else
            {
                // ---- 无输入坐标：走原始 ForceAlign 水平主链 ----
                double cursorX = 3.0, cursorY = 3.0, rowHeight = 0;
                double componentGap = 3.5;

                foreach (var compIds in components)
                {
                    var (targets, bounds, mainPathSet) = LayoutComponent(
                        compIds, coreAdj, nodeTypes, nodeRadii, sideHint, entityR, diamondR);

                    double width = (bounds.MaxX - bounds.MinX) + componentGap;
                    double height = (bounds.MaxY - bounds.MinY) + componentGap;

                    double offsetX = cursorX - bounds.MinX;
                    double offsetY = cursorY - bounds.MinY;

                    foreach (var kv in targets)
                        globalTargets[kv.Key] = (kv.Value.X + offsetX, kv.Value.Y + offsetY);
                    foreach (var id in mainPathSet)
                        mainChainIds.Add(id);

                    cursorX += width;
                    rowHeight = Math.Max(rowHeight, height);
                }
            }

            // ---- 全局防重叠（JS L688-720）----
            var coreIds = coreAdj.Keys.ToList();
            for (int iter = 0; iter < 120; iter++)
            {
                double maxPush = 0;
                for (int i = 0; i < coreIds.Count; i++)
                {
                    for (int j = i + 1; j < coreIds.Count; j++)
                    {
                        if (!globalTargets.TryGetValue(coreIds[i], out var pa)) continue;
                        if (!globalTargets.TryGetValue(coreIds[j], out var pb)) continue;
                        double dx = pb.X - pa.X, dy = pb.Y - pa.Y;
                        double dist = Math.Sqrt(dx * dx + dy * dy);
                        if (dist < 0.01) dist = 0.01;
                        double rA = nodeRadii.GetValueOrDefault(coreIds[i], 0.5);
                        double rB = nodeRadii.GetValueOrDefault(coreIds[j], 0.5);
                        double minDist = rA + rB + 0.2;
                        if (dist < minDist)
                        {
                            double overlap = minDist - dist;
                            double pushA = mainChainIds.Contains(coreIds[i]) ? 0 : overlap / (mainChainIds.Contains(coreIds[j]) ? 1 : 2);
                            double pushB = mainChainIds.Contains(coreIds[j]) ? 0 : overlap / (mainChainIds.Contains(coreIds[i]) ? 1 : 2);
                            double nx = dx / dist, ny = dy / dist;
                            globalTargets[coreIds[i]] = (pa.X - nx * pushA, pa.Y - ny * pushA);
                            globalTargets[coreIds[j]] = (pb.X + nx * pushB, pb.Y + ny * pushB);
                            maxPush = Math.Max(maxPush, Math.Max(pushA, pushB));
                        }
                    }
                }
                if (maxPush < 0.01) break;
            }

            // ---- 恢复主线固定位置（JS L725-728）----
            var mainAnchorPos = new Dictionary<string, (double X, double Y)>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in mainChainIds)
            {
                if (globalTargets.TryGetValue(id, out var p))
                    mainAnchorPos[id] = p;
            }

            // ---- 同侧关系均分半圆（JS L490-528 evenSideSpacing）----
            var entityIds2 = erDoc.Entities.Select(e => e.Name).ToList();
            foreach (var eid in entityIds2)
            {
                if (!globalTargets.TryGetValue(eid, out var entityPos)) continue;
                var relNeighbors = coreAdj.GetValueOrDefault(eid, [])
                    .Where(id => nodeTypes.GetValueOrDefault(id) == "relationship").ToList();
                if (relNeighbors.Count == 0) continue;

                var up = new List<string>();
                var down = new List<string>();
                foreach (var rid in relNeighbors)
                {
                    int known = sideHint.GetValueOrDefault(rid, 0);
                    int sign;
                    if (known != 0) sign = known;
                    else if (globalTargets.TryGetValue(rid, out var rp))
                        sign = Math.Sign(rp.Y - entityPos.Y);
                    else sign = 1;
                    if (sign == 0) sign = 1;

                    if (sign >= 0) up.Add(rid); else down.Add(rid);
                }

                void PlaceSide(List<string> list, int sign)
                {
                    if (list.Count == 0) return;
                    double jitter = (LayoutUtils.DeterministicHash($"{eid}-{sign}") % 1000) / 1000.0 * 0.35 - 0.175;
                    double halfStart = (sign > 0 ? 0 : Math.PI) + jitter;
                    double step = Math.PI / (list.Count + 1);
                    double maxRelR = list.Max(rid => nodeRadii.GetValueOrDefault(rid, diamondR));
                    double radius = entityR + maxRelR + 0.6;
                    var sorted = list.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
                    for (int idx = 0; idx < sorted.Count; idx++)
                    {
                        if (mainChainIds.Contains(sorted[idx])) continue;
                        double ang = halfStart + step * (idx + 1);
                        globalTargets[sorted[idx]] = (
                            entityPos.X + Math.Cos(ang) * radius,
                            entityPos.Y + Math.Sin(ang) * radius);
                        sideHint[sorted[idx]] = sign;
                    }
                }

                PlaceSide(up.Where(id => !mainChainIds.Contains(id)).ToList(), 1);
                PlaceSide(down.Where(id => !mainChainIds.Contains(id)).ToList(), -1);
            }

            // ---- 分支重投影（JS L535-574 reprojectBranches）----
            foreach (var eid in entityIds2)
            {
                if (!globalTargets.TryGetValue(eid, out var ePos)) continue;
                var relNeighbors = coreAdj.GetValueOrDefault(eid, [])
                    .Where(id => nodeTypes.GetValueOrDefault(id) == "relationship").ToList();
                foreach (var rid in relNeighbors)
                {
                    if (!globalTargets.TryGetValue(rid, out var rPos)) continue;
                    if (mainChainIds.Contains(rid)) continue;
                    double ang = Math.Atan2(rPos.Y - ePos.Y, rPos.X - ePos.X);

                    var neighbors = coreAdj.GetValueOrDefault(rid, [])
                        .Where(id => nodeTypes.GetValueOrDefault(id) == "entity" &&
                                     !id.Equals(eid, StringComparison.OrdinalIgnoreCase)).ToList();
                    foreach (var oid in neighbors)
                    {
                        if (mainChainIds.Contains(oid)) continue;
                        double oR = nodeRadii.GetValueOrDefault(oid, entityR);
                        double rR = nodeRadii.GetValueOrDefault(rid, diamondR);
                        double dist2 = entityR + rR + oR + 1.2;
                        globalTargets[oid] = (
                            ePos.X + Math.Cos(ang) * dist2,
                            ePos.Y + Math.Sin(ang) * dist2);
                    }
                }
            }

            // ---- 局部三元组直线化（JS L580-611 enforceLocalTriplets）----
            foreach (var id in coreIds)
            {
                if (nodeTypes.GetValueOrDefault(id) != "relationship") continue;
                var entNeighbors = coreAdj.GetValueOrDefault(id, [])
                    .Where(nid => nodeTypes.GetValueOrDefault(nid) == "entity").ToList();
                if (entNeighbors.Count != 2) continue;
                string e1 = entNeighbors[0], e2 = entNeighbors[1];
                if (mainChainIds.Contains(e1) && mainChainIds.Contains(e2) && mainChainIds.Contains(id))
                    continue;

                if (!globalTargets.TryGetValue(id, out var pR)) continue;
                if (!globalTargets.TryGetValue(e1, out var p1)) continue;
                if (!globalTargets.TryGetValue(e2, out var p2)) continue;

                double d1 = Math.Sqrt((pR.X - p1.X) * (pR.X - p1.X) + (pR.Y - p1.Y) * (pR.Y - p1.Y));
                double d2 = Math.Sqrt((pR.X - p2.X) * (pR.X - p2.X) + (pR.Y - p2.Y) * (pR.Y - p2.Y));
                var anchor = d1 <= d2 ? p1 : p2;
                string moveTarget = d1 <= d2 ? e2 : e1;
                if (mainChainIds.Contains(moveTarget)) continue;

                double ddx = pR.X - anchor.X, ddy = pR.Y - anchor.Y;
                double len = Math.Sqrt(ddx * ddx + ddy * ddy);
                if (len < 0.01) len = 0.01;
                double ux = ddx / len, uy = ddy / len;
                double moveR = nodeRadii.GetValueOrDefault(moveTarget, entityR);
                double relR2 = nodeRadii.GetValueOrDefault(id, diamondR);
                globalTargets[moveTarget] = (
                    pR.X + ux * (moveR + relR2 + 0.3),
                    pR.Y + uy * (moveR + relR2 + 0.3));
            }

            // ---- 关系中点回正（JS L617-637 adjustRelationshipMidpoints）----
            foreach (var id in coreIds)
            {
                if (nodeTypes.GetValueOrDefault(id) != "relationship") continue;
                if (mainChainIds.Contains(id)) continue;
                var entNeighbors = coreAdj.GetValueOrDefault(id, [])
                    .Where(nid => nodeTypes.GetValueOrDefault(nid) == "entity").ToList();
                if (entNeighbors.Count != 2) continue;
                string re1 = entNeighbors[0], re2 = entNeighbors[1];
                if (!globalTargets.TryGetValue(re1, out var rp1)) continue;
                if (!globalTargets.TryGetValue(re2, out var rp2)) continue;
                double rdist = Math.Sqrt((rp2.X - rp1.X) * (rp2.X - rp1.X) + (rp2.Y - rp1.Y) * (rp2.Y - rp1.Y));
                if (rdist < 0.01) continue;
                double r1 = nodeRadii.GetValueOrDefault(re1, entityR);
                double r2 = nodeRadii.GetValueOrDefault(re2, entityR);
                double rr = nodeRadii.GetValueOrDefault(id, diamondR);
                double minSpan = r1 + r2 + rr * 2 + 0.6;
                if (rdist < minSpan) continue;
                globalTargets[id] = ((rp1.X + rp2.X) / 2, (rp1.Y + rp2.Y) / 2);
            }

            // ---- 恢复主线固定位置（JS L725-728）----
            foreach (var kv in mainAnchorPos)
                globalTargets[kv.Key] = kv.Value;

            // ---- 放置属性（JS L642-678）----
            var attrsByEntity = erDoc.Attributes
                .GroupBy(a => a.EntityName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            foreach (var e in erDoc.Entities)
            {
                if (!globalTargets.TryGetValue(e.Name, out var center)) continue;
                var attrs = attrsByEntity.GetValueOrDefault(e.Name, []);
                if (attrs.Count == 0) continue;

                double baseRing = entityR + attrR + 0.15;
                var relNeighbors = coreAdj.GetValueOrDefault(e.Name, [])
                    .Where(id => nodeTypes.GetValueOrDefault(id) == "relationship").ToList();
                var relAngles = relNeighbors
                    .Where(rid => globalTargets.ContainsKey(rid))
                    .Select(rid =>
                    {
                        var rp = globalTargets[rid];
                        return LayoutUtils.NormalizeAngle(Math.Atan2(rp.Y - center.Y, rp.X - center.X));
                    }).ToList();

                double step = (Math.PI * 2) / attrs.Count;
                var sortedAttrs = attrs.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
                for (int idx = 0; idx < sortedAttrs.Count; idx++)
                {
                    double seed = (LayoutUtils.DeterministicHash(sortedAttrs[idx].Name, idx) % 1000) / 1000.0;
                    double angle = LayoutUtils.NormalizeAngle(step * idx + step * 0.35 + (seed - 0.5) * 0.2);

                    // 避开关系线方向
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

                    string key = $"{e.Name}.{sortedAttrs[idx].Name}";
                    globalTargets[key] = (
                        center.X + Math.Cos(angle) * baseRing,
                        center.Y + Math.Sin(angle) * baseRing
                    );
                }
            }

            return globalTargets;
        }

        // ---- 分量布局（JS L138-447）----
        private static (Dictionary<string, (double X, double Y)> Targets,
            (double MinX, double MaxX, double MinY, double MaxY) Bounds,
            HashSet<string> MainPathSet) LayoutComponent(
            List<string> ids,
            Dictionary<string, HashSet<string>> coreAdj,
            Dictionary<string, string> nodeTypes,
            Dictionary<string, double> nodeRadii,
            Dictionary<string, int> sideHint,
            double entityR, double diamondR)
        {
            var targets = new Dictionary<string, (double X, double Y)>(StringComparer.OrdinalIgnoreCase);
            double maxRadius = ids.Max(id => nodeRadii.GetValueOrDefault(id, 0.5));
            double chainSpacing = Math.Max(3.0, maxRadius * 2 + 0.6);
            var mainPathSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int altSide = 1;

            // ---- 找最长路径（JS L124-136）----
            var mainPath = FindLongestPath(ids, coreAdj);
            double startX = -((mainPath.Count - 1) * chainSpacing) / 2;
            for (int idx = 0; idx < mainPath.Count; idx++)
            {
                targets[mainPath[idx]] = (startX + idx * chainSpacing, 0);
                mainPathSet.Add(mainPath[idx]);
                if (nodeTypes.GetValueOrDefault(mainPath[idx]) == "entity")
                    sideHint[mainPath[idx]] = 0;
            }

            // ---- 非主链分支分侧（JS L156-206）----
            var nonMain = ids.Where(id => !mainPathSet.Contains(id)).ToList();
            var branchVisited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var id in nonMain)
            {
                if (branchVisited.Contains(id)) continue;
                var stack = new Stack<string>();
                var comp = new List<string>();
                stack.Push(id);
                branchVisited.Add(id);
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

                int compSign = 0;
                foreach (var nid in comp)
                {
                    if (sideHint.TryGetValue(nid, out int s) && s != 0) { compSign = s; break; }
                }
                if (compSign == 0)
                {
                    var anchors = comp.SelectMany(nid => coreAdj.GetValueOrDefault(nid, []))
                        .Where(nb => mainPathSet.Contains(nb)).Distinct().ToList();
                    foreach (var aid in anchors)
                    {
                        if (sideHint.TryGetValue(aid, out int s) && s != 0) { compSign = s; break; }
                    }
                }
                if (compSign == 0) { compSign = altSide; altSide = -altSide; }
                foreach (var nid in comp)
                    sideHint[nid] = compSign;
            }

            // ---- BFS 从主链实体向外放置（JS L303-427）----
            var queue = new Queue<string>(
                mainPath.Where(id => nodeTypes.GetValueOrDefault(id) == "entity"));

            while (queue.Count > 0)
            {
                var eid = queue.Dequeue();
                if (!targets.TryGetValue(eid, out var entityPos)) continue;
                double eRadius = nodeRadii.GetValueOrDefault(eid, entityR);

                var relNeighbors = coreAdj.GetValueOrDefault(eid, [])
                    .Where(id => nodeTypes.GetValueOrDefault(id) == "relationship").ToList();
                if (relNeighbors.Count == 0) continue;

                var anchorRels = relNeighbors.Where(rid => targets.ContainsKey(rid)).ToList();
                var unplacedRels = relNeighbors.Where(rid => !targets.ContainsKey(rid)).ToList();

                var anchorAngles = anchorRels.Select(rid =>
                {
                    var rPos = targets[rid];
                    return LayoutUtils.NormalizeAngle(Math.Atan2(rPos.Y - entityPos.Y, rPos.X - entityPos.X));
                }).ToList();

                // 关系子分量分组（JS L322-360）
                var unplacedInfo = unplacedRels.Select(rid =>
                {
                    var others = coreAdj.GetValueOrDefault(rid, [])
                        .Where(id => nodeTypes.GetValueOrDefault(id) == "entity" &&
                                     !id.Equals(eid, StringComparison.OrdinalIgnoreCase)).ToList();
                    return (Rid: rid, Others: others);
                }).ToList();

                var relAdj2 = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                foreach (var ui in unplacedInfo) relAdj2[ui.Rid] = new(StringComparer.OrdinalIgnoreCase);

                for (int i = 0; i < unplacedInfo.Count; i++)
                {
                    for (int j = i + 1; j < unplacedInfo.Count; j++)
                    {
                        var a = unplacedInfo[i]; var b = unplacedInfo[j];
                        bool shared = a.Others.Any(x => b.Others.Contains(x));
                        bool cross = shared || a.Others.Any(x =>
                            b.Others.Any(y => coreAdj.GetValueOrDefault(x, []).Contains(y)));
                        if (shared || cross)
                        {
                            relAdj2[a.Rid].Add(b.Rid);
                            relAdj2[b.Rid].Add(a.Rid);
                        }
                    }
                }

                var compVisited2 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var relComponents = new List<List<string>>();
                foreach (var rid in unplacedRels)
                {
                    if (compVisited2.Contains(rid)) continue;
                    var stk = new Stack<string>();
                    var comp2 = new List<string>();
                    stk.Push(rid); compVisited2.Add(rid);
                    while (stk.Count > 0)
                    {
                        var cur = stk.Pop(); comp2.Add(cur);
                        foreach (var nb in relAdj2.GetValueOrDefault(cur, []))
                        {
                            if (!compVisited2.Contains(nb)) { compVisited2.Add(nb); stk.Push(nb); }
                        }
                    }
                    relComponents.Add(comp2);
                }

                // 锚点角度+侧向标记（JS L362-366）
                int preferredSign = sideHint.GetValueOrDefault(eid, 0);
                int nextAlt = preferredSign == 0 ? 1 : preferredSign;
                var anchorAnglesWithSign = anchorRels.Select((rid, idx) =>
                {
                    double ang = anchorAngles[idx];
                    int sign = sideHint.GetValueOrDefault(rid, 0);
                    if (sign == 0) sign = Math.Sign(Math.Sin(ang));
                    return (Ang: ang, Sign: sign);
                }).ToList();

                // 对每个关系子分量分配角度（JS L368-406）
                foreach (var relComp in relComponents)
                {
                    int compSign = 0;
                    foreach (var rid in relComp)
                    {
                        if (sideHint.TryGetValue(rid, out int rs) && rs != 0) { compSign = rs; break; }
                        var others = unplacedInfo.FirstOrDefault(x => x.Rid == rid).Others ?? [];
                        int es = others.Select(id => sideHint.GetValueOrDefault(id, 0)).FirstOrDefault(s => s != 0);
                        if (es != 0) { compSign = es; break; }
                    }
                    if (compSign == 0) { compSign = nextAlt; nextAlt = -nextAlt; }

                    var anchorsForSide = anchorAnglesWithSign
                        .Where(a => compSign > 0 ? a.Sign >= 0 : a.Sign <= 0)
                        .Select(a => a.Ang).ToList();

                    var angles = ComputeExtraAngles(
                        anchorsForSide.Count > 0 ? anchorsForSide : anchorAngles,
                        relComp.Count, compSign);

                    var sortedComp = relComp.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
                    for (int idx = 0; idx < sortedComp.Count; idx++)
                    {
                        double r = nodeRadii.GetValueOrDefault(sortedComp[idx], diamondR);
                        double angle = idx < angles.Count
                            ? angles[idx]
                            : LayoutUtils.NormalizeAngle((compSign > 0 ? Math.PI / 2 : Math.PI * 1.5) + idx * 0.2);
                        double dist = eRadius + r + 0.6;
                        targets[sortedComp[idx]] = (
                            entityPos.X + Math.Cos(angle) * dist,
                            entityPos.Y + Math.Sin(angle) * dist);
                        int sign = Math.Sign(Math.Sin(angle));
                        if (sign == 0) sign = compSign;
                        if (sign == 0) sign = preferredSign != 0 ? preferredSign : 1;
                        sideHint.TryAdd(sortedComp[idx], sign);
                    }
                }

                // 从菱形继续向外放置实体（JS L408-427）
                foreach (var rid in relNeighbors)
                {
                    if (!targets.TryGetValue(rid, out var relPos)) continue;
                    double rR = nodeRadii.GetValueOrDefault(rid, diamondR);
                    double angle = Math.Atan2(relPos.Y - entityPos.Y, relPos.X - entityPos.X);

                    var neighbors = coreAdj.GetValueOrDefault(rid, [])
                        .Where(id => nodeTypes.GetValueOrDefault(id) == "entity" &&
                                     !id.Equals(eid, StringComparison.OrdinalIgnoreCase)).ToList();
                    foreach (var otherId in neighbors)
                    {
                        if (targets.ContainsKey(otherId)) continue;
                        double oR = nodeRadii.GetValueOrDefault(otherId, entityR);
                        double dist2 = eRadius + rR + oR + 1.2;
                        targets[otherId] = (
                            entityPos.X + Math.Cos(angle) * dist2,
                            entityPos.Y + Math.Sin(angle) * dist2);
                        int sign = Math.Sign(Math.Sin(angle));
                        if (sign == 0) sign = sideHint.GetValueOrDefault(rid, sideHint.GetValueOrDefault(eid, 1));
                        sideHint.TryAdd(otherId, sign);
                        queue.Enqueue(otherId);
                    }
                }
            }

            // 确保每个节点都有位置
            foreach (var id in ids)
                targets.TryAdd(id, (0, 0));

            // 包围盒
            double minX = double.MaxValue, maxXb = double.MinValue;
            double minY = double.MaxValue, maxYb = double.MinValue;
            foreach (var kv in targets)
            {
                double r = nodeRadii.GetValueOrDefault(kv.Key, 0.5);
                minX = Math.Min(minX, kv.Value.X - r);
                maxXb = Math.Max(maxXb, kv.Value.X + r);
                minY = Math.Min(minY, kv.Value.Y - r);
                maxYb = Math.Max(maxYb, kv.Value.Y + r);
            }

            return (targets, (minX, maxXb, minY, maxYb), mainPathSet);
        }

        // ---- 找最长路径（双 BFS，JS L101-136）----
        private static List<string> FindLongestPath(
            List<string> ids,
            Dictionary<string, HashSet<string>> coreAdj)
        {
            var allowed = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
            string first = ids[0];
            string endA = BfsFarthest(first, allowed, coreAdj).Farthest;
            var (endB, _, prev) = BfsFarthest(endA, allowed, coreAdj);

            var path = new List<string>();
            string? cur = endB;
            while (cur != null)
            {
                path.Insert(0, cur);
                prev.TryGetValue(cur, out cur);
            }
            return path.Count > 0 ? path : [first];
        }

        private static (string Farthest, Dictionary<string, int> Dist, Dictionary<string, string> Prev)
            BfsFarthest(string start, HashSet<string> allowed,
                Dictionary<string, HashSet<string>> coreAdj)
        {
            var dist = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [start] = 0 };
            var prev = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>();
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                foreach (var nb in coreAdj.GetValueOrDefault(cur, []))
                {
                    if (!allowed.Contains(nb) || dist.ContainsKey(nb)) continue;
                    dist[nb] = dist[cur] + 1;
                    prev[nb] = cur;
                    queue.Enqueue(nb);
                }
            }

            string farthest = start;
            int maxD = 0;
            foreach (var kv in dist)
                if (kv.Value > maxD) { maxD = kv.Value; farthest = kv.Key; }

            return (farthest, dist, prev);
        }

        // ---- 额外角度分配（JS L210-300）----
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
                if (totalLen < 0.001) totalLen = Math.PI;
                int remaining = extraCount;
                var arcsList = arcs.Select(a =>
                {
                    double ideal = (a.Length / totalLen) * extraCount;
                    int extras = (int)Math.Floor(ideal);
                    remaining -= extras;
                    return (a.Start, a.Length, Extras: extras, Fraction: ideal - extras);
                }).ToList();

                remaining = extraCount - arcsList.Sum(a => a.Extras);
                arcsList = arcsList.OrderByDescending(a => a.Fraction).ToList();
                for (int i = 0; i < remaining && i < arcsList.Count; i++)
                {
                    var a = arcsList[i];
                    arcsList[i] = (a.Start, a.Length, a.Extras + 1, a.Fraction);
                }
                arcsList = arcsList.OrderBy(a => a.Start).ToList();

                var res = new List<double>();
                foreach (var arc in arcsList)
                {
                    if (arc.Length < 1e-6 || arc.Extras <= 0) continue;
                    for (int k = 1; k <= arc.Extras; k++)
                    {
                        double ratio = (double)k / (arc.Extras + 1);
                        res.Add(LayoutUtils.NormalizeAngle(arc.Start + arc.Length * ratio));
                    }
                }
                if (res.Count == 0)
                {
                    double step2 = Math.PI / (extraCount + 1);
                    for (int i = 0; i < extraCount; i++)
                        res.Add(LayoutUtils.NormalizeAngle(halfStart + step2 * (i + 1)));
                }
                res.Sort();
                return res;
            }

            if (anchors.Count == 0)
            {
                double step = (Math.PI * 2) / extraCount;
                return Enumerable.Range(0, extraCount)
                    .Select(i => LayoutUtils.NormalizeAngle(step * i)).ToList();
            }

            var sorted = anchors.OrderBy(a => a).ToList();
            var extended = sorted.Concat([sorted[0] + Math.PI * 2]).ToList();

            var arcs2 = new List<(double Start, double Length, int Extras, double Fraction)>();
            for (int i = 0; i < sorted.Count; i++)
                arcs2.Add((extended[i], extended[i + 1] - extended[i], 0, 0));

            double total = arcs2.Sum(a => a.Length);
            int rem2 = extraCount;
            var arcsList2 = arcs2.Select(a =>
            {
                double ideal = (a.Length / total) * extraCount;
                int extras = (int)Math.Floor(ideal);
                rem2 -= extras;
                return (a.Start, a.Length, Extras: extras, Fraction: ideal - extras);
            }).ToList();

            rem2 = extraCount - arcsList2.Sum(a => a.Extras);
            arcsList2 = arcsList2.OrderByDescending(a => a.Fraction).ToList();
            for (int i = 0; i < rem2 && i < arcsList2.Count; i++)
            {
                var a = arcsList2[i];
                arcsList2[i] = (a.Start, a.Length, a.Extras + 1, a.Fraction);
            }
            arcsList2 = arcsList2.OrderBy(a => a.Start).ToList();

            var result = new List<double>();
            foreach (var arc in arcsList2)
            {
                for (int k = 1; k <= arc.Extras; k++)
                {
                    double ratio = (double)k / (arc.Extras + 1);
                    result.Add(LayoutUtils.NormalizeAngle(arc.Start + arc.Length * ratio));
                }
            }
            result.Sort();
            return result;
        }
    }
}
