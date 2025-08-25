using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using WallpaperSync.Core;
using WallpaperSync.Services;

namespace WallpaperSync.UI
{
    public class CustomTrayMenu : Form
    {
        private readonly ConfigService _configService;
        private readonly StartupService _startupService;
        private readonly WallpaperService _wallpaperService;
        private readonly PerformanceService _performanceService;
        private readonly Action<string, string, ToolTipIcon> _showNotification;
        
        private readonly int MenuItemHeight = 45;
        private readonly int MenuWidth = 280;
        private Color HoverColor = Color.FromArgb(230, 240, 255);
        private Color NormalColor = Color.White;
        private Color BorderColor = Color.FromArgb(180, 180, 180);
        private Form _currentSubMenu = null;
        private bool _subMenuActive = false;
        // 统一定时器管理 - 减少定时器数量和资源占用
        private System.Windows.Forms.Timer _closeCheckTimer = null;  // 主菜单边界检测  
        private System.Windows.Forms.Timer _subMenuCloseTimer = null;  // 子菜单延迟关闭
        private readonly object _timerLock = new object(); // 定时器操作线程安全

        public CustomTrayMenu(ConfigService configService, StartupService startupService, 
                             WallpaperService wallpaperService, PerformanceService performanceService,
                             Action<string, string, ToolTipIcon> showNotification = null)
        {
            _configService = configService;
            _startupService = startupService;
            _wallpaperService = wallpaperService;
            _performanceService = performanceService;
            _showNotification = showNotification;
            
            InitializeMenu();
            Logger.LogDebug("[CustomTrayMenu] 自定义托盘菜单初始化完成");
        }

        private void InitializeMenu()
        {
            // 窗体基本设置
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.StartPosition = FormStartPosition.Manual;
            this.BackColor = Color.White;
            this.Width = MenuWidth;
            
            // 防止窗体获取焦点，保持托盘悬停状态
            this.SetStyle(ControlStyles.Selectable, false);
            
            // 添加边框
            this.Paint += (s, e) =>
            {
                using (var pen = new Pen(BorderColor, 1))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, this.Width - 1, this.Height - 1);
                }
            };

            CreateMenuItems();
            
            // 点击窗体外部时关闭，但要考虑子菜单情况
            this.Deactivate += (s, e) => {
                // 如果子菜单处于活跃状态，不关闭主菜单
                if (!_subMenuActive)
                {
                    this.Hide();
                }
            };
            
            // 菜单隐藏时停止检查
            this.VisibleChanged += (s, e) => {
                if (!this.Visible)
                {
                    StopPeriodicCloseCheck();
                }
            };
        }

        private void CreateMenuItems()
        {
            var menuItems = new[]
            {
                new { Text = "强制同步", Action = new Action(() => {
                    Logger.LogDebug("[CustomTrayMenu] 用户点击强制同步");
                    var success = _wallpaperService.ManualSync();
                    var message = success ? "壁纸同步已执行完成" : "壁纸同步执行失败，请查看日志";
                    var icon = success ? ToolTipIcon.Info : ToolTipIcon.Warning;
                    
                    // 显示Windows通知
                    _showNotification?.Invoke("强制同步完成", message, icon);
                    
                    this.Hide();
                })},
                new { Text = "监控间隔", Action = (Action)null }, // 父菜单项，无直接点击事件
                // 日志级别功能已移除 - 现在通过编译时Debug/Release控制
                new { Text = "获取桌面信息", Action = new Action(() => {
                    Logger.LogDebug("[CustomTrayMenu] 用户点击获取桌面信息");
                    var info = _wallpaperService.GetVirtualDesktopInfo();
                    MessageBox.Show(info, "虚拟桌面信息", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    this.Hide();
                })},
                new { Text = "性能统计", Action = new Action(() => {
                    Logger.LogDebug("[CustomTrayMenu] 用户点击性能统计");
                    var stats = _performanceService.GetPerformanceInfo();
                    MessageBox.Show(stats, "性能统计信息", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    this.Hide();
                })},
                new { Text = "内存诊断", Action = new Action(() => {
                    Logger.LogDebug("[CustomTrayMenu] 用户点击内存诊断");
                    var diagnostic = _performanceService.GetMemoryDiagnosticInfo();
                    MessageBox.Show(diagnostic, "内存诊断报告", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    this.Hide();
                })},
                new { Text = _startupService.GetStatusText(), Action = new Action(() => {
                    Logger.LogDebug("[CustomTrayMenu] 用户切换开机自启状态");
                    var wasEnabled = _startupService.IsEnabled;
                    var success = _startupService.ToggleStartup();
                    if (success)
                    {
                        var newState = _startupService.IsEnabled;
                        var message = newState ? "开机自启已启用" : "开机自启已禁用";
                        _showNotification?.Invoke("设置已更新", message, ToolTipIcon.Info);
                        RefreshMenu();
                    }
                    else
                    {
                        _showNotification?.Invoke("设置失败", "开机自启设置更改失败", ToolTipIcon.Warning);
                    }
                })},
                new { Text = "", Action = (Action)null }, // 分隔线
                new { Text = "退出", Action = new Action(() => {
                    Logger.LogDebug("[CustomTrayMenu] 用户点击退出");
                    Application.Exit();
                })}
            };

            this.Controls.Clear();
            int y = 0;
            int separatorY = -1; // 记录分隔线位置

            foreach (var item in menuItems)
            {
                if (string.IsNullOrEmpty(item.Text))
                {
                    // 分隔线 - 只占用10px高度而不是完整MenuItemHeight
                    separatorY = y; // 记录分隔线的y坐标
                    var separator = new Panel
                    {
                        Location = new Point(10, y + 5), // 上下各留5px边距
                        Size = new Size(MenuWidth - 20, 1),
                        BackColor = Color.FromArgb(220, 220, 220)
                    };
                    this.Controls.Add(separator);
                    y += 10; // 只增加10px而不是完整的MenuItemHeight
                }
                else
                {
                    // 检查是否是分隔线之后的项（如退出）
                    bool useCheckboxLayout = (separatorY == -1); // 分隔线之前的项使用复选框布局
                    CreateMenuItem(item.Text, item.Action, y, useCheckboxLayout);
                    y += MenuItemHeight;
                }
            }

            this.Height = y;
            
            // 添加贯通的竖线分割线（从顶部到分隔线位置）
            if (separatorY > 0)
            {
                var separatorLine = new Panel
                {
                    Location = new Point(40, 0),
                    Size = new Size(1, separatorY), // 改回1px宽度
                    BackColor = Color.FromArgb(180, 180, 180) // 改为中等灰色，既清楚又不突兀
                };
                this.Controls.Add(separatorLine);
                separatorLine.BringToFront(); // 确保在最上层
                Logger.LogDebug($"[CustomTrayMenu] 添加竖线: 位置(40,0), 大小(1,{separatorY}), 颜色=中等灰色");
            }
            else
            {
                Logger.LogDebug($"[CustomTrayMenu] 未添加竖线: separatorY={separatorY}");
            }
            
            Logger.LogDebug($"[CustomTrayMenu] 菜单尺寸: {MenuWidth}x{this.Height}");
        }

        private void CreateMenuItem(string text, Action action, int y, bool useCheckboxLayout = true)
        {
            var menuItem = new Panel
            {
                Location = new Point(0, y),
                Size = new Size(MenuWidth, MenuItemHeight),
                BackColor = NormalColor,
                Cursor = Cursors.Hand
            };

            // 鼠标悬停效果函数
            Action<Color> setBackColor = (color) => menuItem.BackColor = color;

            Label label;
            
            if (useCheckboxLayout)
            {
                // 使用复选框布局（分隔线之前的菜单项）
                
                // 左侧复选框区域（固定40px宽度）
                var checkboxArea = new Panel
                {
                    Location = new Point(0, 0),
                    Size = new Size(40, MenuItemHeight),
                    BackColor = Color.Transparent
                };
                menuItem.Controls.Add(checkboxArea);

                // 如果是开机启动项，在复选框区域添加复选框
                if (text.Contains("开机") && text.Contains("启动"))
                {
                    var checkBox = new Label
                    {
                        Text = _startupService.IsEnabled ? "✓" : "",
                        Font = new Font("Microsoft YaHei", 12F, FontStyle.Bold),
                        ForeColor = Color.FromArgb(51, 153, 255),
                        Location = new Point(0, 0),
                        Size = new Size(40, MenuItemHeight),
                        TextAlign = ContentAlignment.MiddleCenter,
                        BackColor = Color.Transparent
                    };
                    checkboxArea.Controls.Add(checkBox);
                }

                // 文本标签 - 从45px开始
                label = new Label
                {
                    Text = text,
                    Font = new Font("Microsoft YaHei", 10F, FontStyle.Regular),
                    ForeColor = Color.Black,
                    Location = new Point(45, 0),
                    Size = new Size(MenuWidth - 75, MenuItemHeight), // 减去左侧45px和右侧30px
                    TextAlign = ContentAlignment.MiddleLeft,
                    AutoSize = false,
                    BackColor = Color.Transparent
                };
            }
            else
            {
                // 普通布局（分隔线之后的菜单项，如"退出"）- 与上方菜单项文字对齐
                label = new Label
                {
                    Text = text,
                    Font = new Font("Microsoft YaHei", 10F, FontStyle.Regular),
                    ForeColor = Color.Black,
                    Location = new Point(45, 0), // 与上方菜单项保持相同的45px起始位置
                    Size = new Size(MenuWidth - 75, MenuItemHeight), // 调整宽度以匹配上方菜单项
                    TextAlign = ContentAlignment.MiddleLeft,
                    AutoSize = false,
                    BackColor = Color.Transparent
                };
            }
            
            // 如果是监控间隔项，添加箭头指示器
            if (text == "监控间隔")
            {
                var arrow = new Label
                {
                    Text = "▶",
                    Font = new Font("Microsoft YaHei", 9F, FontStyle.Regular), // 缩小字体，约25%
                    ForeColor = Color.Gray,
                    Location = new Point(MenuWidth - 25, 0),
                    Size = new Size(25, MenuItemHeight),
                    TextAlign = ContentAlignment.MiddleCenter,
                    BackColor = Color.Transparent
                };
                menuItem.Controls.Add(arrow);
                
                if (text == "监控间隔")
                {
                    // 监控间隔悬停显示子菜单
                    menuItem.MouseEnter += (s, e) => {
                        setBackColor(HoverColor);
                        ShowIntervalSubMenu(menuItem);
                    };
                    label.MouseEnter += (s, e) => {
                        setBackColor(HoverColor);
                        ShowIntervalSubMenu(menuItem);
                    };
                    arrow.MouseEnter += (s, e) => {
                        setBackColor(HoverColor);
                        ShowIntervalSubMenu(menuItem);
                    };
                }
                // 日志级别功能已移除 - 现在通过编译时Debug/Release控制
                
                // 离开事件 - 使用延迟检测避免闪烁，同时考虑子菜单
                menuItem.MouseLeave += (s, e) => {
                    // 延迟检测，给鼠标移动到子菜单或其他子控件的时间
                    var delayTimer = new System.Windows.Forms.Timer { Interval = 50 };
                    delayTimer.Tick += (timer_s, timer_e) => {
                        delayTimer.Stop();
                        delayTimer.Dispose();
                        
                        // 检查鼠标是否真的离开了主菜单项区域
                        var mousePos = menuItem.PointToClient(Control.MousePosition);
                        var itemBounds = new Rectangle(0, 0, menuItem.Width, menuItem.Height);
                        bool inMainMenu = itemBounds.Contains(mousePos);
                        
                        // 检查鼠标是否在子菜单区域内
                        bool inSubMenu = false;
                        if (_currentSubMenu != null && _currentSubMenu.Visible)
                        {
                            var subMenuMousePos = _currentSubMenu.PointToClient(Control.MousePosition);
                            var subMenuBounds = new Rectangle(0, 0, _currentSubMenu.Width, _currentSubMenu.Height);
                            inSubMenu = subMenuBounds.Contains(subMenuMousePos);
                        }
                        
                        // 只有当鼠标既不在主菜单项也不在子菜单时才关闭
                        if (!inMainMenu && !inSubMenu)
                        {
                            setBackColor(NormalColor);
                            StartCloseTimer();
                        }
                    };
                    delayTimer.Start();
                };
                label.MouseLeave += (s, e) => {
                    // 延迟检测，给鼠标移动到子菜单或其他子控件的时间
                    var delayTimer = new System.Windows.Forms.Timer { Interval = 50 };
                    delayTimer.Tick += (timer_s, timer_e) => {
                        delayTimer.Stop();
                        delayTimer.Dispose();
                        
                        // 检查鼠标是否真的离开了主菜单项区域
                        var mousePos = menuItem.PointToClient(Control.MousePosition);
                        var itemBounds = new Rectangle(0, 0, menuItem.Width, menuItem.Height);
                        bool inMainMenu = itemBounds.Contains(mousePos);
                        
                        // 检查鼠标是否在子菜单区域内
                        bool inSubMenu = false;
                        if (_currentSubMenu != null && _currentSubMenu.Visible)
                        {
                            var subMenuMousePos = _currentSubMenu.PointToClient(Control.MousePosition);
                            var subMenuBounds = new Rectangle(0, 0, _currentSubMenu.Width, _currentSubMenu.Height);
                            inSubMenu = subMenuBounds.Contains(subMenuMousePos);
                        }
                        
                        // 只有当鼠标既不在主菜单项也不在子菜单时才关闭
                        if (!inMainMenu && !inSubMenu)
                        {
                            setBackColor(NormalColor);
                            StartCloseTimer();
                        }
                    };
                    delayTimer.Start();
                };
                arrow.MouseLeave += (s, e) => {
                    // 延迟检测，给鼠标移动到子菜单或其他子控件的时间
                    var delayTimer = new System.Windows.Forms.Timer { Interval = 50 };
                    delayTimer.Tick += (timer_s, timer_e) => {
                        delayTimer.Stop();
                        delayTimer.Dispose();
                        
                        // 检查鼠标是否真的离开了主菜单项区域
                        var mousePos = menuItem.PointToClient(Control.MousePosition);
                        var itemBounds = new Rectangle(0, 0, menuItem.Width, menuItem.Height);
                        bool inMainMenu = itemBounds.Contains(mousePos);
                        
                        // 检查鼠标是否在子菜单区域内
                        bool inSubMenu = false;
                        if (_currentSubMenu != null && _currentSubMenu.Visible)
                        {
                            var subMenuMousePos = _currentSubMenu.PointToClient(Control.MousePosition);
                            var subMenuBounds = new Rectangle(0, 0, _currentSubMenu.Width, _currentSubMenu.Height);
                            inSubMenu = subMenuBounds.Contains(subMenuMousePos);
                        }
                        
                        // 只有当鼠标既不在主菜单项也不在子菜单时才关闭
                        if (!inMainMenu && !inSubMenu)
                        {
                            setBackColor(NormalColor);
                            StartCloseTimer();
                        }
                    };
                    delayTimer.Start();
                };
            }
            else
            {
                // 普通菜单项的鼠标事件
                menuItem.MouseEnter += (s, e) => {
                    setBackColor(HoverColor);
                    Logger.LogDebug($"[CustomTrayMenu] 鼠标悬停: {text}");
                    
                    // 隐藏任何打开的子菜单
                    if (_currentSubMenu != null)
                    {
                        _currentSubMenu.Hide();
                        _currentSubMenu.Dispose();
                        _currentSubMenu = null;
                        _subMenuActive = false;
                    }
                };
                menuItem.MouseLeave += (s, e) => setBackColor(NormalColor);
                
                label.MouseEnter += (s, e) => {
                    setBackColor(HoverColor);
                    
                    // 隐藏任何打开的子菜单
                    if (_currentSubMenu != null)
                    {
                        _currentSubMenu.Hide();
                        _currentSubMenu.Dispose();
                        _currentSubMenu = null;
                        _subMenuActive = false;
                    }
                };
                label.MouseLeave += (s, e) => setBackColor(NormalColor);
            }

            menuItem.Controls.Add(label);
            
            // 点击事件
            if (action != null)
            {
                menuItem.Click += (s, e) => action();
                label.Click += (s, e) => action();
            }

            this.Controls.Add(menuItem);
        }

        private void ShowIntervalSubMenu(Panel parentItem)
        {
            // 如果子菜单已经存在且正确，不要重复创建
            if (_currentSubMenu != null && _subMenuActive)
            {
                Logger.LogDebug("[CustomTrayMenu] 监控间隔子菜单已存在，跳过创建");
                return;
            }
            
            // 隐藏之前的任何子菜单（不管是什么类型）
            if (_currentSubMenu != null)
            {
                _currentSubMenu.Hide();
                _currentSubMenu.Dispose();
                _currentSubMenu = null;
                _subMenuActive = false;
            }
            
            Logger.LogDebug("[CustomTrayMenu] 显示监控间隔子菜单");
            
            var subMenu = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                ShowInTaskbar = false,
                TopMost = true,
                StartPosition = FormStartPosition.Manual,
                BackColor = Color.White,
                Width = 300,  // 扩大宽width以适应文字
                Height = 4 * MenuItemHeight
            };
            
            // 添加边框
            subMenu.Paint += (s, e) =>
            {
                using (var pen = new Pen(BorderColor, 1))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, subMenu.Width - 1, subMenu.Height - 1);
                }
            };

            var intervals = new[]
            {
                new { Value = 200, Text = "200ms (实时监控)" },
                new { Value = 500, Text = "500ms (高频监控)" },
                new { Value = 1000, Text = "1000ms (中频监控)" },
                new { Value = 2000, Text = "2000ms (低频监控)" }
            };

            int subY = 0;
            foreach (var interval in intervals)
            {
                var subItem = new Panel
                {
                    Location = new Point(0, subY),
                    Size = new Size(300, MenuItemHeight),  // 匹配子菜单宽度300px
                    BackColor = NormalColor,
                    Cursor = Cursors.Hand
                };

                var subLabel = new Label
                {
                    Text = interval.Text,
                    Font = new Font("Microsoft YaHei", 9F, FontStyle.Regular),
                    ForeColor = Color.Black,
                    Location = new Point(15, 0),
                    Size = new Size(250, MenuItemHeight),  // 扩大文字区域到250px
                    TextAlign = ContentAlignment.MiddleLeft,
                    AutoSize = false,
                    BackColor = Color.Transparent
                };

                // 当前选中项显示复选框
                if (interval.Value == _configService.MonitoringInterval)
                {
                    var checkMark = new Label
                    {
                        Text = "✓",
                        Font = new Font("Microsoft YaHei", 10F, FontStyle.Bold),
                        ForeColor = Color.FromArgb(51, 153, 255),
                        Location = new Point(270, 0),  // 调整到300px宽度的右侧位置
                        Size = new Size(25, MenuItemHeight),  // 使用相同高度保证垄直对齐
                        TextAlign = ContentAlignment.MiddleCenter
                    };
                    subItem.Controls.Add(checkMark);
                }

                // 鼠标悬停效果
                subItem.MouseEnter += (s, e) => subItem.BackColor = HoverColor;
                subItem.MouseLeave += (s, e) => subItem.BackColor = NormalColor;
                subLabel.MouseEnter += (s, e) => subItem.BackColor = HoverColor;
                subLabel.MouseLeave += (s, e) => subItem.BackColor = NormalColor;

                // 点击事件
                int intervalValue = interval.Value; // 捕获局部变量
                string intervalText = interval.Text; // 捕获描述文本
                subItem.Click += (s, e) => {
                    Logger.LogDebug($"[CustomTrayMenu] 用户选择监控间隔: {intervalValue}ms");
                    _configService.SetMonitoringInterval(intervalValue);
                    
                    // 显示Windows通知
                    _showNotification?.Invoke("设置已更新", $"已更换为 {intervalText}", ToolTipIcon.Info);
                    
                    subMenu.Hide();
                    // 不关闭主菜单，让用户可以继续操作
                };
                subLabel.Click += (s, e) => {
                    Logger.LogDebug($"[CustomTrayMenu] 用户选择监控间隔: {intervalValue}ms");
                    _configService.SetMonitoringInterval(intervalValue);
                    
                    // 显示Windows通知
                    _showNotification?.Invoke("设置已更新", $"已更换为 {intervalText}", ToolTipIcon.Info);
                    
                    subMenu.Hide();
                    // 不关闭主菜单，让用户可以继续操作
                };

                subItem.Controls.Add(subLabel);
                subMenu.Controls.Add(subItem);
                subY += MenuItemHeight;
            }

            // 定位子菜单
            var parentLocation = parentItem.PointToScreen(Point.Empty);
            subMenu.Location = new Point(parentLocation.X + MenuWidth - 5, parentLocation.Y);
            
            // 设置当前子菜单
            _currentSubMenu = subMenu;
            _subMenuActive = true;
            
            // 子菜单失去焦点时关闭
            subMenu.Deactivate += (s, e) => {
                // 延迟关闭，给用户时间移动鼠标
                var closeTimer = new System.Windows.Forms.Timer { Interval = 200 };
                closeTimer.Tick += (timer_s, timer_e) => {
                    closeTimer.Stop();
                    closeTimer.Dispose();
                    
                    // 检查鼠标是否在主菜单或子菜单区域内
                    var mousePos = Control.MousePosition;
                    var mainMenuBounds = new Rectangle(this.Location, this.Size);
                    var subMenuBounds = new Rectangle(subMenu.Location, subMenu.Size);
                    
                    if (!mainMenuBounds.Contains(mousePos) && !subMenuBounds.Contains(mousePos))
                    {
                        subMenu.Hide();
                        subMenu.Dispose();
                        _currentSubMenu = null;
                        _subMenuActive = false;
                    }
                };
                closeTimer.Start();
            };
            
            // 子菜单关闭时清理状态
            subMenu.FormClosed += (s, e) => {
                _currentSubMenu = null;
                _subMenuActive = false;
            };
            
            subMenu.Show();
            // 子菜单也不获取焦点，保持托盘状态
        }
        
        // ShowLogLevelSubMenu 方法已删除 - 日志级别功能通过编译时Debug/Release控制

        private void RefreshMenu()
        {
            CreateMenuItems();
        }


        private void StartPeriodicCloseCheck()
        {
            lock (_timerLock)
            {
                // 停止之前的定期检查
                StopPeriodicCloseCheck();
                
                // 优化定时器频率：从100ms改为300ms，减少70%的CPU占用
                _closeCheckTimer = new System.Windows.Forms.Timer { Interval = 300 };
                Logger.LogDebug("[CustomTrayMenu] 启动边界检查定时器，间隔优化为300ms");
            }
            _closeCheckTimer.Tick += (s, e) => {
                var mousePos = Control.MousePosition;
                
                // 检查鼠标是否在菜单区域内
                bool mouseInMenuArea = false;
                
                // 检查主菜单区域
                if (this.Visible)
                {
                    var mainMenuBounds = new Rectangle(this.Location, this.Size);
                    mouseInMenuArea = mainMenuBounds.Contains(mousePos);
                }
                
                // 检查子菜单区域
                if (!mouseInMenuArea && _currentSubMenu != null && _currentSubMenu.Visible)
                {
                    var subMenuBounds = new Rectangle(_currentSubMenu.Location, _currentSubMenu.Size);
                    mouseInMenuArea = subMenuBounds.Contains(mousePos);
                }
                
                // 如果鼠标不在任何菜单区域内，立即关闭
                if (!mouseInMenuArea)
                {
                    StopPeriodicCloseCheck();
                    this.Hide();
                    if (_currentSubMenu != null)
                    {
                        _currentSubMenu.Hide();
                        _currentSubMenu.Dispose();
                        _currentSubMenu = null;
                        _subMenuActive = false;
                    }
                }
            };
            _closeCheckTimer.Start();
        }

        private void StopPeriodicCloseCheck()
        {
            lock (_timerLock)
            {
                if (_closeCheckTimer != null)
                {
                    _closeCheckTimer.Stop();
                    _closeCheckTimer.Dispose();
                    _closeCheckTimer = null;
                    Logger.LogDebug("[CustomTrayMenu] 边界检查定时器已停止");
                }
            }
        }

        private void StartCloseTimer()
        {
            lock (_timerLock)
            {
                if (!_subMenuActive || _currentSubMenu == null)
                    return;

                // 取消现有的关闭定时器
                CancelCloseTimer();

                // 创建新的关闭定时器 - 保持200ms间隔，平衡响应性和性能
                _subMenuCloseTimer = new System.Windows.Forms.Timer { Interval = 200 };
                Logger.LogDebug("[CustomTrayMenu] 启动子菜单关闭定时器");
            }
            _subMenuCloseTimer.Tick += (sender, args) => {
                _subMenuCloseTimer.Stop();
                _subMenuCloseTimer.Dispose();
                _subMenuCloseTimer = null;
                
                if (_currentSubMenu != null)
                {
                    _currentSubMenu.Hide();
                    _currentSubMenu.Dispose();
                    _currentSubMenu = null;
                    _subMenuActive = false;
                }
            };
            _subMenuCloseTimer.Start();
        }

        private void CancelCloseTimer()
        {
            lock (_timerLock)
            {
                if (_subMenuCloseTimer != null)
                {
                    _subMenuCloseTimer.Stop();
                    _subMenuCloseTimer.Dispose();
                    _subMenuCloseTimer = null;
                    Logger.LogDebug("[CustomTrayMenu] 子菜单关闭定时器已取消");
                }
            }
        }
        public void ShowMenu(Point location)
        {
            // 调整位置确保菜单在屏幕内
            var screen = Screen.FromPoint(location);
            var x = location.X;
            var y = location.Y;

            if (x + MenuWidth > screen.WorkingArea.Right)
                x = screen.WorkingArea.Right - MenuWidth;
            if (y + this.Height > screen.WorkingArea.Bottom)
                y = location.Y - this.Height;

            this.Location = new Point(x, y);
            this.Show();
            // 移除Activate()避免抢夺焦点，保持托盘悬停状态
            
            // 延迟启动边界检查，给用户时间从托盘移动到菜单
            var gracePeriodTimer = new System.Windows.Forms.Timer { Interval = 1500 }; // 1.5秒宽限期
            gracePeriodTimer.Tick += (s, e) => {
                gracePeriodTimer.Stop();
                gracePeriodTimer.Dispose();
                StartPeriodicCloseCheck();
            };
            gracePeriodTimer.Start();
            
            Logger.LogDebug($"[CustomTrayMenu] 显示菜单于位置: ({x}, {y}), 菜单尺寸: {MenuWidth}x{this.Height}");
        }

        #region IDisposable Implementation

        private bool _disposed = false;

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    Logger.Log("[CustomTrayMenu] 开始释放菜单资源");

                    // 释放定时器资源 - 确保所有定时器完全清理
                    lock (_timerLock)
                    {
                        StopPeriodicCloseCheck();
                        CancelCloseTimer();
                        Logger.Log("[CustomTrayMenu] 定时器资源已完全释放");
                    }

                    // 释放子菜单
                    if (_currentSubMenu != null)
                    {
                        _currentSubMenu.Hide();
                        _currentSubMenu.Dispose();
                        _currentSubMenu = null;
                    }

                    Logger.Log("[CustomTrayMenu] 菜单资源释放完成");
                }

                _disposed = true;
            }

            base.Dispose(disposing);
        }

        #endregion
    }
}