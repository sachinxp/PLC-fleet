using Microsoft.AspNetCore.SignalR;

public class FleetHub : Hub
{
    public async Task JoinFleet()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "Fleet");
    }

    public async Task LeaveFleet()
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "Fleet");
    }
}
