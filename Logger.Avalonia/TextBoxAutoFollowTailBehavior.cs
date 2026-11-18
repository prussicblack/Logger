using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace SOEM_FrontEnd.Util.Logging.Avalonia
{
    public static class TextBoxAutoFollowTailBehavior
    {
        // AttachedProperty owner
        private sealed class Owner : AvaloniaObject
        {
        }

        // Enable / Disable
        public static readonly AttachedProperty<bool> IsEnabledProperty =
            AvaloniaProperty.RegisterAttached<Owner, TextBox, bool>("IsEnabled", defaultValue: false);

        // 바닥 근처 판정값
        public static readonly AttachedProperty<double> ThresholdPxProperty =
            AvaloniaProperty.RegisterAttached<Owner, TextBox, double>("ThresholdPx", defaultValue: 24);

        private sealed class State
        {
            public bool FollowTail = true;
            public ScrollViewer? Scroll;
            public EventHandler<ScrollChangedEventArgs>? ScrollHandler;
        }

        private static readonly ConditionalWeakTable<TextBox, State> _states =
            new ConditionalWeakTable<TextBox, State>();

        static TextBoxAutoFollowTailBehavior()
        {
            IsEnabledProperty.Changed.AddClassHandler<TextBox>(OnIsEnabledChanged);
        }

        public static bool GetIsEnabled(AvaloniaObject obj)
            => obj.GetValue(IsEnabledProperty);

        public static void SetIsEnabled(AvaloniaObject obj, bool value)
            => obj.SetValue(IsEnabledProperty, value);

        public static double GetThresholdPx(AvaloniaObject obj)
            => obj.GetValue(ThresholdPxProperty);

        public static void SetThresholdPx(AvaloniaObject obj, double value)
            => obj.SetValue(ThresholdPxProperty, value);

        private static void OnIsEnabledChanged(
            TextBox tb,
            AvaloniaPropertyChangedEventArgs e)
        {
            bool enabled = e.NewValue is bool b && b;

            if (enabled)
                Attach(tb);
            else
                Detach(tb);
        }

        private static void Attach(TextBox tb)
        {
            Detach(tb);

            var st = new State();
            _states.Add(tb, st);

            tb.AttachedToVisualTree += Tb_AttachedToVisualTree;
            tb.DetachedFromVisualTree += Tb_DetachedFromVisualTree;
            tb.PropertyChanged += Tb_PropertyChanged;
            tb.TextChanged += Tb_TextChanged;

            // XAML 로딩 후 런타임에 Behavior를 켠 경우에도 ScrollViewer를 연결한다.
            Dispatcher.UIThread.Post(() =>
            {
                if (!_states.TryGetValue(tb, out var current) ||
                    !ReferenceEquals(current, st) ||
                    st.Scroll != null)
                {
                    return;
                }

                HookScrollViewer(tb, st);
                ScrollToEnd(tb, st, force: true);
            }, DispatcherPriority.Background);
        }

        private static void Detach(TextBox tb)
        {
            tb.AttachedToVisualTree -= Tb_AttachedToVisualTree;
            tb.DetachedFromVisualTree -= Tb_DetachedFromVisualTree;
            tb.PropertyChanged -= Tb_PropertyChanged;
            tb.TextChanged -= Tb_TextChanged;

            if (_states.TryGetValue(tb, out var st))
            {
                UnhookScrollViewer(st);
                _states.Remove(tb);
            }
        }

        private static void Tb_AttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        {
            if (sender is not TextBox tb)
                return;

            if (!_states.TryGetValue(tb, out var st))
                return;

            st.FollowTail = true;

            HookScrollViewer(tb, st);

            // 처음 표시될 때 기존 로그가 있다면 맨 아래로
            ScrollToEnd(tb, st, force: true);
        }

        private static void Tb_DetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        {
            if (sender is not TextBox tb)
                return;

            if (!_states.TryGetValue(tb, out var st))
                return;

            UnhookScrollViewer(st);
        }

        private static void Tb_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (sender is not TextBox tb)
                return;

            if (!_states.TryGetValue(tb, out var st))
                return;

            if (e.Property == ThresholdPxProperty)
            {
                UpdateFollowTail(tb, st);
            }
        }

        private static void Tb_TextChanged(object? sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox tb)
                return;

            if (!_states.TryGetValue(tb, out var st))
                return;

            // 사용자가 위쪽을 보고 있으면 자동 스크롤 금지
            if (!st.FollowTail)
                return;

            ScrollToEnd(tb, st, force: false);
        }

        private static void HookScrollViewer(TextBox tb, State st)
        {
            UnhookScrollViewer(st);

            var sv = FindScrollViewer(tb);

            if (sv == null)
            {
                // 템플릿 적용이 아직 끝나지 않았을 수 있으므로 다음 UI tick에 재시도
                Dispatcher.UIThread.Post(() =>
                {
                    // 이미 Disable/Detach 된 Behavior면 아무것도 하지 않음
                    if (!_states.TryGetValue(tb, out var current))
                        return;

                    if (!ReferenceEquals(current, st))
                        return;

                    var retrySv = FindScrollViewer(tb);
                    if (retrySv == null)
                        return;

                    HookScrollViewerCore(tb, st, retrySv);
                }, DispatcherPriority.Background);

                return;
            }

            HookScrollViewerCore(tb, st, sv);
        }

        private static void HookScrollViewerCore(TextBox tb, State st, ScrollViewer sv)
        {
            st.Scroll = sv;

            st.ScrollHandler = (_, args) =>
            {
                /*
                 * 중요:
                 *
                 * Text가 추가되면 Extent.Height가 커지면서 ScrollChanged가 발생한다.
                 * 이때 Offset은 그대로라면 사용자가 스크롤한 것이 아니다.
                 *
                 * Extent 변화만 보고 IsNearBottom()을 호출하면,
                 * 기존에는 바닥이었던 상태가 새 Text 추가 직후
                 * 잠깐 "바닥이 아님"으로 판정되어 FollowTail이 꺼질 수 있다.
                 *
                 * 따라서 실제 Offset이 변했거나 Viewport가 변한 경우에만
                 * FollowTail을 다시 판정한다.
                 */

                if (Math.Abs(args.OffsetDelta.Y) > 0.001 ||
                    Math.Abs(args.ViewportDelta.Y) > 0.001)
                {
                    UpdateFollowTail(tb, st);
                }
            };

            sv.ScrollChanged += st.ScrollHandler;

            UpdateFollowTail(tb, st);
        }

        private static void UnhookScrollViewer(State st)
        {
            if (st.Scroll != null && st.ScrollHandler != null)
            {
                st.Scroll.ScrollChanged -= st.ScrollHandler;
            }

            st.ScrollHandler = null;
            st.Scroll = null;
        }

        private static ScrollViewer? FindScrollViewer(TextBox tb)
        {
            // Avalonia TextBox 기본 Template의 PART_ScrollViewer 우선
            var viewers = tb.GetVisualDescendants()
                .OfType<ScrollViewer>();

            ScrollViewer? namedViewer = viewers.FirstOrDefault(x => x.Name == "PART_ScrollViewer");
            if (namedViewer != null)
                return namedViewer;

            return viewers.FirstOrDefault();
        }

        private static bool IsNearBottom(TextBox tb, State st)
        {
            ScrollViewer? sv = st.Scroll;
            if (sv == null)
                sv = FindScrollViewer(tb);

            // 초기 Template 구성 중이라 ScrollViewer가 없으면
            // 일단 Tail follow 상태로 간주
            if (sv == null)
                return true;

            double threshold = Math.Max(0, tb.GetValue(ThresholdPxProperty));

            double offsetY = sv.Offset.Y;
            double viewportH = sv.Viewport.Height;
            double extentH = sv.Extent.Height;

            return offsetY + viewportH >= extentH - threshold;
        }

        private static void UpdateFollowTail(TextBox tb, State st)
        {
            st.FollowTail = IsNearBottom(tb, st);
        }

        private static void ScrollToEnd(
            TextBox tb,
            State st,
            bool force)
        {
            if (!force && !st.FollowTail)
                return;

            Dispatcher.UIThread.Post(() =>
            {
                // Post 사이에 Disable 되었을 수도 있음
                if (!_states.TryGetValue(tb, out var current))
                    return;

                if (!ReferenceEquals(current, st))
                    return;

                ScrollViewer? sv = st.Scroll;
                if (sv == null)
                    sv = FindScrollViewer(tb);
                if (sv == null)
                    return;

                try
                {
                    sv.ScrollToEnd();
                    st.FollowTail = true;
                }
                catch
                {
                    // 로그 UI 때문에 앱 전체가 죽지 않도록
                }

            }, DispatcherPriority.Background);
        }
    }
}
