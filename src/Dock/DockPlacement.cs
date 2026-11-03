using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DockBar
{
    public sealed partial class DockWindow
    {
        private const string DockBottomPosition = "Inferior";
        private const string DockTopPosition = "Superior";
        private const string DockLeftPosition = "Esquerda";
        private const string DockRightPosition = "Direita";

        private string dockPosition = DockBottomPosition;
        private Grid dockRoot;
        private FrameworkElement systemCardHolder;
        private Grid systemCardGrid;
        private StackPanel systemClockStack;
        private Grid systemTrayGrid;
        private readonly Dictionary<string, MenuItem> dockPositionMenuItems = new Dictionary<string, MenuItem>(StringComparer.OrdinalIgnoreCase);

        internal static IEnumerable<string> DockPositionChoices
        {
            get { return new[] { DockBottomPosition, DockTopPosition, DockLeftPosition, DockRightPosition }; }
        }

        internal string CurrentDockPosition { get { return dockPosition; } }
        internal bool IsVerticalDock { get { return IsDockLeft || IsDockRight; } }
        internal bool IsDockAtTop { get { return IsDockTop; } }
        internal bool IsMascotPositionSupported { get { return IsDockBottom; } }

        private bool IsDockBottom { get { return String.Equals(dockPosition, DockBottomPosition, StringComparison.OrdinalIgnoreCase); } }
        private bool IsDockTop { get { return String.Equals(dockPosition, DockTopPosition, StringComparison.OrdinalIgnoreCase); } }
        private bool IsDockLeft { get { return String.Equals(dockPosition, DockLeftPosition, StringComparison.OrdinalIgnoreCase); } }
        private bool IsDockRight { get { return String.Equals(dockPosition, DockRightPosition, StringComparison.OrdinalIgnoreCase); } }

        private static bool IsValidDockPosition(string value)
        {
            return String.Equals(value, DockBottomPosition, StringComparison.OrdinalIgnoreCase) ||
                   String.Equals(value, DockTopPosition, StringComparison.OrdinalIgnoreCase) ||
                   String.Equals(value, DockLeftPosition, StringComparison.OrdinalIgnoreCase) ||
                   String.Equals(value, DockRightPosition, StringComparison.OrdinalIgnoreCase);
        }

        internal void SetDockPosition(string position)
        {
            if (!IsValidDockPosition(position)) return;
            if (String.Equals(dockPosition, position, StringComparison.OrdinalIgnoreCase)) return;

            RemoveAppBar();
            dockPosition = position;
            dockAutoHidden = false;
            edgeRevealPending = false;
            edgeRevealTimer.Stop();
            HidePreview();
            ApplyDockOrientation();
            UpdateAppBarRegistration();
            SchedulePositionDock();
            SaveSettings();

            foreach (var item in dockPositionMenuItems)
                item.Value.IsChecked = String.Equals(item.Key, dockPosition, StringComparison.OrdinalIgnoreCase);
        }

        private void ApplyDockOrientation()
        {
            if (dockRoot == null || dockShell == null || dockRow == null) return;

            StopDockWave();
            bool vertical = IsVerticalDock;
            double shellThickness = Math.Max(62, 72 + (dockIconSize - 48) * 1.15);
            double windowThickness = Math.Max(100, 118 + (dockIconSize - 48) * 1.45);
            double scale = dockIconSize / 48.0;

            SizeToContent = SizeToContent.Manual;
            ClearValue(WidthProperty);
            ClearValue(HeightProperty);
            MinWidth = 0;
            MinHeight = 0;
            MaxWidth = Double.PositiveInfinity;
            MaxHeight = Double.PositiveInfinity;

            dockRow.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
            if (taskItems != null) taskItems.Orientation = dockRow.Orientation;
            if (assistantGroup != null) assistantGroup.Orientation = dockRow.Orientation;
            magnifyHost.LayoutTransform = new ScaleTransform(scale, scale);
            Point growthOrigin = IsDockLeft ? new Point(0, .5)
                : IsDockRight ? new Point(1, .5)
                : IsDockTop ? new Point(.5, 0)
                : new Point(.5, 1);
            foreach (FrameworkElement icon in magnifyIcons)
                icon.RenderTransformOrigin = growthOrigin;
            ApplyRunningIndicatorPlacement();

            if (vertical)
            {
                Width = windowThickness;
                MaxHeight = Math.Max(500, SystemParameters.PrimaryScreenHeight - 20);
                SizeToContent = SizeToContent.Height;

                dockShell.ClearValue(HeightProperty);
                dockShell.Width = shellThickness;
                dockShell.VerticalAlignment = VerticalAlignment.Stretch;
                dockShell.HorizontalAlignment = IsDockLeft ? HorizontalAlignment.Left : HorizontalAlignment.Right;
                dockShell.Margin = IsDockLeft ? new Thickness(5, 5, 0, 5) : new Thickness(0, 5, 5, 5);

                dockRow.VerticalAlignment = VerticalAlignment.Center;
                dockRow.HorizontalAlignment = IsDockLeft ? HorizontalAlignment.Left : HorizontalAlignment.Right;
                dockRow.Margin = IsDockLeft ? new Thickness(5, 30, 0, 30) : new Thickness(0, 30, 5, 30);
            }
            else
            {
                Height = windowThickness;
                MaxWidth = Math.Max(500, SystemParameters.PrimaryScreenWidth - 20);
                SizeToContent = SizeToContent.Width;

                dockShell.ClearValue(WidthProperty);
                dockShell.Height = shellThickness;
                dockShell.HorizontalAlignment = HorizontalAlignment.Stretch;
                dockShell.VerticalAlignment = IsDockTop ? VerticalAlignment.Top : VerticalAlignment.Bottom;
                dockShell.Margin = IsDockTop ? new Thickness(5, 5, 5, 0) : new Thickness(5, 0, 5, 5);

                dockRow.HorizontalAlignment = HorizontalAlignment.Center;
                dockRow.VerticalAlignment = IsDockTop ? VerticalAlignment.Top : VerticalAlignment.Bottom;
                dockRow.Margin = IsDockTop ? new Thickness(30, 5, 30, 0) : new Thickness(30, 0, 30, 5);
            }

            ApplySeparatorOrientation(vertical);
            ApplySystemCardOrientation(vertical);
            EnforceMascotPositionPolicy();
            waveLayoutDirty = true;
            if (dockMascot != null) dockMascot.NotifyDockPositionChanged();
            InvalidateMeasure();
            InvalidateArrange();
            UpdateLayout();
        }

        private void ApplySeparatorOrientation(bool vertical)
        {
            foreach (Border separator in themedSeparators)
            {
                if (vertical)
                {
                    separator.Width = 42;
                    separator.Height = 1;
                    separator.Margin = new Thickness(8, 7, 8, 7);
                    separator.HorizontalAlignment = HorizontalAlignment.Center;
                    separator.VerticalAlignment = VerticalAlignment.Center;
                }
                else
                {
                    separator.Width = 1;
                    separator.Height = 42;
                    separator.Margin = new Thickness(7, 0, 7, 8);
                    separator.HorizontalAlignment = HorizontalAlignment.Center;
                    separator.VerticalAlignment = VerticalAlignment.Center;
                }
            }
        }

        private void ApplyRunningIndicatorPlacement()
        {
            foreach (StackPanel stack in magnifyIcons.Select(item => item.Parent).OfType<StackPanel>().Distinct())
            {
                Border indicator = FindRunningIndicator(stack);
                if (indicator != null) PlaceRunningIndicator(stack, indicator);
            }
        }

        private void ApplySystemCardOrientation(bool vertical)
        {
            if (systemCardHolder == null || systemCardGrid == null || systemClockStack == null || systemTrayGrid == null) return;

            systemCardGrid.ColumnDefinitions.Clear();
            systemCardGrid.RowDefinitions.Clear();
            if (vertical)
            {
                systemCardHolder.Width = 72;
                systemCardHolder.Height = 118;
                systemCardGrid.Width = 66;
                systemCardGrid.Height = 112;
                systemCardGrid.Margin = new Thickness(3, 3, 3, 3);
                systemCardGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) });
                systemCardGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(60) });
                Grid.SetColumn(systemClockStack, 0);
                Grid.SetRow(systemClockStack, 0);
                Grid.SetColumn(systemTrayGrid, 0);
                Grid.SetRow(systemTrayGrid, 1);
            }
            else
            {
                systemCardHolder.Width = 154;
                systemCardHolder.Height = 63;
                systemCardGrid.Width = 150;
                systemCardGrid.Height = 56;
                systemCardGrid.Margin = new Thickness(4, 0, 0, 7);
                systemCardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
                systemCardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
                Grid.SetRow(systemClockStack, 0);
                Grid.SetColumn(systemClockStack, 0);
                Grid.SetRow(systemTrayGrid, 0);
                Grid.SetColumn(systemTrayGrid, 1);
            }
        }

        private Rect DockScreenArea()
        {
            return taskbarHidden
                ? new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight)
                : SystemParameters.WorkArea;
        }

        private Rect DockAnchorArea()
        {
            return appBarRegistered && !appBarArea.IsEmpty ? appBarArea : DockScreenArea();
        }

        private double RevealedDockLeft()
        {
            Rect area = DockAnchorArea();
            if (IsDockLeft) return area.Left + dockBottomGap - dockShell.Margin.Left;
            if (IsDockRight) return area.Right - dockBottomGap - ActualWidth + dockShell.Margin.Right;
            Rect screen = DockScreenArea();
            return screen.Left + Math.Max(0, (screen.Width - ActualWidth) / 2.0);
        }

        private double RevealedDockTopForPosition()
        {
            Rect area = DockAnchorArea();
            if (IsDockTop) return area.Top + dockBottomGap - dockShell.Margin.Top;
            if (IsDockBottom) return area.Bottom - dockBottomGap - ActualHeight + dockShell.Margin.Bottom;
            Rect screen = DockScreenArea();
            return screen.Top + Math.Max(0, (screen.Height - ActualHeight) / 2.0);
        }

        private double HiddenDockLeft()
        {
            Rect area = DockAnchorArea();
            double shellWidth = dockShell.ActualWidth > 0 ? dockShell.ActualWidth : 72;
            if (IsDockLeft) return area.Left - shellWidth + 7 - dockShell.Margin.Left;
            if (IsDockRight)
            {
                double shellLeftInsideWindow = Math.Max(0, ActualWidth - dockShell.Margin.Right - shellWidth);
                return area.Right - shellLeftInsideWindow - 7;
            }
            return RevealedDockLeft();
        }

        private double HiddenDockTopForPosition()
        {
            Rect area = DockAnchorArea();
            double shellHeight = dockShell.ActualHeight > 0 ? dockShell.ActualHeight : 72;
            if (IsDockTop) return area.Top - shellHeight + 7 - dockShell.Margin.Top;
            if (IsDockBottom)
            {
                double shellTopInsideWindow = Math.Max(0, ActualHeight - dockShell.Margin.Bottom - shellHeight);
                return area.Bottom - shellTopInsideWindow - 7;
            }
            return RevealedDockTopForPosition();
        }

        private void ShowWorkspaceHud(string icon, string title, string subtitle)
        {
            WorkspaceHudSide side = IsDockTop ? WorkspaceHudSide.Below
                : IsDockLeft ? WorkspaceHudSide.Right
                : IsDockRight ? WorkspaceHudSide.Left
                : WorkspaceHudSide.Above;
            double anchorX = IsDockLeft ? Left + ActualWidth + 14
                : IsDockRight ? Left - 14
                : Left + ActualWidth / 2.0;
            double anchorY = IsDockTop ? Top + ActualHeight + 14
                : IsDockBottom ? Top - 14
                : Top + ActualHeight / 2.0;
            WorkspaceHudWindow.ShowHud(icon, title, subtitle, anchorX, anchorY, side);
        }
    }
}
