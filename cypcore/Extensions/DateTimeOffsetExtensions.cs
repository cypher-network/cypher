using System;

using CYPCore.Extentions;

namespace CYPCore.Extensions
{
    public static class DateTimeOffsetExtensions
    {
        public static DateTimeOffset EndOfMonth(this DateTimeOffset date)
        {
            var lastDayInMonth = DateTime.DaysInMonth(date.Year, date.Month);
            var newDate = new DateTime(date.Year, date.Month, lastDayInMonth, 23, 59, 59);
            var timeZoneOffset = newDate.GetTimeZoneOffset();
            return new DateTimeOffset(newDate, timeZoneOffset);
        }

        public static DateTimeOffset FromUnixTimeSeconds(this long seconds)
        {
            var dateTimeOffset = new DateTimeOffset(new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));
            dateTimeOffset = dateTimeOffset.AddSeconds(seconds);
            return dateTimeOffset;
        }

        public static long ToUnixTimeSeconds(this DateTimeOffset dateTimeOffset)
        {
            var unixStart = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            var unixTimeStampInTicks = (dateTimeOffset.ToUniversalTime() - unixStart).Ticks;
            return unixTimeStampInTicks / TimeSpan.TicksPerSecond;
        }
    }
}
