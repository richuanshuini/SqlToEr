using Microsoft.Msagl.Core.Geometry;
using Microsoft.Msagl.Core.Geometry.Curves;
using Microsoft.Msagl.Core.Layout;
using Microsoft.Msagl.Core.Routing;
using Microsoft.Msagl.Layout.MDS;
using Microsoft.Msagl.Miscellaneous;
using MsaglDrawing = Microsoft.Msagl.Drawing;
using SqlToER.Model;

namespace SqlToER.Service
{
    /// <summary>
    /// 使用 MSAGL (Microsoft Automatic Graph Layout) 的 MDS 算法
    /// 在 Headless 模式下计算实体和菱形的最优坐标。
    /// 正确流程：Drawing.Graph → CreateGeometryGraph → 设BoundaryCurve → 布局 → 提取坐标
    /// </summary>
    public static class MsaglLayoutService
    {
        // 英寸 → MSAGL 内部点数（1英寸 = 72点）
        private const double INCH_TO_PT = 72.0;

        /// <summary>
        /// 计算 ER 图布局（仅实体 + 菱形），返回所有节点的中心坐标（Visio 英寸单位）。
        /// </summary>
        public static Dictionary<string, (double X, double Y)> CalculateLayout(
            ErDocument erDoc,
            double entityW, double entityH,
            double attrW,
            double relW, double relH)
        {
            var result = new Dictionary<string, (double X, double Y)>(
                StringComparer.OrdinalIgnoreCase);

            if (erDoc.Entities.Count == 0) return result;

            // ---- 预计算每个实体的属性数 ----
            var attrCounts = erDoc.Attributes
                .GroupBy(a => a.EntityName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            // ============================================================
            // 1. 使用 Drawing.Graph 高层 API 构建图
            // ============================================================

            var drawingGraph = new MsaglDrawing.Graph("ER");

            // 记录每个导入节点的期望宽高（英寸 → MSAGL 点数，1英寸 = 72点）
            var nodeSizes = new Dictionary<string, (double W, double H)>(
                StringComparer.OrdinalIgnoreCase);

            // --- 实体节点 ---
            foreach (var entity in erDoc.Entities)
            {
                int nAttrs = attrCounts.GetValueOrDefault(entity.Name, 0);
                // 包围圆直径（英寸），与 OptimizeAttrAngles 保持一致
                double radius = Math.Max(2.0, nAttrs * attrW * 1.3 / Math.PI);
                double sizeInch = radius * 2 + 1.0;

                var node = drawingGraph.AddNode(entity.Name);
                node.Attr.Shape = MsaglDrawing.Shape.Box;
                nodeSizes[entity.Name] = (sizeInch * INCH_TO_PT, sizeInch * INCH_TO_PT);
            }

            // --- 菱形节点 + 连边 ---
            for (int i = 0; i < erDoc.Relationships.Count; i++)
            {
                var rel = erDoc.Relationships[i];
                string dId = $"◇{rel.Name}_{i}";

                var node = drawingGraph.AddNode(dId);
                node.Attr.Shape = MsaglDrawing.Shape.Diamond;
                nodeSizes[dId] = (relW * 2 * INCH_TO_PT, relH * 2 * INCH_TO_PT);

                // Entity1 → Diamond → Entity2
                drawingGraph.AddEdge(rel.Entity1, dId);
                drawingGraph.AddEdge(dId, rel.Entity2);
            }

            // ============================================================
            // 2. Drawing → Geometry 转换 + 设置 BoundaryCurve
            // ============================================================

            drawingGraph.CreateGeometryGraph();

            // 为每个几何节点设置 BoundaryCurve（MSAGL 必须的步骤）
            foreach (var drawingNode in drawingGraph.Nodes)
            {
                var geoNode = drawingNode.GeometryNode;
                if (geoNode == null) continue;

                if (nodeSizes.TryGetValue(drawingNode.Id, out var size))
                {
                    geoNode.BoundaryCurve = CurveFactory.CreateRectangle(
                        size.W, size.H, new Point(0, 0));
                }
                else
                {
                    geoNode.BoundaryCurve = CurveFactory.CreateRectangle(
                        entityW * INCH_TO_PT, entityH * INCH_TO_PT, new Point(0, 0));
                }
            }

            // ============================================================
            // 3. 配置 MDS 布局算法 + 执行计算
            // ============================================================

            var settings = new MdsLayoutSettings
            {
                ScaleX = 1.0,
                ScaleY = 1.0,
                IterationsWithMajorization = 50,
            };
            settings.NodeSeparation = 20;

            var geometryGraph = drawingGraph.GeometryGraph;
            LayoutHelpers.CalculateLayout(geometryGraph, settings, null);

            // ============================================================
            // 4. 提取坐标（MSAGL 点数 → Visio 英寸）
            // ============================================================

            foreach (var drawingNode in drawingGraph.Nodes)
            {
                var geoNode = drawingNode.GeometryNode;
                if (geoNode == null) continue;

                // 点数 → 英寸
                result[drawingNode.Id] = (
                    geoNode.Center.X / INCH_TO_PT,
                    geoNode.Center.Y / INCH_TO_PT
                );
            }

            return result;
        }
    }
}
