using RichEra.Domain.Entities;

namespace RichEra.Infrastructure.Services;

// Projects the occurrence dates of a scheduled Item into a date range. Shared by income
// projections and statement-import matching so both agree on the schedule. Items without
// a schedule (null Frequency/StartDate) simply yield nothing.
internal static class RecurrenceSchedule
{
    public static IEnumerable<DateOnly> EnumeratePayDates(Item src, DateOnly from, DateOnly to)
    {
        if (src.Frequency is not RepeatFrequency freq || src.StartDate is not DateOnly start)
            yield break;

        var step = freq switch
        {
            RepeatFrequency.Weekly => 7,
            RepeatFrequency.Biweekly => 14,
            RepeatFrequency.Monthly => 0,
            _ => 14,
        };

        if (freq == RepeatFrequency.Monthly)
        {
            var d = start;
            while (d <= to)
            {
                if (d >= from) yield return d;
                d = d.AddMonths(1);
            }
            yield break;
        }

        // Weekly/Biweekly: walk forward in fixed-day steps from StartDate.
        // If StartDate is after `from`, start there. If before, fast-forward.
        var cursor = start;
        if (cursor < from)
        {
            var diff = (from.DayNumber - cursor.DayNumber);
            var skip = diff / step;
            cursor = cursor.AddDays(skip * step);
            while (cursor < from) cursor = cursor.AddDays(step);
        }
        while (cursor <= to)
        {
            yield return cursor;
            cursor = cursor.AddDays(step);
        }
    }
}
