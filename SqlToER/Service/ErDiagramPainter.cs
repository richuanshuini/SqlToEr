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
        private readonly Visio.Master _attrMaster;     // DBCHEN 属性（2D + Control Handle）
        private readonly Visio.Master _relMaster;      // DBCHEN 关系（菱形）
        private readonly Visio.Master _connMaster;     // DBCHEN 关系连接线（1D，蓝色端点）

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
            Visio.Master relMaster,
            Visio.Master connMaster)
        {
            _page = page;
            _entityMaster = entityMaster;
            _attrMaster = attrMaster;
            _relMaster = relMaster;
            _connMaster = connMaster;
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
        /// 将属性的控制点（黄色菱形）动态吸附到实体矩形的边缘
        ///
        /// 策略：
        /// 1. 几何交点算法：求属性→实体方向与矩形边缘的交点百分比
        /// 2. 在实体上添加连接点（Width*p, Height*q 公式）
        /// 3. FormulaForceU 写入 Sheet.{ID}! 跨形状引用公式
        ///    → 移动实体时，属性控制点自动跟随
        /// </summary>
        private static void GlueAttributeToEntity(Visio.Shape attrShape, Visio.Shape entityShape)
        {
            short secControls = (short)Visio.VisSectionIndices.visSectionControls;

            try
            {
                if (attrShape.get_RowExists(secControls, 0,
                    (short)Visio.VisExistsFlags.visExistsAnywhere) == 0)
                    return;

                // --- 几何交点算法（求交点在实体 Width/Height 上的百分比）---
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

                // 交点转换为实体局部坐标的百分比 (0..1)
                double eLeft = eX - eW / 2.0;
                double eBottom = eY - eH / 2.0;
                double pctX = (intersectX - eLeft) / eW;
                double pctY = (intersectY - eBottom) / eH;
                pctX = Math.Max(0, Math.Min(1, pctX));
                pctY = Math.Max(0, Math.Min(1, pctY));

                // --- 在实体上添加连接点 ---
                short secConn = (short)Visio.VisSectionIndices.visSectionConnectionPts;
                if (entityShape.get_SectionExists(secConn, 0) == 0)
                    entityShape.AddSection(secConn);

                string pxStr = pctX.ToString(System.Globalization.CultureInfo.InvariantCulture);
                string pyStr = pctY.ToString(System.Globalization.CultureInfo.InvariantCulture);

                short cpRow = entityShape.AddRow(secConn,
                    (short)Visio.VisRowIndices.visRowLast,
                    (short)Visio.VisRowTags.visTagCnnctPt);
                entityShape.get_CellsSRC(secConn, cpRow,
                    (short)Visio.VisCellIndices.visCnnctX).FormulaU = $"Width*{pxStr}";
                entityShape.get_CellsSRC(secConn, cpRow,
                    (short)Visio.VisCellIndices.visCnnctY).FormulaU = $"Height*{pyStr}";

                // --- 控制点公式：动态引用实体连接点坐标 ---
                int eId = entityShape.ID;
                int formulaIdx = cpRow + 1; // ShapeSheet 公式中 Connections.X 是 1-indexed

                // 实体连接点局部坐标 → 页面坐标 → 属性局部坐标
                // pageX = Sheet.eId!Connections.X{n} + Sheet.eId!PinX - Sheet.eId!LocPinX
                // attrLocalX = pageX - PinX + LocPinX
                string ctlXFormula = $"GUARD(Sheet.{eId}!Connections.X{formulaIdx}+Sheet.{eId}!PinX-Sheet.{eId}!LocPinX-PinX+LocPinX)";
                string ctlYFormula = $"GUARD(Sheet.{eId}!Connections.Y{formulaIdx}+Sheet.{eId}!PinY-Sheet.{eId}!LocPinY-PinY+LocPinY)";

                attrShape.get_CellsSRC(secControls, 0,
                    (short)Visio.VisCellIndices.visCtlX).FormulaForceU = ctlXFormula;
                attrShape.get_CellsSRC(secControls, 0,
                    (short)Visio.VisCellIndices.visCtlY).FormulaForceU = ctlYFormula;
            }
            catch { }
        }

        /// <summary>
        /// 画关系菱形（2D 形状）+ 动态添加4角连接点
        /// </summary>
        public Visio.Shape DrawRelationship(string name, double x, double y)
        {
            var shape = _page.Drop(_relMaster, x, y);
            shape.Text = name;
            SetSize(shape, _relW, _relH);

            // 动态添加菱形4角连接点（ForeignObject 无内置连接点）
            try
            {
                short secConn = (short)Visio.VisSectionIndices.visSectionConnectionPts;
                if (shape.get_SectionExists(secConn, 0) == 0)
                    shape.AddSection(secConn);

                // Row 0: 左角
                AddConnectionPoint(shape, secConn, "Width*0", "Height*0.5");
                // Row 1: 右角
                AddConnectionPoint(shape, secConn, "Width*1", "Height*0.5");
                // Row 2: 上角
                AddConnectionPoint(shape, secConn, "Width*0.5", "Height*1");
                // Row 3: 下角
                AddConnectionPoint(shape, secConn, "Width*0.5", "Height*0");
            }
            catch { }

            return shape;
        }

        /// <summary>
        /// 在形状上添加一个连接点（辅助方法）
        /// </summary>
        private static void AddConnectionPoint(Visio.Shape shape, short secConn,
            string xFormula, string yFormula)
        {
            short row = shape.AddRow(secConn,
                (short)Visio.VisRowIndices.visRowLast,
                (short)Visio.VisRowTags.visTagCnnctPt);
            shape.get_CellsSRC(secConn, row,
                (short)Visio.VisCellIndices.visCnnctX).FormulaU = xFormula;
            shape.get_CellsSRC(secConn, row,
                (short)Visio.VisCellIndices.visCnnctY).FormulaU = yFormula;
        }

        /// <summary>
        /// 画连接线（通用版，用最近连接点或 PinX）
        /// </summary>
        public Visio.Shape DrawConnector(Visio.Shape from, Visio.Shape to, string label = "")
        {
            double mx = (from.get_CellsU("PinX").ResultIU + to.get_CellsU("PinX").ResultIU) / 2;
            double my = (from.get_CellsU("PinY").ResultIU + to.get_CellsU("PinY").ResultIU) / 2;
            var conn = _page.Drop(_connMaster, mx, my);

            GlueEndpoint(conn, "BeginX", "BeginY", from);
            GlueEndpoint(conn, "EndX", "EndY", to);

            if (!string.IsNullOrEmpty(label))
                conn.Text = label;
            return conn;
        }

        /// <summary>
        /// 将 1D 连接线的端点吸附到目标形状
        /// - 菱形：用最近的预置角尖连接点（不添加新连接点）
        /// - 实体：用 AddDirectionalConnPt 添加朝向对方的边缘连接点
        /// </summary>
        private static void GlueEndpoint(Visio.Shape conn, string cellX, string cellY,
            Visio.Shape target, Visio.Shape? referenceShape = null)
        {
            short secConn = (short)Visio.VisSectionIndices.visSectionConnectionPts;

            // 判断 target 是否为菱形（关系形状）
            // 菱形的几何是旋转45°的正方形，不能用 AddDirectionalConnPt（矩形公式）
            bool isDiamond = IsDiamondShape(target);

            if (isDiamond && referenceShape != null)
            {
                // 菱形：用 referenceShape 的位置找最近的角尖连接点
                try
                {
                    if (target.get_SectionExists(secConn, 0) != 0)
                    {
                        double refPX = referenceShape.get_CellsU("PinX").ResultIU;
                        double refPY = referenceShape.get_CellsU("PinY").ResultIU;
                        short bestRow = FindNearestConnectionPoint(target, secConn, refPX, refPY);
                        if (bestRow >= 0)
                        {
                            conn.get_CellsU(cellX).GlueTo(
                                target.get_CellsSRC(secConn, bestRow,
                                    (short)Visio.VisCellIndices.visCnnctX));
                            return;
                        }
                    }
                }
                catch { }
            }
            else if (!isDiamond && referenceShape != null)
            {
                // 实体：添加朝向对方的边缘连接点
                try
                {
                    short cpRow = AddDirectionalConnPt(target, referenceShape);
                    if (cpRow >= 0)
                    {
                        var connPtCell = target.get_CellsSRC(secConn, cpRow,
                            (short)Visio.VisCellIndices.visCnnctX);
                        conn.get_CellsU(cellX).GlueTo(connPtCell);
                        return;
                    }
                }
                catch { }
            }

            // 回退：最近连接点
            try
            {
                if (target.get_SectionExists(secConn, 0) != 0)
                {
                    double cx = conn.get_CellsU(cellX).ResultIU;
                    double cy = conn.get_CellsU(cellY).ResultIU;
                    short bestRow = FindNearestConnectionPoint(target, secConn, cx, cy);
                    if (bestRow >= 0)
                    {
                        conn.get_CellsU(cellX).GlueTo(
                            target.get_CellsSRC(secConn, bestRow,
                                (short)Visio.VisCellIndices.visCnnctX));
                        return;
                    }
                }
            }
            catch { }

            // 回退：PinX
            try
            {
                conn.get_CellsU(cellX).GlueTo(target.get_CellsU("PinX"));
                return;
            }
            catch { }

            conn.get_CellsU(cellX).ResultIU = target.get_CellsU("PinX").ResultIU;
            conn.get_CellsU(cellY).ResultIU = target.get_CellsU("PinY").ResultIU;
        }

        /// <summary>
        /// 判断形状是否为菱形（关系形状）
        /// 检查方式：菱形由 DrawRelationship 创建，预置了4个角尖连接点
        /// Width*0/Height*0.5, Width*1/Height*0.5, Width*0.5/Height*1, Width*0.5/Height*0
        /// </summary>
        private static bool IsDiamondShape(Visio.Shape shape)
        {
            try
            {
                // 菱形的几何 Section 通常有旋转45°的路径
                // 最可靠的判断：检查是否有恰好 4 个连接点且位置符合角尖模式
                short secConn = (short)Visio.VisSectionIndices.visSectionConnectionPts;
                if (shape.get_SectionExists(secConn, 0) == 0) return false;

                short rows = shape.get_RowCount(secConn);
                if (rows < 4) return false;

                // 检查前 4 个连接点的公式是否匹配角尖模式
                string x0 = shape.get_CellsSRC(secConn, 0, (short)Visio.VisCellIndices.visCnnctX).FormulaU;
                string y0 = shape.get_CellsSRC(secConn, 0, (short)Visio.VisCellIndices.visCnnctY).FormulaU;
                // 第一个角尖: Width*0, Height*0.5 (左角)
                return x0.Contains("Width*0") && y0.Contains("Height*0.5");
            }
            catch { return false; }
        }

        /// <summary>
        /// 在 shape 上添加朝向 towardShape 方向的边缘连接点
        /// 返回新连接点的行号
        /// </summary>
        private static short AddDirectionalConnPt(Visio.Shape shape, Visio.Shape towardShape)
        {
            double sX = shape.get_CellsU("PinX").ResultIU;
            double sY = shape.get_CellsU("PinY").ResultIU;
            double sW = shape.get_CellsU("Width").ResultIU;
            double sH = shape.get_CellsU("Height").ResultIU;
            double tX = towardShape.get_CellsU("PinX").ResultIU;
            double tY = towardShape.get_CellsU("PinY").ResultIU;

            double dx = tX - sX;
            double dy = tY - sY;

            // 计算矩形边缘交点百分比
            double tx = double.MaxValue, ty = double.MaxValue;
            if (Math.Abs(dx) > 0.0001) tx = (sW / 2.0) / Math.Abs(dx);
            if (Math.Abs(dy) > 0.0001) ty = (sH / 2.0) / Math.Abs(dy);
            double t = Math.Min(tx, ty);

            double ix = sX + t * dx;
            double iy = sY + t * dy;

            double pctX = (ix - (sX - sW / 2.0)) / sW;
            double pctY = (iy - (sY - sH / 2.0)) / sH;
            pctX = Math.Max(0, Math.Min(1, pctX));
            pctY = Math.Max(0, Math.Min(1, pctY));

            short secConn = (short)Visio.VisSectionIndices.visSectionConnectionPts;
            if (shape.get_SectionExists(secConn, 0) == 0)
                shape.AddSection(secConn);

            string px = pctX.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string py = pctY.ToString(System.Globalization.CultureInfo.InvariantCulture);

            short cpRow = shape.AddRow(secConn,
                (short)Visio.VisRowIndices.visRowLast,
                (short)Visio.VisRowTags.visTagCnnctPt);
            shape.get_CellsSRC(secConn, cpRow,
                (short)Visio.VisCellIndices.visCnnctX).FormulaU = $"Width*{px}";
            shape.get_CellsSRC(secConn, cpRow,
                (short)Visio.VisCellIndices.visCnnctY).FormulaU = $"Height*{py}";

            return cpRow;
        }

        /// <summary>
        /// 在目标形状的连接点中找最近的一个
        /// </summary>
        private static short FindNearestConnectionPoint(Visio.Shape shape, short secConn,
            double refX, double refY)
        {
            short rowCount = shape.get_RowCount(secConn);
            if (rowCount == 0) return -1;

            double pinX = shape.get_CellsU("PinX").ResultIU;
            double pinY = shape.get_CellsU("PinY").ResultIU;
            double locPinX = shape.get_CellsU("LocPinX").ResultIU;
            double locPinY = shape.get_CellsU("LocPinY").ResultIU;

            short bestRow = 0;
            double bestDist = double.MaxValue;

            for (short r = 0; r < rowCount; r++)
            {
                // 连接点局部坐标 → 页面坐标
                double localX = shape.get_CellsSRC(secConn, r,
                    (short)Visio.VisCellIndices.visCnnctX).ResultIU;
                double localY = shape.get_CellsSRC(secConn, r,
                    (short)Visio.VisCellIndices.visCnnctY).ResultIU;
                double pageX = pinX - locPinX + localX;
                double pageY = pinY - locPinY + localY;

                double dist = (pageX - refX) * (pageX - refX) + (pageY - refY) * (pageY - refY);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestRow = r;
                }
            }
            return bestRow;
        }

        // ============================================================
        // 第 2 层：组件
        // ============================================================

        /// <summary>
        /// 画实体 + 伞形属性
        /// centerAngle: 属性扇面的中心方向（弧度）
        /// maxGap: 可用角度间隙
        /// dynRadius: 动态计算的属性半径
        /// </summary>
        public Visio.Shape DrawEntityWithAttrs(
            string entityName, List<ErAttribute> attrs, double x, double y,
            double centerAngle = Math.PI / 2, double maxGap = Math.PI,
            double dynRadius = AttrRadius)
        {
            var entity = DrawEntity(entityName, x, y);

            int n = attrs.Count;
            if (n == 0) return entity;

            double r = dynRadius;

            // 扇面张角：基于椭圆宽度和动态半径计算
            // 同时限制在可用间隙内
            double minStep = (_attrW * 1.2) / r;
            double idealSpan = (n + 1) * minStep;
            double availableGap = Math.Max(maxGap - 0.3, minStep * 2);
            double fanSpan = Math.Min(idealSpan, availableGap);
            double startAngle = centerAngle - fanSpan / 2;
            double angleStep = fanSpan / (n + 1);

            for (int i = 0; i < n; i++)
            {
                double angle = startAngle + (i + 1) * angleStep;
                double ax = x + r * Math.Cos(angle);
                double ay = y + r * Math.Sin(angle);

                DrawAttribute(attrs[i].Name, ax, ay, entity, attrs[i].IsPrimaryKey);
            }

            return entity;
        }

        /// <summary>
        /// 画关系菱形 + 两条连线（带基数标注）
        /// diamondX/Y: 可指定菱形位置（null = 自动计算中点）
        /// </summary>
        public Visio.Shape DrawRelBetween(
            string relName, string cardinality,
            Visio.Shape entity1, Visio.Shape entity2,
            double? diamondX = null, double? diamondY = null)
        {
            double x1 = entity1.get_CellsU("PinX").ResultIU;
            double x2 = entity2.get_CellsU("PinX").ResultIU;
            double y1 = entity1.get_CellsU("PinY").ResultIU;
            double y2 = entity2.get_CellsU("PinY").ResultIU;

            double dx = diamondX ?? (x1 + x2) / 2.0;
            double dy = diamondY ?? (y1 + y2) / 2.0;

            var diamond = DrawRelationship(relName, dx, dy);

            var parts = cardinality.Split(':');
            string cardL = parts.Length == 2 ? parts[0] : cardinality;
            string cardR = parts.Length == 2 ? parts[1] : "";

            // entity1 → diamond: 两端都用方向连接点
            var conn1 = DrawConnectorDirectional(entity1, diamond, cardL);
            // diamond → entity2: 两端都用方向连接点
            var conn2 = DrawConnectorDirectional(diamond, entity2, cardR);

            return diamond;
        }

        /// <summary>
        /// 画关系连线 — 两端都用方向连接点（朝向对方边缘）
        /// </summary>
        private Visio.Shape DrawConnectorDirectional(Visio.Shape from, Visio.Shape to, string label = "")
        {
            double mx = (from.get_CellsU("PinX").ResultIU + to.get_CellsU("PinX").ResultIU) / 2;
            double my = (from.get_CellsU("PinY").ResultIU + to.get_CellsU("PinY").ResultIU) / 2;
            var conn = _page.Drop(_connMaster, mx, my);

            GlueEndpoint(conn, "BeginX", "BeginY", from, to);
            GlueEndpoint(conn, "EndX", "EndY", to, from);

            if (!string.IsNullOrEmpty(label))
                conn.Text = label;
            return conn;
        }

        // ============================================================
        // 第 3 层：组合器 — ER 星形布局算法
        // ============================================================

        /// <summary>
        /// 布局结果：每个实体的位置 + 属性扇面方向 + 可用间隙 + 动态半径
        /// </summary>
        private record EntityPlacement(double X, double Y, double AttrAngle,
            double AttrGap = Math.PI, double DynRadius = AttrRadius);

        public void DrawErDiagram(ErDocument erDoc, Action<string>? onStatus = null)
        {
            var attrsByEntity = erDoc.Attributes
                .GroupBy(a => a.EntityName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            // ---- 步骤1: 计算布局 ----
            onStatus?.Invoke("正在计算布局...");
            var layout = CalculateLayout(erDoc);

            // ---- 步骤1.5: 角度分区 — 属性放到关系线的空隙中 ----
            onStatus?.Invoke("正在计算属性避让方向...");
            layout = OptimizeAttrAngles(erDoc, layout);

            // ---- 步骤2: 画实体+属性 ----
            onStatus?.Invoke("正在绘制实体...");
            var entityShapes = new Dictionary<string, Visio.Shape>(StringComparer.OrdinalIgnoreCase);
            foreach (var entity in erDoc.Entities)
            {
                if (!layout.TryGetValue(entity.Name, out var place)) continue;
                var attrs = attrsByEntity.GetValueOrDefault(entity.Name, []);
                var shape = DrawEntityWithAttrs(entity.Name, attrs,
                    place.X, place.Y, place.AttrAngle, place.AttrGap, place.DynRadius);
                entityShapes[entity.Name] = shape;
            }

            // ---- 步骤3: 画关系菱形+连线 ----
            onStatus?.Invoke("正在绘制关系...");
            var diamondPositions = CalculateDiamondPositions(erDoc, layout);
            for (int i = 0; i < erDoc.Relationships.Count; i++)
            {
                var rel = erDoc.Relationships[i];
                if (entityShapes.TryGetValue(rel.Entity1, out var s1) &&
                    entityShapes.TryGetValue(rel.Entity2, out var s2))
                {
                    var (dx, dy) = diamondPositions[i];
                    DrawRelBetween(rel.Name, rel.Cardinality, s1, s2, dx, dy);
                }
            }

            // ---- 步骤4: Visio Layout 融合 — 锁定形状 + 路由优化 ----
            onStatus?.Invoke("正在优化连线路由...");
            try
            {
                // 4a. 锁定所有 2D 形状位置，让 Page.Layout 只重排连线
                foreach (Visio.Shape s in _page.Shapes)
                {
                    try
                    {
                        // 判断是否为 1D 连接线（OneDBegin/OneDEnd 存在则为 1D）
                        bool is1D = false;
                        try { is1D = s.OneD != 0; } catch { }

                        if (!is1D)
                        {
                            // 2D 形状：锁定位置 + 不可让连线压在上面
                            s.get_CellsU("ShapeFixedCode").FormulaU = "3";
                            // visSLOFixedPlacement(1) | visSLOFixedPlow(2) = 3
                        }
                    }
                    catch { }
                }

                // 4b. 配置页面级路由参数
                var pageSheet = _page.PageSheet;
                pageSheet.get_CellsU("RouteStyle").FormulaU = "16";
                // 16 = visLORouteCenterToCenter
                pageSheet.get_CellsU("LineRouteExt").FormulaU = "1";
                // 1 = 直线
                pageSheet.get_CellsU("PlaceStyle").FormulaU = "0";
                // 0 = 不重新放置，只路由

                // 4c. 调用 Visio 内置布局引擎 — 只重排连线
                _page.Layout();
            }
            catch { }

            _page.AutoSizeDrawing();
        }

        /// <summary>
        /// 角度分区 + 动态半径算法
        /// 
        /// 1. 收集每个实体的所有关系线方向角
        /// 2. 找最大间隙，属性放入间隙
        /// 3. dynRadius = max(baseR, numAttrs × attrW × 1.3 / gap)
        /// </summary>
        private Dictionary<string, EntityPlacement> OptimizeAttrAngles(
            ErDocument erDoc, Dictionary<string, EntityPlacement> layout)
        {
            // 属性计数
            var attrCounts = erDoc.Attributes
                .GroupBy(a => a.EntityName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            // 预计算菱形位置
            var diamondPos = CalculateDiamondPositions(erDoc, layout);

            // 收集每个实体的关系线方向角
            var lineAngles = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in layout) lineAngles[kv.Key] = new List<double>();

            for (int i = 0; i < erDoc.Relationships.Count; i++)
            {
                var rel = erDoc.Relationships[i];
                var (dX, dY) = diamondPos[i];

                if (layout.TryGetValue(rel.Entity1, out var p1))
                    lineAngles[rel.Entity1].Add(Math.Atan2(dY - p1.Y, dX - p1.X));
                if (layout.TryGetValue(rel.Entity2, out var p2))
                    lineAngles[rel.Entity2].Add(Math.Atan2(dY - p2.Y, dX - p2.X));
            }

            var result = new Dictionary<string, EntityPlacement>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in layout)
            {
                int numAttrs = attrCounts.GetValueOrDefault(kv.Key, 0);
                var angles = lineAngles[kv.Key];

                double maxGap, bestMid;

                if (angles.Count == 0)
                {
                    // 没有关系线，全 360° 可用
                    maxGap = 2 * Math.PI;
                    bestMid = kv.Value.AttrAngle;
                }
                else
                {
                    // 归一化到 [0, 2π)
                    for (int i = 0; i < angles.Count; i++)
                        angles[i] = ((angles[i] % (2 * Math.PI)) + 2 * Math.PI) % (2 * Math.PI);
                    angles.Sort();

                    maxGap = 0;
                    bestMid = kv.Value.AttrAngle;
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

                // 动态半径：确保所有属性在可用间隙内不重叠
                double dynR = AttrRadius;
                if (numAttrs > 0)
                {
                    double usableGap = Math.Max(maxGap - 0.3, 0.5);
                    // 需要的弧长 = numAttrs × 椭圆宽度 × 1.3
                    double neededArc = numAttrs * _attrW * 1.3;
                    double neededR = neededArc / usableGap;
                    dynR = Math.Max(AttrRadius, neededR);
                }

                result[kv.Key] = new EntityPlacement(kv.Value.X, kv.Value.Y, bestMid, maxGap, dynR);
            }

            return result;
        }

        /// <summary>
        /// ER 力导向布局算法（Fruchterman-Reingold 变体）
        /// 
        /// 核心：所有实体平等参与力模拟
        /// - 排斥力：所有实体互相排斥，距离越近排斥越强
        /// - 吸引力：有关系的实体互相吸引（弹簧力）
        /// - 最小距离：2*AttrRadius+2，确保属性扇面不重叠
        /// - AttrAngle：由 OptimizeAttrAngles 后处理
        /// </summary>
        private Dictionary<string, EntityPlacement> CalculateLayout(ErDocument erDoc)
        {
            var result = new Dictionary<string, EntityPlacement>(StringComparer.OrdinalIgnoreCase);
            var entities = erDoc.Entities;
            var rels = erDoc.Relationships;
            int count = entities.Count;

            if (count == 0) return result;

            // 动态安全距离：基于最大属性数量估算半径
            var attrCounts = erDoc.Attributes
                .GroupBy(a => a.EntityName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            int maxAttrs = attrCounts.Count > 0 ? attrCounts.Values.Max() : 0;
            // 最大属性数实体的估算半径（假设 PI 弧度可用）
            double maxR = Math.Max(AttrRadius, maxAttrs * _attrW * 1.3 / Math.PI);
            double safeDistance = 2 * maxR + 2.0;

            // === 边缘情况：只有1个实体 ===
            if (count == 1)
            {
                result[entities[0].Name] = new(EntityStartX + AttrRadius, EntityY + AttrRadius, Math.PI / 2);
                return result;
            }

            // === 边缘情况：只有2个实体 ===
            if (count == 2)
            {
                result[entities[0].Name] = new(EntityStartX + AttrRadius, EntityY + AttrRadius, Math.PI * 3 / 4);
                result[entities[1].Name] = new(EntityStartX + AttrRadius + safeDistance, EntityY + AttrRadius, Math.PI / 4);
                return result;
            }

            // === 3+ 实体：力导向布局 ===

            // 构建邻接关系
            var neighbors = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var degree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in entities)
            {
                neighbors[e.Name] = new(StringComparer.OrdinalIgnoreCase);
                degree[e.Name] = 0;
            }
            foreach (var rel in rels)
            {
                if (neighbors.ContainsKey(rel.Entity1) && neighbors.ContainsKey(rel.Entity2))
                {
                    neighbors[rel.Entity1].Add(rel.Entity2);
                    neighbors[rel.Entity2].Add(rel.Entity1);
                    degree[rel.Entity1]++;
                    degree[rel.Entity2]++;
                }
            }

            // 初始位置：大圆上均匀分布（Greedy-Append 排序）
            string hub = entities[0].Name;
            foreach (var kv in degree)
                if (kv.Value > degree.GetValueOrDefault(hub, 0)) hub = kv.Key;

            var ordered = GreedyAppendOrder(hub, entities, neighbors, degree);
            // hub 也放到圆上（不再放中心）
            var allEntities = new List<string> { hub };
            allEntities.AddRange(ordered);

            double initRadius = count * safeDistance / (2 * Math.PI);
            double cx = EntityStartX + initRadius + AttrRadius;
            double cy = EntityY + initRadius + AttrRadius;

            var posX = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var posY = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < allEntities.Count; i++)
            {
                double angle = 2.0 * Math.PI * i / allEntities.Count;
                posX[allEntities[i]] = cx + initRadius * Math.Cos(angle);
                posY[allEntities[i]] = cy + initRadius * Math.Sin(angle);
            }

            // ---- 力模拟迭代 ----
            double idealDist = safeDistance; // 理想边长
            double k2 = idealDist * idealDist;
            int iterations = 200;

            for (int iter = 0; iter < iterations; iter++)
            {
                double temperature = idealDist * (1.0 - (double)iter / iterations);

                var dispX = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                var dispY = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                foreach (var e in allEntities)
                {
                    dispX[e] = 0;
                    dispY[e] = 0;
                }

                // 排斥力：所有节点对
                for (int i = 0; i < allEntities.Count; i++)
                {
                    for (int j = i + 1; j < allEntities.Count; j++)
                    {
                        string u = allEntities[i], v = allEntities[j];
                        double dx = posX[u] - posX[v];
                        double dy = posY[u] - posY[v];
                        double dist = Math.Sqrt(dx * dx + dy * dy);
                        if (dist < 0.01) { dist = 0.01; dx = 0.01; }

                        double force = k2 / dist; // 排斥力 = k² / d
                        double fx = force * dx / dist;
                        double fy = force * dy / dist;

                        dispX[u] += fx; dispY[u] += fy;
                        dispX[v] -= fx; dispY[v] -= fy;
                    }
                }

                // 吸引力：有关系的节点对
                foreach (var rel in rels)
                {
                    if (!posX.ContainsKey(rel.Entity1) || !posX.ContainsKey(rel.Entity2)) continue;
                    string u = rel.Entity1, v = rel.Entity2;
                    double dx = posX[u] - posX[v];
                    double dy = posY[u] - posY[v];
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    if (dist < 0.01) continue;

                    double force = dist * dist / idealDist; // 吸引力 = d² / k
                    double fx = force * dx / dist;
                    double fy = force * dy / dist;

                    dispX[u] -= fx; dispY[u] -= fy;
                    dispX[v] += fx; dispY[v] += fy;
                }

                // 应用位移（温度限制最大位移）
                foreach (var e in allEntities)
                {
                    double dx = dispX[e], dy = dispY[e];
                    double len = Math.Sqrt(dx * dx + dy * dy);
                    if (len > 0.01)
                    {
                        double factor = Math.Min(len, temperature) / len;
                        posX[e] += dx * factor;
                        posY[e] += dy * factor;
                    }
                }
            }

            // ---- 重叠消除：确保最小距离 ----
            for (int pass = 0; pass < 50; pass++)
            {
                bool moved = false;
                for (int i = 0; i < allEntities.Count; i++)
                {
                    for (int j = i + 1; j < allEntities.Count; j++)
                    {
                        string u = allEntities[i], v = allEntities[j];
                        double dx = posX[u] - posX[v];
                        double dy = posY[u] - posY[v];
                        double dist = Math.Sqrt(dx * dx + dy * dy);
                        if (dist < safeDistance)
                        {
                            double push = (safeDistance - dist) / 2.0 + 0.1;
                            double nx = dx / Math.Max(dist, 0.01);
                            double ny = dy / Math.Max(dist, 0.01);
                            posX[u] += nx * push; posY[u] += ny * push;
                            posX[v] -= nx * push; posY[v] -= ny * push;
                            moved = true;
                        }
                    }
                }
                if (!moved) break;
            }

            // ---- 归一化：确保所有坐标为正 ----
            double minX = double.MaxValue, minY = double.MaxValue;
            foreach (var e in allEntities)
            {
                minX = Math.Min(minX, posX[e]);
                minY = Math.Min(minY, posY[e]);
            }
            double offsetX = EntityStartX + AttrRadius - minX + 1;
            double offsetY = EntityY + AttrRadius - minY + 1;

            foreach (var e in allEntities)
            {
                double attrAngle = Math.PI / 2; // 默认朝上，OptimizeAttrAngles 会调整
                result[e] = new EntityPlacement(posX[e] + offsetX, posY[e] + offsetY, attrAngle);
            }

            return result;
        }

        /// <summary>
        /// Greedy-Append 贪心排序算法
        /// 
        /// 从枢纽的邻居开始，每次选择与已放置实体连接最多的候选实体
        /// 追加到序列末尾 → 有关系的实体尽量相邻，减少圆上边交叉
        /// </summary>
        private static List<string> GreedyAppendOrder(
            string hub, List<ErEntity> entities,
            Dictionary<string, HashSet<string>> neighbors,
            Dictionary<string, int> degree)
        {
            var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { hub };
            var order = new List<string>();

            // 候选池：所有非枢纽实体
            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in entities)
            {
                if (!e.Name.Equals(hub, StringComparison.OrdinalIgnoreCase))
                    candidates.Add(e.Name);
            }

            // 从枢纽的度数最高的邻居开始
            string? first = null;
            int bestDeg = -1;
            foreach (var nb in neighbors[hub])
            {
                if (candidates.Contains(nb) && degree.GetValueOrDefault(nb, 0) > bestDeg)
                {
                    bestDeg = degree.GetValueOrDefault(nb, 0);
                    first = nb;
                }
            }
            // 如果枢纽没有邻居，取度数最高的候选
            if (first == null)
            {
                foreach (var c in candidates)
                {
                    if (degree.GetValueOrDefault(c, 0) > bestDeg)
                    {
                        bestDeg = degree.GetValueOrDefault(c, 0);
                        first = c;
                    }
                }
            }
            if (first == null && candidates.Count > 0) first = candidates.First();
            if (first != null)
            {
                order.Add(first);
                placed.Add(first);
                candidates.Remove(first);
            }

            // 贪心追加：每次选与已放置实体连接最多的候选
            while (candidates.Count > 0)
            {
                string? best = null;
                int bestScore = -1;

                foreach (var c in candidates)
                {
                    // 分数 = 与已放置实体的连接数（包括枢纽）
                    int score = 0;
                    foreach (var nb in neighbors.GetValueOrDefault(c, []))
                    {
                        if (placed.Contains(nb)) score++;
                    }
                    // 优先连接多的，平局时选度数高的
                    if (score > bestScore || (score == bestScore &&
                        degree.GetValueOrDefault(c, 0) > degree.GetValueOrDefault(best ?? "", 0)))
                    {
                        bestScore = score;
                        best = c;
                    }
                }

                if (best == null) best = candidates.First();
                order.Add(best);
                placed.Add(best);
                candidates.Remove(best);
            }

            return order;
        }

        /// <summary>
        /// 计算每个关系菱形的位置
        /// 放在两个连接实体的中线上，垂直偏移避免重叠
        /// </summary>
        private List<(double X, double Y)> CalculateDiamondPositions(
            ErDocument erDoc, Dictionary<string, EntityPlacement> layout)
        {
            var positions = new List<(double X, double Y)>();

            // 统计每对实体之间的关系数量（用于同对多关系的偏移）
            var pairCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var rel in erDoc.Relationships)
            {
                if (!layout.TryGetValue(rel.Entity1, out var p1) ||
                    !layout.TryGetValue(rel.Entity2, out var p2))
                {
                    positions.Add((0, 0));
                    continue;
                }

                // 中点
                double mx = (p1.X + p2.X) / 2.0;
                double my = (p1.Y + p2.Y) / 2.0;

                // 同对实体间多个关系时，垂直偏移
                string pairKey = string.Compare(rel.Entity1, rel.Entity2,
                    StringComparison.OrdinalIgnoreCase) < 0
                    ? $"{rel.Entity1}|{rel.Entity2}" : $"{rel.Entity2}|{rel.Entity1}";

                if (!pairCount.TryGetValue(pairKey, out int idx))
                    idx = 0;
                pairCount[pairKey] = idx + 1;

                // 垂直偏移（两实体连线的法向偏移）
                double dx = p2.X - p1.X;
                double dy = p2.Y - p1.Y;
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 0.001) len = 1;

                // 法向量（垂直于连线方向）
                double nx = -dy / len;
                double ny = dx / len;

                double offset = (idx - 0) * 1.5; // 每个额外关系偏移 1.5
                mx += nx * offset;
                my += ny * offset;

                positions.Add((mx, my));
            }

            return positions;
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
