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
    /// 使用 MSAGL MDS 计算所有节点（实体+菱形+属性）的二维坐标。
    /// </summary>
    public static class MsaglLayoutService
    {
        private const double INCH_TO_PT = 72.0;

        public static Dictionary<string, (double X, double Y)> CalculateLayout(
            ErDocument erDoc,
            double entityW, double entityH,
            double attrW,
            double relW, double relH)
        {
            var result = new Dictionary<string, (double X, double Y)>(
                StringComparer.OrdinalIgnoreCase);

            if (erDoc.Entities.Count == 0) return result;

            var attrCounts = erDoc.Attributes
                .GroupBy(a => a.EntityName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            var drawingGraph = new MsaglDrawing.Graph("ER");
            var nodeSizes = new Dictionary<string, (double W, double H)>(
                StringComparer.OrdinalIgnoreCase);

            // --- 实体节点（膨胀尺寸包含属性轨道）---
            foreach (var entity in erDoc.Entities)
            {
                int nAttrs = attrCounts.GetValueOrDefault(entity.Name, 0);
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

                drawingGraph.AddEdge(rel.Entity1, dId);
                drawingGraph.AddEdge(dId, rel.Entity2);
            }

            // ---- 构建几何图 ----
            drawingGraph.CreateGeometryGraph();

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

            // ---- MDS 布局 ----
            var settings = new MdsLayoutSettings
            {
                ScaleX = 1.0,
                ScaleY = 1.0,
                IterationsWithMajorization = 200,
            };
            settings.NodeSeparation = 40;

            var geometryGraph = drawingGraph.GeometryGraph;
            LayoutHelpers.CalculateLayout(geometryGraph, settings, null);

            // ---- 提取坐标 ----
            foreach (var drawingNode in drawingGraph.Nodes)
            {
                var geoNode = drawingNode.GeometryNode;
                if (geoNode == null) continue;

                result[drawingNode.Id] = (
                    geoNode.Center.X / INCH_TO_PT,
                    geoNode.Center.Y / INCH_TO_PT
                );
            }

            return result;
        }

        /// <summary>
        /// 全节点布局（模拟参考项目 G6 force2）——
        /// entity + relationship + attribute 全部参与 MDS，不膨胀实体。
        /// 节点尺寸统一：entity=140px, rel=90px, attr=90px（与参考项目 sql2er.html:308-314 一致）
        /// </summary>
        public static Dictionary<string, (double X, double Y)> CalculateLayoutAllNodes(
            ErDocument erDoc,
            double entityW, double entityH,
            double attrW,
            double relW, double relH,
            LayoutTier tier)
        {
            var result = new Dictionary<string, (double X, double Y)>(StringComparer.OrdinalIgnoreCase);
            if (erDoc.Entities.Count == 0) return result;

            var drawingGraph = new MsaglDrawing.Graph("ER_AllNodes");
            var nodeSizes = new Dictionary<string, (double W, double H)>(StringComparer.OrdinalIgnoreCase);

            // 参考项目统一尺寸（px → pt: 1px ≈ 0.75pt）
            const double entitySizePt = 140 * 0.75;  // 105pt
            const double relSizePt = 90 * 0.75;      // 67.5pt
            const double attrSizePt = 90 * 0.75;     // 67.5pt

            // --- 实体节点（不膨胀，统一尺寸）---
            foreach (var entity in erDoc.Entities)
            {
                var node = drawingGraph.AddNode(entity.Name);
                node.Attr.Shape = MsaglDrawing.Shape.Box;
                nodeSizes[entity.Name] = (entitySizePt, entitySizePt);
            }

            // --- 菱形节点 + Entity↔Diamond 连边 ---
            for (int i = 0; i < erDoc.Relationships.Count; i++)
            {
                var rel = erDoc.Relationships[i];
                string dId = $"◇{rel.Name}_{i}";

                var node = drawingGraph.AddNode(dId);
                node.Attr.Shape = MsaglDrawing.Shape.Diamond;
                nodeSizes[dId] = (relSizePt, relSizePt);

                drawingGraph.AddEdge(rel.Entity1, dId);
                drawingGraph.AddEdge(dId, rel.Entity2);
            }

            // --- 属性节点 + Entity↔Attr 连边 ---
            var attrsByEntity = erDoc.Attributes
                .GroupBy(a => a.EntityName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            foreach (var entity in erDoc.Entities)
            {
                var attrs = attrsByEntity.GetValueOrDefault(entity.Name, []);
                foreach (var attr in attrs)
                {
                    string attrId = $"{entity.Name}.{attr.Name}";
                    var node = drawingGraph.AddNode(attrId);
                    node.Attr.Shape = MsaglDrawing.Shape.Circle;
                    nodeSizes[attrId] = (attrSizePt, attrSizePt);

                    drawingGraph.AddEdge(entity.Name, attrId);
                }
            }

            // ---- 构建几何图 ----
            drawingGraph.CreateGeometryGraph();

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
                        attrSizePt, attrSizePt, new Point(0, 0));
                }
            }

            // ---- MDS 布局（参考项目 force2: maxIteration=800）----
            var settings = new MdsLayoutSettings
            {
                ScaleX = 1.0,
                ScaleY = 1.0,
                IterationsWithMajorization = tier.MdsIterations,
            };
            settings.NodeSeparation = tier.NodeSeparation;

            var geometryGraph = drawingGraph.GeometryGraph;
            LayoutHelpers.CalculateLayout(geometryGraph, settings, null);

            // ---- 提取坐标 ----
            foreach (var drawingNode in drawingGraph.Nodes)
            {
                var geoNode = drawingNode.GeometryNode;
                if (geoNode == null) continue;

                result[drawingNode.Id] = (
                    geoNode.Center.X / INCH_TO_PT,
                    geoNode.Center.Y / INCH_TO_PT
                );
            }

            return result;
        }
    }
}
