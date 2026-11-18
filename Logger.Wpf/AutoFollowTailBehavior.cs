using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace SOEM_FrontEnd.Util.Logging.Wpf
{
    public static class AutoFollowTailBehavior
    {
        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled",
                typeof(bool),
                typeof(AutoFollowTailBehavior),
                new PropertyMetadata(false, OnIsEnabledChanged));

        public static readonly DependencyProperty ThresholdProperty =
            DependencyProperty.RegisterAttached(
                "Threshold",
                typeof(double),
                typeof(AutoFollowTailBehavior),
                new PropertyMetadata(24d, OnThresholdChanged));

        private sealed class State
        {
            public bool FollowTail = true;
            public ScrollViewer? ScrollViewer;
        }

        private static readonly ConditionalWeakTable<ListBox, State> States =
            new ConditionalWeakTable<ListBox, State>();

        public static bool GetIsEnabled(DependencyObject obj) =>
            (bool)obj.GetValue(IsEnabledProperty);

        public static void SetIsEnabled(DependencyObject obj, bool value) =>
            obj.SetValue(IsEnabledProperty, value);

        public static double GetThreshold(DependencyObject obj) =>
            (double)obj.GetValue(ThresholdProperty);

        public static void SetThreshold(DependencyObject obj, double value) =>
            obj.SetValue(ThresholdProperty, value);

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ListBox listBox)
                return;

            if (e.NewValue is true)
                Attach(listBox);
            else
                Detach(listBox);
        }

        private static void OnThresholdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ListBox listBox && States.TryGetValue(listBox, out State? state))
                state.FollowTail = IsNearBottom(listBox, state);
        }

        private static void Attach(ListBox listBox)
        {
            Detach(listBox);

            var state = new State();
            States.Add(listBox, state);

            listBox.Loaded += OnLoaded;
            listBox.Unloaded += OnUnloaded;
            listBox.ItemContainerGenerator.ItemsChanged += OnItemsChanged;

            if (listBox.IsLoaded)
                HookScrollViewer(listBox, state);
        }

        private static void Detach(ListBox listBox)
        {
            listBox.Loaded -= OnLoaded;
            listBox.Unloaded -= OnUnloaded;
            listBox.ItemContainerGenerator.ItemsChanged -= OnItemsChanged;

            if (States.TryGetValue(listBox, out State? state))
            {
                UnhookScrollViewer(state);
                States.Remove(listBox);
            }
        }

        private static void OnLoaded(object sender, RoutedEventArgs e)
        {
            var listBox = (ListBox)sender;
            if (!States.TryGetValue(listBox, out State? state))
                return;

            HookScrollViewer(listBox, state);
            ScrollToEnd(listBox, state, force: true);
        }

        private static void OnUnloaded(object sender, RoutedEventArgs e)
        {
            var listBox = (ListBox)sender;
            if (States.TryGetValue(listBox, out State? state))
                UnhookScrollViewer(state);
        }

        private static void OnItemsChanged(object? sender, System.Windows.Controls.Primitives.ItemsChangedEventArgs e)
        {
            if (sender is not System.Windows.Controls.ItemContainerGenerator generator)
                return;
            if (generator.Items.Count == 0)
                return;

            ListBox? listBox = FindOwningListBox(generator);
            if (listBox == null || !States.TryGetValue(listBox, out State? state) || !state.FollowTail)
                return;

            ScrollToEnd(listBox, state, force: false);
        }

        private static ListBox? FindOwningListBox(System.Windows.Controls.ItemContainerGenerator generator)
        {
            // ItemContainerGenerator는 소유 컨트롤을 공개하지 않으므로 등록된 ListBox를 찾습니다.
            // 동시 UI 목록 수는 일반적으로 매우 작고, ConditionalWeakTable은 수명을 방해하지 않습니다.
            foreach (var entry in States)
            {
                if (ReferenceEquals(entry.Key.ItemContainerGenerator, generator))
                    return entry.Key;
            }

            return null;
        }

        private static void HookScrollViewer(ListBox listBox, State state)
        {
            UnhookScrollViewer(state);

            listBox.ApplyTemplate();
            state.ScrollViewer = FindVisualChild<ScrollViewer>(listBox);

            if (state.ScrollViewer == null)
            {
                listBox.Dispatcher.BeginInvoke(
                    new Action(() =>
                    {
                        if (States.TryGetValue(listBox, out State? current))
                            HookScrollViewer(listBox, current);
                    }),
                    DispatcherPriority.Loaded);
                return;
            }

            state.ScrollViewer.ScrollChanged += OnScrollChanged;
            state.FollowTail = IsNearBottom(listBox, state);
        }

        private static void UnhookScrollViewer(State state)
        {
            if (state.ScrollViewer != null)
            {
                state.ScrollViewer.ScrollChanged -= OnScrollChanged;
                state.ScrollViewer = null;
            }
        }

        private static void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (sender is not ScrollViewer scrollViewer)
                return;

            foreach (var entry in States)
            {
                if (!ReferenceEquals(entry.Value.ScrollViewer, scrollViewer))
                    continue;

                // 콘텐츠 크기 변화가 아닌 실제 스크롤일 때 follow 상태를 갱신합니다.
                if (Math.Abs(e.ExtentHeightChange) < double.Epsilon)
                    entry.Value.FollowTail = IsNearBottom(entry.Key, entry.Value);
                else if (entry.Value.FollowTail)
                    ScrollToEnd(entry.Key, entry.Value, force: false);

                break;
            }
        }

        private static bool IsNearBottom(ListBox listBox, State state)
        {
            ScrollViewer? scroll = state.ScrollViewer;
            if (scroll == null)
                return true;

            double threshold = Math.Max(0, GetThreshold(listBox));
            return scroll.VerticalOffset + scroll.ViewportHeight >= scroll.ExtentHeight - threshold;
        }

        private static void ScrollToEnd(ListBox listBox, State state, bool force)
        {
            if (listBox.Items.Count == 0)
                return;
            if (!force && !state.FollowTail)
                return;

            object lastItem = listBox.Items[listBox.Items.Count - 1];
            listBox.Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    if (listBox.Items.Contains(lastItem))
                        listBox.ScrollIntoView(lastItem);
                }),
                DispatcherPriority.Background);
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match)
                    return match;

                T? nested = FindVisualChild<T>(child);
                if (nested != null)
                    return nested;
            }

            return null;
        }
    }
}
