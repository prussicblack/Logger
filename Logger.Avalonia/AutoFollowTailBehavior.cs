using System;
using System.Collections;
using System.Collections.Specialized;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace SOEM_FrontEnd.Util.Logging.Avalonia
{
    public static class AutoFollowTailBehavior
    {
        private sealed class Owner : AvaloniaObject { }

        public static readonly AttachedProperty<bool> IsEnabledProperty =
            AvaloniaProperty.RegisterAttached<Owner, ListBox, bool>(
                "IsEnabled", defaultValue: false);

        public static readonly AttachedProperty<double> ThresholdProperty =
            AvaloniaProperty.RegisterAttached<Owner, ListBox, double>(
                "Threshold", defaultValue: 24);

        private sealed class State
        {
            public bool FollowTail = true;
            public ScrollViewer? ScrollViewer;
            public NotifyCollectionChangedEventHandler? CollectionHandler;
            public object? LastItemsSource;
        }

        private static readonly ConditionalWeakTable<ListBox, State> States =
            new ConditionalWeakTable<ListBox, State>();

        static AutoFollowTailBehavior()
        {
            IsEnabledProperty.Changed.AddClassHandler<ListBox>(OnIsEnabledChanged);
        }

        public static bool GetIsEnabled(AvaloniaObject obj) => obj.GetValue(IsEnabledProperty);

        public static void SetIsEnabled(AvaloniaObject obj, bool value) =>
            obj.SetValue(IsEnabledProperty, value);

        public static double GetThreshold(AvaloniaObject obj) => obj.GetValue(ThresholdProperty);

        public static void SetThreshold(AvaloniaObject obj, double value) =>
            obj.SetValue(ThresholdProperty, value);

        private static void OnIsEnabledChanged(ListBox listBox, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.NewValue is true)
                Attach(listBox);
            else
                Detach(listBox);
        }

        private static void Attach(ListBox listBox)
        {
            Detach(listBox);

            var state = new State();
            States.Add(listBox, state);

            listBox.AttachedToVisualTree += OnAttachedToVisualTree;
            listBox.DetachedFromVisualTree += OnDetachedFromVisualTree;
            listBox.PropertyChanged += OnPropertyChanged;

            RewireItemsSource(listBox, state, listBox.ItemsSource);
        }

        private static void Detach(ListBox listBox)
        {
            listBox.AttachedToVisualTree -= OnAttachedToVisualTree;
            listBox.DetachedFromVisualTree -= OnDetachedFromVisualTree;
            listBox.PropertyChanged -= OnPropertyChanged;

            if (!States.TryGetValue(listBox, out State? state))
                return;

            UnhookScrollViewer(state);
            UnhookCollection(state);
            States.Remove(listBox);
        }

        private static void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        {
            var listBox = (ListBox)sender!;
            if (!States.TryGetValue(listBox, out State? state))
                return;

            HookScrollViewer(listBox, state);
            ScrollToEnd(listBox, state, force: true);
        }

        private static void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        {
            var listBox = (ListBox)sender!;
            if (States.TryGetValue(listBox, out State? state))
                UnhookScrollViewer(state);
        }

        private static void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            var listBox = (ListBox)sender!;
            if (!States.TryGetValue(listBox, out State? state))
                return;

            if (e.Property == ItemsControl.ItemsSourceProperty)
                RewireItemsSource(listBox, state, e.NewValue);
            else if (e.Property == ThresholdProperty)
                state.FollowTail = IsNearBottom(listBox, state);
        }

        private static void HookScrollViewer(ListBox listBox, State state)
        {
            UnhookScrollViewer(state);

            state.ScrollViewer = listBox
                .GetVisualDescendants()
                .OfType<ScrollViewer>()
                .FirstOrDefault();

            if (state.ScrollViewer == null)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (States.TryGetValue(listBox, out State? current) &&
                        ReferenceEquals(current, state))
                    {
                        HookScrollViewer(listBox, state);
                    }
                }, DispatcherPriority.Background);
                return;
            }

            state.ScrollViewer.ScrollChanged += OnScrollChanged;
            state.FollowTail = IsNearBottom(listBox, state);
        }

        private static void UnhookScrollViewer(State state)
        {
            if (state.ScrollViewer == null)
                return;

            state.ScrollViewer.ScrollChanged -= OnScrollChanged;
            state.ScrollViewer = null;
        }

        private static void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
        {
            if (sender is not ScrollViewer scrollViewer)
                return;

            foreach (var entry in States)
            {
                if (!ReferenceEquals(entry.Value.ScrollViewer, scrollViewer))
                    continue;

                entry.Value.FollowTail = IsNearBottom(entry.Key, entry.Value);
                break;
            }
        }

        private static void RewireItemsSource(ListBox listBox, State state, object? newSource)
        {
            if (ReferenceEquals(state.LastItemsSource, newSource))
                return;

            UnhookCollection(state);
            state.LastItemsSource = newSource;

            if (newSource is not INotifyCollectionChanged collection)
                return;

            state.CollectionHandler = (_, args) =>
            {
                if (!state.FollowTail)
                    return;

                if (args.Action == NotifyCollectionChangedAction.Add ||
                    args.Action == NotifyCollectionChangedAction.Reset ||
                    args.Action == NotifyCollectionChangedAction.Replace)
                {
                    ScrollToEnd(listBox, state, force: false);
                }
            };

            collection.CollectionChanged += state.CollectionHandler;
        }

        private static void UnhookCollection(State state)
        {
            if (state.LastItemsSource is INotifyCollectionChanged collection &&
                state.CollectionHandler != null)
            {
                collection.CollectionChanged -= state.CollectionHandler;
            }

            state.CollectionHandler = null;
            state.LastItemsSource = null;
        }

        private static bool IsNearBottom(ListBox listBox, State state)
        {
            ScrollViewer? scroll = state.ScrollViewer;
            if (scroll == null)
                return true;

            double threshold = Math.Max(0, listBox.GetValue(ThresholdProperty));
            return scroll.Offset.Y + scroll.Viewport.Height >= scroll.Extent.Height - threshold;
        }

        private static void ScrollToEnd(ListBox listBox, State state, bool force)
        {
            if (!force && !state.FollowTail)
                return;
            if (listBox.ItemsSource is not IList items || items.Count == 0)
                return;

            object? lastItem = items[items.Count - 1];
            if (lastItem == null)
                return;

            Dispatcher.UIThread.Post(() =>
            {
                try { listBox.ScrollIntoView(lastItem); }
                catch { }
            }, DispatcherPriority.Background);
        }
    }
}
