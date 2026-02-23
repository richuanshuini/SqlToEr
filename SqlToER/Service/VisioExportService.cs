using System.Runtime.InteropServices;
using SqlToER.Model;
using Visio = Microsoft.Office.Interop.Visio;

namespace SqlToER.Service
{
    /// <summary>
    /// Visio 导出服务 — 纯 DBCHEN 一套模具
    /// 属性/主键属性是 1D 形状，通过 EndX GlueTo 实体 PinX 连接
    /// </summary>
    public class VisioExportService
    {
        private static readonly string[] ChenStencilNames =
            ["DBCHEN_M.VSSX", "DBCHEN_U.VSSX"];

        public void ExportToVsdx(ErDocument erDoc, string savePath,
            TemplateLayout? tpl = null,
            Action<string>? onStatus = null,
            LayoutTier? overrideTier = null)
        {
            Visio.InvisibleApp? app = null;
            Visio.Document? doc = null;
            Visio.Document? templateStencil = null;
            Visio.Document? chenStencil = null;

            try
            {
                onStatus?.Invoke("正在启动 Visio...");

                // 关闭残留 Visio 进程，防止文件锁定错误
                try
                {
                    foreach (var p in System.Diagnostics.Process.GetProcessesByName("VISIO"))
                    {
                        try { p.Kill(); p.WaitForExit(3000); } catch { }
                    }
                }
                catch { }

                app = new Visio.InvisibleApp();
                doc = app.Documents.Add("");
                var page = doc.Pages[1];

                onStatus?.Invoke("正在加载模具...");

                // 模板模具（可选）
                if (tpl != null && !string.IsNullOrEmpty(tpl.TemplatePath))
                {
                    try
                    {
                        templateStencil = app.Documents.OpenEx(
                            tpl.TemplatePath,
                            (short)Visio.VisOpenSaveArgs.visOpenRO);
                    }
                    catch { }
                }

                // Chen ER 模具
                chenStencil = OpenStencil(app, ChenStencilNames);

                // 查找 Master
                var entityMaster = FindMaster(chenStencil, ["Entity", "实体"]);
                var attrMaster = FindMaster(chenStencil, ["Attribute", "属性"]);
                var relMaster = FindMaster(chenStencil, ["Relationship", "关系"]);
                var connMaster = FindMaster(chenStencil, ["Relationship connector", "关系连接线"]);

                onStatus?.Invoke($"模具：{entityMaster.Name}/{attrMaster.Name}/{relMaster.Name}/{connMaster.Name}");

                var painter = new ErDiagramPainter(page, entityMaster, attrMaster, relMaster, connMaster);

                if (tpl != null)
                    painter.ApplyTemplateSizes(tpl);

                painter.DrawErDiagram(erDoc, onStatus, overrideTier);

                CloseDoc(ref templateStencil);
                CloseDoc(ref chenStencil);

                onStatus?.Invoke("正在保存文件...");
                doc.SaveAs(savePath);
                onStatus?.Invoke("✅ 导出完成");
            }
            catch (COMException ex) when (ex.HResult == unchecked((int)0x80040154))
            {
                throw new InvalidOperationException("未检测到 Microsoft Visio，请先安装。", ex);
            }
            finally
            {
                CloseDoc(ref templateStencil);
                CloseDoc(ref chenStencil);
                try { doc?.Close(); } catch { }
                try { app?.Quit(); } catch { }
                ReleaseComObject(doc);
                ReleaseComObject(app);
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        private static Visio.Master FindMaster(Visio.Document stencil, string[] keywords)
        {
            foreach (var kw in keywords)
            {
                try { return stencil.Masters[kw]; }
                catch (COMException) { }

                foreach (Visio.Master m in stencil.Masters)
                {
                    if ((m.NameU ?? "").Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                        (m.Name ?? "").Contains(kw, StringComparison.OrdinalIgnoreCase))
                        return m;
                }
            }
            return stencil.Masters[1]; // 兜底
        }

        private static Visio.Document OpenStencil(Visio.InvisibleApp app, string[] names)
        {
            foreach (var name in names)
            {
                try
                {
                    return app.Documents.OpenEx(name,
                        (short)Visio.VisOpenSaveArgs.visOpenRO);
                }
                catch (COMException) { }
            }
            throw new InvalidOperationException(
                $"找不到模具（{string.Join(" / ", names)}），请检查 Visio 安装。");
        }

        private static void CloseDoc(ref Visio.Document? d)
        {
            if (d == null) return;
            try { d.Saved = true; d.Close(); } catch { }
            ReleaseComObject(d);
            d = null;
        }

        private static void ReleaseComObject(object? obj)
        {
            if (obj == null) return;
            try { Marshal.ReleaseComObject(obj); } catch { }
        }
    }
}
