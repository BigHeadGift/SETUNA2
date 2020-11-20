﻿namespace SETUNA.Main
{
    using SETUNA.Main.Option;
    using SETUNA.Main.Other;
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Drawing;
    using System.Drawing.Imaging;
    using System.Runtime.CompilerServices;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Threading;
    using System.Windows.Forms;

    public sealed class CaptureForm : Form
    {
        private List<IntPtr> aryHWnd;
        private bool blAreaVisible;
        private bool blDrag;
        private Bitmap bmpClip;
        private const int CAPTUREBLT = 0x40000000;
        public CaptureClosedDelegate CaptureClosedHandler;
        private IContainer components;
        private const int GW_HWNDNEXT = 2;
        private const int GW_OWNER = 4;
        private Size ptClipSize;
        private Point ptClipStart;
        private Point ptEnd;
        private Point ptPrevEnd;
        private Point ptStart;
        private static Rectangle rctLast = new Rectangle(0, 0, 0, 0);
        private static Form selArea;
        private static CaptureSelLine selLineHor1;
        private static CaptureSelLine selLineHor2;
        private static CaptureSelLine selLineVer1;
        private static CaptureSelLine selLineVer2;
        private const int SRCCOPY = 0xcc0020;
        private System.Windows.Forms.Timer timer1;
        private Thread trd;
        private LayerInfo mLayerInfo;

        private static Image imgSnap;

        private Screen targetScreen
        {
            get
            {
                var tCurrentScreen = Screen.FromPoint(Cursor.Position);
                return tCurrentScreen ?? Screen.PrimaryScreen;
            }
        }

        public Size screenNewSize
        {
            get
            {
                var tTarget = targetScreen;
                var tScale = DPIUtils.GetPrimaryDpi() / DPIUtils.GetDpiByScreen(tTarget);
                var tSize = new Size((int)(tTarget.Bounds.Width / tScale), (int)(tTarget.Bounds.Height / tScale));
                return tSize;
            }
        }

        public CaptureForm(SetunaOption.SetunaOptionData opt)
        {
            this.InitializeComponent();
            ImgSnap = new Bitmap(this.screenNewSize.Width, this.screenNewSize.Height, PixelFormat.Format24bppRgb);
            selArea = new Form();
            selArea.AutoScaleMode = AutoScaleMode.None;
            selArea.BackColor = Color.Blue;
            selArea.BackgroundImageLayout = ImageLayout.None;
            selArea.ControlBox = false;
            selArea.FormBorderStyle = FormBorderStyle.None;
            selArea.MaximizeBox = false;
            selArea.MinimizeBox = false;
            selArea.MinimumSize = new Size(1, 1);
            selArea.ClientSize = new Size(1, 1);
            selArea.ShowIcon = false;
            selArea.ShowInTaskbar = false;
            selArea.SizeGripStyle = SizeGripStyle.Hide;
            selArea.StartPosition = FormStartPosition.Manual;
            selArea.Text = typeof(CaptureForm).Name;
            selArea.TopMost = true;
            selArea.Left = 0;
            selArea.Top = 0;
            selArea.Width = 1;
            selArea.Height = 1;
            selArea.Visible = false;
            base.AddOwnedForm(selArea);
            selLineHor1 = new CaptureSelLine(SelLineType.Horizon, opt.SelectLineSolid, opt.SelectLineColor);
            base.AddOwnedForm(selLineHor1);
            selLineHor1.Show(this);
            selLineHor2 = new CaptureSelLine(SelLineType.Horizon, opt.SelectLineSolid, opt.SelectLineColor);
            base.AddOwnedForm(selLineHor2);
            selLineHor2.Show(this);
            selLineVer1 = new CaptureSelLine(SelLineType.Vertical, opt.SelectLineSolid, opt.SelectLineColor);
            base.AddOwnedForm(selLineVer1);
            selLineVer1.Show(this);
            selLineVer2 = new CaptureSelLine(SelLineType.Vertical, opt.SelectLineSolid, opt.SelectLineColor);
            base.AddOwnedForm(selLineVer2);
            selLineVer2.Show(this);
            selLineHor1.Visible = false;
            selLineHor2.Visible = false;
            selLineVer1.Visible = false;
            selLineVer2.Visible = false;
            selArea.Visible = false;
            base.Opacity = 0.99000000953674316;
        }

        [DllImport("gdi32.dll")]
        private static extern int BitBlt(IntPtr hDestDC, int x, int y, int nWidth, int nHeight, IntPtr hSrcDC, int xSrc, int ySrc, int dwRop);
        private void CancelForm()
        {
            base.DialogResult = DialogResult.Cancel;
            this.EndCapture();
        }

        private void CaptureForm_FormClosing(object sender, FormClosingEventArgs e)
        {
        }

        private void CaptureForm_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == '0')
            {
                Console.WriteLine("Captureform KeyPress Start---");
                Rectangle rctLast = CaptureForm.rctLast;
                this.EntryCapture(new Point(rctLast.Left, rctLast.Top), new Point(rctLast.Right, rctLast.Bottom));
                Console.WriteLine("Captureform KeyPress End---");
            }
            else if (e.KeyChar == '\x001b')
            {
                base.DialogResult = DialogResult.Cancel;
                this.EndCapture();
            }
        }

        private void CaptureForm_KeyUp(object sender, KeyEventArgs e)
        {
            if ((e.Control && (e.KeyCode == Keys.V)) && Clipboard.ContainsImage())
            {
                this.ptClipStart = Cursor.Position;
                this.bmpClip = (Bitmap)Clipboard.GetImage();
                base.DialogResult = DialogResult.OK;
                this.EndCapture();
            }
        }

        private void CaptureForm_MouseDown(object sender, MouseEventArgs e)
        {
            this.ptStart.X = this.targetScreen.Bounds.X + e.Location.X;
            this.ptStart.Y = this.targetScreen.Bounds.Y + e.Location.Y;
            this.DrawSelectArea(0, 0, 1, 1, BoundsSpecified.Size);
            this.blDrag = true;
        }

        private void CaptureForm_MouseMove(object sender, MouseEventArgs e)
        {
            if (this.blDrag)
            {
                this.DrawSelRect();
            }
            else
            {
                this.DrawSelectArea(0, 0, 1, 1, BoundsSpecified.All);
            }
        }

        private void CaptureForm_MouseUp(object sender, MouseEventArgs e)
        {
            this.ptEnd.X = this.targetScreen.Bounds.X + e.X;
            this.ptEnd.Y = this.targetScreen.Bounds.Y + e.Y;
            this.blDrag = false;
            this.EntryCapture(this.ptStart, this.ptEnd);
        }

        private void CaptureForm_Paint(object sender, PaintEventArgs e)
        {
            if (ImgSnap != null)
            {
                e.Graphics.DrawImageUnscaled(ImgSnap, 0, 0);
            }
        }

        public static bool CopyFromScreen(Image img, Point location)
        {
            bool flag = true;
            IntPtr zero = IntPtr.Zero;
            try
            {
                zero = GetDC(IntPtr.Zero);
                using (Graphics graphics = Graphics.FromImage(img))
                {
                    IntPtr hDestDC = IntPtr.Zero;
                    try
                    {
                        try
                        {
                            hDestDC = graphics.GetHdc();
                            BitBlt(hDestDC, 0, 0, img.Width, img.Height, zero, location.X, location.Y, 0x40cc0020);
                        }
                        catch (Exception exception)
                        {
                            throw exception;
                        }
                        return flag;
                    }
                    finally
                    {
                        if (hDestDC != IntPtr.Zero)
                        {
                            graphics.ReleaseHdc(hDestDC);
                        }
                    }
                    return flag;
                }
            }
            catch (Exception exception2)
            {
                Console.WriteLine(exception2.Message);
                flag = false;
            }
            finally
            {
                if (zero != IntPtr.Zero)
                {
                    DeleteDC(zero);
                }
            }
            return flag;
        }

        private void CreateClip(Point pt, Size size)
        {
            if (this.bmpClip != null)
            {
                this.bmpClip.Dispose();
            }
            this.bmpClip = new Bitmap(size.Width, size.Height, PixelFormat.Format24bppRgb);
            using (Graphics graphics = Graphics.FromImage(this.bmpClip))
            {
                graphics.DrawImageUnscaled(ImgSnap, -(pt.X - this.targetScreen.Bounds.X), -(pt.Y - this.targetScreen.Bounds.Y));
            }
        }

        [DllImport("Gdi32.dll")]
        private static extern IntPtr CreateDC(string Display, string c, object b, object a);
        [DllImport("Gdi32.dll")]
        private static extern bool DeleteDC(IntPtr handle);
        protected override void Dispose(bool disposing)
        {
            if (disposing && (this.components != null))
            {
                this.components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void DrawSelectArea(int x1, int y1, int x2, int y2, BoundsSpecified bound)
        {
            bool flag;
            Rectangle rectangle = new Rectangle(selArea.Location, selArea.Size);
            if (((bound & BoundsSpecified.Size) != BoundsSpecified.None) && this.blAreaVisible)
            {
                rectangle.Width = x2;
                rectangle.Height = y2;
            }
            if (((bound & BoundsSpecified.Location) != BoundsSpecified.None) && this.blAreaVisible)
            {
                rectangle.X = x1;
                rectangle.Y = y1;
            }
            if (((x1 == 0) && (y1 == 0)) && ((x2 == 1) && (y2 == 1)))
            {
                flag = false;
            }
            else
            {
                flag = true;
            }
            selLineHor1.Visible = flag;
            selLineHor2.Visible = flag;
            selLineVer1.Visible = flag;
            selLineVer2.Visible = flag;
            selArea.Visible = flag;
            selLineHor1.SetSelSize(x1 - this.targetScreen.Bounds.X, x2);
            if (selLineHor1.Top != y1)
            {
                selLineHor1.Top = y1;
            }
            selLineHor2.SetSelSize(x1 - this.targetScreen.Bounds.X, x2);
            if (selLineHor2.Top != (y1 + y2))
            {
                selLineHor2.Top = y1 + y2;
            }
            selLineVer1.SetSelSize(y1 - this.targetScreen.Bounds.Y, y2);
            if (selLineVer1.Left != x1)
            {
                selLineVer1.Left = x1;
            }
            selLineVer2.SetSelSize(y1 - this.targetScreen.Bounds.Y, y2);
            if (selLineVer2.Left != (x1 + x2))
            {
                selLineVer2.Left = x1 + x2;
            }
            if (this.blAreaVisible)
            {
                try
                {
                    selArea.SetBounds(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height, BoundsSpecified.All);
                    selArea.Refresh();
                }
                catch (Exception exception)
                {
                    Console.WriteLine("CaptureForm DrawSelectArea Exception: " + exception.Message);
                }
            }
        }

        private void DrawSelRect()
        {
            Point point = new Point();
            Point point2 = new Point();
            this.ptEnd = new Point(Cursor.Position.X, Cursor.Position.Y);
            if ((this.ptEnd.X != this.ptPrevEnd.X) || (this.ptEnd.Y != this.ptPrevEnd.Y))
            {
                this.ptPrevEnd.X = this.ptEnd.X;
                this.ptPrevEnd.Y = this.ptEnd.Y;
                point.X = Math.Min(this.ptStart.X, this.ptEnd.X);
                point.Y = Math.Min(this.ptStart.Y, this.ptEnd.Y);
                point2.X = Math.Max(this.ptStart.X, this.ptEnd.X);
                point2.Y = Math.Max(this.ptStart.Y, this.ptEnd.Y);
                this.DrawSelectArea(point.X, point.Y, point2.X - point.X, point2.Y - point.Y, BoundsSpecified.All);
            }
        }

        private void EndCapture()
        {
            this.Hide();
            if (this.CaptureClosedHandler != null)
            {
                this.CaptureClosedHandler(this);
            }
        }

        public void EntryCapture(Point lptStart, Point lptEnd)
        {
            try
            {
                Console.WriteLine("EntryCapture Start---");
                Console.WriteLine(lptStart.ToString() + ", " + lptEnd.ToString());
                Point pt = new Point(Math.Min(lptStart.X, lptEnd.X), Math.Min(lptStart.Y, lptEnd.Y));
                this.ptClipStart = pt;
                Size size = new Size(Math.Abs((int)(lptStart.X - lptEnd.X)), Math.Abs((int)(lptStart.Y - lptEnd.Y)));
                this.ptClipSize = size;
                if ((size.Width < 10) || (size.Height < 10))
                {
                    base.DialogResult = DialogResult.Cancel;
                }
                else
                {
                    this.CreateClip(pt, size);
                    base.DialogResult = DialogResult.OK;
                    rctLast = new Rectangle(pt, size);
                }
                Console.WriteLine("EntryCapture End---");
                this.EndCapture();
            }
            catch (Exception exception)
            {
                Console.WriteLine("EntryCaptureException: " + exception.Message);
                Console.WriteLine("");
            }
        }

        [DllImport("user32.dll")]
        private static extern int EnumWindows(EnumWindowsCallBack lpFunc, int lParam);
        ~CaptureForm()
        {
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("User32.Dll")]
        private static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll", EntryPoint = "GetWindow")]
        private static extern IntPtr GetNextWindow(IntPtr hWnd, uint wCmd);
        [DllImport("user32.dll")]
        private static extern IntPtr GetTopWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint cCmd);
        private void GetWindowInfo()
        {
            this.aryHWnd = new List<IntPtr>();
            IntPtr topWindow = GetTopWindow(GetDesktopWindow());
            while ((topWindow = GetNextWindow(topWindow, 2)) != IntPtr.Zero)
            {
                if ((IsWindowVisible(topWindow) != 0) && (GetWindow(topWindow, 4) == IntPtr.Zero))
                {
                    this.aryHWnd.Add(topWindow);
                }
            }
        }

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCnt);
        public void Hide()
        {
            Console.WriteLine("Hide Start---");
            selArea.Hide();
            selLineHor1.Hide();
            selLineHor2.Hide();
            selLineVer1.Hide();
            selLineVer2.Hide();
            selArea.SetBounds(this.targetScreen.Bounds.X, this.targetScreen.Bounds.Y, 1, 1);
            selLineHor1.SetBounds(this.targetScreen.Bounds.X, this.targetScreen.Bounds.Y - 10, this.screenNewSize.Width, 1);
            selLineHor2.SetBounds(this.targetScreen.Bounds.X, this.targetScreen.Bounds.Y - 10, this.screenNewSize.Width, 1);
            selLineVer1.SetBounds(this.targetScreen.Bounds.X - 10, this.targetScreen.Bounds.Y, 1, this.screenNewSize.Height);
            selLineVer2.SetBounds(this.targetScreen.Bounds.X - 10, this.targetScreen.Bounds.Y, 1, selLineVer2.Height = this.screenNewSize.Height);
            base.Hide();
            Console.WriteLine("Hide end---");
        }

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.timer1 = new System.Windows.Forms.Timer(this.components);
            this.SuspendLayout();
            // 
            // timer1
            // 
            this.timer1.Interval = 250;
            this.timer1.Tick += new System.EventHandler(this.timer1_Tick);
            // 
            // CaptureForm
            // 
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            this.ClientSize = new System.Drawing.Size(681, 598);
            this.Cursor = System.Windows.Forms.Cursors.Cross;
            this.DoubleBuffered = true;
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.KeyPreview = true;
            this.Margin = new System.Windows.Forms.Padding(7);
            this.Name = "CaptureForm";
            this.ShowInTaskbar = false;
            this.Text = "CaptureForm";
            this.TopMost = true;
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.CaptureForm_FormClosing);
            this.Paint += new System.Windows.Forms.PaintEventHandler(this.CaptureForm_Paint);
            this.KeyPress += new System.Windows.Forms.KeyPressEventHandler(this.CaptureForm_KeyPress);
            this.KeyUp += new System.Windows.Forms.KeyEventHandler(this.CaptureForm_KeyUp);
            this.MouseDown += new System.Windows.Forms.MouseEventHandler(this.CaptureForm_MouseDown);
            this.MouseMove += new System.Windows.Forms.MouseEventHandler(this.CaptureForm_MouseMove);
            this.MouseUp += new System.Windows.Forms.MouseEventHandler(this.CaptureForm_MouseUp);
            this.ResumeLayout(false);
        }

        protected override void OnLoad(EventArgs e)
        {
            mLayerInfo = new LayerInfo(this);
        }

        protected override void OnClosed(EventArgs e)
        {
            mLayerInfo.Dispose();
        }

        [DllImport("user32.dll")]
        private static extern int IsWindowVisible(IntPtr hWnd);
        private void LineRefresh()
        {
            selLineVer1.Refresh();
            selLineVer2.Refresh();
            selLineHor1.Refresh();
            selLineHor2.Refresh();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (this.bmpClip != null)
            {
                this.bmpClip.Dispose();
            }
            this.bmpClip = null;
            Console.WriteLine("打开截取");
            base.OnClosing(e);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
        }

        [DllImport("user32.dll")]
        private static extern IntPtr ReleaseDC(IntPtr hwnd, IntPtr hdc);
        private void SelectWindowRect(Point ptMouse)
        {
            if (this.aryHWnd != null)
            {
                foreach (IntPtr ptr in this.aryHWnd)
                {
                    RECT rect;
                    if (((GetWindowRect(ptr, out rect) && (rect.left <= ptMouse.X)) && ((ptMouse.X <= rect.right) && (rect.top <= ptMouse.Y))) && (ptMouse.Y <= rect.bottom))
                    {
                        this.DrawSelectArea(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top, BoundsSpecified.All);
                        break;
                    }
                }
            }
        }

        public void ShowCapture(SetunaOption.SetunaOptionData opt)
        {
            if (ImgSnap != null)
            {
                if ((this.screenNewSize.Width != ImgSnap.Width) || (this.screenNewSize.Height != ImgSnap.Height))
                {
                    ImgSnap = new Bitmap(this.screenNewSize.Width, this.screenNewSize.Height, PixelFormat.Format24bppRgb);
                }
                this.trd = new Thread(new ThreadStart(this.ThreadTask));
                this.trd.IsBackground = true;
                this.trd.Start();
                Console.WriteLine(string.Concat(new object[] { "10 - ", DateTime.Now.ToString(), " ", DateTime.Now.Millisecond }));
                Console.WriteLine(string.Concat(new object[] { "11 - ", DateTime.Now.ToString(), " ", DateTime.Now.Millisecond }));
                this.blAreaVisible = opt.SelectAreaTransparent != 100;
                selArea.Opacity = 1f - (((float)opt.SelectAreaTransparent) / 100f);
                selArea.BackColor = opt.SelectBackColor;
                if (!selArea.Visible)
                {
                    selArea.Show(this);
                }
                Console.WriteLine(string.Concat(new object[] { "12 - ", DateTime.Now.ToString(), " ", DateTime.Now.Millisecond }));
                this.SetBoundsCore(this.targetScreen.Bounds.X, this.targetScreen.Bounds.Y, this.screenNewSize.Width, this.screenNewSize.Height, BoundsSpecified.All);
                Console.WriteLine(string.Concat(new object[] { "13 - ", DateTime.Now.ToString(), " ", DateTime.Now.Millisecond }));
                selLineHor1.SetPen(opt.SelectLineSolid, opt.SelectLineColor);
                selLineHor1.SetBounds(this.targetScreen.Bounds.X, this.targetScreen.Bounds.Y - 10, this.screenNewSize.Width, 1);
                if (!selLineHor1.Visible)
                {
                    selLineHor1.Show(this);
                }
                selLineHor2.SetPen(opt.SelectLineSolid, opt.SelectLineColor);
                selLineHor2.SetBounds(this.targetScreen.Bounds.X, this.targetScreen.Bounds.Y - 10, this.screenNewSize.Width, 1);
                if (!selLineHor2.Visible)
                {
                    selLineHor2.Show(this);
                }
                selLineVer1.SetPen(opt.SelectLineSolid, opt.SelectLineColor);
                selLineVer1.SetBounds(this.targetScreen.Bounds.X - 10, this.targetScreen.Bounds.Y, 1, this.screenNewSize.Height);
                if (!selLineVer1.Visible)
                {
                    selLineVer1.Show(this);
                }
                selLineVer2.SetPen(opt.SelectLineSolid, opt.SelectLineColor);
                selLineVer2.SetBounds(this.targetScreen.Bounds.X - 10, this.targetScreen.Bounds.Y, 1, selLineVer2.Height = this.screenNewSize.Height);
                if (!selLineVer2.Visible)
                {
                    selLineVer2.Show(this);
                }
                Console.WriteLine(string.Concat(new object[] { "14 - ", DateTime.Now.ToString(), " ", DateTime.Now.Millisecond }));

                Thread.Sleep(1);
                Cursor.Current = Cursors.Cross;
                Cursor.Clip = this.targetScreen.Bounds;
            }
        }

        private void ShowForm()
        {
            base.Opacity = 0.0099999997764825821;
            base.Visible = true;
            base.Opacity = 0.0099999997764825821;
            this.Refresh();
            Thread.Sleep(10);
            base.Opacity = 0.99000000953674316;
        }

        private void ThreadTask()
        {
            var tScreen = targetScreen;
            var tScreenSize = screenNewSize;
            var tDpi = DPIUtils.GetDpiByScreen(tScreen);

            var tBitmap = new Bitmap(tScreenSize.Width, tScreenSize.Height, PixelFormat.Format24bppRgb);
            tBitmap.SetResolution(tDpi * 96, tDpi * 96);
            ImgSnap = tBitmap;

            if (CopyFromScreen(ImgSnap, new Point(this.targetScreen.Bounds.X, this.targetScreen.Bounds.Y)))
            {
                base.Invoke(new ShowFormDelegate(this.ShowForm));
            }
            else
            {
                base.Invoke(new ShowFormDelegate(this.CancelForm));
            }
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            CaptureSelLine.AddDashOffset();
            selLineVer1.Refresh();
            selLineVer2.Refresh();
            selLineHor1.Refresh();
            selLineHor2.Refresh();
        }

        public Bitmap ClipBitmap
        {
            get
            {
                if (this.bmpClip != null)
                {
                    return this.bmpClip;
                }
                return null;
            }
        }

        public Size ClipSize =>
            this.ptClipSize;

        public Point ClipStart =>
            this.ptClipStart;

        public CaptureClosedDelegate OnCaptureClose
        {
            set
            {
                this.CaptureClosedHandler = value;
            }
        }

        public static Image ImgSnap
        {
            get
            {
                return imgSnap;
            }
            set
            {
                if (imgSnap != null)
                {
                    imgSnap.Dispose();
                }

                imgSnap = value;
            }
        }

        public delegate void CaptureClosedDelegate(CaptureForm cform);

        private delegate int EnumWindowsCallBack(IntPtr hWnd, int lParam);

        private delegate void LineRefreshDelegate();

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        private delegate void ShowFormDelegate();
    }
}

