using SqlToER.Model;
using Visio = Microsoft.Office.Interop.Visio;

namespace SqlToER.Service
{
    /// <summary>
    /// 积木式 ER 图绘制引擎 — 纯 DBCHEN 一套标准
    ///
    /// 核心原理（Visio ShapeSheet 黑科技）：
    /// DBCHEN 的"属性"和"主键属性"是 1D 形状！
    /// - BeginX/BeginY = 椭圆所在位置（通过 Geometry 硬编码画椭圆）
    /// - EndX/EndY = 连线末端（可 GlueTo 到实体 PinX）
    /// 所以 Drop 后直接 GlueTo 就是最标准的连接方式。
    /// </summary>
    public class ErDiagramPainter
    {
        private readonly Visio.Page _page;
        private readonly Visio.Master _entityMaster;   // DBCHEN 实体（2D 矩形）
        private readonly Visio.Master _attrMaster;     // DBCHEN 属性（1D：椭圆画在 Begin，线到 End）
        private readonly Visio.Master _relMaster;      // DBCHEN 关系（菱形）

        // 排版参数
        private const double EntityY = 5.0;
        private const double EntityStartX = 3.0;
        private const double EntitySpacing = 5.0;
        private const double AttrRadius = 2.0;

        // 默认尺寸
        private double _entityW = 1.5, _entityH = 0.6;
        private double _attrW = 1.0, _attrH = 0.5;
        private double _relW = 1.0, _relH = 0.8;

        public ErDiagramPainter(
            Visio.Page page,
            Visio.Master entityMaster,
            Visio.Master attrMaster,
            Visio.Master relMaster)
        {
            _page = page;
            _entityMaster = entityMaster;
            _attrMaster = attrMaster;
            _relMaster = relMaster;
        }

        public void ApplyTemplateSizes(TemplateLayout tpl)
        {
            if (tpl.EntityAvgWidth > 0) _entityW = tpl.EntityAvgWidth;
            if (tpl.EntityAvgHeight > 0) _entityH = tpl.EntityAvgHeight;
            if (tpl.AttributeAvgWidth > 0) _attrW = tpl.AttributeAvgWidth;
            if (tpl.AttributeAvgHeight > 0) _attrH = tpl.AttributeAvgHeight;
            if (tpl.RelationshipAvgWidth > 0) _relW = tpl.RelationshipAvgWidth;
            if (tpl.RelationshipAvgHeight > 0) _relH = tpl.RelationshipAvgHeight;
        }

        // ============================================================
        // 第 1 层：原子操作
        // ============================================================

        /// <summary>
        /// 画实体矩形（2D 形状，标准 Drop + SetSize）
        /// </summary>
        public Visio.Shape DrawEntity(string name, double x, double y)
        {
            var shape = _page.Drop(_entityMaster, x, y);
            shape.Text = name;
            SetSize(shape, _entityW, _entityH);
            return shape;
        }

        /// <summary>
        /// 画属性并通过控制点（Control Handle/黄色菱形）吸附到实体
        ///
        /// DBCHEN 属性的底层真相：
        /// - 它是 2D 形状，带一个 Control Handle（Visio 界面上的黄色菱形）
        /// - 椭圆通过 Geometry 硬编码在形状中心
        /// - 连线通过 Geometry 从椭圆中心画到 Controls.Row_1 的坐标
        /// - 操作 Controls.X 的 GlueTo = 手动拖动黄点到实体上
        /// </summary>
        public Visio.Shape DrawAttribute(string name, double x, double y,
            Visio.Shape targetEntity, bool isPK = false)
        {
            var shape = _page.Drop(_attrMaster, x, y);
            shape.Text = name;

            // 通过控制点 GlueTo 实体 PinX（黄色菱形吸附到实体中心）
            GlueAttributeToEntity(shape, targetEntity);

            if (isPK)
            {
                try
                {
                    shape.Characters.set_CharProps(
                        (short)Visio.VisCellIndices.visCharacterStyle, (short)4);
                }
                catch { }
            }

            return shape;
        }

        /// <summary>
        /// 将属性的控制点（黄色菱形）吸附到实体矩形的边缘
        ///
        /// DBCHEN 属性是带 Control Handle 的 2D 形状，线从椭圆画到控制点坐标。
        /// 1. 几何交点算法：求属性→实体方向与矩形边缘的交点
        /// 2. 转换为属性的局部坐标
        /// 3. FormulaForceU 强制设置控制点 X/Y
        /// </summary>
        private static void GlueAttributeToEntity(Visio.Shape attrShape, Visio.Shape entityShape)
        {
            short secControls = (short)Visio.VisSectionIndices.visSectionControls;

            try
            {
                if (attrShape.get_RowExists(secControls, 0,
                    (short)Visio.VisExistsFlags.visExistsAnywhere) == 0)
                    return;

                // 几何交点算法
                double eX = entityShape.get_CellsU("PinX").ResultIU;
                double eY = entityShape.get_CellsU("PinY").ResultIU;
                double eW = entityShape.get_CellsU("Width").ResultIU;
                double eH = entityShape.get_CellsU("Height").ResultIU;
                double aX = attrShape.get_CellsU("PinX").ResultIU;
                double aY = attrShape.get_CellsU("PinY").ResultIU;

                double dx = aX - eX;
                double dy = aY - eY;

                double tX = double.MaxValue, tY = double.MaxValue;
                if (Math.Abs(dx) > 0.0001) tX = (eW / 2.0) / Math.Abs(dx);
                if (Math.Abs(dy) > 0.0001) tY = (eH / 2.0) / Math.Abs(dy);
                double t = Math.Min(tX, tY);

                double intersectX = eX + t * dx;
                double intersectY = eY + t * dy;

                // 边缘交点 → 属性的局部坐标
                double locPinX = attrShape.get_CellsU("LocPinX").ResultIU;
                double locPinY = attrShape.get_CellsU("LocPinY").ResultIU;
                double localX = intersectX - aX + locPinX;
                double localY = intersectY - aY + locPinY;

                // FormulaForceU 强制设置控制点坐标
                string localXStr = localX.ToString(System.Globalization.CultureInfo.InvariantCulture);
                string localYStr = localY.ToString(System.Globalization.CultureInfo.InvariantCulture);

                attrShape.get_CellsSRC(secControls, 0,
                    (short)Visio.VisCellIndices.visCtlX).FormulaForceU = localXStr;
                attrShape.get_CellsSRC(secControls, 0,
                    (short)Visio.VisCellIndices.visCtlY).FormulaForceU = localYStr;
            }
            catch { }
        }

        /// <summary>
        /// 画关系菱形（2D 形状）
        /// </summary>
        public Visio.Shape DrawRelationship(string name, double x, double y)
        {
            var shape = _page.Drop(_relMaster, x, y);
            shape.Text = name;
            SetSize(shape, _relW, _relH);
            return shape;
        }

        /// <summary>
        /// 画连接线（用于关系↔实体的连接）
        /// </summary>
        public Visio.Shape DrawConnector(Visio.Shape from, Visio.Shape to, string label = "")
        {
            double x1 = from.get_CellsU("PinX").ResultIU;
            double y1 = from.get_CellsU("PinY").ResultIU;
            double x2 = to.get_CellsU("PinX").ResultIU;
            double y2 = to.get_CellsU("PinY").ResultIU;

            var line = _page.DrawLine(x1, y1, x2, y2);
            if (!string.IsNullOrEmpty(label))
                line.Text = label;
            return line;
        }

        // ============================================================
        // 第 2 层：组件
        // ============================================================

        /// <summary>
        /// 画实体 + 伞形属性（1D 属性自动 GlueTo 实体）
        /// </summary>
        public Visio.Shape DrawEntityWithAttrs(
            string entityName, List<ErAttribute> attrs, double x, double y)
        {
            var entity = DrawEntity(entityName, x, y);

            int n = attrs.Count;
            if (n == 0) return entity;

            double angleStep = Math.PI / (n + 1);
            for (int i = 0; i < n; i++)
            {
                double angle = Math.PI - (i + 1) * angleStep;
                double ax = x + AttrRadius * Math.Cos(angle);
                double ay = y + AttrRadius * Math.Sin(angle);

                DrawAttribute(attrs[i].Name, ax, ay, entity, attrs[i].IsPrimaryKey);
            }

            return entity;
        }

        /// <summary>
        /// 画关系菱形 + 两条连线（带基数标注）
        /// </summary>
        public Visio.Shape DrawRelBetween(
            string relName, string cardinality,
            Visio.Shape entity1, Visio.Shape entity2)
        {
            double x1 = entity1.get_CellsU("PinX").ResultIU;
            double x2 = entity2.get_CellsU("PinX").ResultIU;
            double y1 = entity1.get_CellsU("PinY").ResultIU;
            double y2 = entity2.get_CellsU("PinY").ResultIU;

            double dx = (x1 + x2) / 2.0;
            double dy = (y1 + y2) / 2.0 - 1.0;

            var diamond = DrawRelationship(relName, dx, dy);

            var parts = cardinality.Split(':');
            string cardL = parts.Length == 2 ? parts[0] : cardinality;
            string cardR = parts.Length == 2 ? parts[1] : "";

            DrawConnector(entity1, diamond, cardL);
            DrawConnector(diamond, entity2, cardR);

            return diamond;
        }

        // ============================================================
        // 第 3 层：组合器
        // ============================================================

        public void DrawErDiagram(ErDocument erDoc, Action<string>? onStatus = null)
        {
            onStatus?.Invoke("正在绘制实体...");
            var entityShapes = new Dictionary<string, Visio.Shape>(StringComparer.OrdinalIgnoreCase);
            var attrsByEntity = erDoc.Attributes
                .GroupBy(a => a.EntityName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            double curX = EntityStartX;
            foreach (var entity in erDoc.Entities)
            {
                var attrs = attrsByEntity.GetValueOrDefault(entity.Name, []);
                var shape = DrawEntityWithAttrs(entity.Name, attrs, curX, EntityY);
                entityShapes[entity.Name] = shape;
                curX += EntitySpacing;
            }

            onStatus?.Invoke("正在绘制关系...");
            foreach (var rel in erDoc.Relationships)
            {
                if (entityShapes.TryGetValue(rel.Entity1, out var s1) &&
                    entityShapes.TryGetValue(rel.Entity2, out var s2))
                {
                    DrawRelBetween(rel.Name, rel.Cardinality, s1, s2);
                }
            }

            _page.AutoSizeDrawing();
        }

        // ============================================================
        // 工具
        // ============================================================

        private static void SetSize(Visio.Shape shape, double w, double h)
        {
            try
            {
                try { shape.get_CellsU("LockWidth").ResultIU = 0; } catch { }
                try { shape.get_CellsU("LockHeight").ResultIU = 0; } catch { }
                shape.get_CellsU("Width").ResultIU = w;
                shape.get_CellsU("Height").ResultIU = h;
            }
            catch { }
        }
    }
}
