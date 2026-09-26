using MediatR;
using NWFM.Shared.Results;
using Workflow.Domain.Repositories;

namespace Workflow.Application.Commands;

public sealed record RemoveCalendarItem(Guid CalendarId, Guid ItemId, bool Holiday) : IRequest<Result>;
public sealed class RemoveCalendarItemHandler(IBusinessCalendarRepository calendars) : IRequestHandler<RemoveCalendarItem, Result>
{
    public async Task<Result> Handle(RemoveCalendarItem request, CancellationToken ct)
    {
        var calendar = await calendars.GetByIdWithDetailsAsync(request.CalendarId, ct);
        if (calendar is null || !(request.Holiday ? calendar.RemoveHoliday(request.ItemId) : calendar.RemovePeriod(request.ItemId)))
            return Result.Failure(new Error("Calendar.ItemNotFound", "Calendar item not found."));
        await calendars.SaveChangesAsync(ct);
        return Result.Success();
    }
}
