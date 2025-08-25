using System.Drawing;
using System.Windows.Forms;
using WallpaperSync.Core;

namespace WallpaperSync.UI
{
    public class CustomMenuRenderer : ToolStripProfessionalRenderer
    {
        public CustomMenuRenderer() : base(new CustomColorTable())
        {
            Logger.Log("[CustomMenuRenderer] 自定义菜单渲染器初始化");
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            Logger.Log($"[CustomMenuRenderer] 渲染菜单项背景: {e.Item.Text}, 尺寸: {e.Item.Size}, ContentRect: {e.Item.ContentRectangle}");
            
            if (e.Item.Selected)
            {
                e.Graphics.FillRectangle(new SolidBrush(Color.FromArgb(51, 153, 255)), e.Item.ContentRectangle);
            }
            else
            {
                e.Graphics.FillRectangle(Brushes.White, e.Item.ContentRectangle);
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            // 使用更大的字体尺寸来匹配宽敞的菜单
            e.TextFont = new Font("Microsoft YaHei", 10F, FontStyle.Regular);
            e.TextColor = Color.Black;
            
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine;
            TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, e.TextRectangle, e.TextColor, flags);
        }
    }

    public class CustomColorTable : ProfessionalColorTable
    {
        public override Color MenuItemSelected => Color.FromArgb(51, 153, 255);
        public override Color MenuItemBorder => Color.FromArgb(51, 153, 255);
        public override Color MenuBorder => Color.Gray;
        public override Color MenuItemSelectedGradientBegin => Color.FromArgb(51, 153, 255);
        public override Color MenuItemSelectedGradientEnd => Color.FromArgb(51, 153, 255);
        public override Color MenuItemPressedGradientBegin => Color.FromArgb(41, 128, 204);
        public override Color MenuItemPressedGradientEnd => Color.FromArgb(41, 128, 204);
    }
}