using System;
using System.Globalization;
using System.IO;
using System.Text;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Sinks.File;

namespace LogUtil
{
    internal readonly struct SensorLogEnvelope
    {
        public SensorLogEnvelope(DateTime timestamp, object?[] values)
        {
            Timestamp = timestamp;
            Values = values;
        }

        public DateTime Timestamp { get; }
        public object?[] Values { get; }
    }

    internal sealed class SensorCsvFormatter : ITextFormatter
    {
        internal const string CsvLinePropertyName = "SensorCsvLine";

        public void Format(LogEvent logEvent, TextWriter output)
        {
            if (logEvent.Properties.TryGetValue(CsvLinePropertyName, out LogEventPropertyValue? value) &&
                value is ScalarValue { Value: string line })
            {
                output.Write(line);
                output.WriteLine();
            }
        }

        public static string SerializeHeader(SensorCsvOptions options)
        {
            var builder = new StringBuilder(64);
            string timestampColumnName;
            if (options.TimestampColumnName == null)
                timestampColumnName = "Timestamp";
            else
                timestampColumnName = options.TimestampColumnName;
            AppendField(builder, NormalizeLineBreaks(timestampColumnName));

            string[] columns;
            if (options.Columns == null)
                columns = Array.Empty<string>();
            else
                columns = options.Columns;

            foreach (string column in columns)
            {
                builder.Append(',');
                string columnName;
                if (column == null)
                    columnName = string.Empty;
                else
                    columnName = column;
                AppendField(builder, NormalizeLineBreaks(columnName));
            }

            return builder.ToString();
        }

        public static string SerializeRow(in SensorLogEnvelope envelope, SensorCsvOptions options)
        {
            var builder = new StringBuilder(64 + (envelope.Values.Length * 16));
            string timestampFormat;
            if (string.IsNullOrWhiteSpace(options.TimestampFormat))
                timestampFormat = "yyyy-MM-dd HH:mm:ss.fff";
            else
                timestampFormat = options.TimestampFormat;
            AppendField(builder, envelope.Timestamp.ToString(timestampFormat, CultureInfo.InvariantCulture));

            foreach (object? value in envelope.Values)
            {
                builder.Append(',');
                AppendField(builder, FormatValue(value));
            }

            return builder.ToString();
        }

        private static string FormatValue(object? value)
        {
            if (value == null)
                return string.Empty;
            if (value is string text)
                return NormalizeLineBreaks(text);
            if (value is DateTime dateTime)
                return dateTime.ToString("O", CultureInfo.InvariantCulture);
            if (value is DateTimeOffset dateTimeOffset)
                return dateTimeOffset.ToString("O", CultureInfo.InvariantCulture);
            if (value is IFormattable formattable)
            {
                string? formatted = formattable.ToString(null, CultureInfo.InvariantCulture);
                if (formatted == null)
                    return string.Empty;
                return formatted;
            }

            string? valueText = value.ToString();
            if (valueText == null) valueText = string.Empty;
            return NormalizeLineBreaks(valueText);
        }

        private static string NormalizeLineBreaks(string value) =>
            value.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');

        private static void AppendField(StringBuilder builder, string value)
        {
            bool quote = value.IndexOf(',') >= 0 || value.IndexOf('"') >= 0;
            if (!quote)
            {
                builder.Append(value);
                return;
            }

            builder.Append('"');
            foreach (char ch in value)
            {
                if (ch == '"') builder.Append("\"\"");
                else builder.Append(ch);
            }
            builder.Append('"');
        }
    }

    internal sealed class SensorCsvHeaderHook : FileLifecycleHooks
    {
        private readonly string _header;

        public SensorCsvHeaderHook(string header)
        {
            if (header == null)
                throw new ArgumentNullException(nameof(header));
            _header = header;
        }

        public override Stream OnFileOpened(string path, Stream underlyingStream, Encoding encoding)
        {
            if (underlyingStream.CanSeek && underlyingStream.Length == 0)
            {
                byte[] bytes = encoding.GetBytes(_header + Environment.NewLine);
                underlyingStream.Write(bytes, 0, bytes.Length);
                underlyingStream.Flush();
            }

            return underlyingStream;
        }
    }
}
