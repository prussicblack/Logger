using System;
using System.Windows.Forms;

namespace SOEM_FrontEnd.Util.Logging.WinForms
{
    public static class ListBoxAutoFollow
    {
        public static bool IsNearBottom(this ListBox listBox, int thresholdItems = 1)
        {
            if (listBox == null)
                throw new ArgumentNullException(nameof(listBox));
            if (listBox.Items.Count == 0)
                return true;

            int visibleItems = GetVisibleItemCount(listBox);
            int lastVisibleIndex = listBox.TopIndex + visibleItems - 1;
            return lastVisibleIndex >= listBox.Items.Count - 1 - Math.Max(0, thresholdItems);
        }

        public static void ScrollToBottom(this ListBox listBox)
        {
            if (listBox == null)
                throw new ArgumentNullException(nameof(listBox));
            if (listBox.Items.Count == 0)
                return;

            int visibleItems = GetVisibleItemCount(listBox);
            listBox.TopIndex = Math.Max(0, listBox.Items.Count - visibleItems);
        }

        /// <summary>
        /// 사용자가 바닥 근처를 보고 있을 때만 새 로그를 따라갑니다.
        /// </summary>
        public static void AppendLogLine(
            this ListBox listBox,
            string line,
            int maxItems = 3000,
            int thresholdItems = 1)
        {
            if (listBox == null)
                throw new ArgumentNullException(nameof(listBox));
            if (maxItems <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxItems));

            bool followTail = listBox.IsNearBottom(thresholdItems);

            listBox.BeginUpdate();
            try
            {
                listBox.Items.Add(line);
                while (listBox.Items.Count > maxItems)
                    listBox.Items.RemoveAt(0);

                if (followTail)
                    listBox.ScrollToBottom();
            }
            finally
            {
                listBox.EndUpdate();
            }
        }

        private static int GetVisibleItemCount(ListBox listBox)
        {
            int itemHeight = Math.Max(1, listBox.ItemHeight);
            return Math.Max(1, listBox.ClientSize.Height / itemHeight);
        }
    }
}
