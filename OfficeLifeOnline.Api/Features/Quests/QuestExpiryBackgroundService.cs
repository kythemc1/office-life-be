using Microsoft.AspNetCore.SignalR;
using OfficeLifeOnline.Api.Hubs;

namespace OfficeLifeOnline.Api.Features.Quests;

public sealed class QuestExpiryBackgroundService : BackgroundService
{
    private readonly QuestService _quests;
    private readonly IHubContext<GameHub> _hub;

    public QuestExpiryBackgroundService(QuestService quests, IHubContext<GameHub> hub)
    {
        _quests = quests;
        _hub = hub;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var expired in _quests.ExpireOverdue())
            {
                foreach (var participant in expired.Assignment.Participants)
                {
                    var personal = _quests.GetPlayerAssignments(participant.ConnectionId)
                        .FirstOrDefault(x => x.AssignmentId == expired.Assignment.AssignmentId);
                    if (personal is null) continue;
                    await _hub.Clients.Client(participant.ConnectionId)
                        .SendAsync("QuestAssignmentUpdated", personal, stoppingToken);
                }
            }
        }
    }
}