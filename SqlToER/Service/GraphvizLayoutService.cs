using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using SqlToER.Model;

namespace SqlToER.Service
{
    /// <summary>
    /// Graphviz sfdp 引擎布局服务 —— T3 专用
    /// 采用复合图降维法：DOT 只含实体+菱形，实体按属性数膨胀，属性后处理环绕
    /// </summary>
    public static class GraphvizLayoutService
    {
        private const double PT_TO_INCH = 1.0 / 72.0;

        /// <summary>
        /// 计算布局坐标（同步版本，内部异步调用 sfdp）
        /// </summary>
        public static Dictionary<string, (double X, double Y)> CalculateLayout(
            ErDocument erDoc, double entityW, double entityH,
            double attrW, double relW, double relH,
            Action<string>? onStatus = null)
        {
            // 1. 按实体属性数统计（用于膨胀实体尺寸）
            var attrCounts = erDoc.Attributes
                .GroupBy(a => a.EntityName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            // 2. 生成 DOT 脚本（只含实体+菱形，实体按属性数膨胀）
            onStatus?.Invoke("正在生成 DOT 骨架图...");
            string dotScript = GenerateDotScript(erDoc, entityW, entityH, relW, relH, attrCounts);

            // 3. 调用 sfdp.exe
            onStatus?.Invoke("正在调用 Graphviz sfdp 引擎计算骨架布局...");
            string jsonOutput = ExecuteGraphviz(dotScript);

            // 4. 解析 JSON 获取实体+菱形坐标（骨架）
            onStatus?.Invoke("正在解析 Graphviz 骨架坐标...");
            return ParseGraphvizJson(jsonOutput);

            // 注意：属性坐标由 ArrangeLayoutService.Optimize 后续处理
        }

        /// <summary>
        /// 生成 DOT 脚本 —— 复合图降维法
        /// 只放实体+菱形，实体按属性数膨胀尺寸
        /// </summary>
        private static string GenerateDotScript(
            ErDocument erDoc, double eW, double eH, double rW, double rH,
            Dictionary<string, int> attrCounts)
        {
            var sb = new StringBuilder();
            sb.AppendLine("graph ER {");

            // neato 引擎全局设置（stress majorization，无向图专用）
            sb.AppendLine("  layout=neato;");
            sb.AppendLine("  mode=sgd;");              // 随机梯度下降，大图更稳定
            sb.AppendLine("  overlap=prism;");          // proximity graph 消除重叠
            sb.AppendLine("  overlap_scaling=-4;");     // 负值=先缩放再消除，极紧凑
            sb.AppendLine("  sep=\"+0.5\";");            // 节点周围留白（英寸）
            sb.AppendLine("  splines=true;");           // 曲线连线（自动避障）
            sb.AppendLine("  start=random42;");         // 固定随机种子，结果可复现

            // 实体节点 —— 按属性数膨胀（预留属性环绕空间）
            foreach (var entity in erDoc.Entities)
            {
                int nAttr = attrCounts.GetValueOrDefault(entity.Name, 0);
                double inflatedW = eW + nAttr * 0.3;
                double inflatedH = eH + nAttr * 0.3;
                string w = inflatedW.ToString("F2", CultureInfo.InvariantCulture);
                string h = inflatedH.ToString("F2", CultureInfo.InvariantCulture);
                sb.AppendLine($"  \"{EscapeDot(entity.Name)}\" [shape=box, width={w}, height={h}, fixedsize=true];");
            }

            // 菱形节点
            for (int i = 0; i < erDoc.Relationships.Count; i++)
            {
                string dId = $"◇{erDoc.Relationships[i].Name}_{i}";
                string w = rW.ToString("F2", CultureInfo.InvariantCulture);
                string h = rH.ToString("F2", CultureInfo.InvariantCulture);
                sb.AppendLine($"  \"{EscapeDot(dId)}\" [shape=diamond, width={w}, height={h}, fixedsize=true];");
            }

            // 边：实体 — 菱形 — 实体（neato 无向图）
            for (int i = 0; i < erDoc.Relationships.Count; i++)
            {
                var rel = erDoc.Relationships[i];
                string dId = $"◇{rel.Name}_{i}";
                sb.AppendLine($"  \"{EscapeDot(rel.Entity1)}\" -- \"{EscapeDot(dId)}\";");
                sb.AppendLine($"  \"{EscapeDot(dId)}\" -- \"{EscapeDot(rel.Entity2)}\";");
            }

            sb.AppendLine("}");
            return sb.ToString();
        }

        /// <summary>
        /// 调用本地 neato.exe -Tjson（stress majorization，无向图最优布局）
        /// </summary>
        private static string ExecuteGraphviz(string dotScript)
        {
            // 优先使用项目内嵌的 Graphviz
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string neatoPath = System.IO.Path.Combine(baseDir, "Graphviz", "neato.exe");

            if (!System.IO.File.Exists(neatoPath))
            {
                // 回退到系统 PATH
                neatoPath = "neato";
            }

            var psi = new ProcessStartInfo
            {
                FileName = neatoPath,
                Arguments = "-Tjson",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardInputEncoding = new UTF8Encoding(false), // 无 BOM，Graphviz 不识别 BOM
            };

            using var process = new Process { StartInfo = psi };
            process.Start();

            // 写入 DOT
            process.StandardInput.Write(dotScript);
            process.StandardInput.Close();

            // 读取输出
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();

            process.WaitForExit(30000); // 最多等 30 秒

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            {
                throw new InvalidOperationException(
                    $"Graphviz sfdp 执行失败 (exit={process.ExitCode}): {error}");
            }

            return output;
        }

        /// <summary>
        /// 解析 Graphviz JSON 输出，提取节点坐标
        /// </summary>
        private static Dictionary<string, (double X, double Y)> ParseGraphvizJson(string json)
        {
            var result = new Dictionary<string, (double X, double Y)>(StringComparer.OrdinalIgnoreCase);

            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("objects", out var objects))
                return result;

            foreach (var obj in objects.EnumerateArray())
            {
                if (!obj.TryGetProperty("name", out var nameEl) ||
                    !obj.TryGetProperty("pos", out var posEl))
                    continue;

                string name = nameEl.GetString() ?? "";
                string posStr = posEl.GetString() ?? "";

                // Graphviz pos 格式: "x,y"
                var parts = posStr.Split(',');
                if (parts.Length >= 2 &&
                    double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double ptX) &&
                    double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double ptY))
                {
                    result[name] = (ptX * PT_TO_INCH, ptY * PT_TO_INCH);
                }
            }

            return result;
        }

        /// <summary>
        /// 后处理：为每个实体计算属性的环绕坐标
        /// 使用角度分区法，避开关系线方向
        /// </summary>
        private static void AddAttributeCoords(
            Dictionary<string, (double X, double Y)> coords,
            ErDocument erDoc,
            Dictionary<string, List<ErAttribute>> attrsByEntity,
            Dictionary<string, int> attrCounts,
            double entityW, double attrW)
        {
            // 预计算每个实体的关系线方向角
            var lineAngles = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);
            foreach (var entity in erDoc.Entities)
                lineAngles[entity.Name] = new List<double>();

            for (int i = 0; i < erDoc.Relationships.Count; i++)
            {
                var rel = erDoc.Relationships[i];
                string dId = $"◇{rel.Name}_{i}";

                if (coords.TryGetValue(rel.Entity1, out var e1Pos) &&
                    coords.TryGetValue(dId, out var dPos1))
                    lineAngles[rel.Entity1].Add(Math.Atan2(dPos1.Y - e1Pos.Y, dPos1.X - e1Pos.X));

                if (coords.TryGetValue(rel.Entity2, out var e2Pos) &&
                    coords.TryGetValue(dId, out var dPos2))
                    lineAngles[rel.Entity2].Add(Math.Atan2(dPos2.Y - e2Pos.Y, dPos2.X - e2Pos.X));
            }

            // 为每个实体环绕属性
            foreach (var entity in erDoc.Entities)
            {
                if (!coords.TryGetValue(entity.Name, out var ePos)) continue;
                var attrs = attrsByEntity.GetValueOrDefault(entity.Name, []);
                if (attrs.Count == 0) continue;

                var angles = lineAngles[entity.Name];

                // 找最大间隙
                double bestMid, maxGap;
                if (angles.Count == 0)
                {
                    maxGap = 2 * Math.PI;
                    bestMid = Math.PI / 2;
                }
                else
                {
                    for (int i = 0; i < angles.Count; i++)
                        angles[i] = ((angles[i] % (2 * Math.PI)) + 2 * Math.PI) % (2 * Math.PI);
                    angles.Sort();

                    maxGap = 0;
                    bestMid = Math.PI / 2;
                    for (int i = 0; i < angles.Count; i++)
                    {
                        double next = (i + 1 < angles.Count) ? angles[i + 1] : angles[0] + 2 * Math.PI;
                        double gap = next - angles[i];
                        if (gap > maxGap)
                        {
                            maxGap = gap;
                            bestMid = angles[i] + gap / 2.0;
                        }
                    }
                }

                // 动态半径
                int n = attrs.Count;
                double usableGap = Math.Max(maxGap - 0.3, 0.5);
                double neededArc = n * attrW * 1.3;
                double dynR = Math.Max(1.2, neededArc / usableGap);

                // 扇形分配
                double fanSpan = Math.Min(n * (attrW * 1.2 / dynR) * 1.5, usableGap);
                double startAngle = bestMid - fanSpan / 2;
                double angleStep = fanSpan / (n + 1);

                for (int i = 0; i < n; i++)
                {
                    double angle = startAngle + (i + 1) * angleStep;
                    double ax = ePos.X + dynR * Math.Cos(angle);
                    double ay = ePos.Y + dynR * Math.Sin(angle);
                    string attrId = $"{entity.Name}.{attrs[i].Name}";
                    coords[attrId] = (ax, ay);
                }
            }
        }

        /// <summary>
        /// 转义 DOT 中的特殊字符
        /// </summary>
        private static string EscapeDot(string s)
        {
            return s.Replace("\"", "\\\"");
        }
    }
}
