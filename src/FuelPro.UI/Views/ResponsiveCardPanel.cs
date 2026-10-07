using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FuelPro.Core.DTOs;

namespace FuelPro.UI.Views;

public enum CardPanelLayoutMode
{
    Masonry,
    RowWrap,
    MitaliTankLayout
}

/// <summary>
/// A responsive layout panel for cards that adapts columns based on available width,
/// allows each card to size naturally to its content height (no forced equal heights),
/// and packs cards compactly.
/// 
/// In MitaliTankLayout mode (or when 3 columns are available for tank cards):
/// TOP ROW:    [CNG]   [HSD]   [SPEED]
/// SECOND ROW: [MS-I]  [MS-II] [ (SPEED continues down) ]
/// 
/// Column 0: CNG, then MS-I
/// Column 1: HSD, then MS-II
/// Column 2: SPEED (extends freely down the right side)
/// </summary>
public class ResponsiveCardPanel : Panel
{
    public static readonly DependencyProperty MinColumnWidthProperty =
        DependencyProperty.Register(
            nameof(MinColumnWidth),
            typeof(double),
            typeof(ResponsiveCardPanel),
            new FrameworkPropertyMetadata(360.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty MaxColumnsProperty =
        DependencyProperty.Register(
            nameof(MaxColumns),
            typeof(int),
            typeof(ResponsiveCardPanel),
            new FrameworkPropertyMetadata(3, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty LayoutModeProperty =
        DependencyProperty.Register(
            nameof(LayoutMode),
            typeof(CardPanelLayoutMode),
            typeof(ResponsiveCardPanel),
            new FrameworkPropertyMetadata(CardPanelLayoutMode.MitaliTankLayout, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public double MinColumnWidth
    {
        get => (double)GetValue(MinColumnWidthProperty);
        set => SetValue(MinColumnWidthProperty, value);
    }

    public int MaxColumns
    {
        get => (int)GetValue(MaxColumnsProperty);
        set => SetValue(MaxColumnsProperty, value);
    }

    public CardPanelLayoutMode LayoutMode
    {
        get => (CardPanelLayoutMode)GetValue(LayoutModeProperty);
        set => SetValue(LayoutModeProperty, value);
    }

    private int CalculateColumns(double availableWidth, int childCount)
    {
        if (childCount <= 0) return 1;
        if (double.IsInfinity(availableWidth) || availableWidth <= 0) return Math.Min(MaxColumns, childCount);

        int cols = (int)(availableWidth / Math.Max(100.0, MinColumnWidth));
        if (cols < 1) cols = 1;
        if (cols > MaxColumns) cols = MaxColumns;
        if (cols > childCount) cols = childCount;
        return cols;
    }

    private static string GetChildProductIdentifier(UIElement child)
    {
        if (child is FrameworkElement fe)
        {
            if (fe.DataContext is NozzleGroupDto dto)
            {
                return $"{dto.GroupName} {dto.FuelType}";
            }
            if (fe is ContentPresenter cp && cp.Content is NozzleGroupDto cpDto)
            {
                return $"{cpDto.GroupName} {cpDto.FuelType}";
            }
        }
        return string.Empty;
    }

    /// <summary>
    /// Identifies the designated slot for Mitali product cards:
    /// 0 = CNG (Col 0, Top)
    /// 1 = HSD (Col 1, Top)
    /// 2 = SPEED (Col 2, Top)
    /// 3 = MS-I (Col 0, Bottom)
    /// 4 = MS-II (Col 1, Bottom)
    /// </summary>
    private static int GetProductSlot(string identifier, int fallbackIndex)
    {
        var text = identifier.ToUpperInvariant();
        if (text.Contains("CNG")) return 0;
        if (text.Contains("HSD") && !text.Contains("HSD-II") && !text.Contains("HSD 2") && !text.Contains("HSD II")) return 1;
        if (text.Contains("SPEED") || text.Contains("POWER") || text.Contains("XP")) return 2;
        if (text.Contains("MS-II") || text.Contains("MS 2") || text.Contains("MSII") || text.Contains("HSD-II") || text.Contains("HSD 2")) return 4;
        if (text.Contains("MS-I") || text.Contains("MS 1") || text.Contains("MSI") || text.Contains("MS") || text.Contains("PETROL")) return 3;
        return 100 + fallbackIndex;
    }

    private class ChildSlotInfo
    {
        public UIElement Child { get; set; } = null!;
        public int Slot { get; set; }
        public int OriginalIndex { get; set; }
    }

    private List<ChildSlotInfo> GetOrderedSlots()
    {
        var list = new List<ChildSlotInfo>();
        var count = InternalChildren.Count;
        for (int i = 0; i < count; i++)
        {
            var child = InternalChildren[i];
            if (child == null) continue;
            var ident = GetChildProductIdentifier(child);
            var slot = GetProductSlot(ident, i);
            list.Add(new ChildSlotInfo { Child = child, Slot = slot, OriginalIndex = i });
        }
        return list;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var count = InternalChildren.Count;
        if (count == 0) return new Size(0, 0);

        double width = double.IsInfinity(availableSize.Width) || availableSize.Width <= 0
            ? 1200.0
            : availableSize.Width;

        int cols = CalculateColumns(width, count);
        double cardWidth = width / cols;

        // Measure all children with the designated card width
        for (int i = 0; i < count; i++)
        {
            InternalChildren[i]?.Measure(new Size(cardWidth, double.PositiveInfinity));
        }

        if (cols >= 3 && (LayoutMode == CardPanelLayoutMode.MitaliTankLayout || LayoutMode == CardPanelLayoutMode.Masonry))
        {
            // Calculate column heights based on intentional 3-column composition:
            // Col 0: CNG (Slot 0), MS-I (Slot 3)
            // Col 1: HSD (Slot 1), MS-II (Slot 4)
            // Col 2: SPEED (Slot 2)
            var colHeights = new double[cols];
            var items = GetOrderedSlots();

            foreach (var item in items)
            {
                int targetCol;
                switch (item.Slot)
                {
                    case 0: // CNG
                    case 3: // MS-I
                        targetCol = 0;
                        break;
                    case 1: // HSD
                    case 4: // MS-II
                        targetCol = 1;
                        break;
                    case 2: // SPEED
                        targetCol = 2;
                        break;
                    default:
                        // Extra / unknown cards go to shortest column
                        targetCol = GetShortestColumnIndex(colHeights);
                        break;
                }

                if (targetCol < cols)
                {
                    colHeights[targetCol] += item.Child.DesiredSize.Height;
                }
            }

            double maxHeight = colHeights.Max();
            return new Size(width, maxHeight);
        }
        else if (LayoutMode == CardPanelLayoutMode.RowWrap)
        {
            int numRows = (int)Math.Ceiling((double)count / cols);
            double totalRowHeight = 0;
            for (int r = 0; r < numRows; r++)
            {
                double rowH = 0;
                for (int c = 0; c < cols; c++)
                {
                    int idx = r * cols + c;
                    if (idx < count && InternalChildren[idx] != null)
                    {
                        rowH = Math.Max(rowH, InternalChildren[idx].DesiredSize.Height);
                    }
                }
                totalRowHeight += rowH;
            }
            return new Size(width, totalRowHeight);
        }
        else
        {
            // Responsive 2-column or 1-column masonry with prioritized product order
            var colHeights = new double[cols];
            var sorted = GetOrderedSlots().OrderBy(x => x.Slot).ToList();

            foreach (var item in sorted)
            {
                int minCol = GetShortestColumnIndex(colHeights);
                colHeights[minCol] += item.Child.DesiredSize.Height;
            }

            double maxHeight = colHeights.Max();
            return new Size(width, maxHeight);
        }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var count = InternalChildren.Count;
        if (count == 0) return finalSize;

        double width = finalSize.Width;
        int cols = CalculateColumns(width, count);
        double cardWidth = width / cols;

        if (cols >= 3 && (LayoutMode == CardPanelLayoutMode.MitaliTankLayout || LayoutMode == CardPanelLayoutMode.Masonry))
        {
            // Intentional 3-column composition:
            // Column 0: Slot 0 (CNG) at top, Slot 3 (MS-I) underneath
            // Column 1: Slot 1 (HSD) at top, Slot 4 (MS-II) underneath
            // Column 2: Slot 2 (SPEED) at top (extends downward)
            var colHeights = new double[cols];
            var items = GetOrderedSlots();

            // Order within each column: top items first (Slot <= 2), then bottom items (Slot >= 3)
            var columnBuckets = new List<ChildSlotInfo>[cols];
            for (int c = 0; c < cols; c++) columnBuckets[c] = new List<ChildSlotInfo>();

            // First assign the 5 primary slots
            foreach (var item in items)
            {
                switch (item.Slot)
                {
                    case 0: // CNG -> Col 0 top
                        columnBuckets[0].Add(item);
                        break;
                    case 3: // MS-I -> Col 0 bottom
                        columnBuckets[0].Add(item);
                        break;
                    case 1: // HSD -> Col 1 top
                        columnBuckets[1].Add(item);
                        break;
                    case 4: // MS-II -> Col 1 bottom
                        columnBuckets[1].Add(item);
                        break;
                    case 2: // SPEED -> Col 2 top
                        columnBuckets[2].Add(item);
                        break;
                    default:
                        // Extra items go to shortest column later
                        break;
                }
            }

            // Ensure proper vertical ordering inside each bucket:
            // Col 0: CNG (Slot 0) before MS-I (Slot 3)
            columnBuckets[0] = columnBuckets[0].OrderBy(x => x.Slot == 0 ? 0 : 1).ToList();
            // Col 1: HSD (Slot 1) before MS-II (Slot 4)
            columnBuckets[1] = columnBuckets[1].OrderBy(x => x.Slot == 1 ? 0 : 1).ToList();

            // Arrange known primary columns
            for (int c = 0; c < cols; c++)
            {
                double currentY = 0;
                double x = c * cardWidth;
                foreach (var item in columnBuckets[c])
                {
                    double h = item.Child.DesiredSize.Height;
                    item.Child.Arrange(new Rect(x, currentY, cardWidth, h));
                    currentY += h;
                }
                colHeights[c] = currentY;
            }

            // Arrange any extra items (fallback)
            var extraItems = items.Where(x => x.Slot >= 100).ToList();
            foreach (var extra in extraItems)
            {
                int minCol = GetShortestColumnIndex(colHeights);
                double x = minCol * cardWidth;
                double y = colHeights[minCol];
                double h = extra.Child.DesiredSize.Height;
                extra.Child.Arrange(new Rect(x, y, cardWidth, h));
                colHeights[minCol] += h;
            }
        }
        else if (LayoutMode == CardPanelLayoutMode.RowWrap)
        {
            int numRows = (int)Math.Ceiling((double)count / cols);
            double currentY = 0;
            for (int r = 0; r < numRows; r++)
            {
                double rowH = 0;
                for (int c = 0; c < cols; c++)
                {
                    int idx = r * cols + c;
                    if (idx < count && InternalChildren[idx] != null)
                    {
                        rowH = Math.Max(rowH, InternalChildren[idx].DesiredSize.Height);
                    }
                }

                for (int c = 0; c < cols; c++)
                {
                    int idx = r * cols + c;
                    if (idx < count && InternalChildren[idx] != null)
                    {
                        var child = InternalChildren[idx];
                        double x = c * cardWidth;
                        child.Arrange(new Rect(x, currentY, cardWidth, child.DesiredSize.Height));
                    }
                }

                currentY += rowH;
            }
        }
        else
        {
            // Responsive 2-column or 1-column layout with prioritized product order
            var colHeights = new double[cols];
            var sorted = GetOrderedSlots().OrderBy(x => x.Slot).ToList();

            foreach (var item in sorted)
            {
                int minCol = GetShortestColumnIndex(colHeights);
                double x = minCol * cardWidth;
                double y = colHeights[minCol];
                double h = item.Child.DesiredSize.Height;

                item.Child.Arrange(new Rect(x, y, cardWidth, h));
                colHeights[minCol] += h;
            }
        }

        return finalSize;
    }

    private static int GetShortestColumnIndex(double[] colHeights)
    {
        int minCol = 0;
        double minVal = colHeights[0];
        for (int c = 1; c < colHeights.Length; c++)
        {
            if (colHeights[c] < minVal)
            {
                minVal = colHeights[c];
                minCol = c;
            }
        }
        return minCol;
    }
}
